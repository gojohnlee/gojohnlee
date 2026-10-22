using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>재료 하나: 프리팹 이름과 포탈 하나에 드는 수.</summary>
    public struct PortalCost
    {
        public string Item;
        public int Amount;
        public PortalCost(string item, int amount) { Item = item; Amount = amount; }
    }

    public enum Departing { None, PlaceBaseAndCarry, Carry }

    /// <summary>
    /// 일꾼 포탈 규칙(사용자 결정 2026-10-23, 건축 예외로 포탈만): 이름표 "일꾼-&lt;일터&gt;", 최대 3쌍,
    /// [기본안] 거점에서 300m 넘는 일터에 두 번째 갈 때 짝으로 놓고, 마지막으로 쓴 뒤 게임 날짜로 IdleDays일 지나면 철거.
    /// </summary>
    public static class WorkerPortal
    {
        public const string TagPrefix = "일꾼-";
        /// <summary>이름표 최대 길이(바닐라 입력창은 10자지만 ZDO 문자열은 제한 없음 — 읽기 좋게 자름).</summary>
        public const int MaxTagLength = 20;
        public const int MaxPairs = 3;
        public const float FarFromBase = 300f;
        public const int IdleDays = 5;

        /// <summary>일터 이름 → 이름표. 빈 이름이면 "일꾼-일터".</summary>
        public static string Tag(string site)
        {
            string s = string.IsNullOrWhiteSpace(site) ? "일터" : site.Trim();
            string tag = TagPrefix + s;
            return tag.Length > MaxTagLength ? tag.Substring(0, MaxTagLength) : tag;
        }

        public static bool IsWorkerTag(string tag) => tag != null && tag.StartsWith(TagPrefix, StringComparison.Ordinal);

        /// <summary>일꾼 포탈 이름표들 → 쌍 수(같은 이름표 = 한 쌍).</summary>
        public static int Pairs(IEnumerable<string> tags)
        {
            var set = new HashSet<string>();
            foreach (var t in tags) if (IsWorkerTag(t)) set.Add(t);
            return set.Count;
        }

        public static bool CanAddPair(int pairs) => pairs < MaxPairs;

        /// <summary>[기본안] 놓을까: 거점에서 300m 넘고, 그 일터에 두 번째(이상) 가고, 이어진 포탈로 이미 갈 수 없을 때.</summary>
        public static bool ShouldPlace(double siteFromBase, int visits, bool linkedAlready) =>
            siteFromBase > FarFromBase && visits >= 2 && !linkedAlready;

        /// <summary>
        /// 거점에서 먼 일터로 떠날 때 할 일(R2 나, [기본안]). 일터 쪽 구역은 거점에서 안 불러와져 있으니 거점 쪽만 지금 놓고 한 몫은 들고 가서 도착해 놓는다.
        /// mineWithTag = 그 이름표의 일꾼 포탈 수, carrying = 한 몫을 이미 들고 있나.
        /// </summary>
        public static Departing OnDepart(double siteFromBase, int visitsBefore, bool linkedAlready, int mineWithTag, bool carrying, int pairs)
        {
            if (mineWithTag >= 2 || carrying) return Departing.None;
            if (mineWithTag == 1) return Departing.Carry; // 거점 쪽만 있음(지난번에 일터 쪽을 못 놓음): 한 몫만 들고 감
            if (!ShouldPlace(siteFromBase, visitsBefore + 1, linkedAlready) || !CanAddPair(pairs)) return Departing.None;
            return Departing.PlaceBaseAndCarry;
        }

        /// <summary>오래 안 씀: 마지막으로 쓴 게임 날짜에서 IdleDays일 지남.</summary>
        public static bool IsIdle(long lastUsedDay, long today) => today - lastUsedDay >= IdleDays;

        /// <summary>portals개를 놓는 데 모자란 것(프리팹 → 모자란 수). 비면 다 있음.</summary>
        public static Dictionary<string, int> Missing(IList<PortalCost> cost, Func<string, int> have, int portals)
        {
            var miss = new Dictionary<string, int>();
            foreach (var c in Need(cost, portals))
            {
                int short_ = c.Value - have(c.Key);
                if (short_ > 0) miss[c.Key] = short_;
            }
            return miss;
        }

        /// <summary>portals개에 드는 재료 합(같은 재료가 두 줄이면 더함).</summary>
        public static Dictionary<string, int> Need(IList<PortalCost> cost, int portals)
        {
            var need = new Dictionary<string, int>();
            foreach (var c in cost)
            {
                if (string.IsNullOrEmpty(c.Item) || c.Amount <= 0) continue;
                need.TryGetValue(c.Item, out int n);
                need[c.Item] = n + c.Amount * portals;
            }
            return need;
        }
    }
}
