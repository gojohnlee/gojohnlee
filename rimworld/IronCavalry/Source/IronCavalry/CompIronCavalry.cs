using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace IronCavalry
{
    public class CompProperties_IronCavalry : CompProperties
    {
        public CompProperties_IronCavalry()
        {
            compClass = typeof(CompIronCavalry);
        }
    }

    /// <summary>철기병 개체 상태(자동 돌격 토글·재사용 대기)와 자동 돌격 판단, 명령 버튼.</summary>
    public class CompIronCavalry : ThingComp
    {
        public bool autoCharge = true;
        private int cooldownUntilTick = -1;

        // 저장하지 않는 판단 주기
        private int nextAutoCheckTick;
        private int nextRetreatCheckTick;
        private int nextStrayCheckTick;

        public Pawn Pawn => (Pawn)parent;

        public bool CooldownReady => Find.TickManager.TicksGame >= cooldownUntilTick;

        public int CooldownTicksLeft => Mathf.Max(0, cooldownUntilTick - Find.TickManager.TicksGame);

        /// <summary>적·비소유 철기병은 자동 돌격이 항상 켜져 있다(기획서 A2-1).</summary>
        public bool AutoChargeEnabled => autoCharge || Pawn.Faction != Faction.OfPlayer;

        public bool CanReceiveManualOrder
        {
            get
            {
                Pawn p = Pawn;
                return p.Spawned && !p.Downed && p.Faction == Faction.OfPlayer && p.IsColonyMechPlayerControlled;
            }
        }

        public void StartCooldown()
        {
            cooldownUntilTick = Find.TickManager.TicksGame + IC_Tuning.CooldownTicks;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref autoCharge, "GJL_IC_autoCharge", true);
            Scribe_Values.Look(ref cooldownUntilTick, "GJL_IC_cooldownUntil", -1);
        }

        // 1.6부터 생긴 간격 틱과 매 틱 중 어느 쪽으로 불려도 같은 판단을 한 번만 하도록 둘 다 연결
        public override void CompTick()
        {
            AutoCheck();
        }

        public override void CompTickInterval(int delta)
        {
            AutoCheck();
        }

        private void AutoCheck()
        {
            int now = Find.TickManager.TicksGame;
            if (now < nextAutoCheckTick) return;
            nextAutoCheckTick = now + IC_Tuning.AutoCheckInterval;

            Pawn pawn = Pawn;
            if (!pawn.Spawned || pawn.Downed) return;
            if (pawn.CurJobDef == IC_DefOf.GJL_IC_Charge) return;

            // 돌격 잡 밖에서 가속이 남아 있으면 정리(정상이라면 잡 종료 시 이미 지워짐)
            if (now >= nextStrayCheckTick)
            {
                nextStrayCheckTick = now + IC_Tuning.StrayHediffCheckInterval;
                ChargeUtility.RemoveSpeed(pawn);
            }

            if (!AutoChargeEnabled || !CooldownReady) return;

            // 실제 근접 전투 중일 때만. 돌격하려고 따로 적을 찾지 않는다(기획서 A3·A4).
            Job job = pawn.CurJob;
            if (job == null || job.def != JobDefOf.AttackMelee) return;
            Thing target = job.targetA.Thing;
            if (!ChargeUtility.IsValidTarget(pawn, target)) return;

            float gap = pawn.Position.DistanceTo(target.Position);
            if (gap >= IC_Tuning.MinStartGap && gap <= IC_Tuning.ChargeStartGap)
            {
                ChargeUtility.StartCharge(pawn, target, job.playerForced, false, IntVec3.Invalid, job.killIncappedTarget);
            }
            else if (gap < IC_Tuning.MinStartGap && now >= nextRetreatCheckTick)
            {
                nextRetreatCheckTick = now + IC_Tuning.RetreatRecheckTicks;
                if (RetreatPlanner.TryFindRetreatCell(pawn, target, out IntVec3 cell))
                {
                    ChargeUtility.StartCharge(pawn, target, job.playerForced, false, cell, job.killIncappedTarget);
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!CanReceiveManualOrder) yield break;

            yield return new Command_IronCavalryCharge();

            yield return new Command_Toggle
            {
                defaultLabel = "GJL_IC_AutoCharge".Translate(),
                defaultDesc = "GJL_IC_AutoChargeDesc".Translate(),
                icon = IC_Textures.AutoCharge,
                isActive = () => autoCharge,
                toggleAction = () => autoCharge = !autoCharge
            };
        }
    }
}
