using System.Collections.Generic;

namespace ValheimHelper.Core.Chat
{
    /// <summary>
    /// Claude Code 명령줄 호출 규칙 (구상서 5-1장, claude 2.1.304로 확인): 도구·스킬·설정·세션 저장을 모두 끄고
    /// 모델 별칭(haiku/sonnet)으로 부른다. 사용자 채팅은 인자가 아니라 표준 입력으로 넘겨 따옴표·길이 문제를 없앤다.
    /// </summary>
    public static class ClaudeCall
    {
        public const int TimeoutSeconds = 30;

        /// <summary>"!" 문장은 Haiku, 그 밖의 채팅은 Sonnet. Opus는 쓰지 않는다.</summary>
        public static string Model(bool bang) => bang ? "haiku" : "sonnet";

        /// <summary>프로세스 인자(채팅 본문 제외). systemPrompt에 큰따옴표가 있어도 되게 호출자가 따로 인용한다.</summary>
        public static List<string> Args(string model, string systemPrompt) => new List<string>
        {
            "-p", "--model", model, "--tools", "", "--no-session-persistence", "--disable-slash-commands",
            "--setting-sources", "", "--output-format", "json", "--system-prompt", systemPrompt,
        };

        /// <summary>--output-format json 출력에서 답 본문. 오류거나 형식이 틀리면 null.</summary>
        public static string ExtractResult(string stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout)) return null;
            object root;
            try { root = MiniJson.Parse(stdout.Trim()); }
            catch (System.FormatException) { return null; }
            if (!(root is Dictionary<string, object> obj)) return null;
            if (obj.TryGetValue("is_error", out var err) && err is bool b && b) return null;
            return obj.TryGetValue("result", out var res) && res is string s && s.Trim().Length > 0 ? s : null;
        }

        /// <summary>Windows 명령줄 한 줄로 합칠 때 인자 하나를 인용한다.</summary>
        const char BS = (char)92;

        public static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '"', '\t', '\n' }) < 0) return a;
            var sb = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in a)
            {
                if (c == BS) { slashes++; continue; }
                if (c == '"') { sb.Append(BS, slashes * 2 + 1).Append('"'); }
                else { sb.Append(BS, slashes).Append(c); }
                slashes = 0;
            }
            sb.Append(BS, slashes * 2).Append('"');
            return sb.ToString();
        }
    }
}
