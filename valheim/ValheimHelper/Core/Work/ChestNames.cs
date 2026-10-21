using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>상자 이름이 가리키는 것(I-2, 사용자 결정 2026-10-22). 역할은 겹칠 수 있다.</summary>
    [Flags]
    public enum ChestTags { None = 0, Gear = 1, Food = 2, Mats = 4, Private = 8 }

    /// <summary>
    /// 상자 이름(ChestTag) → 역할. 이름 안에 글자가 어디든 들어 있으면 맞음(부분 문자열, 대소문자 무시, 낱말 단위 아님).
    /// 개인·보물·private·personal이 들어 있으면 손대지 않음(역할 낱말과 같이 있어도 이쪽 우선).
    /// </summary>
    public static class ChestNames
    {
        static readonly string[] PrivateWords = { "개인", "보물", "private", "personal" };
        static readonly string[] GearWords = { "장비", "도구", "gear", "equipment", "tool" };
        static readonly string[] FoodWords = { "음식", "미드", "food", "mead" };
        static readonly string[] MatsWords = { "재료", "material", "mats" };

        static bool Has(string name, string[] words)
        {
            foreach (var w in words) if (name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static ChestTags Parse(string name)
        {
            if (string.IsNullOrEmpty(name)) return ChestTags.None;
            if (Has(name, PrivateWords)) return ChestTags.Private;
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
        public static bool[] Select(ChestTags[] tags, ChestTags role)
        {
            var ok = new bool[tags.Length];
            bool anyRole = false, anyGeneral = false;
            for (int i = 0; i < tags.Length; i++)
            {
                if ((tags[i] & ChestTags.Private) != 0) continue;
                if (role != ChestTags.None && (tags[i] & role) != 0) anyRole = true;
                if (tags[i] == ChestTags.None) anyGeneral = true;
            }
            for (int i = 0; i < tags.Length; i++)
            {
                if ((tags[i] & ChestTags.Private) != 0) continue;
                if (role == ChestTags.None) ok[i] = true;
                else if (anyRole) ok[i] = (tags[i] & role) != 0;
                else if (anyGeneral) ok[i] = tags[i] == ChestTags.None;
                else ok[i] = true;
            }
            return ok;
        }
    }
}
