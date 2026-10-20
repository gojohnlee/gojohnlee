using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HelloRimWorld
{
    public class HelloRimWorldMod : Mod
    {
        public HelloRimWorldMod(ModContentPack content) : base(content)
        {
            string harmony;
            try
            {
                harmony = ApplyHarmony(content.PackageId);
            }
            catch (Exception e)
            {
                harmony = "FAILED: " + e.GetType().Name + ": " + e.Message;
            }
            WriteDiagnostics(content, harmony);
            Log.Message("[HelloRimWorld] Loaded. Harmony: " + harmony);
        }

        // Harmony가 없을 때도 생성자가 죽지 않도록 별도 메서드로 분리
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ApplyHarmony(string id)
        {
            new Harmony(id).PatchAll();
            return "OK " + typeof(Harmony).Assembly.GetName().Version;
        }

        // 설치 스크립트가 읽어가는 진단 파일 (모드 폴더/diag.txt)
        private static void WriteDiagnostics(ModContentPack content, string harmony)
        {
            try
            {
                var lines = new[]
                {
                    "time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    "game: " + VersionControl.CurrentVersionStringWithRev,
                    "runtime: " + Environment.Version,
                    "harmony: " + harmony,
                    "mods:",
                }.Concat(LoadedModManager.RunningModsListForReading.Select(m => "  " + m.PackageIdPlayerFacing + " | " + m.Name));
                File.WriteAllLines(Path.Combine(content.RootDir, "diag.txt"), lines);
            }
            catch (Exception e)
            {
                Log.Warning("[HelloRimWorld] diag write failed: " + e.Message);
            }
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
