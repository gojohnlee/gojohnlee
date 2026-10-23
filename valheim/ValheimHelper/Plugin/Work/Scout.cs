using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 지도와 탐험 (구상서 8-2장, 12장): 밝혀진 곳 읽기(Minimap.IsExplored — 지도 공유로 남이 밝힌 곳 포함),
    /// NPC가 다닌 곳 밝히기(플레이어와 같은 Minimap.Explore 반경), 다음 경계 구역 고르기.
    /// 지도는 호스트 것이다(사용자 PC가 호스트이자 플레이어).
    /// </summary>
    public static class Scout
    {
        /// <summary>새 땅을 찾을 때 거점에서 최대 몇 구역(64m)까지 보는지. 시간 상한(30분)이 먼저 걸린다.</summary>
        public const int MaxRing = 40;

        static readonly System.Func<Minimap, Vector3, bool> MinimapExplored =
            HarmonyLib.AccessTools.MethodDelegate<System.Func<Minimap, Vector3, bool>>(
                HarmonyLib.AccessTools.Method(typeof(Minimap), "IsExplored", new[] { typeof(Vector3) }));

        static readonly System.Action<Minimap, Vector3, float> MinimapExplore =
            HarmonyLib.AccessTools.MethodDelegate<System.Action<Minimap, Vector3, float>>(
                HarmonyLib.AccessTools.Method(typeof(Minimap), "Explore", new[] { typeof(Vector3), typeof(float) }));

        /// <summary>지도에서 그 한 점이 밝혀졌나(지도 없으면 false). 구역 여러 점 확인은 MapSense.Explored.</summary>
        public static bool ExploredAt(Vector3 p)
        {
            Minimap map = Minimap.instance;
            return map != null && MinimapExplored(map, p);
        }

        public static bool Explored(ZoneXY z)
        {
            Minimap map = Minimap.instance;
            return map == null || MinimapExplored(map, Senses.ZoneCenter(z));
        }

        /// <summary>걸어서 갈 땅인지: 구역 중심이 물 위로 1m 넘게 솟아 있다. 바다는 못 건넌다(포탈은 ⑤).</summary>
        public static bool Land(ZoneXY z)
        {
            Vector3 c = Senses.ZoneCenter(z);
            WorldGenerator wg = WorldGenerator.instance;
            if (wg == null) return false;
            return wg.GetHeight(c.x, c.z) > ZoneSystem.instance.m_waterLevel + 1f && wg.GetBiome(c) != Heightmap.Biome.Ocean;
        }

        // ---- 물 (WaterRule: 좁은 강만 헤엄, 늪·바다·긴 물은 안 건넘) ----

        /// <summary>물 깊이(물 높이 - 땅 높이). 지형이 불러와져 있으면 실제 땅(파고 메운 것 포함), 아니면 월드 생성 높이.</summary>
        public static float WaterDepth(Vector3 p)
        {
            float ground = ZoneSystem.instance.GetGroundHeight(p, out float h) ? h : WorldGenerator.instance.GetHeight(p.x, p.z);
            return ZoneSystem.instance.m_waterLevel - ground;
        }

        static double LiveDepth(double x, double z) => WaterDepth(new Vector3((float)x, 0f, (float)z));
        static double BaseDepth(double x, double z) => ZoneSystem.instance.m_waterLevel - WorldGenerator.instance.GetHeight((float)x, (float)z);

        static bool NoSwim(double x, double z)
        {
            Heightmap.Biome b = WorldGenerator.instance.GetBiome((float)x, (float)z);
            return b == Heightmap.Biome.Swamp || b == Heightmap.Biome.Ocean;
        }

        /// <summary>걷다가 앞을 볼 때(지금 지형으로 2m 간격).</summary>
        public static bool CanWalk(Vector3 from, Vector3 to) =>
            WorldGenerator.instance != null && WaterRule.CanCross(from.x, from.z, to.x, to.z, LiveDepth, NoSwim, 2.0);

        /// <summary>
        /// 목표 고를 때: 지금 자리에서 그 구역 중심까지 곧장 걸어 닿거나(월드 생성 높이로 4m 간격, 먼 곳은 안 불러와져 있다),
        /// 못 걸어가면(바다 건너) 걸어 닿는 이어진 포탈로 갈 수 있는지(사용자 결정 2026-10-23 "바다 건너 일터는 포탈로만").
        /// </summary>
        public static bool Reachable(Vector3 from, ZoneXY z)
        {
            if (WorldGenerator.instance == null) return true;
            Vector3 c = Senses.ZoneCenter(z);
            return WalkFar(from.x, from.z, c.x, c.z) || PortalRoute.Choose(from.x, from.z, c.x, c.z, Portals.LinkedRecent(), WalkFar) >= 0;
        }

        /// <summary>곧장 걸어 닿는지만(포탈 안 봄). 바다 건너 말을 고를 때.</summary>
        public static bool WalkReachable(Vector3 from, ZoneXY z)
        {
            if (WorldGenerator.instance == null) return true;
            Vector3 c = Senses.ZoneCenter(z);
            return WalkFar(from.x, from.z, c.x, c.z);
        }

        /// <summary>곧은 선 위(16m 간격)에 바다(월드 생성 바이옴 Ocean)가 있는지 — 못 걸어가는 까닭이 바다인지(좁은 강·늪 물과 구분).</summary>
        public static bool OceanBetween(Vector3 a, Vector3 b)
        {
            if (WorldGenerator.instance == null) return false;
            float d = Utils.DistanceXZ(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(d / 16f));
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (float)i / n);
                if (WorldGenerator.instance.GetBiome(p.x, p.z) == Heightmap.Biome.Ocean) return true;
            }
            return false;
        }

        /// <summary>먼 두 점 사이를 걸어 건너는지(월드 생성 높이, 4m 간격) — 포탈 고르기(PortalRoute canWalk)에도.</summary>
        public static bool WalkFar(float ax, float az, float bx, float bz) =>
            WorldGenerator.instance == null || WaterRule.CanCross(ax, az, bx, bz, BaseDepth, NoSwim, 4.0);

        static readonly System.Reflection.MethodInfo AddPinMethod = HarmonyLib.AccessTools.Method(typeof(Minimap), "AddPin");

        /// <summary>
        /// 호스트 지도에 죽음 핀(이름 = NPC 이름)을 찍는다. Minimap.AddPin의 마지막 인자(PlatformUserID)는 Splatform 어셈블리에 있어
        /// 직접 부르면 그 참조가 필요하다 → 리플렉션으로 기본값을 넣어 부른다.
        /// </summary>
        public static void DeathPin(Vector3 pos, string name) => AddPin(pos, Minimap.PinType.Death, name, true);

        /// <summary>Minimap.AddPin(체크 안 됨, 주인 0)을 리플렉션으로. save false면 저장 안 되고 플레이어가 지울 수도 없다.</summary>
        public static Minimap.PinData AddPin(Vector3 pos, Minimap.PinType type, string name, bool save)
        {
            if (Minimap.instance == null || AddPinMethod == null) return null;
            var ps = AddPinMethod.GetParameters();
            var args = new object[ps.Length];
            args[0] = pos;
            args[1] = type;
            args[2] = name;
            args[3] = save;
            args[4] = false; // 체크 안 됨
            for (int i = 5; i < ps.Length; i++)
                args[i] = ps[i].ParameterType.IsValueType ? System.Activator.CreateInstance(ps[i].ParameterType) : null;
            return (Minimap.PinData)AddPinMethod.Invoke(Minimap.instance, args);
        }

        /// <summary>NPC 주변을 지도에 밝힌다(플레이어처럼, 2초마다 Senses가 부른다).</summary>
        public static void Reveal(Vector3 pos)
        {
            Minimap map = Minimap.instance;
            if (map != null) MinimapExplore(map, pos, map.m_exploreRadius);
        }

        /// <param name="preferBiome">이 바이옴(비트 합)으로 가 본 구역 옆의 경계 먼저. 0이면 없음.</param>
        public static bool NextFrontier(WorkerNpc npc, int dirX, int dirY, int preferBiome, out ZoneXY zone) =>
            NextFrontier(npc, dirX, dirY, preferBiome, null, out zone);

        /// <param name="avoid">위험해서 버린 구역(Guard). null이면 없음.</param>
        public static bool NextFrontier(WorkerNpc npc, int dirX, int dirY, int preferBiome, ICollection<ZoneXY> avoid, out ZoneXY zone)
        {
            Knowledge k = Brain.Get();
            System.Func<ZoneXY, bool> prefer = null;
            if (preferBiome != 0) prefer = n => (k.BiomeOf(n) & preferBiome) != 0;
            Vector3 from = npc.transform.position;
            return ExplorePlan.Next(Senses.ZoneOf(npc.Home), Senses.ZoneOf(from), MaxRing, k,
                Explored, Land, dirX, dirY, prefer, z => (avoid == null || !avoid.Contains(z)) && Reachable(from, z), out zone);
        }

        /// <summary>구역 중심의 땅 위 위치.</summary>
        public static Vector3 GroundPos(ZoneXY z)
        {
            Vector3 c = Senses.ZoneCenter(z);
            c.y = ZoneSystem.instance.GetSolidHeight(c, out float h) ? h : WorldGenerator.instance.GetHeight(c.x, c.z);
            return c;
        }

        /// <summary>거점에서 본 방위와 거리(10m 단위): "북동쪽", "230".</summary>
        public static void FromHome(Vector3 home, Vector3 p, out string dir, out string dist)
        {
            dir = Bearing.Of(p.x - home.x, p.z - home.z) ?? "바로 옆";
            dist = (Mathf.Round(Utils.DistanceXZ(home, p) / 10f) * 10f).ToString("0");
        }

        public static int BiomeAt(Vector3 p) => WorldGenerator.instance != null ? (int)WorldGenerator.instance.GetBiome(p) : 0;
    }
}
