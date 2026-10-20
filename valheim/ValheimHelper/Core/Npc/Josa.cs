namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 받침에 따라 조사 고르기 (나무는 / 철은, 돌이 / 나무가). 대사의 "{item|는}"을 Speech가 여기로 넘긴다.
    /// 어느 쪽 형태로 써도 된다("{item|은}" = "{item|는}").
    /// </summary>
    public static class Josa
    {
        // 받침 있을 때, 없을 때
        static readonly string[][] Pairs =
        {
            new[] { "은", "는" }, new[] { "이", "가" }, new[] { "을", "를" }, new[] { "과", "와" },
            new[] { "이요", "요" }, new[] { "이라", "라" }, new[] { "이나", "나" }, new[] { "이야", "야" },
            new[] { "으로", "로" },
        };

        /// <summary>word 뒤에 붙일 조사. 모르는 조사면 그대로.</summary>
        public static string For(string word, string josa)
        {
            foreach (var p in Pairs)
            {
                if (josa != p[0] && josa != p[1]) continue;
                int b = Batchim(word);
                if (b < 0) return p[0] == "으로" ? "(으)로" : p[0] + "(" + p[1] + ")";
                // ㄹ 받침 뒤의 "로"는 "으로"가 아니다(돌로, 철로).
                if (p[0] == "으로") return b == 0 || b == 8 ? "로" : "으로";
                return b > 0 ? p[0] : p[1];
            }
            return josa;
        }

        static readonly int[] DigitBatchim = { 1, 8, 0, 1, 0, 0, 1, 8, 8, 0 };

        /// <summary>마지막 글자의 받침: 0 = 없음, 8 = ㄹ, 그 밖의 양수 = 있음, -1 = 모름.</summary>
        static int Batchim(string word)
        {
            if (string.IsNullOrEmpty(word)) return -1;
            for (int i = word.Length - 1; i >= 0; i--)
            {
                char c = word[i];
                if (c >= '가' && c <= '힣') return (c - 0xAC00) % 28;
                // 영 일 이 삼 사 오 육 칠 팔 구. 끝이 0인 큰 수(십 백 천 만)도 받침이 있다.
                if (c >= '0' && c <= '9') return DigitBatchim[c - '0'];
                char l = char.ToLowerInvariant(c);
                if (l >= 'a' && l <= 'z') return l == 'l' ? 8 : "mnk".IndexOf(l) >= 0 ? 1 : 0;
                // 괄호·기호는 건너뛴다
            }
            return -1;
        }
    }
}
