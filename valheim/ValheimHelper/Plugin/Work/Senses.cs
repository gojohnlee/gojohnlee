using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Storage;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// NPC 시야 (구상서 8-2장): 2초마다 주변 40m에서 보이는 자원을 지식에 적고, 지금 구역의 바이옴을 적고, 지도를 밝힌다.
    /// 구역 중심 가까이(12m) 가면 그 구역은 "둘러봄"이 된다. 지식은 두 NPC가 같이 쓰고 NPC ZDO에 저장한다.
    /// </summary>
    public static class Senses
    {
        public const float SightRange = 40f;
        const float LookedRange = 12f;

        static readonly List<ZDO> s_temp = new List<ZDO>();

        public static ZoneXY ZoneOf(Vector3 p)
        {
            Vector2s z = ZoneSystem.GetZone(p);
            return new ZoneXY(z.x, z.y);
        }

        public static Vector3 ZoneCenter(ZoneXY z) => ZoneSystem.GetZonePos(new Vector2s(z.X, z.Y));

        /// <summary>주변 ZDO(구역 3×3, 64m 넘는 range는 5×5) 중 range 안의 것.</summary>
        public static List<ZDO> Nearby(Vector3 pos, float range) => Nearby(pos, range, null);

        /// <summary>keep이 참인 것만(거리 재기 전에 거름 — 125m 상자 찾기처럼 수천 개 중 몇 개만 볼 때).</summary>
        public static List<ZDO> Nearby(Vector3 pos, float range, System.Func<ZDO, bool> keep)
        {
            s_temp.Clear();
            // 둘레 구역 수: 64m 넘는 범위(거점 125m)는 ±2구역까지 봐야 다 들어옴. classic = 네모 전부
            // (아니면 ZonesWithinRadius 원이라 ±2에서 모서리 구역이 빠져 대각선 ~90m까지만 — 게임 1.0.20 ZDOMan.FindSectorObjects)
            int area = Mathf.Max(1, Mathf.CeilToInt(range / ZoneSystem.c_ZoneSize));
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(pos), new SimulationDistance(area, 0, classic: true), s_temp);
            var list = new List<ZDO>();
            float r2 = range * range;
            foreach (var z in s_temp)
            {
                if (keep != null && !keep(z)) continue;
                Vector3 d = z.GetPosition() - pos;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) list.Add(z);
            }
            return list;
        }

        public static void Look(Vector3 pos)
        {
            var seen = new Dictionary<ZoneXY, Dictionary<string, int>>();
            foreach (var zdo in Nearby(pos, SightRange))
            {
                if (Character.InInterior(zdo.GetPosition())) continue; // 지하묘지 안(y+5000)은 그 구역 바깥 지식이 아니다
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null) continue;
                if (!HiddenOres.Known(zdo, info)) continue; // 묻힌 은 광맥은 위시본 없이는 모른다(⑪)
                ZoneXY zone = ZoneOf(zdo.GetPosition());
                if (!seen.TryGetValue(zone, out var counts)) seen[zone] = counts = new Dictionary<string, int>();
                foreach (var item in info.Produces)
                {
                    counts.TryGetValue(item, out int n);
                    counts[item] = n + 1;
                }
            }
            var full = new List<ZoneXY>();
            ZoneXY here = ZoneOf(pos);
            Brain.Get().SetBiome(here, Scout.BiomeAt(pos)); // 가 본 곳의 바이옴("구리는 검은 숲")
            Scout.Reveal(pos);
            Vector3 c = ZoneCenter(here);
            if (Utils.DistanceXZ(c, pos) < LookedRange)
            {
                full.Add(here);
                Brain.Get().MarkLooked(here);
            }
            Brain.Get().Saw(seen, full, ZNet.instance.GetTimeSeconds());
        }
    }

    /// <summary>두 NPC가 같이 쓰는 지식. 월드를 불러오면 NPC ZDO에서 읽는다. 세트·보충·밭·개인은 바뀌면 30초마다 ZDO에, 기억(지식·장소·적)은 월드 저장 순간에만(SaveMemory).</summary>
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
        /// <summary>내 개인 상자 준비(상자 기능 ③) — 사람마다 물건·수.</summary>
        public const string KeyPersonal = "vh_personal";
        static Knowledge s_k;
        static PersonalBook s_personal;
        static PlaceBook s_places;
        static SetMenuBook s_sets;
        static RefillBook s_refill;
        static FarmBook s_farms;
        static FoeBook s_foes;
        static string s_foesSaved = "";
        static ZDOMan s_for;
        static float s_saveTimer;

        public static Knowledge Get()
        {
            if (s_k == null || s_for != ZDOMan.instance)
            {
                s_for = ZDOMan.instance;
                string best = "", foes = "", places = "", sets = "", refill = "", farms = "", personal = "";
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
                }
                s_k = Knowledge.Load(best);
                s_foes = FoeBook.Load(foes);
                s_foesSaved = foes;
                s_places = PlaceBook.Load(places);
                s_sets = SetMenuBook.Load(sets);
                s_refill = RefillBook.Load(refill);
                s_farms = FarmBook.Load(farms);
                s_personal = PersonalBook.Load(personal);
                LoadedZones = s_k.Count;
                if (ZNet.instance != null && ZNet.instance.IsServer())
                    Plugin.Log.LogInfo($"일꾼 기억 불러옴: 일꾼 ZDO {zdos.Count}개, 지식 {s_k.Count}구역({best.Length}자), 장소 {places.Length}자, 적 {foes.Length}자");
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
            if (s_k.Dirty || s_places.Dirty || s_sets.Dirty || s_refill.Dirty || s_farms.Dirty || s_personal.Dirty || s_foes.Save() != s_foesSaved) Save();
        }

        public static void Save()
        {
            if (s_k == null) return;
            string foes = s_foes.Save();
            string sets = s_sets.Save(), refill = s_refill.Save(), farms = s_farms.Save(), personal = s_personal.Save();
            bool skipped = false;
            foreach (var zdo in Workers.Zdos())
            {
                // 남(친구 컴퓨터)이 주인인 ZDO에는 안 쓴다(vh_peertest "싸우는 중": 30초 저장이 뺏긴 순간과 겹쳐 주인 아닌데 씀).
                // Workers.Reclaim이 1초 안에 되찾으니 바뀐 표시를 남기고 2초 뒤 다시 저장한다.
                if (zdo.HasOwner() && !zdo.IsOwner()) { skipped = true; continue; }
                zdo.Set(KeySets, sets); zdo.Set(KeyRefill, refill); zdo.Set(KeyFarms, farms); zdo.Set(KeyPersonal, personal); // 기억 셋은 SaveMemory가
            }
            if (skipped) { s_saveTimer = 2f; return; }
            s_k.Dirty = false;
            s_places.Dirty = false;
            s_sets.Dirty = false;
            s_refill.Dirty = false;
            s_farms.Dirty = false;
            s_personal.Dirty = false;
            s_foesSaved = foes;
        }

        // ---- 일꾼 기억(지식·장소·적)은 월드 저장 순간에만 ZDO에 (E052, 회차 309) ----
        // 서버만 쓰는 기억인데 지식은 본 구역마다 한 줄씩 늘어 커진다(시험 월드 6.2KB → 일꾼 ZDO 7.1KB).
        // 움직이는 일꾼 ZDO는 자리가 바뀔 때마다 통째로 다시 보내지고(ZDOMan.SendZDOs: Serialize 전체, 손님마다 초당 최대 20번)
        // 남 소유 Prioritized라 맨 앞에 서므로(ServerSendCompare) 한 번에 보낼 몫(10KB − 소켓 큐)을 일꾼 둘이 다 씀
        // → 서버가 손님에게 상자·나뭇가지 주인 바뀜 같은 다른 ZDO를 영영 못 보냄(손님 화면에 상자 없음, 손님 주인 나뭇가지 끝없이 거두기).
        // 그래서 평소엔 일꾼 ZDO에서 빼 두고, ZDOMan.PrepareSave(저장할 ZDO·데이터를 그 자리에서 복사) 앞에 넣었다 뒤에 뺀다.
        // 저장 파일 꼴은 그대로(옛 저장도 같은 키로 읽음). 직접 ZDOExtraData에 써서 판(revision)은 안 올림 → 손님에게 안 감·남 주인 ZDO도 안 꼬임.
        static readonly int[] MemoryKeys = { Key.GetStableHashCode(), KeyPlaces.GetStableHashCode(), KeyFoes.GetStableHashCode() };
        static List<ZDO> s_savingInto;
        /// <summary>월드 불러올 때 읽은 지식 구역 수(시험용).</summary>
        public static int LoadedZones;

        /// <summary>불러온 뒤(Get): 서버에선 일꾼 ZDO에서 기억을 뺀다(손님에게 안 보내게).</summary>
        static void StripMemory(List<ZDO> zdos)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            foreach (var zdo in zdos)
                foreach (int h in MemoryKeys) zdo.RemoveString(h);
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.PrepareSave))]
        static class SaveMemory
        {
            static void Prefix()
            {
                s_savingInto = null;
                if (s_k != null && s_for != ZDOMan.instance) Plugin.Log.LogInfo("일꾼 기억 저장 건너뜀: ZDOMan 바뀜");
                if (s_k == null || s_for != ZDOMan.instance || ZNet.instance == null || !ZNet.instance.IsServer()) return; // 아직 안 불러왔으면 ZDO에 불러온 그대로 저장됨
                string[] vals = { s_k.Save(), s_places.Save(), s_foes.Save() };
                s_savingInto = Workers.AllZdos();
                foreach (var zdo in s_savingInto)
                {
                    for (int i = 0; i < MemoryKeys.Length; i++) ZDOExtraData.Set(zdo.m_uid, MemoryKeys[i], vals[i]);
                    ZDOMan.instance.SetDirtySector(zdo); // 청크 저장은 바뀐 청크만
                }
                Plugin.Log.LogInfo($"일꾼 기억 저장 넣음: 일꾼 ZDO {s_savingInto.Count}개, 지식 {s_k.Count}구역({vals[0].Length}자)");
            }

            static void Postfix()
            {
                if (s_savingInto == null) return;
                // 저장 복사본(ZDOExtraData.s_saveStrings)의 ZDO별 표는 BinarySearchDictionary.Clone = MemberwiseClone이라 살아 있는 표와 배열을 같이 씀
                // → 그대로 빼면 저장본에서도 기억이 사라짐(회차 309: 저장 6구역 → 불러옴 0). 빼기 전에 일꾼 것만 진짜로 복사해 바꿔 끼운다.
                if (AccessTools.Field(typeof(ZDOExtraData), "s_saveStrings")?.GetValue(null) is Dictionary<ZDOID, BinarySearchDictionary<int, string>> save)
                    foreach (var zdo in s_savingInto)
                        if (save.TryGetValue(zdo.m_uid, out var d))
                        {
                            var copy = new BinarySearchDictionary<int, string>();
                            foreach (var kv in d) copy.Add(kv.Key, kv.Value);
                            save[zdo.m_uid] = copy;
                        }
                StripMemory(s_savingInto);
                s_savingInto = null;
            }
        }
    }
}
