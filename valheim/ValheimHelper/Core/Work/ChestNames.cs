using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>상자 이름이 가리키는 것(I-2, 사용자 결정 2026-10-22). 역할은 겹칠 수 있다.</summary>
    [Flags]
    public enum ChestTags { None = 0, Gear = 1, Food = 2, Mats = 4, Private = 8, Livestock = 16 }

    /// <summary>
    /// 상자 이름(ChestTag) → 역할. 이름 안에 글자가 어디든 들어 있으면 맞음(부분 문자열, 대소문자 무시, 낱말 단위 아님).
    /// 개인·보물·private·personal이 들어 있으면 손대지 않음(역할 낱말과 같이 있어도 이쪽 우선).
    /// </summary>
    public static class ChestNames
    {
        static readonly string[] PrivateWords = { "개인", "보물", "private", "personal" };
        static readonly string[] GearWords = { "장비", "도구", "gear", "equipment", "tool" };
        static readonly string[] FoodWords = { "음식", "미드", "food", "mead" };
        /// <summary>가축 우리 먹이 상자(사용자 11-09 요청 다): 일반 일엔 손대지 않고 우리 먹이 주기(LivestockJob)만 꺼냄.</summary>
        static readonly string[] LivestockWords = { "가축", "livestock" };
        static readonly string[] MatsWords = { "재료", "material", "mats" };

        static bool Has(string name, string[] words)
        {
            foreach (var w in words) if (name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }


        /// <summary>
        /// 종 우리(사용자 11-10 가축 4): 상자 이름이 종 이름이면(띄어쓰기·대소문자 무시, 뒤에 "우리"·"pen" 붙어도) 그 종 프리팹 이름.
        /// "늑대 가죽"처럼 다른 말이 붙으면 아님(재료 상자를 우리로 잘못 보지 않게).
        /// </summary>
        static readonly (string Prefab, string[] Words)[] SpeciesWords =
        {
            ("Boar", new[] { "돼지", "멧돼지", "boar", "pig" }),
            ("Wolf", new[] { "늑대", "wolf" }),
            ("Lox", new[] { "록스", "lox" }),
            ("Hen", new[] { "닭", "chicken", "hen" }),
            ("Asksvin", new[] { "아스크스빈", "asksvin" }),
        };

        public static string PenSpecies(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string s = name.Replace(" ", "").ToLowerInvariant();
            foreach (var suffix in new[] { "우리", "pen" })
                if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.Ordinal)) { s = s.Substring(0, s.Length - suffix.Length); break; }
            foreach (var (prefab, words) in SpeciesWords)
                foreach (var w in words) if (s == w) return prefab;
            return null;
        }
        public static ChestTags Parse(string name)
        {
            if (string.IsNullOrEmpty(name)) return ChestTags.None;
            if (Has(name, PrivateWords)) return ChestTags.Private;
            if (Has(name, LivestockWords) || PenSpecies(name) != null) return ChestTags.Livestock;
            var t = ChestTags.None;
            if (Has(name, GearWords)) t |= ChestTags.Gear;
            if (Has(name, FoodWords)) t |= ChestTags.Food;
            if (Has(name, MatsWords)) t |= ChestTags.Mats;
            return t;
        }

        /// <summary>
        /// 거점 상자들의 태그로 그 역할에 쓸 상자 고르기. role이 None이면 역할 구분 없이(손대지 않는 것만 뺌).
        /// 그 역할 상자가 하나라도 있으면 그것들만, 없으면 일반(이름 없음·안 맞음) 상자, 일반도 없으면 손대지 않는 것 빼고 전부.
        /// </summary>
        /// <summary>일반 일(꺼내기·넣기·정리)에 손대지 않는 상자.</summary>
        public const ChestTags Untouched = ChestTags.Private | ChestTags.Livestock;

        public static bool[] Select(ChestTags[] tags, ChestTags role)
        {
            var ok = new bool[tags.Length];
            bool anyRole = false, anyGeneral = false;
            for (int i = 0; i < tags.Length; i++)
            {
                if ((tags[i] & Untouched) != 0) continue;
                if (role != ChestTags.None && (tags[i] & role) != 0) anyRole = true;
                if (tags[i] == ChestTags.None) anyGeneral = true;
            }
            for (int i = 0; i < tags.Length; i++)
            {
                if ((tags[i] & Untouched) != 0) continue;
                if (role == ChestTags.None) ok[i] = true;
                else if (anyRole) ok[i] = (tags[i] & role) != 0;
                else if (anyGeneral) ok[i] = tags[i] == ChestTags.None;
                else ok[i] = true;
            }
            return ok;
        }
    }
}
