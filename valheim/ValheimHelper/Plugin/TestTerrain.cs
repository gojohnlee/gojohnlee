using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 시험 전용: 시험이 판 땅을 원래대로 되돌린다(시험 월드에 구덩이가 쌓여 다른 시험이 빠지지 않게).
    /// 바닐라에 되돌리기 기능이 없어 TerrainComp 내부 배열(게임 1.0.20: m_modifiedHeight·m_levelDelta·m_smoothDelta)을 지우고 저장한다.
    /// </summary>
    static class TestTerrain
    {
        static readonly FieldInfo s_mod = AccessTools.Field(typeof(TerrainComp), "m_modifiedHeight");
        static readonly FieldInfo s_level = AccessTools.Field(typeof(TerrainComp), "m_levelDelta");
        static readonly FieldInfo s_smooth = AccessTools.Field(typeof(TerrainComp), "m_smoothDelta");
        static readonly FieldInfo s_hmap = AccessTools.Field(typeof(TerrainComp), "m_hmap");
        static readonly FieldInfo s_width = AccessTools.Field(typeof(TerrainComp), "m_width");
        static readonly MethodInfo s_save = AccessTools.Method(typeof(TerrainComp), "Save");

        /// <returns>되돌린 꼭짓점 수.</returns>
        public static int Reset(Vector3 center, float radius)
        {
            var comps = new HashSet<TerrainComp>();
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    foreach (var tc in TerrainComp.FindAllTerrainCompilers(center + new Vector3(i * radius, 0f, j * radius)))
                        if (tc != null) comps.Add(tc);
            int n = 0;
            foreach (var tc in comps)
            {
                var mod = s_mod.GetValue(tc) as bool[];
                var level = s_level.GetValue(tc) as float[];
                var smooth = s_smooth.GetValue(tc) as float[];
                var hm = s_hmap.GetValue(tc) as Heightmap;
                if (mod == null || level == null || smooth == null || hm == null) continue;
                int width = (int)s_width.GetValue(tc), pitch = width + 1, changed = 0;
                for (int y = 0; y < pitch; y++)
                    for (int x = 0; x < pitch; x++)
                    {
                        int k = y * pitch + x;
                        if (!mod[k]) continue;
                        Vector3 v = hm.transform.position + new Vector3((x - width / 2) * hm.m_scale, 0f, (y - width / 2) * hm.m_scale);
                        if (Utils.DistanceXZ(v, center) > radius) continue;
                        mod[k] = false;
                        level[k] = 0f;
                        smooth[k] = 0f;
                        changed++;
                    }
                if (changed == 0) continue;
                s_save.Invoke(tc, new object[] { false });
                hm.Poke(0, false);
                n += changed;
            }
            return n;
        }

        public static float Height(Vector3 p) => ZoneSystem.instance.GetGroundHeight(p, out float h) ? h : p.y;
    }
}
