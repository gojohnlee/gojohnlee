using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Chat
{
    /// <summary>
    /// 정해진 명령의 채집 자원 별칭 → 게임 프리팹 이름 (구상서 5장 표).
    /// 그 밖의 물건(구리주괴, 청동곡괭이 …)은 게임 쪽 번역 이름으로 찾는다.
    /// </summary>
    public static class ItemAliases
    {
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["나무"] = "Wood", ["목재"] = "Wood", ["wood"] = "Wood",
            ["고급목재"] = "FineWood", ["고급 목재"] = "FineWood", ["finewood"] = "FineWood", ["fine wood"] = "FineWood",
            ["코어목재"] = "RoundLog", ["코어 목재"] = "RoundLog", ["corewood"] = "RoundLog", ["core wood"] = "RoundLog",
            ["돌"] = "Stone", ["stone"] = "Stone",
            ["부싯돌"] = "Flint", ["flint"] = "Flint",
            ["구리"] = "CopperOre", ["구리 광석"] = "CopperOre", ["copper"] = "CopperOre", ["copper ore"] = "CopperOre",
            ["주석"] = "TinOre", ["주석 광석"] = "TinOre", ["tin"] = "TinOre", ["tin ore"] = "TinOre",
            ["철"] = "IronScrap", ["고철"] = "IronScrap", ["iron"] = "IronScrap", ["scrap iron"] = "IronScrap",
        };

        /// <returns>프리팹 이름, 별칭이 아니면 null.</returns>
        public static string Resolve(string name) =>
            name != null && Map.TryGetValue(name.Trim(), out var prefab) ? prefab : null;
    }
}
