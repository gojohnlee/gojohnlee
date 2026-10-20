using UnityEngine;

namespace IronCavalry
{
    /// <summary>밸런스 값 모음. 근거와 의미는 docs/ironcavalry/spec-v2.txt A5·A6·A8.</summary>
    public static class IC_Tuning
    {
        // 거리(칸)
        public const float MinApproach = 3f;      // 강화가 붙는 최소 유효 접근량
        public const float MaxApproach = 8f;      // 강화 상한
        public const float MinStartGap = 4f;      // 이보다 가까우면 돌격하지 않음
        public const float ChargeStartGap = 9f;   // 이 간격 안으로 들어오면 돌격 시작
        public const float TeleportStep = 1.5f;   // 한 번에 이보다 크게 움직이면 접근량 계산 제외

        // 속도 (GJL_IC_ChargeSpeed 헤디프의 MoveSpeed 배율과 같아야 함)
        public const float ChargeSpeedFactor = 2.1f;

        // 시간(틱)
        public const int CooldownTicks = 900;
        public const int ChargeLimitTicks = 180;
        public const int RetreatLimitTicks = 180;
        public const int AutoCheckInterval = 15;
        public const int RetreatRecheckTicks = 120;
        public const int StrayHediffCheckInterval = 60;

        // 자동 후퇴
        public const float RetreatSearchRadius = 8f;
        public const int RetreatCandidates = 5;

        // 충돌 스턴
        public const int StunMinTicks = 90;           // 3칸 1.5초
        public const int StunMaxTicks = 180;          // 8칸 3.0초
        public const int StunImmunityTicks = 180;     // 스턴 종료 후 재스턴 금지
        public const float StunHalfIfBiggerThan = 1f; // 대상 체구 > 철기병 체구 × 1 → 절반
        public const float StunNoneIfAtLeast = 2f;    // 대상 체구 ≥ 철기병 체구 × 2 → 없음

        /// <summary>유효 접근량 → 첫 공격 피해 배율. 3칸 1.5배, 1칸당 +0.2, 8칸 2.5배 상한.</summary>
        public static float DamageFactor(float approach)
        {
            if (approach < MinApproach) return 1f;
            return 1.5f + 0.2f * (Mathf.Min(approach, MaxApproach) - MinApproach);
        }

        /// <summary>유효 접근량 → 스턴 틱. 3칸 90틱 → 8칸 180틱.</summary>
        public static int StunTicks(float approach)
        {
            if (approach < MinApproach) return 0;
            float t = (Mathf.Min(approach, MaxApproach) - MinApproach) / (MaxApproach - MinApproach);
            return Mathf.RoundToInt(StunMinTicks + (StunMaxTicks - StunMinTicks) * t);
        }
    }
}
