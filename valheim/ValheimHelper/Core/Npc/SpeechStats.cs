using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ValheimHelper.Core.Npc
{
    /// <summary>실플레이에서 상황(대사 키)별로 말한 횟수(사용자 10-29). 파일은 "횟수\t키\t줄 수(라그나르/플로키)" 줄,
    /// 자주 나오는데 줄이 적은 상황(전투 제외)은 맨 위 "# 늘릴 것" 줄로 — 루프가 읽어 history에 알린다.</summary>
    public sealed class SpeechStats
    {
        /// <summary>이만큼 나왔는데 한쪽이라도 줄이 이보다 적으면 늘릴 것.</summary>
        public const int OftenCount = 20, FewLines = 15;
        static readonly string[] CombatKeys = { "fightStart", "flee", "huntHurt", "backOff" };

        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public int Total => _counts.Values.Sum();
        public int this[string key] => _counts.TryGetValue(key, out var n) ? n : 0;

        public void Add(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _counts[key] = this[key] + 1;
        }

        public static bool IsCombat(string key) => Array.IndexOf(CombatKeys, key) >= 0;

        static int LinesOf(Personality p, string key) => Speech.Options(p, key).Length;

        /// <summary>자주 나오는데(OftenCount 이상) 줄이 적은(FewLines 미만) 상황, 많이 나온 순. 전투·없는 키 제외.</summary>
        public List<string> NeedMore() => _counts
            .Where(kv => kv.Value >= OftenCount && !IsCombat(kv.Key) && Speech.Has(Personality.Ragnar, kv.Key)
                && Math.Min(LinesOf(Personality.Ragnar, kv.Key), LinesOf(Personality.Floki, kv.Key)) < FewLines)
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key).ToList();

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("# 상황별 말한 횟수(실플레이만, 시험 제외). 횟수\t키\t줄 수(라그나르/플로키)\n");
            sb.Append("# 늘릴 것: ").Append(string.Join(" ", NeedMore())).Append('\n');
            foreach (var kv in _counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                sb.Append(kv.Value).Append('\t').Append(kv.Key).Append('\t')
                  .Append(LinesOf(Personality.Ragnar, kv.Key)).Append('/').Append(LinesOf(Personality.Floki, kv.Key)).Append('\n');
            return sb.ToString();
        }

        /// <summary>ToText 꼴을 읽어 지금 횟수에 더한다(깨진 줄·# 줄은 건너뜀).</summary>
        public void Load(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split('\t');
                if (parts.Length < 2 || !int.TryParse(parts[0], out var n) || n <= 0 || parts[1].Length == 0) continue;
                _counts[parts[1]] = this[parts[1]] + n;
            }
        }
    }
}
