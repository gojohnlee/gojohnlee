using System.Collections.Generic;
using RimWorld;
using Verse;

namespace IronCavalry
{
    /// <summary>
    /// 수동 돌격 명령(단축키 C). 여러 기를 선택하면 재사용 대기가 끝난 철기병 모두에게 같은 대상을 지정한다.
    /// 버튼 묶음이 몇 번 실행되든 같은 틱·같은 대상이면 한 번만 처리한다.
    /// </summary>
    public class Command_IronCavalryCharge : Command_Target
    {
        private static int lastIssueTick = -1;
        private static Thing lastIssueTarget;
        private static readonly List<CompIronCavalry> tmpSelected = new List<CompIronCavalry>();

        public Command_IronCavalryCharge()
        {
            defaultLabel = "GJL_IC_Charge".Translate();
            defaultDesc = "GJL_IC_ChargeDesc".Translate();
            icon = IC_Textures.Charge;
            hotKey = IC_DefOf.GJL_IC_ChargeKey;
            targetingParams = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetHumans = true,
                canTargetAnimals = true,
                canTargetMechs = true,
                canTargetBuildings = true,
                canTargetItems = false,
                canTargetLocations = false,
                canTargetSelf = false,
                mapObjectTargetsMustBeAutoAttackable = false
            };
            action = target => IssueToSelected(target.Thing);

            int minLeft = int.MaxValue;
            bool anyReady = false;
            foreach (CompIronCavalry comp in SelectedCavalry())
            {
                if (comp.CooldownReady) anyReady = true;
                else if (comp.CooldownTicksLeft < minLeft) minLeft = comp.CooldownTicksLeft;
            }
            if (!anyReady && minLeft != int.MaxValue)
            {
                Disable("GJL_IC_OnCooldown".Translate(SecondsLabel(minLeft)));
            }
        }

        private static string SecondsLabel(int ticks)
        {
            return (ticks / 60f).ToString("0.0");
        }

        private static List<CompIronCavalry> SelectedCavalry()
        {
            tmpSelected.Clear();
            List<object> selected = Find.Selector.SelectedObjectsListForReading;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] is Pawn p)
                {
                    CompIronCavalry comp = p.GetComp<CompIronCavalry>();
                    if (comp != null && comp.CanReceiveManualOrder) tmpSelected.Add(comp);
                }
            }
            return tmpSelected;
        }

        private static void IssueToSelected(Thing target)
        {
            if (target == null) return;
            int now = Find.TickManager.TicksGame;
            if (now == lastIssueTick && target == lastIssueTarget) return;
            lastIssueTick = now;
            lastIssueTarget = target;

            bool issued = false;
            string reject = null;
            foreach (CompIronCavalry comp in SelectedCavalry())
            {
                if (!comp.CooldownReady) continue;
                Pawn pawn = comp.Pawn;
                string why = ChargeUtility.ManualRejectReason(pawn, target);
                if (why != null)
                {
                    reject = why;
                    continue;
                }
                ChargeUtility.StartCharge(pawn, target, true, true, IntVec3.Invalid);
                issued = true;
            }
            if (!issued && reject != null)
            {
                Messages.Message(reject, target, MessageTypeDefOf.RejectInput, false);
            }
        }
    }
}
