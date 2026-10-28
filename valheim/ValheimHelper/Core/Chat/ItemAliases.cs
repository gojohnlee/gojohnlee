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
            ["은"] = "SilverOre", ["은 광석"] = "SilverOre", ["은광석"] = "SilverOre", ["silver"] = "SilverOre", ["silver ore"] = "SilverOre",
            ["수정"] = "Crystal", ["crystal"] = "Crystal",
            ["검은 핵"] = "BlackCore", ["검은핵"] = "BlackCore", ["black core"] = "BlackCore",
            ["용융 핵"] = "MoltenCore", ["용융핵"] = "MoltenCore", ["molten core"] = "MoltenCore", ["유황"] = "SulfurStone", ["유황석"] = "SulfurStone", ["sulfur"] = "SulfurStone",
            ["불꽃금속"] = "FlametalOreNew", ["불꽃금속 광석"] = "FlametalOreNew", ["flametal"] = "FlametalOreNew", ["flametal ore"] = "FlametalOreNew", ["그라우스텐"] = "Grausten", ["grausten"] = "Grausten",
            ["검은나무"] = "Blackwood", ["검은 나무"] = "Blackwood", ["blackwood"] = "Blackwood", ["볼처 알"] = "VoltureEgg", ["볼처알"] = "VoltureEgg", ["volture egg"] = "VoltureEgg",
            ["바인베리"] = "Vineberry", ["vineberry"] = "Vineberry", ["고사리"] = "Fiddleheadfern", ["fiddlehead"] = "Fiddleheadfern",
            ["붉은 황마"] = "JuteRed", ["붉은황마"] = "JuteRed", ["붉은 황마 천"] = "JuteRed", ["red jute"] = "JuteRed",
            ["늑대 털"] = "WolfHairBundle", ["늑대털"] = "WolfHairBundle", ["wolf hair"] = "WolfHairBundle",
            ["구리주괴"] = "Copper", ["구리 주괴"] = "Copper", ["copper bar"] = "Copper", ["주석주괴"] = "Tin", ["주석 주괴"] = "Tin", ["tin bar"] = "Tin",
            ["청동"] = "Bronze", ["청동주괴"] = "Bronze", ["청동 주괴"] = "Bronze", ["bronze"] = "Bronze", ["철주괴"] = "Iron", ["철 주괴"] = "Iron", ["iron bar"] = "Iron", ["은주괴"] = "Silver", ["은 주괴"] = "Silver", ["silver bar"] = "Silver",
        };

        /// <returns>프리팹 이름, 별칭이 아니면 null.</returns>
        public static string Resolve(string name) =>
            name != null && Map.TryGetValue(name.Trim(), out var prefab) ? prefab : null;
    }
}
