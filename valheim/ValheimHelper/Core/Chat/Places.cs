using System.Collections.Generic;

namespace ValheimHelper.Core.Chat
{
    /// <summary>"!탐험" 인자: 방향과 바이옴 이름 (구상서 8-2장).</summary>
    public static class Places
    {
        static readonly Dictionary<string, string> Directions = new Dictionary<string, string>
        {
            ["동"] = "east", ["동쪽"] = "east", ["east"] = "east", ["e"] = "east",
            ["서"] = "west", ["서쪽"] = "west", ["west"] = "west", ["w"] = "west",
            ["남"] = "south", ["남쪽"] = "south", ["south"] = "south", ["s"] = "south",
            ["북"] = "north", ["북쪽"] = "north", ["north"] = "north", ["n"] = "north",
            ["북동"] = "northeast", ["북동쪽"] = "northeast", ["northeast"] = "northeast", ["ne"] = "northeast",
            ["북서"] = "northwest", ["북서쪽"] = "northwest", ["northwest"] = "northwest", ["nw"] = "northwest",
            ["남동"] = "southeast", ["남동쪽"] = "southeast", ["southeast"] = "southeast", ["se"] = "southeast",
            ["남서"] = "southwest", ["남서쪽"] = "southwest", ["southwest"] = "southwest", ["sw"] = "southwest",
        };

        // 값은 게임 Heightmap.Biome 이름. 바다는 못 가므로 넣지 않는다.
        static readonly Dictionary<string, string> Biomes = new Dictionary<string, string>
        {
            ["초원"] = "Meadows", ["meadows"] = "Meadows",
            ["검은숲"] = "BlackForest", ["검은 숲"] = "BlackForest", ["blackforest"] = "BlackForest", ["black forest"] = "BlackForest",
            ["늪"] = "Swamp", ["swamp"] = "Swamp",
            ["산"] = "Mountain", ["mountain"] = "Mountain", ["mountains"] = "Mountain",
            ["평원"] = "Plains", ["plains"] = "Plains",
            ["안개땅"] = "Mistlands", ["mistlands"] = "Mistlands",
            ["애쉬랜드"] = "AshLands", ["애시랜드"] = "AshLands", ["ashlands"] = "AshLands",
            ["딥노스"] = "DeepNorth", ["deepnorth"] = "DeepNorth", ["deep north"] = "DeepNorth",
        };

        /// <param name="s">소문자.</param>
        public static string Direction(string s) => Directions.TryGetValue(s, out var d) ? d : null;

        /// <param name="s">소문자.</param>
        public static string Biome(string s) => Biomes.TryGetValue(s, out var b) ? b : null;
    }
}
