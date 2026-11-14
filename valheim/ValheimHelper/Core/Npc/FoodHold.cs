using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 명령으로 만든 음식 몫(decisions "갓 만든 음식" 기본안, 회차 673): 플레이어가 시킨 음식은 다른 음식이 있으면 들고 갈 음식에서 뒤로 미룬다.
    /// 몫은 HoldTime 동안만, 상자에 그보다 적게 남으면(플레이어가 가져감) 남은 만큼만 지킨다.
    /// </summary>
    /// <summary>들고 갈 음식 한 종류(게임 Mono에 System.ValueTuple이 없어 튜플 대신).</summary>
    public sealed class FoodKind
    {
        public string Name;
        public float Food;
        public int Have;
        public FoodKind(string name, float food, int have) { Name = name; Food = food; Have = have; }
    }

    public sealed class FoodHold
    {
        /// <summary>게임 하루(1800초) — 그 뒤엔 보통 음식.</summary>
        public const double HoldTime = 1800;

        readonly Dictionary<string, KeyValuePair<int, double>> m_held = new Dictionary<string, KeyValuePair<int, double>>();

        public void Add(string name, int n, double now)
        {
            if (string.IsNullOrEmpty(name) || n <= 0) return;
            int old = Held(name, now);
            m_held[name] = new KeyValuePair<int, double>(old + n, now + HoldTime);
        }

        public int Held(string name, double now)
        {
            if (name == null || !m_held.TryGetValue(name, out var h)) return 0;
            if (now >= h.Value) { m_held.Remove(name); return 0; }
            return h.Key;
        }

        public void Clear() => m_held.Clear();

        /// <summary>
        /// 들고 갈 음식 순서: kinds는 이름·체력·상자에 있는 수. 체력 높은 것부터 한 종류에 하나씩 돌아가며 need개,
        /// 먼저 몫 밖 수로만 돌고, 모자라면 몫에서도.
        /// </summary>
        public static List<string> Pick(IList<FoodKind> kinds, int need, Func<string, int> held)
        {
            var order = new List<FoodKind>(kinds);
            order.Sort((a, b) => b.Food.CompareTo(a.Food));
            var left = new int[order.Count];
            var free = new int[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                int h = Math.Min(Math.Max(0, held(order[i].Name)), order[i].Have);
                free[i] = order[i].Have - h;
                left[i] = h;
            }
            var res = new List<string>();
            foreach (var pool in new[] { free, left })
            {
                bool any = true;
                while (res.Count < need && any)
                {
                    any = false;
                    for (int i = 0; i < order.Count && res.Count < need; i++)
                        if (pool[i] > 0) { pool[i]--; res.Add(order[i].Name); any = true; }
                }
            }
            return res;
        }
    }
}
