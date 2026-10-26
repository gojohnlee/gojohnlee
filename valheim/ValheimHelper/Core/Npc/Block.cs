using System;

namespace ValheimHelper.Core.Npc
{
    /// <summary>방패 하나의 막기 수치(전투 보강 가). 게임 값을 옮겨 담는다.</summary>
    public struct Shield
    {
        /// <summary>막기 힘 = 바닐라 ItemData.GetBlockPower(스킬 반영).</summary>
        public float Power;
        /// <summary>비틀 한계 = 최대 체력 × m_staggerDamageFactor. 0 이하면 안 비틀림(막기가 안 깨짐).</summary>
        public float StaggerLimit;
    }

    /// <summary>
    /// 방패 막기 셈(전투 보강 가, 바닐라 Humanoid.BlockAttack·Character.RPC_Damage 순서 — codemap "확인된 게임 사실"):
    /// 앞에서 온 막을 수 있는 한 방은 먼저 막기 힘을 갑옷처럼 매기고(ApplyArmor), 그 뒤 저항·몸 갑옷이 매겨진다.
    /// 막고 남은 비틀 피해가 쌓여 비틀 한계를 넘으면 그 한 방은 막기 실패(통째로 들어옴). 스태미나는 따지지 않는다(일꾼은 스태미나 없음 — 사용자 10-26).
    /// </summary>
    public static class Block
    {
        /// <summary>바닐라 HitData.DamageTypes.ApplyArmor(dmg, ac): 갑옷(또는 막기 힘)이 한 방의 반보다 작으면 그만큼 빼고, 크면 dmg²/(4ac).</summary>
        public static float Armor(float dmg, float ac)
        {
            if (ac <= 0f || dmg <= 0f) return Math.Max(0f, dmg);
            float result = Math.Min(1f, Math.Max(0f, dmg / (ac * 4f))) * dmg;
            if (ac < dmg / 2f) result = dmg - ac;
            return result;
        }

        /// <summary>
        /// 막은 한 방이 비틀 한계의 이 몫 아래여야 막기로 친다. 비틀 피해는 한계의 1/5씩 매초 줄고(UpdateStagger) 적은 대략 2.5초에 한 번 치니
        /// 한 방이 한계의 반 아래면 쌓이지 않는다. [기본안 10-26]
        /// </summary>
        public const float StaggerShare = 0.5f;

        /// <summary>적이 이보다 많으면 막기를 셈에 넣지 않는다(앞에서 오는 것만 막히고 비틀 피해가 겹쳐 쌓임). [기본안 10-26]</summary>
        public const int MaxFoes = 2;

        /// <summary>
        /// 막느라 덜 휘두르는 몫: 적이 휘두르는 동안은 방패를 들고 치지 않는다(Guard.UpdateFight) → 같은 싸움이 이만큼 길어진다고 보고
        /// 막고 남은 피해에 곱한다(막기 없는 셈보다 나빠지면 막기 없는 셈을 쓴다). [기본안 10-26]
        /// </summary>
        public const float SlowFactor = 1.3f;

        /// <summary>막기로 칠 수 있는 한 방인가(막고 남은 피해가 비틀 한계 몫 아래).</summary>
        public static bool Holds(float hit, Shield s)
        {
            if (s.Power <= 0f) return false;
            if (s.StaggerLimit <= 0f) return true;
            return Armor(hit, s.Power) < s.StaggerLimit * StaggerShare;
        }

        /// <summary>
        /// 실제 한 방(난이도 배율 반영, 갑옷 전) → 몸에 들어오는 피해. shield가 없거나 적이 MaxFoes보다 많으면 갑옷만.
        /// 막을 수 있으면 막기 → 갑옷, 덜 휘두르는 몫(SlowFactor)을 곱해도 갑옷만보다 클 땐 갑옷만.
        /// </summary>
        public static float Taken(float hit, float armor, Shield? shield, int foes)
        {
            float plain = Armor(hit, armor);
            if (shield == null || foes > MaxFoes || !Holds(hit, shield.Value)) return plain;
            float blocked = Armor(Armor(hit, shield.Value.Power), armor) * SlowFactor;
            return Math.Min(plain, blocked);
        }
    }
}
