using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Storage;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>두 NPC가 같이 쓰는 지식. 세트·보충·밭·개인·빚은 바뀌면 30초마다 일꾼 ZDO에,
    /// 기억(지식·장소·적)은 서버 월드별 파일(worlds_local/&lt;월드&gt;.vh-memory)에 월드 저장 순간에만(SaveMemory, 사용자 11-11).</summary>
    public static partial class Brain
    {
        public const string Key = "vh_knowledge";
        /// <summary>싸워 보고 배운 적(8-2장). 지식처럼 두 NPC ZDO에 같이 저장한다.</summary>
        public const string KeyFoes = "vh_foes";
        /// <summary>장소 기억(8-1장, 설계 A장). 위험한 일이 있었던 곳 100곳.</summary>
        public const string KeyPlaces = "vh_places";
        /// <summary>세트메뉴(6-4장)·자동 보충 기준(11장). 모두 같이 쓴다.</summary>
        public const string KeySets = "vh_sets", KeyRefill = "vh_refill";
        /// <summary>"!밭"으로 정한 밭들(⑨).</summary>
        public const string KeyFarms = "vh_farms";
        /// <summary>쓴 재료 다시 캐기 몫(Departures.Repay, 사용자 11-10 저장).</summary>
        public const string KeyDebt = "vh_debt";
        /// <summary>지원 대기열(요청 지원 기다리기) — 재시작 뒤에도 엇갈리지 않게.</summary>
        public const string KeyHelp = "vh_help";
        /// <summary>내 개인 상자 준비(상자 기능 ③) — 사람마다 물건·수.</summary>
        public const string KeyPersonal = "vh_personal";
        /// <summary>공유 값 저장 번호(Save마다 +1) — 불러올 때 큰 쪽을 고른다(SaveStamp, 옛 저장 = 0).</summary>
        public const string KeySaveNo = "vh_saveno";
        static long s_saveNo;
        static Knowledge s_k;
        static PersonalBook s_personal;
        static PlaceBook s_places;
        static SetMenuBook s_sets;
        static RefillBook s_refill;
        static FarmBook s_farms;
        static DebtBook s_debt;
        static HelpQueue s_help;
        static string s_helpSaved = "";
        static FoeBook s_foes;
        static string s_foesSaved = "";
        static ZDOMan s_for;
        static string s_file;
        static PathMemory s_paths;
        static float s_saveTimer;

        public static Knowledge Get()
        {
            if (s_k == null || s_for != ZDOMan.instance)
            {
                s_for = ZDOMan.instance;
                string best = "", foes = "", places = "", paths = "", sets = "", refill = "", farms = "", personal = "", debt = "", help = "";
                long noSets = 0, noRefill = 0, noFarms = 0, noPersonal = 0, noDebt = 0, noHelp = 0;
                s_saveNo = 0;
                var zdos = Workers.AllZdos();
                foreach (var zdo in zdos) // 월드 불러온 직후 한 번만
                {
                    string t = zdo.GetString(Key, "");
                    if (t.Length > best.Length) best = t;
                    string f = zdo.GetString(KeyFoes, "");
                    if (f.Length > foes.Length) foes = f;
                    string pl = zdo.GetString(KeyPlaces, "");
                    if (pl.Length > places.Length) places = pl;
                    long no = zdo.GetLong(KeySaveNo, 0L); // 공유 값은 저장 번호 큰 쪽(같으면 긴 쪽)
                    Pick(zdo, KeySets, no, ref sets, ref noSets);
                    Pick(zdo, KeyRefill, no, ref refill, ref noRefill);
                    Pick(zdo, KeyFarms, no, ref farms, ref noFarms);
                    Pick(zdo, KeyPersonal, no, ref personal, ref noPersonal);
                    Pick(zdo, KeyDebt, no, ref debt, ref noDebt);
                    Pick(zdo, KeyHelp, no, ref help, ref noHelp);
                    if (no > s_saveNo) s_saveNo = no;
                }
                bool server = ZNet.instance != null && ZNet.instance.IsServer();
                string from = best.Length + places.Length + foes.Length > 0 ? "옛 ZDO" : "없음";
                s_file = server ? MemoryPath() : null;
                if (ReadMemory(s_file, ref best, ref places, ref foes, ref paths)) from = "파일";
                else if (server && s_file == null) Plugin.Log.LogWarning("일꾼 기억 파일 자리 모름(월드 없음) — 이번엔 저장 안 함");
                s_k = Knowledge.Load(best);
                s_paths = new PathMemory();
                s_paths.Load(paths);
                s_foes = FoeBook.Load(foes);
                s_foesSaved = foes;
                s_places = PlaceBook.Load(places);
                s_sets = SetMenuBook.Load(sets);
                s_refill = RefillBook.Load(refill);
                s_farms = FarmBook.Load(farms);
                s_personal = PersonalBook.Load(personal);
                s_debt = DebtBook.Load(debt);
                s_help = HelpQueue.Load(help);
                s_helpSaved = s_help.Save();
                LoadedZones = s_k.Count;
                if (server)
                    Plugin.Log.LogInfo($"일꾼 기억 불러옴({from}, {Path.GetFileName(s_file ?? "")}):일꾼 ZDO {zdos.Count}개, 지식 {s_k.Count}구역({best.Length}자), 장소 {places.Length}자, 적 {foes.Length}자, 길 {s_paths.Count}개");
                StripMemory(zdos);
            }
            return s_k;
        }

        static void Pick(ZDO zdo, string key, long no, ref string best, ref long bestNo)
        {
            string t = zdo.GetString(key, "");
            if (SaveStamp.Better(no, t, bestNo, best)) { best = t; bestNo = no; }
        }

        /// <summary>시험용: 지식·장소 기억을 통째로 바꾼다(시험 상황 만들기, 끝나면 원래 문자열로 되돌림).</summary>
        internal static void ReplaceForTest(string knowledge, string places)
        {
            Get();
            s_k = Knowledge.Load(knowledge);
            s_places = PlaceBook.Load(places);
            s_k.Dirty = true;
            s_places.Dirty = true;
        }

        public static FoeBook Foes
        {
            get { Get(); return s_foes; }
        }

        public static PlaceBook Places
        {
            get { Get(); return s_places; }
        }

        public static SetMenuBook Sets
        {
            get { Get(); return s_sets; }
        }

        public static RefillBook Refill
        {
            get { Get(); return s_refill; }
        }

        public static FarmBook Farms
        {
            get { Get(); return s_farms; }
        }

        public static DebtBook Debt
        {
            get { Get(); return s_debt; }
        }

        public static HelpQueue Help
        {
            get { Get(); return s_help; }
        }

        public static PersonalBook Personal
        {
            get { Get(); return s_personal; }
        }

        public static void Update(float dt)
        {
            if (s_k == null || ZNet.instance == null || !ZNet.instance.IsServer()) return; // 손님 게임에서는 NPC ZDO에 쓰지 않는다(vh_peertest 3)
            s_saveTimer -= dt;
            if (s_saveTimer > 0f) return;
            s_saveTimer = 30f;
            if (s_k.Dirty || s_places.Dirty || s_sets.Dirty || s_refill.Dirty || s_farms.Dirty || s_personal.Dirty || s_debt.Dirty || s_foes.Save() != s_foesSaved || s_help.Save() != s_helpSaved) Save();
        }

        public static void Save()
        {
            if (s_k == null) return;
            string foes = s_foes.Save();
            string sets = s_sets.Save(), refill = s_refill.Save(), farms = s_farms.Save(), personal = s_personal.Save(), debt = s_debt.Save(), help = s_help.Save();
            bool skipped = false;
            long no = ++s_saveNo;
            foreach (var zdo in Workers.Zdos())
            {
                // 남(친구 컴퓨터)이 주인인 ZDO에는 안 쓴다(vh_peertest "싸우는 중": 30초 저장이 뺏긴 순간과 겹쳐 주인 아닌데 씀).
                // Workers.Reclaim이 1초 안에 되찾으니 바뀐 표시를 남기고 2초 뒤 다시 저장한다.
                if (zdo.HasOwner() && !zdo.IsOwner()) { skipped = true; continue; }
                zdo.Set(KeySets, sets); zdo.Set(KeyRefill, refill); zdo.Set(KeyFarms, farms); zdo.Set(KeyPersonal, personal); zdo.Set(KeyDebt, debt); zdo.Set(KeyHelp, help); zdo.Set(KeySaveNo, no); // 기억 셋은 SaveMemory가
            }
            if (skipped) { s_saveTimer = 2f; return; }
            s_k.Dirty = false;
            s_places.Dirty = false;
            s_sets.Dirty = false;
            s_refill.Dirty = false;
            s_farms.Dirty = false;
            s_personal.Dirty = false;
            s_debt.Dirty = false;
            s_helpSaved = help;
            s_foesSaved = foes;
        }
    }
}
