using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHelper.Core.Work
{
    public sealed class SetMenu
    {
        public int Number;
        /// <summary>붙인 이름("늪 세트"). 없으면 null.</summary>
        public string Name;
        /// <summary>물건 프리팹 이름들(음식 3개 + 미드 등).</summary>
        public readonly List<string> Items = new List<string>();
        /// <summary>만든 사람.</summary>
        public string By;
    }

    /// <summary>
    /// 세트메뉴 (구상서 6-4장): 최대 10개, 모두 같이 쓴다. "세트 1번 20개" = 세트 안의 물건마다 20개(6-1 제작 계획에 그대로 넣음).
    /// 저장은 말(Claude 해석) 또는 "지금 먹은 거"(플레이어 게임이 먹는 음식 3개를 보냄)로 들어온다.
    /// </summary>
    public sealed class SetMenuBook
    {
        public const int Max = 10;
        /// <summary>한 세트에 넣을 수 있는 물건 수 [기본안: 음식 3 + 미드 몇 개].</summary>
        public const int MaxItems = 8;

        readonly SortedDictionary<int, SetMenu> _sets = new SortedDictionary<int, SetMenu>();
        public bool Dirty;
        public int Count => _sets.Count;
        public IEnumerable<SetMenu> All => _sets.Values;

        public SetMenu Get(int number) => _sets.TryGetValue(number, out var s) ? s : null;

        /// <summary>다음 빈 번호. 다 찼으면 0.</summary>
        public int NextFree()
        {
            for (int i = 1; i <= Max; i++) if (!_sets.ContainsKey(i)) return i;
            return 0;
        }

        /// <summary>저장(같은 번호면 덮어씀). number 0이면 다음 빈 번호. 저장한 번호, 못 했으면 0.</summary>
        public int Save(int number, IEnumerable<string> items, string name, string by)
        {
            if (number == 0) number = NextFree();
            if (number < 1 || number > Max) return 0;
            var s = new SetMenu { Number = number, Name = Clean(name), By = Clean(by) };
            foreach (var it in items)
                if (!string.IsNullOrEmpty(it) && !s.Items.Contains(it) && s.Items.Count < MaxItems) s.Items.Add(Clean(it));
            if (s.Items.Count == 0) return 0;
            _sets[number] = s;
            Dirty = true;
            return number;
        }

        public bool Remove(int number)
        {
            if (!_sets.Remove(number)) return false;
            Dirty = true;
            return true;
        }

        /// <summary>"1번에서 소시지 빼고 스태미나 미드 넣어".</summary>
        public bool Edit(int number, IEnumerable<string> remove, IEnumerable<string> add)
        {
            if (!_sets.TryGetValue(number, out var s)) return false;
            bool changed = false;
            if (remove != null) foreach (var it in remove) changed |= s.Items.Remove(it);
            if (add != null)
                foreach (var it in add)
                    if (!string.IsNullOrEmpty(it) && !s.Items.Contains(it) && s.Items.Count < MaxItems) { s.Items.Add(Clean(it)); changed = true; }
            if (changed) Dirty = true;
            return changed;
        }

        /// <summary>"세트 n번 amount개씩" → 제작 계획 목표. 범위 밖 물건은 계획이 걸러 알린다(6-4).</summary>
        public List<Ingredient> Targets(int number, int amount)
        {
            var list = new List<Ingredient>();
            if (amount <= 0 || !_sets.TryGetValue(number, out var s)) return list;
            foreach (var it in s.Items) list.Add(new Ingredient(it, amount));
            return list;
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? null : s.Replace("|", "").Replace(";", "").Replace("\n", "").Trim();

        // ---- 저장: 한 줄에 한 세트 "번호|이름|만든 사람|물건;물건;…" ----

        public string Save()
        {
            var sb = new StringBuilder();
            foreach (var s in _sets.Values)
                sb.Append(s.Number).Append('|').Append(s.Name ?? "").Append('|').Append(s.By ?? "").Append('|')
                  .Append(string.Join(";", s.Items.ToArray())).Append('\n');
            return sb.ToString();
        }

        public static SetMenuBook Load(string text)
        {
            var b = new SetMenuBook();
            if (string.IsNullOrEmpty(text)) return b;
            foreach (var line in text.Split('\n'))
            {
                var p = line.Split('|');
                if (p.Length != 4 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int no)) continue;
                b.Save(no, p[3].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), p[1].Length > 0 ? p[1] : null, p[2].Length > 0 ? p[2] : null);
            }
            b.Dirty = false;
            return b;
        }
    }
}
