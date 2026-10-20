using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// NPC 시야 (구상서 8-2장): 2초마다 주변 40m에서 보이는 자원을 지식에 적는다.
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
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null) continue;
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
        static Knowledge s_k;
        static ZDOMan s_for;
        static float s_saveTimer;

        public static Knowledge Get()
        {
            if (s_k == null || s_for != ZDOMan.instance)
            {
                s_for = ZDOMan.instance;
                string best = "";
                foreach (var zdo in Workers.AllZdos()) // 월드 불러온 직후 한 번만
                {
                    string t = zdo.GetString(Key, "");
                    if (t.Length > best.Length) best = t;
                }
                s_k = Knowledge.Load(best);
            }
            return s_k;
        }

        public static void Update(float dt)
        {
            if (s_k == null || !s_k.Dirty) return;
            s_saveTimer -= dt;
            if (s_saveTimer > 0f) return;
            s_saveTimer = 30f;
            Save();
        }

        public static void Save()
        {
            if (s_k == null) return;
            string text = s_k.Save();
            foreach (var zdo in Workers.Zdos()) zdo.Set(Key, text);
            s_k.Dirty = false;
        }
    }
}
