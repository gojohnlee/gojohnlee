using System.Collections.Generic;
using System.Globalization;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 쓴 재료 다시 캐기(사용자 11-09 "지가 쓴 재료는 다시 캐면"): 쉴 때 만들기가 상자에서 쓴 캘 수 있는 재료 → 아직 안 채운 수.
    /// 일꾼 ZDO에 저장(사용자 11-10 — 다시 켜도 잊지 않음). 월드마다 따로(그 월드 일꾼 ZDO에서 읽음).
    /// </summary>
    public sealed class DebtBook
    {
        readonly Dictionary<string, int> _items = new Dictionary<string, int>();
        public bool Dirty;
        public int Count => _items.Count;
        public IEnumerable<KeyValuePair<string, int>> Items => _items;

        /// <summary>더한 뒤 그 물건의 몫.</summary>
        public int Add(string item, int n)
        {
            if (string.IsNullOrEmpty(item) || item.IndexOf(';') >= 0 || item.IndexOf('=') >= 0 || n <= 0) return Get(item);
            _items.TryGetValue(item, out int d);
            _items[item] = d + n;
            Dirty = true;
            return d + n;
        }

        public void Repaid(string item, int n)
        {
            if (item == null || n <= 0 || !_items.TryGetValue(item, out int d)) return;
            d -= n;
            if (d <= 0) _items.Remove(item); else _items[item] = d;
            Dirty = true;
        }

        public int Get(string item) => item != null && _items.TryGetValue(item, out int d) ? d : 0;

        public string Save()
        {
            var parts = new List<string>();
            foreach (var kv in _items) parts.Add(kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture));
            return string.Join(";", parts.ToArray());
        }

        public static DebtBook Load(string text)
        {
            var b = new DebtBook();
            if (string.IsNullOrEmpty(text)) return b;
            foreach (var part in text.Split(';'))
            {
                int eq = part.LastIndexOf('=');
                if (eq < 1 || !int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;
                b.Add(part.Substring(0, eq), n);
            }
            b.Dirty = false;
            return b;
        }
    }
}
