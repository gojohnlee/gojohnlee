using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 화살 아끼기 (사용자 11-13 15:20, decisions 화살 아끼기): 챙길 때 나무 화살(깃털 없이 나무 8개)을 먼저,
    /// 깃털을 쓰는 화살·볼트(재료에 깃털 2)는 상자 깃털이 FeatherFloor(50)보다 많을 때만 챙긴다.
    /// 금속 화살은 일꾼이 만들지 않는다. 게임 값은 vh_arrowtest(회차 552)에서 확인.
    /// </summary>
    public static class AmmoRule
    {
        /// <summary>이 개수 이하의 깃털은 깃털 화살에 쓰지 않는다.</summary>
        public const int FeatherFloor = 50;

        /// <summary>깃털 화살을 챙겨도 되는지: 상자 깃털이 FeatherFloor보다 많아야 한다.</summary>
        public static bool MayTakeFeatherArrows(int chestFeathers) => chestFeathers > FeatherFloor;

        /// <summary>
        /// 가장 싼 것부터 줄 세움(볼트·깃털 화살 — 사용자 11-13 15:50). 값이 같으면 상자 순서를 그대로 둔다(OrderBy는 안정 정렬).
        /// </summary>
        public static List<T> CheapestFirst<T>(IEnumerable<T> items, Func<T, int> cost) => items.OrderBy(cost).ToList();
    }
}
