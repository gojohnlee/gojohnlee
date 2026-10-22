using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 경작기(Cultivator)로 심을 수 있는 작물 목록(⑨, 게임 데이터에서 읽음 — 손으로 넣지 않는다).
    /// 경작기 m_buildPieces 조각 중 Plant가 있고 자란 모습(m_grownPrefabs)에 Pickable이 있는 것만 작물(나무 묘목은 뺌).
    /// 갈기 조각 = Plant 없이 TerrainOp(갈기 칠하기, 높이 안 바꿈)가 있는 것.
    /// </summary>
    public static class Crops
    {
        public const string Cultivator = "Cultivator";
        static List<Crop> s_all;
        static string s_cultivate;
        static ObjectDB s_for;

        public static List<Crop> All
        {
            get { Build(); return s_all; }
        }

        /// <summary>갈기 조각 프리팹 이름(cultivate_v2 등). 못 찾으면 null.</summary>
        public static string CultivatePiece
        {
            get { Build(); return s_cultivate; }
        }

        public static Crop BySapling(string sapling) => All.Find(c => c.Sapling == sapling);

        /// <summary>그 바이옴에서 자라는 작물이 하나라도 있는지.</summary>
        public static bool AnyGrows(int biome) => All.Exists(c => Farm.CanGrow(c, biome));

        static void Build()
        {
            if (s_all != null && s_for == ObjectDB.instance) return;
            s_for = ObjectDB.instance;
            s_all = new List<Crop>();
            s_cultivate = null;
            var tool = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(Cultivator) : null;
            var table = tool != null ? tool.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces : null;
            if (table == null) return;
            foreach (var go in table.m_pieces)
            {
                if (go == null) continue;
                var plant = go.GetComponent<Plant>();
                if (plant == null)
                {
                    var op = go.GetComponent<TerrainOp>();
                    if (s_cultivate == null && op != null && op.m_settings.m_paintCleared && op.m_settings.m_paintType == TerrainModifier.PaintType.Cultivate
                        && !op.m_settings.m_level && !op.m_settings.m_raise)
                        s_cultivate = go.name;
                    continue;
                }
                var crop = Read(go, plant);
                if (crop != null) s_all.Add(crop);
            }
        }

        static Crop Read(GameObject go, Plant plant)
        {
            var piece = go.GetComponent<Piece>();
            if (piece == null || piece.m_resources == null || piece.m_resources.Length == 0) return null;
            var req = piece.m_resources[0];
            if (req?.m_resItem == null) return null;
            Pickable grown = null;
            foreach (var g in plant.m_grownPrefabs)
                if (g != null && (grown = g.GetComponent<Pickable>()) != null && grown.m_itemPrefab != null) break;
            if (grown == null || grown.m_itemPrefab == null) return null;
            return new Crop
            {
                Sapling = go.name,
                Seed = req.m_resItem.gameObject.name,
                SeedCost = Mathf.Max(1, req.m_amount),
                Produce = grown.m_itemPrefab.name,
                Yield = Mathf.Max(1, grown.m_amount),
                Biomes = (int)plant.m_biome,
                TolerateCold = plant.m_tolerateCold,
                TolerateHeat = plant.m_tolerateHeat,
                GrowRadius = plant.m_growRadius,
                GrowMin = plant.m_growTime,
                GrowMax = plant.m_growTimeMax,
            };
        }

        /// <summary>diag 한 줄: "sapling_carrot(CarrotSeeds→Carrot×1, r0.5, 4000~5000s) …".</summary>
        public static string Describe()
        {
            var parts = new List<string>();
            foreach (var c in All) parts.Add($"{c.Sapling}({c.Seed}→{c.Produce}×{c.Yield}, r{c.GrowRadius:0.0}, {c.GrowMin:0}~{c.GrowMax:0}s)");
            return $"갈기 {CultivatePiece ?? "없음"}, 작물 {All.Count}: " + string.Join(" ", parts);
        }
    }
}
