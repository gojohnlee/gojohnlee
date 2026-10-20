using RimWorld;
using Verse;
using Verse.AI;

namespace IronCavalry
{
    public static class ChargeUtility
    {
        /// <summary>돌격 첫 공격에 쓰는 도구의 원문 이름(XML tools/label).</summary>
        public const string LanceLabel = "lance";

        public static bool IsValidTarget(Pawn pawn, Thing target)
        {
            if (target == null || target == pawn || !target.Spawned || target.Map != pawn.Map) return false;
            if (target is Pawn p && p.Dead) return false;
            return true;
        }

        /// <summary>돌격 잡 시작. retreatCell이 유효하면 먼저 그 칸으로 물러났다가 돌격한다.</summary>
        public static void StartCharge(Pawn pawn, Thing target, bool playerForced, bool ordered, IntVec3 retreatCell,
            bool killIncappedTarget = false)
        {
            Job job = JobMaker.MakeJob(IC_DefOf.GJL_IC_Charge, target,
                retreatCell.IsValid ? new LocalTargetInfo(retreatCell) : LocalTargetInfo.Invalid);
            job.playerForced = playerForced;
            job.killIncappedTarget = killIncappedTarget;
            if (ordered)
            {
                pawn.jobs.TryTakeOrderedJob(job);
            }
            else
            {
                pawn.jobs.StartJob(job, JobCondition.InterruptForced, cancelBusyStances: false);
            }
        }

        /// <summary>수동 명령을 받을 수 없는 이유. 받을 수 있으면 null.</summary>
        public static string ManualRejectReason(Pawn pawn, Thing target)
        {
            if (!IsValidTarget(pawn, target)) return "GJL_IC_CannotAttack".Translate();
            if (!(target is Pawn) && !(target is Building && target.def.useHitPoints)) return "GJL_IC_CannotAttack".Translate();
            if (pawn.IsColonyMech && !MechanitorUtility.InMechanitorCommandRange(pawn, target)) return "GJL_IC_OutOfRange".Translate();
            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly)) return "GJL_IC_NoPath".Translate();
            return null;
        }

        public static Verb LanceVerb(Pawn pawn)
        {
            var verbs = pawn.meleeVerbs?.GetUpdatedAvailableVerbsList(false);
            if (verbs == null) return null;
            for (int i = 0; i < verbs.Count; i++)
            {
                Verb v = verbs[i].verb;
                if (v?.tool != null && v.tool.untranslatedLabel == LanceLabel) return v;
            }
            return null;
        }

        public static Tool LanceTool(Pawn pawn)
        {
            var tools = pawn.def.tools;
            if (tools == null) return null;
            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].untranslatedLabel == LanceLabel) return tools[i];
            }
            return null;
        }

        public static void AddSpeed(Pawn pawn)
        {
            if (pawn.health.hediffSet.GetFirstHediffOfDef(IC_DefOf.GJL_IC_ChargeSpeed) == null)
            {
                pawn.health.AddHediff(IC_DefOf.GJL_IC_ChargeSpeed);
            }
        }

        public static void RemoveSpeed(Pawn pawn)
        {
            Hediff h = pawn.health?.hediffSet?.GetFirstHediffOfDef(IC_DefOf.GJL_IC_ChargeSpeed);
            if (h != null) pawn.health.RemoveHediff(h);
        }

        /// <summary>돌격 명중 시 충돌 스턴(체구 약화·연속 스턴 방지 포함).</summary>
        public static void TryStun(Pawn attacker, Thing target, float approach)
        {
            if (!(target is Pawn victim) || victim.Dead || !victim.Spawned) return;
            StunHandler stunner = victim.stances?.stunner;
            if (stunner == null || stunner.Stunned) return;

            float ours = attacker.BodySize;
            float theirs = victim.BodySize;
            if (theirs >= ours * IC_Tuning.StunNoneIfAtLeast) return;
            int ticks = IC_Tuning.StunTicks(approach);
            if (theirs > ours * IC_Tuning.StunHalfIfBiggerThan) ticks /= 2;
            if (ticks <= 0) return;

            GameComponent_IronCavalry gc = GameComponent_IronCavalry.Instance;
            int now = Find.TickManager.TicksGame;
            if (gc != null && gc.IsStunImmune(victim, now)) return;

            stunner.StunFor(ticks, attacker, true, true, false);
            gc?.SetStunImmuneUntil(victim, now + ticks + IC_Tuning.StunImmunityTicks);
        }
    }
}
