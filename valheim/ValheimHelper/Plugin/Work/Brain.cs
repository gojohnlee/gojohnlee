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
    public static class Brain
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
        /// <summary>내 개인 상자 준비(상자 기능 ③) — 사람마다 물건·수.</summary>
        public const string KeyPersonal = "vh_personal";
        static Knowledge s_k;
        static PersonalBook s_personal;
        static PlaceBook s_places;
        static SetMenuBook s_sets;
        static RefillBook s_refill;
        static FarmBook s_farms;
        static DebtBook s_debt;
        static FoeBook s_foes;
        static string s_foesSaved = "";
        static ZDOMan s_for;
        static string s_file;
        static float s_saveTimer;

        public static Knowledge Get()
        {
            if (s_k == null || s_for != ZDOMan.instance)
            {
                s_for = ZDOMan.instance;
                string best = "", foes = "", places = "", sets = "", refill = "", farms = "", personal = "", debt = "";
                var zdos = Workers.AllZdos();
                foreach (var zdo in zdos) // 월드 불러온 직후 한 번만
                {
                    string t = zdo.GetString(Key, "");
                    if (t.Length > best.Length) best = t;
                    string f = zdo.GetString(KeyFoes, "");
                    if (f.Length > foes.Length) foes = f;
                    string pl = zdo.GetString(KeyPlaces, "");
                    if (pl.Length > places.Length) places = pl;
                    string se = zdo.GetString(KeySets, "");
                    if (se.Length > sets.Length) sets = se;
                    string re = zdo.GetString(KeyRefill, "");
                    if (re.Length > refill.Length) refill = re;
                    string fa = zdo.GetString(KeyFarms, "");
                    if (fa.Length > farms.Length) farms = fa;
                    string pe = zdo.GetString(KeyPersonal, "");
                    if (pe.Length > personal.Length) personal = pe;
                    string de = zdo.GetString(KeyDebt, "");
                    if (de.Length > debt.Length) debt = de;
                }
                bool server = ZNet.instance != null && ZNet.instance.IsServer();
                string from = best.Length + places.Length + foes.Length > 0 ? "옛 ZDO" : "없음";
                s_file = server ? MemoryPath() : null;
                if (ReadMemory(s_file, ref best, ref places, ref foes)) from = "파일";
                else if (server && s_file == null) Plugin.Log.LogWarning("일꾼 기억 파일 자리 모름(월드 없음) — 이번엔 저장 안 함");
                s_k = Knowledge.Load(best);
                s_foes = FoeBook.Load(foes);
                s_foesSaved = foes;
                s_places = PlaceBook.Load(places);
                s_sets = SetMenuBook.Load(sets);
                s_refill = RefillBook.Load(refill);
                s_farms = FarmBook.Load(farms);
                s_personal = PersonalBook.Load(personal);
                s_debt = DebtBook.Load(debt);
                LoadedZones = s_k.Count;
                if (server)
                    Plugin.Log.LogInfo($"일꾼 기억 불러옴({from}, {Path.GetFileName(s_file ?? "")}):일꾼 ZDO {zdos.Count}개, 지식 {s_k.Count}구역({best.Length}자), 장소 {places.Length}자, 적 {foes.Length}자");
                StripMemory(zdos);
            }
            return s_k;
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
            if (s_k.Dirty || s_places.Dirty || s_sets.Dirty || s_refill.Dirty || s_farms.Dirty || s_personal.Dirty || s_debt.Dirty || s_foes.Save() != s_foesSaved) Save();
        }

        public static void Save()
        {
            if (s_k == null) return;
            string foes = s_foes.Save();
            string sets = s_sets.Save(), refill = s_refill.Save(), farms = s_farms.Save(), personal = s_personal.Save(), debt = s_debt.Save();
            bool skipped = false;
            foreach (var zdo in Workers.Zdos())
            {
                // 남(친구 컴퓨터)이 주인인 ZDO에는 안 쓴다(vh_peertest "싸우는 중": 30초 저장이 뺏긴 순간과 겹쳐 주인 아닌데 씀).
                // Workers.Reclaim이 1초 안에 되찾으니 바뀐 표시를 남기고 2초 뒤 다시 저장한다.
                if (zdo.HasOwner() && !zdo.IsOwner()) { skipped = true; continue; }
                zdo.Set(KeySets, sets); zdo.Set(KeyRefill, refill); zdo.Set(KeyFarms, farms); zdo.Set(KeyPersonal, personal); zdo.Set(KeyDebt, debt); // 기억 셋은 SaveMemory가
            }
            if (skipped) { s_saveTimer = 2f; return; }
            s_k.Dirty = false;
            s_places.Dirty = false;
            s_sets.Dirty = false;
            s_refill.Dirty = false;
            s_farms.Dirty = false;
            s_personal.Dirty = false;
            s_debt.Dirty = false;
            s_foesSaved = foes;
        }

        // ---- 일꾼 기억(지식·장소·적)은 서버 월드별 파일에, 월드 저장 순간에만 (사용자 11-11) ----
        // 지식은 본 구역마다 늘어 커진다 → 일꾼 ZDO에 두면 손님에게 통째로 계속 보내져 다른 ZDO가 막힘(E052, 회차 309).
        // 파일은 월드 폴더 곁(worlds_local/<월드>.vh-memory): 폴더 안은 게임이 이름 바꿀 때 모르는 파일을 지움(SaveSystem.RenameDirectory),
        // 곁의 모르는 확장자는 월드 목록이 안 봄(SaveSystem.IsWorldSaveExtension). 게임 월드 백업에는 안 들어감.
        const int MemoryVersion = 1;
        static readonly int[] MemoryKeys = { Key.GetStableHashCode(), KeyPlaces.GetStableHashCode(), KeyFoes.GetStableHashCode() };
        /// <summary>월드 불러올 때 읽은 지식 구역 수(시험용).</summary>
        public static int LoadedZones;

        /// <summary>서버 월드의 기억 파일 경로. 월드를 모르면 null.</summary>
        static string MemoryPath()
        {
            var w = ZNet.GetWorldIfIsHost();
            if (w == null || string.IsNullOrEmpty(w.m_worldName)) return null;
            string root = w.m_fileSource.IsLocal() ? SaveSystem.GetWorldsSaveRootPath(w.m_fileSource)
                : Path.Combine(BepInEx.Paths.ConfigPath, "ValheimHelper", "worlds"); // 클라우드 월드는 파일 경로가 없음
            return Path.Combine(root, w.m_worldName + ".vh-memory");
        }

        /// <summary>파일에서 지식·장소·적을 읽는다. 없거나 못 읽으면 false(그땐 옛 ZDO 것).</summary>
        static bool ReadMemory(string path, ref string knowledge, ref string places, ref string foes)
        {
            if (path == null || !File.Exists(path)) return false;
            try
            {
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadInt32() != MemoryVersion) throw new IOException("판 다름");
                    string k = r.ReadString(), p = r.ReadString(), f = r.ReadString();
                    knowledge = k; places = p; foes = f;
                }
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"일꾼 기억 파일 못 읽음({path}): {e.Message} — ZDO 것으로"); return false; }
        }

        /// <summary>불러온 뒤(Get): 서버에선 일꾼 ZDO의 옛 기억 키를 뺀다(손님에게 안 보내게, 다음 저장부터 ZDO엔 없음).</summary>
        static void StripMemory(List<ZDO> zdos)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            foreach (var zdo in zdos)
                foreach (int h in MemoryKeys) zdo.RemoveString(h);
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.PrepareSave))]
        static class SaveMemory
        {
            static readonly FieldInfo s_endSave = AccessTools.Field(typeof(ZDOMan), "m_endSave"), s_state = AccessTools.Field(typeof(ZDOMan), "m_currentSaveState"),
                s_dirty = AccessTools.Field(typeof(ZDOMan), "m_dirtyChunks");
            static readonly MethodInfo s_updateSaveState = AccessTools.Method(typeof(ZDOMan), "UpdateSaveState");

            static void Prefix()
            {
                // E072: 앞 저장이 끝났어도 "저장 중" 표(둘째 표에 바뀐 조각)는 다음 ZDOMan.Update에서야 풀림 → 그 전에 다시 저장(끄기)하면
                // 이번 저장이 첫째 표만 보고 바뀐 조각을 안 씀. 게임이 다음 프레임에 할 합치기를 지금 함(SaveWorld가 앞 저장 스레드를 기다린 뒤라 안전).
                try
                {
                    var dirty = s_dirty?.GetValue(ZDOMan.instance) as HashSet<ZoneSystem.ChunkIndex>[];
                    object end = s_endSave?.GetValue(ZDOMan.instance);
                    string line = $"E072 저장 전: 상태 {s_state?.GetValue(ZDOMan.instance)}, 끝 {end ?? "없음"}, 바뀐 조각 {dirty?[0].Count}/{dirty?[1].Count}, 저장 번호 {SaveSystem.GetSaveNumber()}";
                    Plugin.Log.LogInfo(line);
                    File.AppendAllText(Path.Combine(TestRunner.PluginDir, "e072-save.txt"), System.DateTime.Now.ToString("HH:mm:ss ") + line + "\n"); // 다음 실행(restart2)이 읽음
                    if (end != null) s_updateSaveState?.Invoke(ZDOMan.instance, null);
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("E072 저장 전 확인 못 함: " + e.Message); }
                if (s_k != null && s_for != ZDOMan.instance) Plugin.Log.LogInfo("일꾼 기억 저장 건너뜀: ZDOMan 바뀜");
                if (s_k == null || s_for != ZDOMan.instance || s_file == null || ZNet.instance == null || !ZNet.instance.IsServer()) return; // 안 불러왔으면 파일·옛 ZDO 그대로
                string[] vals = { s_k.Save(), s_places.Save(), s_foes.Save() };
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(s_file));
                    string tmp = s_file + ".new";
                    using (var w = new BinaryWriter(File.Create(tmp)))
                    {
                        w.Write(MemoryVersion);
                        foreach (var v in vals) w.Write(v);
                    }
                    if (File.Exists(s_file)) File.Replace(tmp, s_file, null);
                    else File.Move(tmp, s_file);
                    Plugin.Log.LogInfo($"일꾼 기억 파일 저장: {Path.GetFileName(s_file)} {new FileInfo(s_file).Length}바이트, 지식 {s_k.Count}구역({vals[0].Length}자), 장소 {vals[1].Length}자, 적 {vals[2].Length}자");
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"일꾼 기억 파일 저장 못 함({s_file}): {e.Message}"); }
            }
        }
    }
}

