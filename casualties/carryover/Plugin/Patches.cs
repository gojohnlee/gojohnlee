using System;
using HarmonyLib;

namespace CasualtiesCarryover
{
    // 게임 코드 근거(Assembly-CSharp 디컴파일로 확인):
    // - Body.Awake: skills = new Skills(); skills.Setup(charType) → BaseSkills 값으로 STR·RES·INT, exp = 그 레벨 최소값.
    // - WorldGeneration.Start(private): SaveSystem.TryLoadGame() 뒤 biomeDepth==0이면 run setting(mindwiped·zeroskills)으로 skills를 바꾼다.
    //   층 이동은 RegenerateWorld로 같은 씬에서 하므로 Start는 회차 시작(또는 이어하기·재시작) 때만 돈다.
    // - SaveSystem.loadedRun: 이어하기면 true(세이브가 없거나 깨지면 TryLoadGame이 false로 바꿈). 튜토리얼은 runSettings=null.
    // - PlayerCamera.EndSequence(int type): 0=죽음, 1·2·3=엔딩. 회차 결과 화면(deathStats)을 띄우는 코루틴.
    // - PreRunScript.UpdateCharacterText(private): 캐릭터 선택 화면 charSelectStats에 BaseSkills를 적는다.
    internal static class Patches
    {
        internal static int[] Levels(Skills s) => new[] { s.STR, s.RES, s.INT };

        [HarmonyPatch(typeof(WorldGeneration), "Start")]
        [HarmonyPostfix]
        private static void RunStarted()
        {
            try
            {
                var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
                if (body == null || body.skills == null) { Plugin.Log.LogWarning("run start: no body/skills"); return; }
                var sk = body.skills;
                if (WorldGeneration.runSettings == null)
                {
                    Plugin.State.OnUncountedRun();
                    Subspace.DropBag();
                    Plugin.Log.LogInfo("tutorial run: not counted");
                }
                else if (SaveSystem.loadedRun)
                {
                    Plugin.State.OnLoadedRun(Levels(sk));
                    var up = Plugin.State.RaiseToKept(Levels(sk));   // 유지 규칙 전에 시작한 회차도 지난 끝 능력치까지
                    if (up[0] != sk.STR || up[1] != sk.RES || up[2] != sk.INT) { sk.STR = up[0]; sk.RES = up[1]; sk.INT = up[2]; sk.UpdateExpBoundaries(); sk.expSTR = sk.minSTR; sk.expRES = sk.minRES; sk.expINT = sk.minINT; Plugin.Log.LogInfo($"loaded run raised to kept {Plugin.Fmt(up)}"); }
                    Plugin.Log.LogInfo($"loaded run: start stays {Plugin.Fmt(Plugin.State.Start)}");
                    Subspace.Bag = null;   // 이전 씬의 가방은 씬과 함께 사라졌다
                    Subspace.RestoreFromFile(body);
                }
                else
                {
                    var before = Levels(sk);
                    var bonus = Plugin.State.BonusOver(Levels(sk));
                    var lv = Plugin.State.OnNewRun(before);
                    sk.STR = lv[0]; sk.RES = lv[1]; sk.INT = lv[2];
                    sk.UpdateExpBoundaries();
                    sk.expSTR = sk.minSTR; sk.expRES = sk.minRES; sk.expINT = sk.minINT;
                    Plugin.Log.LogInfo($"new run: base {Plugin.Fmt(before)} + bonus {Plugin.Fmt(bonus)} = {Plugin.Fmt(lv)}");
                    if (bonus[0] + bonus[1] + bonus[2] > 0 && PlayerCamera.main != null)
                    {
                        try { PlayerCamera.main.DoAlert(BonusText(bonus)); } catch (Exception e) { Plugin.Log.LogWarning("alert: " + e.Message); }
                    }
                    Plugin.Perks.OnNewRun();
                    Plugin.SavePerks();
                    Subspace.DropBag();
                    Subspace.EnsureBag(body);
                    Subspace.RestoreFromFile(body);   // 지난 회차 아공간 물건
                }
                Plugin.SaveState();
                SelfTest.OnRunStarted(Levels(sk));
                PerkTest.OnRunStarted();
            }
            catch (Exception e) { Plugin.Log.LogError("run start: " + e); }
        }

        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.EndSequence))]
        [HarmonyPrefix]
        private static void RunEnded(PlayerCamera __instance, int type)
        {
            try
            {
                Subspace.EndRun();   // 죽음·엔딩: 아공간 물건은 다음 회차로
                var sk = __instance.body != null ? __instance.body.skills : null;
                if (sk == null) return;
                var gain = Plugin.State.OnRunEnd(Levels(sk));
                if (gain == null) { Plugin.Log.LogInfo($"run end (type {type}): not counted"); return; }
                Plugin.SaveState();
                Plugin.Log.LogInfo($"run end (type {type}): start {Plugin.Fmt(Plugin.State.Start)} end {Plugin.Fmt(Levels(sk))} gain {Plugin.Fmt(gain)} -> next bonus");
                float secs = WorldGeneration.TotalRunTime();
                int depth = WorldGeneration.world != null ? WorldGeneration.world.biomeDepth : 0;
                int added = Plugin.Perks.OnRunEnd(secs, gain[0] + gain[1] + gain[2], depth);
                Plugin.SavePerks();
                Plugin.Log.LogInfo($"perk points: run {secs:0}s depth {depth} -> +{added} (total {Plugin.Perks.Points})");
                PerkTest.OnRunEnded(added);
                SelfTest.OnRunEnded(gain);
            }
            catch (Exception e) { Plugin.Log.LogError("run end: " + e); }
        }

        [HarmonyPatch(typeof(PreRunScript), "UpdateCharacterText")]
        [HarmonyPostfix]
        private static void ShowBonus(PreRunScript __instance)
        {
            try
            {
                var b = Skills.BaseSkills(PreRunScript.characterSelected);
                var p = Plugin.State.BonusOver(new[] { b[0], b[1], b[2] });
                if (__instance.charSelectStats == null || p[0] + p[1] + p[2] == 0) return;
                string[] n = { "STR", "RES", "INT" };
                var t = "";
                for (int i = 0; i < 3; i++) t += $"[{n[i]}] {b[i] + p[i]}" + (p[i] > 0 ? $" (+{p[i]})" : "") + "\n";
                __instance.charSelectStats.text = t;
            }
            catch (Exception e) { Plugin.Log.LogError("char select: " + e); }
        }

        private static bool Korean => (Locale.currentLangName ?? "").StartsWith("ko", StringComparison.OrdinalIgnoreCase);

        // 능력치 이름은 게임 언어 파일의 것(strength·resilience·intelligence)을 쓴다.
        internal static string BonusText(int[] b) =>
            (Korean ? "이전 회차 보너스: " : "Carryover bonus: ")
            + $"{Locale.GetOther("strength")} +{b[0]}, {Locale.GetOther("resilience")} +{b[1]}, {Locale.GetOther("intelligence")} +{b[2]}";
    }
}
