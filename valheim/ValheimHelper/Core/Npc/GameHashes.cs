using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 통째로 바꾼 게임 함수 변경 감지(사용자 11-15): 원본을 건너뛰고 같은 순서를 모드가 대신 하는 Prefix는
    /// 게임 업데이트로 원본이 바뀌면 모드 쪽이 옛 동작으로 남는다. 그 함수들의 IL 바이트 해시를 기록 파일과 비교해 다르면 알린다.
    /// 기록 파일 꼴: "# 주석" 줄, "이름=해시" 줄(이름은 Type.Method).
    /// </summary>
    public static class GameHashes
    {
        public enum State { Same, Changed, New, Missing }

        public readonly struct Result
        {
            public readonly string Name, Known, Now;
            public readonly State State;
            public Result(string name, string known, string now, State state) { Name = name; Known = known; Now = now; State = state; }
        }

        /// <summary>IL 바이트 → SHA256 앞 16자(소문자). null·빈 것은 "".</summary>
        public static string Hash(byte[] il)
        {
            if (il == null || il.Length == 0) return "";
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(il).Take(8).Select(b => b.ToString("x2")));
        }

        public static Dictionary<string, string> Parse(IEnumerable<string> lines)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (lines == null) return d;
            foreach (var raw in lines)
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        public static List<string> Format(IDictionary<string, string> now, string header)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(header)) lines.Add("# " + header);
            foreach (var kv in now.OrderBy(k => k.Key, StringComparer.Ordinal)) lines.Add($"{kv.Key}={kv.Value}");
            return lines;
        }

        /// <summary>지금 해시 하나하나를 기록과 비교. 기록에만 있는 이름은 Missing(목록에서 뺀 함수).
        /// 지금 해시가 ""(함수 못 찾음)이면 Changed — 게임이 함수를 없앴거나 이름을 바꾼 것.</summary>
        public static List<Result> Compare(IDictionary<string, string> known, IDictionary<string, string> now)
        {
            var r = new List<Result>();
            foreach (var kv in now.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!known.TryGetValue(kv.Key, out string k)) r.Add(new Result(kv.Key, "", kv.Value, kv.Value == "" ? State.Changed : State.New));
                else r.Add(new Result(kv.Key, k, kv.Value, k == kv.Value && kv.Value != "" ? State.Same : State.Changed));
            }
            foreach (var kv in known.OrderBy(k => k.Key, StringComparer.Ordinal))
                if (!now.ContainsKey(kv.Key)) r.Add(new Result(kv.Key, kv.Value, "", State.Missing));
            return r;
        }
    }
}
