namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 저항 미드 판단(구상서 4장·11장, 결정 D). 게임 값 대신 이름 문자열을 쓴다:
    /// 피해 종류 "Poison"·"Frost"·"Fire", 바이옴은 Heightmap.Biome 이름("Swamp" 등).
    /// </summary>
    public static class Resist
    {
        public static readonly string[] Kinds = { "Poison", "Frost", "Fire" };

        /// <summary>들고 다닐 저항 미드 수(종류마다).</summary>
        public const int ToCarry = 2;

        /// <summary>출발 조건 문자열(DepartureResult.Condition·저장값). "resist:Poison".</summary>
        public static string Condition(string kind) => "resist:" + kind;

        /// <summary>조건 문자열이 저항 미드면 그 종류, 아니면 null.</summary>
        public static string KindOf(string condition) =>
            condition != null && condition.StartsWith("resist:", System.StringComparison.Ordinal) ? condition.Substring(7) : null;

        public static string MeadName(string kind) =>
            kind == "Poison" ? "독 저항 미드" : kind == "Frost" ? "냉기 저항 미드" : kind == "Fire" ? "불 저항 미드" : kind;

        /// <summary>그 바이옴의 적이 주로 주는 피해 중 미드로 막을 것. 없으면 null.</summary>
        public static string ForBiome(string biome)
        {
            switch (biome)
            {
                case "Swamp": return "Poison";      // 블롭·거머리·우저 독
                case "Mountain": return "Frost";    // 드레이크 냉기
                case "DeepNorth": return "Frost";
                case "AshLands": return "Fire";
                default: return null;
            }
        }

        /// <summary>
        /// 지금 어떤 저항 미드를 마실지. 이미 그 저항이 있으면 안 마신다(미드 효과가 아직 있거나 장비로 막음).
        /// 중독 중이면 바이옴과 상관없이 독 저항, 아니면 적이 보일 때 바이옴의 것. 마실 게 없으면 null.
        /// </summary>
        public static string ToDrink(string biome, bool foesInView, bool poisoned, System.Func<string, bool> resisted, System.Func<string, bool> have)
        {
            string need = poisoned ? "Poison" : foesInView ? ForBiome(biome) : null;
            if (need == null || resisted(need) || !have(need)) return null;
            return need;
        }
    }
}
