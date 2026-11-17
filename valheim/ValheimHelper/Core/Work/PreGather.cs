using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 미리 채집(사용자 11-18, decisions "쉬는 일꾼 순서" ③): 쉬는 일꾼 맨 끝에 최근 꺼내 쓴 캘 수 있는 물건을 물건당 Target개 안팎까지 채운다.
    /// 꺼내 씀 = 거점 상자 + 일꾼 가방 합이 줄어든 것(옮기기·정리는 합이 그대로, 플레이어가 가져가거나 만들기에 쓰면 줄어듦).
    /// 메모리에만 둠 — 다시 켜면 처음 본 값이 기준.
    /// </summary>
    public sealed class PreGatherBook
    {
        /// <summary>채울 목표 수(20~30 안팎).</summary>
        public const int Target = 25;
        /// <summary>재고가 이보다 적을 때만 감.</summary>
        public const int Low = 20;
        /// <summary>최근 = 이 초 안에 줄어든 것.</summary>
        public const double Window = 1800;
        /// <summary>창 안에 이만큼 넘게 쓴 것만(한두 개 꺼낸 건 안 셈).</summary>
        public const int MinUsed = 5;

        sealed class Use { public double Last; public int N; }
        Dictionary<string, int> _last;
        readonly Dictionary<string, Use> _used = new Dictionary<string, Use>();

        /// <summary>기준·쓴 기록을 지움(집안일이 꺼진 동안 — 시험 준비·정리를 쓴 것으로 안 셈).</summary>
        public void Reset() { _last = null; _used.Clear(); }

        /// <summary>지금 합(물건 → 수)을 보고 지난 번보다 줄어든 것을 쓴 것으로 적는다. 처음은 기준만.</summary>
        public void Observe(IDictionary<string, int> totals, double now)
        {
            if (_last != null)
                foreach (var kv in _last)
                {
                    totals.TryGetValue(kv.Key, out int n);
                    int d = kv.Value - n;
                    if (d <= 0) continue;
                    if (!_used.TryGetValue(kv.Key, out Use u) || now - u.Last > Window) _used[kv.Key] = u = new Use();
                    u.Last = now;
                    u.N += d;
                }
            _last = new Dictionary<string, int>(totals);
        }

        /// <summary>
        /// 채울 것(최근 쓴 순): 창 안에 MinUsed 넘게 쓰고 재고 Low 밑이며 skip이 아닌 것, 수 = Target − 재고.
        /// </summary>
        public List<Ingredient> Wants(Func<string, int> stock, Func<string, bool> skip, double now)
        {
            var list = new List<KeyValuePair<string, Use>>();
            foreach (var kv in _used)
                if (now - kv.Value.Last <= Window && kv.Value.N >= MinUsed) list.Add(kv);
            list.Sort((a, b) => b.Value.Last.CompareTo(a.Value.Last));
            var due = new List<Ingredient>();
            foreach (var kv in list)
            {
                if (skip != null && skip(kv.Key)) continue;
                int s = stock(kv.Key);
                if (s < Low) due.Add(new Ingredient(kv.Key, Target - s));
            }
            return due;
        }

        /// <summary>상자 칸이 넉넉한지: 가져올 묶음 수 + 2칸 여유.</summary>
        public static bool Room(int emptySlots, int amount, int maxStack) => emptySlots >= (amount + Math.Max(1, maxStack) - 1) / Math.Max(1, maxStack) + 2;
    }
}
