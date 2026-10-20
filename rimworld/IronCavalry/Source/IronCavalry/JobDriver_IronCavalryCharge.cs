using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace IronCavalry
{
    /// <summary>
    /// 돌격 잡. 단계: (후퇴) → 접근 → 돌격 → 첫 창 공격 → 일반 근접전으로 넘김.
    /// targetA = 공격 대상, targetB = 자동 후퇴 목적지(없으면 바로 접근).
    /// 기획서 A6·A7·A8.
    /// </summary>
    public class JobDriver_IronCavalryCharge : JobDriver
    {
        private enum Phase : byte
        {
            Approach,
            Retreat,
            Charge
        }

        private Phase phase = Phase.Approach;
        private int phaseStartTick;
        private float netApproach;
        private IntVec3 lastPos = IntVec3.Invalid;
        private bool cooldownPaid;

        // 불러오기 직후에는 진행 중이던 돌격·후퇴를 정리한다(기획서 A9). 저장하지 않음.
        private bool cleanupAfterLoad;
        private CompIronCavalry compCached;

        private Thing Target => job.targetA.Thing;

        private CompIronCavalry Comp => compCached ?? (compCached = pawn.GetComp<CompIronCavalry>());

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref phase, "GJL_IC_phase", Phase.Approach);
            Scribe_Values.Look(ref phaseStartTick, "GJL_IC_phaseStart");
            Scribe_Values.Look(ref netApproach, "GJL_IC_netApproach");
            Scribe_Values.Look(ref lastPos, "GJL_IC_lastPos", IntVec3.Invalid);
            Scribe_Values.Look(ref cooldownPaid, "GJL_IC_cooldownPaid");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && phase != Phase.Approach)
            {
                cleanupAfterLoad = true;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Target is Pawn p && p.Downed && !job.killIncappedTarget);
            this.FailOn(() => Comp == null);
            AddFinishAction(_ => ChargeUtility.RemoveSpeed(pawn));

            if (job.targetB.IsValid)
            {
                Toil retreat = ToilMaker.MakeToil("GJL_IC_Retreat");
                retreat.initAction = () =>
                {
                    phase = Phase.Retreat;
                    phaseStartTick = Find.TickManager.TicksGame;
                    // 후퇴→돌격 한 묶음에 재사용 대기 1회, 후퇴 시작 시 소모
                    Comp.StartCooldown();
                    cooldownPaid = true;
                    ChargeUtility.AddSpeed(pawn);
                    pawn.pather.StartPath(job.targetB, PathEndMode.OnCell);
                };
                retreat.AddPreTickAction(() =>
                {
                    if (HandleLoadCleanup()) return;
                    if (Find.TickManager.TicksGame - phaseStartTick > IC_Tuning.RetreatLimitTicks) ReadyForNextToil();
                });
                retreat.AddFinishAction(() => ChargeUtility.RemoveSpeed(pawn));
                retreat.defaultCompleteMode = ToilCompleteMode.PatherArrival;
                yield return retreat;
            }

            Toil attack = Toils_Combat.FollowAndMeleeAttack(TargetIndex.A, StrikeOrHandOff);
            attack.AddPreInitAction(() =>
            {
                phase = Phase.Approach;
                lastPos = IntVec3.Invalid;
            });
            attack.AddPreTickAction(PhaseTick);
            yield return attack;
        }

        public override void Notify_PatherFailed()
        {
            // 후퇴 경로가 막히면 그 자리에서 돌격 판단으로 넘어간다
            if (phase == Phase.Retreat)
            {
                ReadyForNextToil();
                return;
            }
            base.Notify_PatherFailed();
        }

        private bool HandleLoadCleanup()
        {
            if (!cleanupAfterLoad) return false;
            cleanupAfterLoad = false;
            ChargeUtility.RemoveSpeed(pawn);
            EndJobWith(JobCondition.InterruptForced);
            return true;
        }

        private void PhaseTick()
        {
            if (HandleLoadCleanup()) return;
            Thing target = Target;
            if (target == null) return;
            IntVec3 pos = pawn.Position;

            if (phase == Phase.Approach)
            {
                if (pos == lastPos) return;
                lastPos = pos;
                float gap = pos.DistanceTo(target.Position);
                if (gap > IC_Tuning.ChargeStartGap) return;
                if (gap < IC_Tuning.MinStartGap)
                {
                    // 이미 붙어 있으면 돌격하지 않고 일반 근접 공격(재사용 대기 소모 없음)
                    HandOffToMelee();
                }
                else if (cooldownPaid || Comp.CooldownReady)
                {
                    BeginCharge();
                }
                return;
            }

            if (phase == Phase.Charge)
            {
                if (Find.TickManager.TicksGame - phaseStartTick > IC_Tuning.ChargeLimitTicks)
                {
                    HandOffToMelee();
                    return;
                }
                if (pos == lastPos) return;
                if ((pos - lastPos).LengthHorizontal <= IC_Tuning.TeleportStep)
                {
                    // 이동 전후를 같은 대상 위치로 비교: 대상 자신의 이동은 빼고, 멀어지면 차감(순접근량)
                    IntVec3 targetPos = target.Position;
                    netApproach += lastPos.DistanceTo(targetPos) - pos.DistanceTo(targetPos);
                }
                lastPos = pos;
            }
        }

        private void BeginCharge()
        {
            phase = Phase.Charge;
            phaseStartTick = Find.TickManager.TicksGame;
            netApproach = 0f;
            lastPos = pawn.Position;
            if (!cooldownPaid)
            {
                Comp.StartCooldown();
                cooldownPaid = true;
            }
            ChargeUtility.AddSpeed(pawn);
        }

        /// <summary>근접 거리에서 매 틱 호출됨(바닐라 FollowAndMeleeAttack).</summary>
        private void StrikeOrHandOff()
        {
            if (phase != Phase.Charge)
            {
                HandOffToMelee();
                return;
            }

            Thing target = Target;
            float approach = Mathf.Clamp(netApproach, 0f, IC_Tuning.MaxApproach);
            // 창 부위가 부서져 창 공격을 못 하면 다른 공격으로 돌격을 소모하되 강화·스턴은 없다
            Verb lance = ChargeUtility.LanceVerb(pawn);
            if (lance == null) approach = 0f;
            bool attacked;
            bool hit;
            ChargeStrike.Begin(pawn, IC_Tuning.DamageFactor(approach));
            try
            {
                attacked = pawn.meleeVerbs.TryMeleeAttack(target, lance, false);
            }
            finally
            {
                hit = ChargeStrike.End();
            }
            if (!attacked) return; // 공격 자세가 아직 바쁨 → 다음 틱에 다시

            if (Prefs.DevMode)
            {
                Log.Message($"[IronCavalry] {pawn.LabelShort} 돌격: 접근 {approach:0.0}칸, 배율 {IC_Tuning.DamageFactor(approach):0.00}, 명중 {hit}");
            }
            if (hit && approach >= IC_Tuning.MinApproach)
            {
                ChargeUtility.TryStun(pawn, target, approach);
            }
            HandOffToMelee();
        }

        /// <summary>돌격을 마치고 일반 근접전으로. 플레이어 명령이었다면 같은 대상 근접 공격을 이어간다.</summary>
        private void HandOffToMelee()
        {
            ChargeUtility.RemoveSpeed(pawn);
            Thing target = Target;
            if (job.playerForced && target != null && target.Spawned)
            {
                Job melee = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                melee.playerForced = true;
                melee.killIncappedTarget = job.killIncappedTarget;
                pawn.jobs.jobQueue.EnqueueFirst(melee);
            }
            EndJobWith(JobCondition.Succeeded);
        }
    }
}
