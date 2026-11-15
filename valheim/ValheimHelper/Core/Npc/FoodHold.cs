using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 명령으로 만든 음식 몫(decisions "갓 만든 음식" 기본안, 회차 673·702): 플레이어가 시킨 음식은 몫 동안 일꾼·호위병이 먹지도 들고 가지도 않는다
    /// (회차 702 vh_finaltest: "다른 음식 없으면 몫에서도"였더니 구운 고기 4개를 호위병이 먹고 사냥꾼이 들고 가 상자에 1개).
    /// 몫은 주문마다 HoldTime 동안만(사용자 11-16: 같은 음식을 또 시켜도 옛 몫 만료는 그대로), 시킨 수까지만 —
    /// 거점 상자에 그보다 적게 남으면(플레이어가 가져감) Trim이 몫을 줄여, 나중에 더 생긴 것은 보통 음식.
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
        /// <summary>게임 사흘(하루 1800초) — 그 뒤엔 보통 음식.</summary>
        public const double HoldTime = 5400;

        /// <summary>음식 이름 → 주문들(수, 만료), 오래된 것부터.</summary>
        readonly Dictionary<string, List<KeyValuePair<int, double>>> m_held = new Dictionary<string, List<KeyValuePair<int, double>>>();

        public void Add(string name, int n, double now)
        {
            if (string.IsNullOrEmpty(name) || n <= 0) return;
            Held(name, now);
            if (!m_held.TryGetValue(name, out var l)) m_held[name] = l = new List<KeyValuePair<int, double>>();
            l.Add(new KeyValuePair<int, double>(n, now + HoldTime));
        }

        public int Held(string name, double now)
        {
            if (name == null || !m_held.TryGetValue(name, out var l)) return 0;
            l.RemoveAll(o => now >= o.Value);
            if (l.Count == 0) { m_held.Remove(name); return 0; }
            int n = 0;
            foreach (var o in l) n += o.Key;
            return n;
        }

        /// <summary>거점 상자 합 have가 몫보다 적으면(플레이어가 가져감) 오래된 주문부터 몫을 have까지 줄인다.</summary>
        public void Trim(string name, int have, double now)
        {
            int over = Held(name, now) - Math.Max(0, have);
            if (over <= 0) return;
            var l = m_held[name];
            while (over > 0 && l.Count > 0)
            {
                int cut = Math.Min(over, l[0].Key);
                over -= cut;
                if (cut == l[0].Key) l.RemoveAt(0);
                else l[0] = new KeyValuePair<int, double>(l[0].Key - cut, l[0].Value);
            }
            if (l.Count == 0) m_held.Remove(name);
        }

        /// <summary>거점 음식 상자의 음식별 합 → 몫(Trim 뒤)을 뺀 일꾼 음식 합(사용자 11-16: 쉴 때 음식 셈이 몫까지 넉넉으로 보면 일꾼이 굶음).</summary>
        public int FreeTotal(IDictionary<string, int> haves, double now)
        {
            int n = 0;
            foreach (var kv in haves) { Trim(kv.Key, kv.Value, now); n += Free(kv.Key, kv.Value, now); }
            return n;
        }

        public void Clear() => m_held.Clear();

        /// <summary>
        /// 들고 갈 음식 순서: kinds는 이름·체력·상자에 있는 수. 체력 높은 것부터 한 종류에 하나씩 돌아가며 need개,
        /// 몫 밖 수로만(모자라도 몫은 안 씀).
        /// </summary>
        public static List<string> Pick(IList<FoodKind> kinds, int need, Func<string, int> held)
        {
            var order = new List<FoodKind>(kinds);
            order.Sort((a, b) => b.Food.CompareTo(a.Food));
            var free = new int[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                int h = Math.Min(Math.Max(0, held(order[i].Name)), order[i].Have);
                free[i] = order[i].Have - h;
            }
            var res = new List<string>();
            bool any = true;
            while (res.Count < need && any)
            {
                any = false;
                for (int i = 0; i < order.Count && res.Count < need; i++)
                    if (free[i] > 0) { free[i]--; res.Add(order[i].Name); any = true; }
            }
            return res;
        }

        /// <summary>상자에 have개 있을 때 일꾼이 먹거나 들고 가도 되는 수(몫 밖).</summary>
        public int Free(string name, int have, double now) => Math.Max(0, have - Held(name, now));
    }
}
