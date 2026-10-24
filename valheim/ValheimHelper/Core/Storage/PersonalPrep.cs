using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimHelper.Core.Storage
{
    /// <summary>
    /// 내 개인 상자 준비(상자 기능 ③, 사용자 결정 2026-10-23): "!준비 미드 5"(한 번) · "!채워 개인 미드 5"(유지).
    /// 사람(플레이어 ID)마다 물건 → 개인 상자에 둘 수. 일꾼은 개인 상자에 넣기만 한다(빼지 않음).
    /// 한 번 준비는 채우면(또는 더 넣을 게 없으면) 지운다. 유지는 !채워처럼 기준치의 Slack 비율 넘게 모자랄 때만 나선다.
    /// </summary>
    public sealed class PersonalBook
    {
        public sealed class Entry
        {
            public long Owner;
            public string Item;
            public int Amount;
            public bool Once;
        }

        public struct Need
        {
            public Entry Entry;
            public int Missing;
        }

        readonly List<Entry> _list = new List<Entry>();
        public bool Dirty;
        public int Count => _list.Count;
        public IEnumerable<Entry> Entries => _list;

        public Entry Find(long owner, string item) => _list.Find(e => e.Owner == owner && e.Item == item);

        /// <summary>같은 사람·같은 물건이면 바꾼다.</summary>
        public void Set(long owner, string item, int amount, bool once)
        {
            if (owner == 0 || string.IsNullOrEmpty(item) || amount <= 0) return;
            Entry e = Find(owner, item);
            if (e == null) { e = new Entry { Owner = owner, Item = item }; _list.Add(e); }
            e.Amount = amount;
            e.Once = once;
            Dirty = true;
        }

        /// <summary>그 사람의 물건 하나, item이 null이면 그 사람 것 전부. 지운 수.</summary>
        public int Off(long owner, string item = null)
        {
            int n = _list.RemoveAll(e => e.Owner == owner && (item == null || e.Item == item));
            if (n > 0) Dirty = true;
            return n;
        }

        public void Done(Entry e)
        {
            if (_list.Remove(e)) Dirty = true;
        }

        /// <summary>
        /// 지금 넣어야 할 것(넣은 순서대로): stock(주인, 물건) = 그 사람 개인 상자에 든 수.
        /// 다 찬 한 번 준비는 여기서 지운다. 유지는 기준치의 RefillBook.Slack 비율보다 적게 모자라면 건너뛴다.
        /// </summary>
        public List<Need> Due(Func<long, string, int> stock)
        {
            var due = new List<Need>();
            foreach (var e in new List<Entry>(_list))
            {
                int missing = e.Amount - stock(e.Owner, e.Item);
                if (missing <= 0) { if (e.Once) Done(e); continue; }
                if (!e.Once && missing < Math.Max(1, (int)Math.Ceiling(e.Amount * Work.RefillBook.Slack))) continue;
                due.Add(new Need { Entry = e, Missing = missing });
            }
            return due;
        }

        // ---- 저장: "주인|물건|수|o 또는 k;…" ----

        public string Save()
        {
            var parts = new List<string>();
            var inv = CultureInfo.InvariantCulture;
            foreach (var e in _list) parts.Add(e.Owner.ToString(inv) + "|" + e.Item + "|" + e.Amount.ToString(inv) + "|" + (e.Once ? "o" : "k"));
            return string.Join(";", parts.ToArray());
        }

        public static PersonalBook Load(string text)
        {
            var b = new PersonalBook();
            if (string.IsNullOrEmpty(text)) return b;
            var inv = CultureInfo.InvariantCulture;
            foreach (var part in text.Split(';'))
            {
                string[] f = part.Split('|');
                if (f.Length != 4 || !long.TryParse(f[0], NumberStyles.Integer, inv, out long owner)
                    || !int.TryParse(f[2], NumberStyles.Integer, inv, out int n)) continue;
                b.Set(owner, f[1], n, f[3] == "o");
            }
            b.Dirty = false;
            return b;
        }
    }
}
