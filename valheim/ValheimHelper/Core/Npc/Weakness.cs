using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 적 약점·저항(전투 보강 나, 바닐라 HitData.ApplyResistance — codemap "확인된 게임 사실"): 피해 종류마다 적의 배율을 곱한다.
    /// 종류 순서는 Kinds(둔기·베기·찌르기·불·냉기·번개·독·영). 참 피해(m_damage)는 배율을 안 받는다.
    /// 벌목(chop)·채굴(pickaxe) 피해는 셈하지 않는다(Gear.WeaponScore와 같이 — 생물 배율 기본값을 몰라 넉넉히).
    /// </summary>
    public static class Weakness
    {
        public static readonly string[] Kinds = { "Blunt", "Slash", "Pierce", "Fire", "Frost", "Lightning", "Poison", "Spirit" };
        public const int Blunt = 0;

        /// <summary>
        /// 바닐라 HitData.DamageModifier 값(정수) → 배율. Normal 0=1, Resistant 1=0.5, Weak 2=1.5, Immune 3=0, Ignore 4=0,
        /// VeryResistant 5=0.25, VeryWeak 6=2, SlightlyResistant 7=0.75, SlightlyWeak 8=1.25. 모르는 값은 1.
        /// </summary>
        public static float Mult(int modifier)
        {
            switch (modifier)
            {
                case 1: return 0.5f;
                case 2: return 1.5f;
                case 3: case 4: return 0f;
                case 5: return 0.25f;
                case 6: return 2f;
                case 7: return 0.75f;
                case 8: return 1.25f;
                default: return 1f;
            }
        }

        /// <summary>한 방(참 피해 + 종류별 피해)이 그 적의 배율을 거친 크기. mults가 null이면 배율 없이.</summary>
        public static float Hit(float trueDamage, float[] damages, float[] mults)
        {
            float sum = Math.Max(0f, trueDamage);
            for (int i = 0; i < damages.Length; i++)
                sum += Math.Max(0f, damages[i]) * (mults != null && i < mults.Length ? mults[i] : 1f);
            return sum;
        }

        /// <summary>약점·저항을 셈한 한 방이 가장 큰 것의 순서(같으면 앞의 것). 비었으면 −1.</summary>
        public static int Pick(IList<float> hits)
        {
            int best = -1;
            for (int i = 0; i < hits.Count; i++)
                if (best < 0 || hits[i] > hits[best]) best = i;
            return best;
        }

        /// <summary>
        /// 위험 판단의 적 체력 보정: 판단식은 배율 없는 내 한 방(raw)으로 세니, 그 적에게 실제로 들어가는 한 방(effective)과의 비만큼 체력을 늘리고 줄인다.
        /// 하나도 안 들어가면(면역) MaxScale배 — 못 이기는 적으로 본다.
        /// </summary>
        public static float HealthScale(float raw, float effective)
        {
            if (raw <= 0f) return 1f;
            if (effective <= 0f) return MaxScale;
            return Math.Min(MaxScale, Math.Max(MinScale, raw / effective));
        }

        public const float MaxScale = 10f, MinScale = 0.25f;
    }
}
