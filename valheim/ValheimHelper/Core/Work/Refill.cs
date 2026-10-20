using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 자동 보충 (구상서 11장, 기본 꺼짐): "!채워 나무 200" → 상자 재고가 200 아래면 200까지. 만든 물건·세트메뉴도 기준치로.
    /// 1분마다 확인, 우선순위는 직접 명령 > 묘비 회수 > 자동 보충(플러그인이 한가할 때만 부른다). 스스로 짐작하지는 않는다.
    /// </summary>
    public sealed class RefillBook
    {
        public const double CheckEvery = 60.0;
        /// <summary>[기본안] 기준치 아래로 이만큼(비율) 이상 내려가야 나선다 — 1개 쓸 때마다 나가지 않게.</summary>
        public const float Slack = 0.1f;

        readonly SortedDictionary<string, int> _items = new SortedDictionary<string, int>(StringComparer.Ordinal);
        readonly SortedDictionary<int, int> _sets = new SortedDictionary<int, int>();
        public bool Dirty;
        public int Count => _items.Count + _sets.Count;

        public void Keep(string item, int amount)
        {
            if (string.IsNullOrEmpty(item) || amount <= 0) return;
            _items[item] = amount;
            Dirty = true;
        }

        public void KeepSet(int number, int amount)
        {
            if (number < 1 || amount <= 0) return;
            _sets[number] = amount;
            Dirty = true;
        }

        /// <summary>!채워 끔 [물건]: 물건 하나, 없으면 전부.</summary>
        public void Off(string item = null)
        {
            if (item == null) { if (Count > 0) Dirty = true; _items.Clear(); _sets.Clear(); return; }
            if (_items.Remove(item)) Dirty = true;
        }

        public IEnumerable<KeyValuePair<string, int>> Items => _items;
        public IEnumerable<KeyValuePair<int, int>> Sets => _sets;

        /// <summary>
        /// 지금 채워야 할 것: 물건 → 모자란 수(기준치 − 재고). 세트는 세트 안의 물건마다 기준치(같은 물건이면 큰 기준치).
        /// 기준치의 Slack 비율보다 적게 모자라면 건너뛴다.
        /// </summary>
        public List<Ingredient> Due(Func<string, int> stock, SetMenuBook sets)
        {
            var goal = new Dictionary<string, int>();
            foreach (var kv in _items) goal[kv.Key] = kv.Value;
            if (sets != null)
                foreach (var kv in _sets)
                {
                    var s = sets.Get(kv.Key);
                    if (s == null) continue;
                    foreach (var it in s.Items)
                        if (!goal.TryGetValue(it, out int g) || g < kv.Value) goal[it] = kv.Value;
                }
            var due = new List<Ingredient>();
            var keys = new List<string>(goal.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var item in keys)
            {
                int want = goal[item], have = stock(item);
                int missing = want - have;
                if (missing <= 0 || missing < Math.Max(1, (int)Math.Ceiling(want * Slack))) continue;
                due.Add(new Ingredient(item, missing));
            }
            return due;
        }

        // ---- 저장: "i:물건=수;s:번호=수;…" ----

        public string Save()
        {
            var parts = new List<string>();
            foreach (var kv in _items) parts.Add("i:" + kv.Key + "=" + kv.Value);
            foreach (var kv in _sets) parts.Add("s:" + kv.Key + "=" + kv.Value);
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
                if (part[0] == 'i') b.Keep(key, n);
                else if (part[0] == 's' && int.TryParse(key, NumberStyles.Integer, inv, out int no)) b.KeepSet(no, n);
            }
            b.Dirty = false;
            return b;
        }
    }
}
