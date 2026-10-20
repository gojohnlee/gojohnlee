using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ValheimHelper.Core.Chat
{
    /// <summary>
    /// 채팅 한 줄을 3갈래로 나눈다 (구상서 5장, 5-1장, 8-1장 대답 받기).
    /// NPC 자신의 메시지·시스템 메시지는 부르는 쪽에서 미리 걸러야 한다.
    /// </summary>
    public sealed class ChatParser
    {
        public const int MaxRadius = 20;
        public const int DefaultRadius = 10;

        readonly Dictionary<string, string> _npcNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Func<string, string> _resolveItem;

        /// <summary>수량 상한. 넘으면 상한으로 줄이고 Clamped를 켠다.</summary>
        public int MaxAmount = 999;

        /// <param name="npcNames">별칭 → 설정 이름 (예: "alarak" → "Alarak", "알라라크" → "Alarak").</param>
        /// <param name="resolveItem">별칭표에 없는 물건 이름 → 게임 프리팹 이름. 모르면 null. 게임 쪽에서 번역 이름으로 찾는다.</param>
        public ChatParser(IDictionary<string, string> npcNames, Func<string, string> resolveItem = null)
        {
            if (npcNames != null)
                foreach (var kv in npcNames) _npcNames[kv.Key] = kv.Value;
            _resolveItem = resolveItem;
        }

        /// <param name="awaitingAnswer">NPC가 질문형 반응을 한 뒤 대답을 기다리는 중인지.</param>
        public ChatResult Parse(string line, bool awaitingAnswer = false)
        {
            string text = (line ?? "").Trim();
            if (text.Length == 0) return Ignore();

            if (text[0] == '!' || text[0] == '！')
            {
                string body = text.TrimStart('!', '！').Trim();
                if (body.Length == 0) return Ignore();
                string[] tokens = Split(body);
                string target = null;
                if (tokens.Length > 1 && _npcNames.TryGetValue(tokens[0], out var name))
                {
                    target = name;
                    tokens = tokens.Skip(1).ToArray();
                }
                var cmd = ParseCommand(tokens);
                if (cmd == null) return new ChatResult { Route = ChatRoute.Haiku, Text = text, Target = target };
                if (cmd.Kind != CommandKind.SetChest || cmd.Target == null) cmd.Target = target;
                return new ChatResult { Route = ChatRoute.Command, Command = cmd };
            }

            if (awaitingAnswer && TryAnswer(text, out var answer))
                return new ChatResult { Route = ChatRoute.Answer, Answer = answer };

            if (IsChatter(text)) return Ignore();
            return new ChatResult { Route = ChatRoute.Sonnet, Text = text, Target = LeadingName(text) };
        }

        static ChatResult Ignore() => new ChatResult { Route = ChatRoute.Ignore };

        static string[] Split(string s) =>
            s.Split(new[] { ' ', '\t', '　' }, StringSplitOptions.RemoveEmptyEntries);

        static string Lower(string s) => s.ToLowerInvariant();

        ChatCommand ParseCommand(string[] t)
        {
            string head = Lower(t[0]);
            int n = t.Length;
            string joined = Lower(string.Join(" ", t));

            switch (joined)
            {
                case "같이 가자": case "같이가자": case "escort": return Kind(CommandKind.Escort);
                case "그냥 가": case "그냥가": case "go anyway": case "goanyway": return Kind(CommandKind.GoAnyway);
                case "취소 전부": case "전부 취소": case "clear": return Kind(CommandKind.ClearQueue);
            }

            switch (head)
            {
                case "상태": case "status": return n == 1 ? Kind(CommandKind.Status) : null;
                case "스킬": case "skills": case "skill": return n == 1 ? Kind(CommandKind.Skills) : null;
                case "기억": case "memory": return n == 1 ? Kind(CommandKind.Memory) : null;
                case "지식": case "knowledge": return n == 1 ? Kind(CommandKind.Knowledge) : null;
                case "멈춰": case "stop": return n == 1 ? Kind(CommandKind.Stop) : null;
                case "길": case "path": return n == 1 ? Kind(CommandKind.Path) : null;
                case "탐험": case "explore": return ParseExplore(t);
                case "상자": case "chest": return ParseChest(t);
                case "채워": case "keep": return ParseKeep(t);
                case "세트": case "set": return ParseSet(t);
                case "밭": case "farm": return ParseRadius(CommandKind.Farm, t);
                case "평탄화": case "level": return ParseRadius(CommandKind.Level, t);
            }
            return ParseItemAmount(t, 0, t.Length);
        }

        static ChatCommand Kind(CommandKind k) => new ChatCommand { Kind = k };

        /// <summary>t[from..to) = "&lt;물건 이름(여러 낱말 가능)&gt; &lt;수량&gt;".</summary>
        ChatCommand ParseItemAmount(string[] t, int from, int to, CommandKind kind = CommandKind.Item)
        {
            if (to - from < 2) return null;
            if (!TryAmount(t[to - 1], out int amount)) return null;
            string item = ResolveItem(t, from, to - 1);
            if (item == null) return null;
            var cmd = new ChatCommand { Kind = kind, Item = item };
            SetAmount(cmd, amount);
            return cmd;
        }

        void SetAmount(ChatCommand cmd, int amount)
        {
            cmd.Amount = Math.Min(amount, MaxAmount);
            cmd.Clamped = amount > MaxAmount;
        }

        string ResolveItem(string[] t, int from, int to)
        {
            if (to <= from) return null;
            string spaced = string.Join(" ", t, from, to - from);
            string compact = string.Concat(t.Skip(from).Take(to - from));
            return ItemAliases.Resolve(spaced) ?? ItemAliases.Resolve(compact)
                ?? _resolveItem?.Invoke(spaced) ?? (compact != spaced ? _resolveItem?.Invoke(compact) : null);
        }

        /// <summary>양의 정수. "50개", "50x"도 받는다.</summary>
        static bool TryAmount(string s, out int amount)
        {
            s = Lower(s);
            if (s.EndsWith("개")) s = s.Substring(0, s.Length - 1);
            else if (s.EndsWith("x")) s = s.Substring(0, s.Length - 1);
            return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out amount) && amount > 0;
        }

        ChatCommand ParseExplore(string[] t)
        {
            var cmd = Kind(CommandKind.Explore);
            if (t.Length == 1) return cmd;
            string arg = Lower(string.Join(" ", t, 1, t.Length - 1));
            string dir = Places.Direction(arg);
            if (dir != null) { cmd.Arg = dir; return cmd; }
            string biome = Places.Biome(arg);
            if (biome != null) { cmd.Biome = biome; return cmd; }
            return null;
        }

        ChatCommand ParseChest(string[] t)
        {
            if (t.Length < 2) return null;
            int end = t.Length;
            string owner = null;
            if (end > 2 && _npcNames.TryGetValue(t[end - 1], out var name)) { owner = name; end--; }

            var cmd = Kind(CommandKind.SetChest);
            cmd.Target = owner;
            string kind = Lower(string.Join(" ", t, 1, end - 1));
            switch (kind)
            {
                case "장비": case "equipment": case "gear": cmd.Arg = "equipment"; return cmd;
                case "음식": case "food": cmd.Arg = "food"; return cmd;
                case "씨앗": case "seeds": case "seed": cmd.Arg = "seeds"; return cmd;
            }
            if (owner != null) return null; // 자원 상자는 같이 쓴다 (3-2장)
            cmd.Item = ResolveItem(t, 1, end);
            if (cmd.Item == null) return null;
            cmd.Arg = "item";
            return cmd;
        }

        ChatCommand ParseKeep(string[] t)
        {
            if (t.Length < 2) return null;
            string second = Lower(t[1]);
            if (second == "끔" || second == "off")
            {
                var off = Kind(CommandKind.KeepOff);
                if (t.Length == 2) return off;
                off.Item = ResolveItem(t, 2, t.Length);
                return off.Item == null ? null : off;
            }
            if ((second == "세트" || second == "set") && t.Length == 4)
            {
                var set = ParseSetNumbers(CommandKind.KeepSet, t[2], t[3]);
                return set;
            }
            return ParseItemAmount(t, 1, t.Length, CommandKind.Keep);
        }

        ChatCommand ParseSet(string[] t)
        {
            if (t.Length == 1) return Kind(CommandKind.SetList);
            if (t.Length == 3) return ParseSetNumbers(CommandKind.SetMake, t[1], t[2]);
            return null;
        }

        ChatCommand ParseSetNumbers(CommandKind kind, string number, string amount)
        {
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int no) || no < 1 || no > SetMenuMax)
                return null;
            if (!TryAmount(amount, out int a)) return null;
            var cmd = new ChatCommand { Kind = kind, Number = no };
            SetAmount(cmd, a);
            return cmd;
        }

        /// <summary>세트메뉴 최대 개수 (6-4장).</summary>
        public const int SetMenuMax = 10;

        static ChatCommand ParseRadius(CommandKind kind, string[] t)
        {
            var cmd = new ChatCommand { Kind = kind, Number = DefaultRadius };
            if (t.Length == 1) return cmd;
            if (t.Length != 2) return null;
            string s = Lower(t[1]);
            if (s.EndsWith("m")) s = s.Substring(0, s.Length - 1);
            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int r) || r < 1) return null;
            cmd.Number = Math.Min(r, MaxRadius);
            cmd.Clamped = r > MaxRadius;
            return cmd;
        }

        // ---- "!" 없는 채팅 ----

        static readonly HashSet<string> Yes = new HashSet<string>
        {
            "응", "ㅇㅇ", "ㅇ", "어", "네", "예", "넹", "가", "그래", "ㄱㄱ", "ㄱ", "고", "좋아", "콜",
            "ok", "okay", "yes", "y", "yep", "go", "sure",
        };

        static readonly HashSet<string> No = new HashSet<string>
        {
            "아니", "ㄴㄴ", "ㄴ", "됐어", "아냐", "싫어", "아니요", "아뇨", "놉",
            "no", "n", "nope", "nah",
        };

        static bool TryAnswer(string text, out AnswerKind answer)
        {
            string s = Lower(text).TrimEnd('!', '.', '~', '?', ' ', '！');
            if (Yes.Contains(s)) { answer = AnswerKind.Yes; return true; }
            if (No.Contains(s)) { answer = AnswerKind.No; return true; }
            answer = default;
            return false;
        }

        static readonly HashSet<string> Laughs = new HashSet<string> { "lol", "lmao", "xd", "gg", "ㅇㅋ" };

        /// <summary>Claude를 부르지 않을 줄: 2글자 이하, 웃음·자모·이모티콘·기호뿐인 줄.</summary>
        static bool IsChatter(string text)
        {
            string compact = string.Concat(Split(text));
            if (new StringInfo(compact).LengthInTextElements <= 2) return true;
            string lower = Lower(compact);
            if (Laughs.Contains(lower)) return true;
            if (lower.Trim('h', 'a') == "") return true; // haha, hahaha
            foreach (char c in compact)
            {
                bool jamo = c >= 'ㄱ' && c <= 'ㆎ';
                bool symbol = char.IsPunctuation(c) || char.IsSymbol(c) || char.IsSurrogate(c) || c == '‍' || c == '️';
                if (!jamo && !symbol) return false;
            }
            return true;
        }

        /// <summary>"Claude야 나무 좀", "Alarak, 철" — 줄 앞에 NPC 이름이 있으면 그 이름.</summary>
        string LeadingName(string text)
        {
            foreach (var kv in _npcNames)
            {
                if (!text.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) continue;
                if (text.Length == kv.Key.Length) return kv.Value;
                char next = text[kv.Key.Length];
                bool asciiWord = next < 128 && char.IsLetterOrDigit(next);
                if (!asciiWord) return kv.Value;
            }
            return null;
        }
    }
}
