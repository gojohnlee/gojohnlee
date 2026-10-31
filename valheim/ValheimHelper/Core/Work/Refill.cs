using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 자동 보충 (구상서 11장, 기본 꺼짐): "!계속채워 나무 200" → 상자 재고가 200 아래면 200까지. 만든 물건·세트메뉴도 기준치로.
    /// "!채워 나무 200"(사용자 10-28)은 한 번만: 200까지 차면 지운다.
    /// 1분마다 확인, 우선순위는 직접 명령 > 묘비 회수 > 자동 보충(플러그인이 한가할 때만 부른다). 스스로 짐작하지는 않는다.
    /// </summary>
    public sealed class RefillBook
    {
        public const double CheckEvery = 60.0;
        /// <summary>[기본안] 기준치 아래로 이만큼(비율) 이상 내려가야 나선다 — 1개 쓸 때마다 나가지 않게.</summary>
        public const float Slack = 0.1f;

        readonly SortedDictionary<string, int> _items = new SortedDictionary<string, int>(StringComparer.Ordinal);
        readonly SortedDictionary<int, int> _sets = new SortedDictionary<int, int>();
        /// <summary>한 번만 채울 것("!채워"): "i:물건"·"s:번호". 다 차면 Due가 지운다. 없으면 유지("!계속채워").</summary>
        readonly HashSet<string> _once = new HashSet<string>(StringComparer.Ordinal);
        public bool Dirty;
        public int Count => _items.Count + _sets.Count;

        public void Keep(string item, int amount, bool once = false)
        {
            if (string.IsNullOrEmpty(item) || amount <= 0) return;
            _items[item] = amount;
            Mark("i:" + item, once);
            Dirty = true;
        }

        public void KeepSet(int number, int amount, bool once = false)
        {
            if (number < 1 || amount <= 0) return;
            _sets[number] = amount;
            Mark("s:" + number, once);
            Dirty = true;
        }

        public bool IsOnce(string item) => _once.Contains("i:" + item);
        public bool IsOnceSet(int number) => _once.Contains("s:" + number);

        void Mark(string key, bool once)
        {
            if (once) _once.Add(key); else _once.Remove(key);
        }

        /// <summary>!계속채워 끔 [물건]: 물건 하나, 없으면 전부.</summary>
        public void Off(string item = null)
        {
            if (item == null) { if (Count > 0) Dirty = true; _items.Clear(); _sets.Clear(); _once.Clear(); return; }
            if (_items.Remove(item)) Dirty = true;
            _once.Remove("i:" + item);
        }

        public IEnumerable<KeyValuePair<string, int>> Items => _items;
        public IEnumerable<KeyValuePair<int, int>> Sets => _sets;

        /// <summary>
        /// 지금 채워야 할 것: 물건 → 모자란 수(기준치 − 재고). 세트는 세트 안의 물건마다 기준치(같은 물건이면 큰 기준치).
        /// 유지 기준치가 걸린 물건은 기준치의 Slack 비율보다 적게 모자라면 건너뛴다(한 번 채움만 걸린 물건은 1개라도).
        /// 한 번 채움은 제 기준치까지 찼으면 여기서 지운다.
        /// </summary>
        public List<Ingredient> Due(Func<string, int> stock, SetMenuBook sets)
        {
            var goal = new Dictionary<string, int>();
            var kept = new HashSet<string>(StringComparer.Ordinal); // 유지 기준치가 하나라도 걸린 물건
            foreach (var kv in _items)
            {
                goal[kv.Key] = kv.Value;
                if (!IsOnce(kv.Key)) kept.Add(kv.Key);
            }
            var setItems = new Dictionary<int, List<string>>();
            if (sets != null)
                foreach (var kv in _sets)
                {
                    var s = sets.Get(kv.Key);
                    if (s == null) continue;
                    setItems[kv.Key] = new List<string>(s.Items);
                    foreach (var it in s.Items)
                    {
                        if (!goal.TryGetValue(it, out int g) || g < kv.Value) goal[it] = kv.Value;
                        if (!IsOnceSet(kv.Key)) kept.Add(it);
                    }
                }
            var due = new List<Ingredient>();
            var keys = new List<string>(goal.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var item in keys)
            {
                int want = goal[item], have = stock(item);
                int missing = want - have;
                if (missing <= 0) continue;
                if (kept.Contains(item) && missing < Math.Max(1, (int)Math.Ceiling(want * Slack))) continue;
                due.Add(new Ingredient(item, missing));
            }
            foreach (var kv in new List<KeyValuePair<string, int>>(_items))
                if (IsOnce(kv.Key) && stock(kv.Key) >= kv.Value) { _items.Remove(kv.Key); _once.Remove("i:" + kv.Key); Dirty = true; }
            foreach (var kv in new List<KeyValuePair<int, int>>(_sets))
            {
                if (!IsOnceSet(kv.Key) || !setItems.TryGetValue(kv.Key, out var its)) continue;
                bool full = true;
                foreach (var it in its) if (stock(it) < kv.Value) full = false;
                if (full) { _sets.Remove(kv.Key); _once.Remove("s:" + kv.Key); Dirty = true; }
            }
            return due;
        }

        // ---- 저장: "i:물건=수;s:번호=수;…" — 한 번 채움은 대문자 I·S ----

        /// <summary>막대 10칸(■ 찬 몫, □ 빈 몫). want 0 이하면 다 참.</summary>
        public static string Bar(int have, int want)
        {
            int f = want <= 0 ? 10 : (int)Math.Min(10L, Math.Max(0L, (long)have * 10 / want));
            return new string('■', f) + new string('□', 10 - f);
        }

        /// <summary>"나무 32/50 ■■■■■■□□□□" 꼴 한 줄. once면 "(한 번)"을 붙인다.</summary>
        public static string Line(string label, int have, int want, bool once) =>
            label + " " + have + "/" + want + " " + Bar(have, want) + (once ? " (한 번)" : "");

        /// <summary>!계속채워 목록(사용자 10-31): 물건마다 한 줄, 세트는 세트 안 가장 적은 물건 수로. 지우는 일은 없다(Due 몫).</summary>
        public List<string> Status(Func<string, int> stock, SetMenuBook sets, Func<string, string> label)
        {
            var lines = new List<string>();
            foreach (var kv in _items) lines.Add(Line(label(kv.Key), stock(kv.Key), kv.Value, IsOnce(kv.Key)));
            foreach (var kv in _sets)
            {
                var s = sets != null ? sets.Get(kv.Key) : null;
                int have = 0;
                if (s != null && s.Items.Count > 0)
                {
                    have = int.MaxValue;
                    foreach (var it in s.Items) have = Math.Min(have, stock(it));
                }
                lines.Add(Line("세트 " + kv.Key + "번", have, kv.Value, IsOnceSet(kv.Key)));
            }
            return lines;
        }

        public string Save()
        {
            var parts = new List<string>();
            foreach (var kv in _items) parts.Add((IsOnce(kv.Key) ? "I:" : "i:") + kv.Key + "=" + kv.Value);
            foreach (var kv in _sets) parts.Add((IsOnceSet(kv.Key) ? "S:" : "s:") + kv.Key + "=" + kv.Value);
            return string.Join(";", parts.ToArray());
        }

        public static RefillBook Load(string text)
        {
            var b = new RefillBook();
            if (string.IsNullOrEmpty(text)) return b;
            var inv = CultureInfo.InvariantCulture;
            foreach (var part in text.Split(';'))
            {
                int eq = part.LastIndexOf('=');
                if (part.Length < 3 || part[1] != ':' || eq < 3 || !int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, inv, out int n)) continue;
                string key = part.Substring(2, eq - 2);
                bool once = part[0] == 'I' || part[0] == 'S';
                char c = char.ToLowerInvariant(part[0]);
                if (c == 'i') b.Keep(key, n, once);
                else if (c == 's' && int.TryParse(key, NumberStyles.Integer, inv, out int no)) b.KeepSet(no, n, once);
            }
            b.Dirty = false;
            return b;
        }
    }
}
