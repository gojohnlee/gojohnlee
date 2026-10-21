using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 밝혀진 곳은 안다 (사용자 결정 2026-10-21, 묶음 1 작업 3): 지도에 밝혀진 구역(지도 공유로 남이 밝힌 곳 포함)의 자원은
    /// 가 보지 않아도 지식에 넣는다. Senses.Look과 같은 셈을 구역 단위로: 구역 ZDO → ResourceCatalog → Brain.Saw(그 구역 전부), 둘러봄, 바이옴.
    /// 안 밝혀진 곳·생성 안 된 곳은 그대로 모름(→ 탐험). 적·도구 배우기는 그대로(시야·싸움). 호스트만, 스위치 Features.KnowExplored.
    /// 둘러봄 표시가 되므로 채집의 "밝혀진 곳 찾기"(SearchPlan)는 그 구역을 다시 뒤지지 않고 안 밝혀진 곳만 남는다. [게임 시험 안 됨]
    /// </summary>
    public static class MapSense
    {
        /// <summary>한 번(Every초)에 지도 확인할 구역 수·지식에 넣을 구역 수(ZDO 읽기는 이쪽이 무거움).</summary>
        const int CheckPerStep = 400, AbsorbPerStep = 12;
        const float Every = 0.5f;

        /// <summary>
        /// 거점에서 몇 구역까지(10 = 약 640m, 채집의 밝혀진 곳 찾기 범위 GatherJob.KnownRing과 같게). 탐험 상한(40)까지 넣으면
        /// 지식 문자열(두 NPC ZDO에 같이 저장)이 수백 KB가 될 수 있어 줄였다. 그 밖은 지금처럼 가 보고 안다.
        /// </summary>
        const int Ring = 10;

        static readonly ExploredSweep s_sweep = new ExploredSweep(Ring);
        static readonly List<ZDO> s_temp = new List<ZDO>();
        static readonly float[][] s_points = ExploredSweep.SamplePoints(ZoneSystem.c_ZoneSize);
        static float s_timer;
        static ZDOMan s_for;

        static readonly System.Func<ZoneSystem, Vector2s, bool> ZoneGenerated =
            AccessTools.MethodDelegate<System.Func<ZoneSystem, Vector2s, bool>>(AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated", new[] { typeof(Vector2s) }));

        /// <summary>지금까지 지식에 넣은 구역 수(시험·로그용).</summary>
        public static int Absorbed => s_sweep.Done;

        /// <summary>시험용: 처음부터 다시 훑는다.</summary>
        public static void Reset() => s_sweep.Reset();

        public static void Tick(float dt)
        {
            if (!Features.KnowExplored || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (Minimap.instance == null || ZoneSystem.instance == null || ZDOMan.instance == null) return; // 화면 없는 서버: 밝힌 지도가 없음
            if (s_for != ZDOMan.instance) { s_for = ZDOMan.instance; s_sweep.Reset(); }
            s_timer -= dt;
            if (s_timer > 0f) return;
            s_timer = Every;
            var homes = new List<ZoneXY>();
            foreach (var npc in WorkerNpc.All)
            {
                if (npc == null) continue;
                ZoneXY h = Senses.ZoneOf(npc.Home);
                if (!homes.Contains(h)) homes.Add(h);
            }
            int n = s_sweep.Step(homes, Explored, Absorb, CheckPerStep, AbsorbPerStep);
            if (n > 0) Plugin.Log.LogDebug($"밝혀진 곳 지식: {n}구역 더함(모두 {s_sweep.Done})");
        }

        /// <summary>구역 안 다섯 점(중심+네 모서리 안쪽) 중 하나라도 지도에 밝혀졌나.</summary>
        public static bool Explored(ZoneXY z)
        {
            Minimap map = Minimap.instance;
            if (map == null) return false;
            Vector3 c = Senses.ZoneCenter(z);
            foreach (var p in s_points)
                if (Scout.ExploredAt(new Vector3(c.x + p[0], c.y, c.z + p[1]))) return true;
            return false;
        }

        /// <summary>그 구역 ZDO를 읽어 지식에 넣는다. 아직 생성 안 된 구역이면 false(모름 그대로).</summary>
        public static bool Absorb(ZoneXY z)
        {
            var sector = new Vector2s(z.X, z.Y);
            if (!ZoneGenerated(ZoneSystem.instance, sector)) return false;
            s_temp.Clear();
            ZDOMan.instance.FindSectorObjects(sector, new SimulationDistance(0, 0), s_temp);
            var counts = new Dictionary<string, int>();
            foreach (var zdo in s_temp)
            {
                if (!Senses.ZoneOf(zdo.GetPosition()).Equals(z)) continue;
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null) continue;
                foreach (var item in info.Produces)
                {
                    counts.TryGetValue(item, out int c);
                    counts[item] = c + 1;
                }
            }
            var seen = new Dictionary<ZoneXY, Dictionary<string, int>>();
            if (counts.Count > 0) seen[z] = counts;
            Knowledge k = Brain.Get();
            k.Saw(seen, new List<ZoneXY> { z }, ZNet.instance.GetTimeSeconds());
            k.MarkLooked(z);
            k.SetBiome(z, Scout.BiomeAt(Senses.ZoneCenter(z)));
            return true;
        }
    }
}
