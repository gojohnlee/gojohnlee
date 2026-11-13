using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 포기 전 시도 순서(요청 "시킨 일을 스스로 접지 말기" ⑦, 사용자 11-13 00:15): 일꾼 1 → 일꾼+호위병 → 일꾼 둘+호위병, 셋도 안 되면 "못 하겠어"+까닭.
    /// 아는 적(FoeBook)은 처음부터 맞는 인원(Needed), 셋도 안 되는 계산이면 출발 안 하고 장비 요청(0). 모르는 적은 해 본 인원 다음(Next).
    /// </summary>
    public static class TeamPlan
    {
        public const int Max = 3;

        /// <summary>
        /// 처음부터 맞는 인원(1~Max), 셋도 안 되면 0. helperHits = 부를 차례대로(호위병, 둘째 일꾼) 한 방 피해.
        /// fights(더한 한 방) = 앞장선 일꾼이 그만큼 더한 한 방으로 싸움 판단이 나는지(Guard.SafePrey의 extraHit).
        /// </summary>
        public static int Needed(Func<float, bool> fights, IList<float> helperHits)
        {
            float extra = 0f;
            if (fights(extra)) return 1;
            for (int i = 0; i < helperHits.Count && i + 2 <= Max; i++)
            {
                extra += Math.Max(0f, helperHits[i]);
                if (fights(extra)) return i + 2;
            }
            return 0;
        }

        /// <summary>모르는 적: tried명으로 안 됐으면 다음 인원, Max명도 안 됐으면 0(못 하겠어).</summary>
        public static int Next(int tried) => tried < 1 ? 1 : tried >= Max ? 0 : tried + 1;

        /// <summary>출발 때 처음 맞출 인원: 아는 적(known)은 Needed 그대로(셋도 안 되면 0 = 출발 안 하고 장비 요청), 모르는 적은 Next(tried).</summary>
        public static int Start(bool known, int needed, int tried) => known ? needed : Next(tried);
    }
}
