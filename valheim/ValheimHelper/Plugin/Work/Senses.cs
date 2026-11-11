using System.Collections.Generic;
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
            // (아니면 ZonesWithinRadius 원이라 ±2에서 모서리 구역이 빠져 대각선 ~90m까지만 — 게임 1.0.20·1.0.21 ZDOMan.FindSectorObjects)
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
}
