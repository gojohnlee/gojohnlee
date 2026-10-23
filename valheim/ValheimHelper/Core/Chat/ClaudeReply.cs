using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHelper.Core.Chat
{
    /// <summary>
    /// Claude 호출 횟수 제한 (구상서 5-1장 기본안): Sonnet은 1분에 6번까지, 넘으면 대기열. 시각은 초.
    /// </summary>
    public sealed class RateLimiter
    {
        readonly int _max;
        readonly double _window;
        readonly Queue<double> _calls = new Queue<double>();

        public RateLimiter(int max, double windowSeconds) { _max = max; _window = windowSeconds; }

        /// <summary>지금 부를 수 있으면 부른 것으로 세고 true.</summary>
        public bool TryTake(double now)
        {
            while (_calls.Count > 0 && now - _calls.Peek() >= _window) _calls.Dequeue();
            if (_calls.Count >= _max) return false;
            _calls.Enqueue(now);
            return true;
        }

        /// <summary>다음에 부를 수 있는 시각(지금 되면 now).</summary>
        public double NextFree(double now)
        {
            while (_calls.Count > 0 && now - _calls.Peek() >= _window) _calls.Dequeue();
            return _calls.Count < _max ? now : _calls.Peek() + _window;
        }
    }

    /// <summary>Claude가 돌려준 작업 하나를 검사한 결과(정해진 명령으로 바꾼 것).</summary>
    public sealed class ClaudeTask
    {
        public ChatCommand Command;
        /// <summary>세트 저장·고치기용 물건들(게임 프리팹 이름).</summary>
        public readonly List<string> Items = new List<string>();
        public readonly List<string> Remove = new List<string>();
        public string Name;
    }

    public sealed class ClaudeResult
    {
        /// <summary>그냥 수다라고 판단했다: 조용히 넘어간다.</summary>
        public bool Chatter;
        /// <summary>NPC가 할 답장 한 줄(검사·잘라낸 것). 없으면 null.</summary>
        public string Reply;
        public readonly List<ClaudeTask> Tasks = new List<ClaudeTask>();
        /// <summary>버린 작업 수와 이유(로그용).</summary>
        public readonly List<string> Rejected = new List<string>();
        /// <summary>JSON이 아니거나 형식이 틀림 → "지금은 정해진 ! 명령만 돼요".</summary>
        public bool Invalid;
    }

    /// <summary>
    /// Claude 답(JSON) 검사 (구상서 5-1장): 허용된 작업만 받고 수량 상한을 둔다. 실제 행동은 늘 NPC가 바닐라 규칙으로 하므로
    /// 여기서 걸러지지 않은 것도 치트가 되지는 않지만, 모르는 작업·물건·지나친 수량은 버린다.
    /// 형식: {"chatter":false,"reply":"…","tasks":[{"kind":"item","item":"Wood","amount":50,"npc":"라그나","urgent":false}, …]}
    /// kind: item, explore(arg 방향 또는 biome), escort, stop, status, memory, knowledge, skills, keep(item, amount), keepoff(item?),
    ///       setmake(number, amount), setsave(number?, name?, items[]), setedit(number, remove[], add[]).
    /// </summary>
    public static class ClaudeReply
    {
        public const int MaxTasks = 5, MaxReply = 150;

        public static ClaudeResult Parse(string json, Func<string, string> resolveItem, ICollection<string> npcNames, int maxAmount = 999)
        {
            var r = new ClaudeResult();
            object root;
            try { root = MiniJson.Parse(ExtractObject(json)); }
            catch (FormatException) { r.Invalid = true; return r; }
            if (!(root is Dictionary<string, object> obj)) { r.Invalid = true; return r; }
            r.Chatter = Bool(obj, "chatter");
            r.Reply = CleanReply(Str(obj, "reply"));
            if (r.Chatter) { r.Reply = null; return r; }
            if (obj.TryGetValue("tasks", out var tasksObj) && tasksObj is List<object> tasks)
            {
                foreach (var t in tasks)
                {
                    if (r.Tasks.Count >= MaxTasks) { r.Rejected.Add("작업이 너무 많음"); break; }
                    if (!(t is Dictionary<string, object> task)) { r.Rejected.Add("작업 형식"); continue; }
                    string why = ToTask(task, resolveItem, npcNames, maxAmount, out var ct);
                    if (why != null) r.Rejected.Add(why);
                    else r.Tasks.Add(ct);
                }
            }
            return r;
        }

        /// <summary>앞뒤 설명이 붙어 와도 첫 { 부터 짝이 맞는 } 까지.</summary>
        static string ExtractObject(string s)
        {
            if (s == null) throw new FormatException();
            int start = s.IndexOf('{'), end = s.LastIndexOf('}');
            if (start < 0 || end <= start) throw new FormatException();
            return s.Substring(start, end - start + 1);
        }

        static string CleanReply(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\n' || c == '\r' || c == '\t') { sb.Append(' '); continue; }
                if (char.IsControl(c) || c == '<' || c == '>') continue; // 채팅 서식 태그 막기
                sb.Append(c);
            }
            string t = sb.ToString().Trim();
            if (t.Length > MaxReply) t = t.Substring(0, MaxReply).TrimEnd() + "…";
            return t.Length == 0 ? null : t;
        }

        static string ToTask(Dictionary<string, object> t, Func<string, string> resolveItem, ICollection<string> npcNames, int maxAmount, out ClaudeTask ct)
        {
            ct = new ClaudeTask();
            var cmd = new ChatCommand();
            ct.Command = cmd;
            string npc = Str(t, "npc");
            if (npc != null)
            {
                string found = null;
                foreach (var n in npcNames) if (string.Equals(n, npc, StringComparison.OrdinalIgnoreCase)) found = n;
                if (found == null) return "모르는 NPC " + npc;
                cmd.Target = found;
            }
            cmd.Urgent = Bool(t, "urgent");
            string kind = (Str(t, "kind") ?? "").ToLowerInvariant();
            switch (kind)
            {
                case "item":
                case "keep":
                    cmd.Kind = kind == "item" ? CommandKind.Item : CommandKind.Keep;
                    cmd.Item = Item(t, "item", resolveItem);
                    if (cmd.Item == null) return "모르는 물건 " + Str(t, "item");
                    return Amount(t, cmd, maxAmount);
                case "keepoff":
                    cmd.Kind = CommandKind.KeepOff;
                    if (Str(t, "item") != null && (cmd.Item = Item(t, "item", resolveItem)) == null) return "모르는 물건 " + Str(t, "item");
                    return null;
                case "explore":
                    cmd.Kind = CommandKind.Explore;
                    string arg = Str(t, "arg");
                    if (arg != null)
                    {
                        string dir = Places.Direction(arg), biome = dir == null ? Places.Biome(arg) : null;
                        if (dir == null && biome == null) return "모르는 탐험 방향 " + arg;
                        cmd.Arg = dir;
                        cmd.Biome = biome;
                    }
                    return null;
                case "escort": cmd.Kind = CommandKind.Escort; return null;
                case "stop": cmd.Kind = CommandKind.Stop; return null;
                case "status": cmd.Kind = CommandKind.Status; return null;
                case "memory": cmd.Kind = CommandKind.Memory; return null;
                case "knowledge": cmd.Kind = CommandKind.Knowledge; return null;
                case "skills": cmd.Kind = CommandKind.Skills; return null;
                case "setmake":
                    cmd.Kind = CommandKind.SetMake;
                    if (!SetNumber(t, cmd, false)) return "세트 번호";
                    return Amount(t, cmd, maxAmount);
                case "setsave":
                    cmd.Kind = CommandKind.SetSave;
                    if (!SetNumber(t, cmd, true)) return "세트 번호";
                    ct.Name = CleanReply(Str(t, "name"));
                    if (!Items(t, "items", resolveItem, ct.Items) || ct.Items.Count == 0) return "세트 물건";
                    return null;
                case "setedit":
                    cmd.Kind = CommandKind.SetEdit;
                    if (!SetNumber(t, cmd, false)) return "세트 번호";
                    if (!Items(t, "remove", resolveItem, ct.Remove) || !Items(t, "add", resolveItem, ct.Items)) return "세트 물건";
                    if (ct.Items.Count == 0 && ct.Remove.Count == 0) return "바꿀 것 없음";
                    return null;
            }
            return "허용 안 된 작업 " + kind;
        }

        static string Amount(Dictionary<string, object> t, ChatCommand cmd, int maxAmount)
        {
            if (!t.TryGetValue("amount", out var a) || !(a is double d) || d < 1 || d != Math.Floor(d)) return "수량";
            cmd.Amount = (int)Math.Min(d, maxAmount);
            cmd.Clamped = d > maxAmount;
            return null;
        }

        static bool SetNumber(Dictionary<string, object> t, ChatCommand cmd, bool optional)
        {
            if (!t.TryGetValue("number", out var n) || n == null) { cmd.Number = 0; return optional; }
            if (!(n is double d) || d != Math.Floor(d) || d < 1 || d > ChatParser.SetMenuMax) return false;
            cmd.Number = (int)d;
            return true;
        }

        static string Item(Dictionary<string, object> t, string key, Func<string, string> resolve)
        {
            string name = Str(t, key);
            if (string.IsNullOrEmpty(name)) return null;
            return ItemAliases.Resolve(name) ?? resolve?.Invoke(name);
        }

        static bool Items(Dictionary<string, object> t, string key, Func<string, string> resolve, List<string> into)
        {
            if (!t.TryGetValue(key, out var v) || v == null) return true;
            if (!(v is List<object> list)) return false;
            foreach (var o in list)
            {
                if (!(o is string s)) return false;
                string item = ItemAliases.Resolve(s) ?? resolve?.Invoke(s);
                if (item == null) return false;
                if (!into.Contains(item)) into.Add(item);
            }
            return true;
        }

        static string Str(Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) ? v as string : null;
        static bool Bool(Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) && v is bool b && b;
    }

    /// <summary>작은 JSON 읽기(게임 Mono에 System.Text.Json이 없다). 객체 → Dictionary, 배열 → List, 숫자 → double.</summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i, 0);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("trailing");
            return v;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Value(string s, ref int i, int depth)
        {
            if (depth > 16) throw new FormatException("deep");
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("end");
            char c = s[i];
            if (c == '{') return Obj(s, ref i, depth);
            if (c == '[') return Arr(s, ref i, depth);
            if (c == '"') return Str(s, ref i);
            if (Lit(s, ref i, "true")) return true;
            if (Lit(s, ref i, "false")) return false;
            if (Lit(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        static bool Lit(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        static Dictionary<string, object> Obj(string s, ref int i, int depth)
        {
            var d = new Dictionary<string, object>();
            i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("key");
                string key = Str(s, ref i);
                Ws(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException(":");
                i++;
                d[key] = Value(s, ref i, depth + 1);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException("}");
            }
        }

        static List<object> Arr(string s, ref int i, int depth)
        {
            var l = new List<object>();
            i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i, depth + 1));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return l; }
                throw new FormatException("]");
            }
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw new FormatException("\\u");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("string");
        }

        static double Num(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException("number");
            return d;
        }
    }
}
