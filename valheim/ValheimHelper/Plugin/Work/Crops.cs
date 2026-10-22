using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 경작기(Cultivator)로 심을 수 있는 작물 목록(⑨, 게임 데이터에서 읽음 — 손으로 넣지 않는다).
    /// 경작기 m_buildPieces 조각 중 Plant가 있고 자란 모습(m_grownPrefabs)에 Pickable이 있는 것만 작물(나무 묘목·붙여 심는 덩굴은 뺌).
    /// 갈기 조각 = Plant 없이 TerrainOp(갈기 칠하기, 높이 안 바꿈)가 있는 것.
    /// </summary>
    public static class Crops
    {
        public const string Cultivator = "Cultivator";
        static List<Crop> s_all;
        static string s_cultivate;
        static GameObject s_cultivateGo;
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

        /// <summary>갈기 조각 프리팹(경작기 조각표의 것 — ZNetScene에 없을 수도 있어 이걸 Instantiate한다). 못 찾으면 null.</summary>
        public static GameObject CultivatePrefab
        {
            get { Build(); return s_cultivateGo; }
        }

        public static Crop BySapling(string sapling) => All.Find(c => c.Sapling == sapling);

        /// <summary>작물을 거두면 나오는 물건(Carrot 등)인지 — 밭에서 다 자란 Pickable·떨군 것 고르기.</summary>
        public static bool IsProduce(string prefab) => All.Exists(c => c.Produce == prefab);

        /// <summary>그 물건이 나오는 작물 중 가장 많이 나오는 것(없으면 null).</summary>
        public static Crop ForProduce(string item)
        {
            Crop best = null;
            foreach (var c in All) if (c.Produce == item && (best == null || c.Yield > best.Yield)) best = c;
            return best;
        }

        /// <summary>밭에서 다 자란 작물 프리팹(Pickable_Carrot)인지 — 들에서 나는 것과 가르기.</summary>
        public static bool IsGrown(string prefab) => All.Exists(c => c.Grown == prefab);

        /// <summary>그 바이옴에서 자라는 작물이 하나라도 있는지.</summary>
        public static bool AnyGrows(int biome) => All.Exists(c => Farm.CanGrow(c, biome));

        static void Build()
        {
            if (s_all != null && s_for == ObjectDB.instance) return;
            s_for = ObjectDB.instance;
            s_all = new List<Crop>();
            s_cultivate = null;
            s_cultivateGo = null;
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
                    { s_cultivate = go.name; s_cultivateGo = go; }
                    continue;
                }
                if (plant.m_attachDistance > 0f) continue; // 덩굴(VineAsh_sapling 등): 벽·조각에 붙여야 자람 — 밭에 못 심음
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
                Grown = grown.gameObject.name,
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
