using HarmonyLib;

namespace CasualtiesCarryover
{
    // 퍽 효과 근거(디컴파일로 확인):
    // - 자물쇠: Openable이 new LockpingMinigame(build, lockpickAnglePrecision × run setting)을 연다. 미니게임은 각도 차이 < anglePrecision이면 성공
    //   → 값이 클수록 쉽다. 생성자에 들어가는 anglePrecision에 배율을 곱한다.
    internal static class PerkPatches
    {
        [HarmonyPatch(typeof(LockpingMinigame), MethodType.Constructor, new[] { typeof(BuildingEntity), typeof(float) })]
        [HarmonyPrefix]
        private static void LockpickPrefix(ref float anglePrecision) => anglePrecision *= Plugin.Perks?.LockpickMult ?? 1f;

        // - 광부: 플레이어가 치는 길은 Body.Attack → (사람·물건을 못 맞히면) WorldGeneration.DamageBlock(점, atk.structuralDamage × …).
        //   Attack 안에서만 표시를 켜고, 그동안 DamageBlock(Vector2Int …) 피해에 배율을 곱한다(Vector2 판도 이 함수를 부름). 다른 것(드릴·폭발·몸 충돌)은 그대로.
        [System.ThreadStatic] internal static bool inPlayerAttack;

        [HarmonyPatch(typeof(Body), nameof(Body.Attack))]
        [HarmonyPrefix]
        private static void AttackPrefix(Body __instance) => inPlayerAttack = PlayerCamera.main != null && __instance == PlayerCamera.main.body;

        [HarmonyPatch(typeof(Body), nameof(Body.Attack))]
        [HarmonyFinalizer]
        private static void AttackFinalizer() { inPlayerAttack = false; numberPending = false; }
        internal static int BoostedHits;   // 시험용: 배율이 붙은 DamageBlock 수

        [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.DamageBlock), new[] { typeof(UnityEngine.Vector2Int), typeof(float), typeof(bool), typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        private static void DamageBlockPrefix(ref float dmg) { if (inPlayerAttack) { dmg *= Plugin.Perks?.MiningMult ?? 1f; BoostedHits++; numberPending = true; } }

        // 화면 숫자: Body.Attack은 DamageBlock 바로 뒤에 CreateDamageNumber((int)(structuralDamage × 힘 배율))를 따로 부른다 → 퍽 배율이 빠진 숫자가 뜸(사용자 11-23: 드릴 10씩).
        //   방금 배율을 붙인 블록 피해 다음의 숫자 하나에만 같은 배율을 곱한다(동물 피해 숫자는 그대로).
        [System.ThreadStatic] private static bool numberPending;
        internal static int LastNumber, LastNumberRaw;
        [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.CreateDamageNumber))]
        [HarmonyPrefix]
        private static void DamageNumberPrefix(ref int num)
        {
            if (!numberPending || !inPlayerAttack) { numberPending = false; return; }
            numberPending = false;
            LastNumberRaw = num;
            num = (int)(num * (Plugin.Perks?.MiningMult ?? 1f));
            LastNumber = num;
        }

        // - 튼튼한 다리: 떨어지거나 부딪힐 때 Limb.OnCollisionEnter2D → Limb.ImpactDamage(force, dir). 근육·피부·뼈·머리 피해와 탈구·골절 문턱이 모두 force로 정해진다
        //   → 플레이어 몸의 팔다리면 force에 배율을 곱한다.
        [HarmonyPatch(typeof(Limb), nameof(Limb.ImpactDamage))]
        [HarmonyPrefix]
        private static void ImpactPrefix(Limb __instance, ref float force)
        {
            if (PlayerCamera.main != null && __instance.body == PlayerCamera.main.body) force *= Plugin.Perks?.ImpactMult ?? 1f;
        }

        // - 온도 적응력: Body.HandleBodyTemperature가 1초마다 temperature = Lerp(체온, 바깥 온도, baseTemperatureLerpRate / GetTotalInsulation()).
        //   GetTotalInsulation을 쓰는 곳은 여기와 상태창 단열 % 표시뿐 → 플레이어 몸이 37℃에서 멀어지는 쪽으로 끌려갈 때만 단열을 1/배율로 높인다(돌아오는 회복은 그대로).
        [HarmonyPatch(typeof(Body), nameof(Body.GetTotalInsulation))]
        [HarmonyPostfix]
        private static void InsulationPostfix(Body __instance, ref float __result)
        {
            float m = Plugin.Perks?.TempDriftMult ?? 1f;
            if (m < 1f && m > 0f && PlayerCamera.main != null && __instance == PlayerCamera.main.body && WorldGeneration.world != null
                && Carryover.Core.PerkState.DriftAwayFromNormal(__instance.temperature, WorldGeneration.world.ambientTemperature)) __result /= m;
        }
    }
}
