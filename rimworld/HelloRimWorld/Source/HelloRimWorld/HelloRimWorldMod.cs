using HarmonyLib;
using RimWorld;
using Verse;

namespace HelloRimWorld
{
    public class HelloRimWorldMod : Mod
    {
        public HelloRimWorldMod(ModContentPack content) : base(content)
        {
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[HelloRimWorld] Loaded.");
        }
    }

    // 맵 로드(새 게임/불러오기)가 끝난 뒤 인사 메시지 표시
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Game_FinalizeInit_Patch
    {
        public static void Postfix()
        {
            Messages.Message("Hello RimWorld 모드가 작동 중입니다!", MessageTypeDefOf.PositiveEvent, historical: false);
        }
    }
}
