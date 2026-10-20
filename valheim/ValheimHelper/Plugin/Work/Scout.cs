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

        /// <summary>NPC 주변을 지도에 밝힌다(플레이어처럼, 2초마다 Senses가 부른다).</summary>
        public static void Reveal(Vector3 pos)
        {
            Minimap map = Minimap.instance;
            if (map != null) MinimapExplore(map, pos, map.m_exploreRadius);
        }

        /// <param name="preferBiome">이 바이옴(비트 합)으로 가 본 구역 옆의 경계 먼저. 0이면 없음.</param>
        public static bool NextFrontier(WorkerNpc npc, int dirX, int dirY, int preferBiome, out ZoneXY zone)
        {
            Knowledge k = Brain.Get();
            System.Func<ZoneXY, bool> prefer = null;
            if (preferBiome != 0) prefer = n => (k.BiomeOf(n) & preferBiome) != 0;
            return ExplorePlan.Next(Senses.ZoneOf(npc.Home), Senses.ZoneOf(npc.transform.position), MaxRing, k,
                Explored, Land, dirX, dirY, prefer, out zone);
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
