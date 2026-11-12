using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 쉬는 일꾼 없게 ①(사용자 11-13): 나중에 쉬게 된 일꾼도 동료가 시킨 채집의 남은 몫이 크면 같이 캔다(GatherJob.Team).
    /// 남은 몫 = 목표 − 모두 넣은 것 − 들고 있는 것. 작으면 가는 사이 끝나 헛걸음이라 안 낌.
    /// </summary>
    public static class TeamJoin
    {
        /// <summary>한 팀(같이 캐는 일꾼) 최대 수.</summary>
        public const int MaxTeam = 3;
        /// <summary>남은 몫이 이보다 적으면 안 낌.</summary>
        public const int MinLeft = 4;

        /// <summary>끼어들 만한지: 남은 몫이 MinLeft 이상이고 목표의 1/3 이상, 팀이 다 차지 않았을 때.</summary>
        public static bool Worth(int remaining, int target, int teamSize)
        {
            if (teamSize >= MaxTeam || target <= 0) return false;
            return remaining >= MinLeft && remaining * 3 >= target;
        }
    }
}
