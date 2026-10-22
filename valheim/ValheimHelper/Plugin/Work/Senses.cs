using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
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

        /// <summary>주변 ZDO(구역 3×3) 중 range 안의 것.</summary>
        public static List<ZDO> Nearby(Vector3 pos, float range)
        {
            s_temp.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(pos), new SimulationDistance(1, 0), s_temp);
            var list = new List<ZDO>();
            float r2 = range * range;
            foreach (var z in s_temp)
            {
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

    /// <summary>두 NPC가 같이 쓰는 지식. 월드를 불러오면 NPC ZDO에서 읽고, 바뀌면 30초마다 저장한다.</summary>
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
        static Knowledge s_k;
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
                string best = "", foes = "", places = "", sets = "", refill = "", farms = "";
                foreach (var zdo in Workers.AllZdos()) // 월드 불러온 직후 한 번만
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
                }
                s_k = Knowledge.Load(best);
                s_foes = FoeBook.Load(foes);
                s_foesSaved = foes;
                s_places = PlaceBook.Load(places);
                s_sets = SetMenuBook.Load(sets);
                s_refill = RefillBook.Load(refill);
                s_farms = FarmBook.Load(farms);
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

        public static void Update(float dt)
        {
            if (s_k == null || ZNet.instance == null || !ZNet.instance.IsServer()) return; // 손님 게임에서는 NPC ZDO에 쓰지 않는다(vh_peertest 3)
            s_saveTimer -= dt;
            if (s_saveTimer > 0f) return;
            s_saveTimer = 30f;
            if (s_k.Dirty || s_places.Dirty || s_sets.Dirty || s_refill.Dirty || s_farms.Dirty || s_foes.Save() != s_foesSaved) Save();
        }

        public static void Save()
        {
            if (s_k == null) return;
            string text = s_k.Save();
            string foes = s_foes.Save();
            string places = s_places.Save();
            string sets = s_sets.Save(), refill = s_refill.Save(), farms = s_farms.Save();
            foreach (var zdo in Workers.Zdos())
            {
                zdo.Set(Key, text); zdo.Set(KeyFoes, foes); zdo.Set(KeyPlaces, places); zdo.Set(KeySets, sets); zdo.Set(KeyRefill, refill); zdo.Set(KeyFarms, farms);
            }
            s_k.Dirty = false;
            s_places.Dirty = false;
            s_sets.Dirty = false;
            s_refill.Dirty = false;
            s_farms.Dirty = false;
            s_foesSaved = foes;
        }
    }
}
