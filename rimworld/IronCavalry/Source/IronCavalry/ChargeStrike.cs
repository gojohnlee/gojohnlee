using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace IronCavalry
{
    /// <summary>
    /// 돌격 첫 공격 한 번에만 피해 배율을 거는 짧은 문맥.
    /// 근접 공격은 TryMeleeAttack 호출 안에서 바로 처리되므로, 호출 전후로 Begin/End 한다.
    /// </summary>
    internal static class ChargeStrike
    {
        internal static Pawn attacker;
        internal static float factor = 1f;
        internal static bool hit;

        internal static void Begin(Pawn pawn, float damageFactor)
        {
            attacker = pawn;
            factor = damageFactor;
            hit = false;
        }

        /// <returns>이번 공격이 명중했는지</returns>
        internal static bool End()
        {
            bool h = hit;
            attacker = null;
            factor = 1f;
            hit = false;
            return h;
        }
    }

    /// <summary>
    /// 명중 시에만 불리는 피해 목록에 돌격 배율을 곱한다. 첫 번째(주) 피해만, 관통력은 그대로.
    /// 돌격 중이 아닐 때는 참조 비교 한 번으로 끝난다.
    /// </summary>
    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), "DamageInfosToApply")]
    internal static class Patch_ChargeDamage
    {
        private static void Postfix(Verb_MeleeAttackDamage __instance, ref IEnumerable<DamageInfo> __result)
        {
            if (ChargeStrike.attacker == null || __instance.CasterPawn != ChargeStrike.attacker) return;
            ChargeStrike.hit = true;
            if (ChargeStrike.factor != 1f) __result = Amplify(__result, ChargeStrike.factor);
        }

        private static IEnumerable<DamageInfo> Amplify(IEnumerable<DamageInfo> source, float factor)
        {
            bool first = true;
            foreach (DamageInfo dinfo in source)
            {
                if (first)
                {
                    first = false;
                    DamageInfo boosted = dinfo;
                    boosted.SetAmount(dinfo.Amount * factor);
                    yield return boosted;
                }
                else
                {
                    yield return dinfo;
                }
            }
        }
    }
}
