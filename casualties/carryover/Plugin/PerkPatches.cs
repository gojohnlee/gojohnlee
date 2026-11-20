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
    }
}
