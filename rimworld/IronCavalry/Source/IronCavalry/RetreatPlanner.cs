using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace IronCavalry
{
    /// <summary>
    /// 자동 후퇴 판단(기획서 A8). 원거리 대상·쫓아와서 간격을 못 벌리는 대상·계산상 손해면 후퇴하지 않는다.
    /// 경로 계산 없이 직선 거리로 판단하고, 실제 경로는 후퇴를 시작할 때 한 번만 요청된다.
    /// </summary>
    public static class RetreatPlanner
    {
        private static readonly float[] CandidateAngles = { 0f, 25f, -25f, 50f, -50f };

        public static bool TryFindRetreatCell(Pawn pawn, Thing target, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            float retreatSpeed = pawn.GetStatValue(StatDefOf.MoveSpeed) * IC_Tuning.ChargeSpeedFactor;
            if (retreatSpeed <= 0.1f) return false;

            if (target is Pawn victim)
            {
                // 원거리 대상에게는 붙어서 계속 때린다
                Verb verb = victim.CurrentEffectiveVerb;
                if (verb != null && !verb.IsMeleeAttack) return false;

                // 나를 쫓는 대상: 제한 시간 안에 최소 간격을 벌릴 수 없으면 후퇴하지 않는다
                if (IsChasing(victim, pawn))
                {
                    float theirSpeed = victim.GetStatValue(StatDefOf.MoveSpeed);
                    float openable = (retreatSpeed - theirSpeed) * (IC_Tuning.RetreatLimitTicks / 60f);
                    if (openable < IC_Tuning.MinStartGap) return false;
                }
            }

            IntVec3 from = pawn.Position;
            IntVec3 targetPos = target.Position;
            Map map = pawn.Map;
            float awayX = from.x - targetPos.x;
            float awayZ = from.z - targetPos.z;
            float len = Mathf.Sqrt(awayX * awayX + awayZ * awayZ);
            if (len < 0.01f)
            {
                awayX = 1f;
                awayZ = 0f;
                len = 1f;
            }
            awayX /= len;
            awayZ /= len;

            float currentGap = from.DistanceTo(targetPos);
            float wantMove = Mathf.Min(IC_Tuning.RetreatSearchRadius, IC_Tuning.ChargeStartGap - currentGap);
            if (wantMove < 1f) return false;

            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MinValue;
            int tried = 0;
            for (int i = 0; i < CandidateAngles.Length && tried < IC_Tuning.RetreatCandidates; i++)
            {
                tried++;
                float rad = CandidateAngles[i] * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                float dx = awayX * cos - awayZ * sin;
                float dz = awayX * sin + awayZ * cos;
                IntVec3 c = new IntVec3(Mathf.RoundToInt(from.x + dx * wantMove), 0, Mathf.RoundToInt(from.z + dz * wantMove));
                if (!c.InBounds(map) || !c.Standable(map)) continue;
                if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                float score = -Mathf.Abs(c.DistanceTo(targetPos) - IC_Tuning.ChargeStartGap);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            if (!best.IsValid) return false;

            // 이득 판정: 돌격 추가 피해 > 공격을 쉬는 동안 못 넣는 평타 피해
            Tool lance = ChargeUtility.LanceTool(pawn);
            if (lance == null || lance.cooldownTime <= 0f) return false;
            float approach = Mathf.Min(best.DistanceTo(targetPos) - 1f, IC_Tuning.MaxApproach);
            if (approach < IC_Tuning.MinApproach) return false;
            float extra = lance.power * (IC_Tuning.DamageFactor(approach) - 1f);
            float idleSeconds = (from.DistanceTo(best) + approach) / retreatSpeed;
            float lost = idleSeconds * (lance.power / lance.cooldownTime);
            if (extra <= lost) return false;

            cell = best;
            return true;
        }

        private static bool IsChasing(Pawn chaser, Pawn pawn)
        {
            if (chaser.Downed || chaser.stances?.stunner?.Stunned == true) return false;
            Job job = chaser.CurJob;
            if (job != null && job.targetA.Thing == pawn) return true;
            return chaser.mindState?.enemyTarget == pawn;
        }
    }
}
