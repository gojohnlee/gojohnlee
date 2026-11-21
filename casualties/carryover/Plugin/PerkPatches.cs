using HarmonyLib;

namespace CasualtiesCarryover
{
    // 퍽 효과 근거(디컴파일로 확인):
    // - 자물쇠: Openable이 new LockpingMinigame(build, lockpickAnglePrecision × run setting)을 연다. 미니게임은 각도 차이 < anglePrecision이면 성공
    //   → 값이 클수록 쉽다. 생성자에 들어가는 anglePrecision에 배율을 곱한다.
    // - 제작: Recipe.TryMake → RecipeResult.SpawnResult(recipe.INT). 안에서 num = 내 INT - recipeInt가 음수면 50%로 실패하거나
    //   품질(결과 condition·양)이 0.2~0.9배로 떨어지고, -3이면 손을 다친다. recipeInt를 퍽 단계만큼 낮춰 '지능 +N으로 친다'.
    // - 경험치: Skills.AddExp(stat, xp) 들어갈 때 xp에 배율.
    internal static class PerkPatches
    {
        [HarmonyPatch(typeof(LockpingMinigame), MethodType.Constructor, new[] { typeof(BuildingEntity), typeof(float) })]
        [HarmonyPrefix]
        private static void LockpickPrefix(ref float anglePrecision) => anglePrecision *= Plugin.Perks?.LockpickMult ?? 1f;

        [HarmonyPatch(typeof(RecipeResult), nameof(RecipeResult.SpawnResult))]
        [HarmonyPrefix]
        private static void CraftPrefix(ref int recipeInt) => recipeInt -= Plugin.Perks?.CraftIntBonus ?? 0;

        [HarmonyPatch(typeof(Skills), nameof(Skills.AddExp))]
        [HarmonyPrefix]
        private static void AddExpPrefix(ref float xp) => xp *= Plugin.Perks?.XpMult ?? 1f;

        // - 광부: 플레이어가 치는 길은 Body.Attack → (사람·물건을 못 맞히면) WorldGeneration.DamageBlock(점, atk.structuralDamage × …).
        //   Attack 안에서만 표시를 켜고, 그동안 DamageBlock(Vector2Int …) 피해에 배율을 곱한다(Vector2 판도 이 함수를 부름). 다른 것(드릴·폭발·몸 충돌)은 그대로.
        [System.ThreadStatic] internal static bool inPlayerAttack;

        [HarmonyPatch(typeof(Body), nameof(Body.Attack))]
        [HarmonyPrefix]
        private static void AttackPrefix(Body __instance) => inPlayerAttack = PlayerCamera.main != null && __instance == PlayerCamera.main.body;

        [HarmonyPatch(typeof(Body), nameof(Body.Attack))]
        [HarmonyFinalizer]
        private static void AttackFinalizer() => inPlayerAttack = false;

        [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.DamageBlock), new[] { typeof(UnityEngine.Vector2Int), typeof(float), typeof(bool), typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        private static void DamageBlockPrefix(ref float dmg) { if (inPlayerAttack) dmg *= Plugin.Perks?.MiningMult ?? 1f; }

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
