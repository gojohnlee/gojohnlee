using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Carryover.Core;
using HarmonyLib;

namespace CasualtiesCarryover
{
    /// <summary>
    /// 로그라이트 회차 보너스: 직전 회차에서 새로 얻은 STR·RES·INT만큼 다음 새 회차를 높여 시작한다(누적 없음).
    /// 게임 세이브(save.sv)는 건드리지 않고, 상태는 BepInEx/config/casualties.carryover.txt 하나에 둔다.
    /// </summary>
    [BepInPlugin(Guid, "Casualties Carryover", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gojohnlee.casualties.carryover";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static string StatePath;
        internal static CarryoverState State;

        private void Awake()
        {
            Log = Logger;
            // 시험 때는 CU_CARRYOVER_STATE로 다른 파일을 써서 사용자 상태를 건드리지 않는다.
            StatePath = Environment.GetEnvironmentVariable("CU_CARRYOVER_STATE");
            if (string.IsNullOrEmpty(StatePath)) StatePath = Path.Combine(Paths.ConfigPath, "casualties.carryover.txt");
            State = LoadState();
            new Harmony(Guid).PatchAll(typeof(Patches));
            var test = Environment.GetEnvironmentVariable("CU_CARRYOVER_TEST");
            if (test == "1") gameObject.AddComponent<SelfTest>();
            else if (test == "lang") gameObject.AddComponent<LangCheck>();
            Log.LogInfo($"loaded, state {StatePath}: runActive={State.RunActive} start={Fmt(State.Start)} pending={Fmt(State.Pending)}");
        }

        private static CarryoverState LoadState()
        {
            try { return File.Exists(StatePath) ? CarryoverState.Parse(File.ReadAllText(StatePath)) : new CarryoverState(); }
            catch (Exception e) { Log.LogWarning("state read failed, starting fresh: " + e.Message); return new CarryoverState(); }
        }

        internal static void SaveState()
        {
            try
            {
                var tmp = StatePath + ".tmp";
                File.WriteAllText(tmp, State.Serialize());
                if (File.Exists(StatePath)) File.Delete(StatePath);
                File.Move(tmp, StatePath);
            }
            catch (Exception e) { Log.LogError("state write failed: " + e.Message); }
        }

        internal static string Fmt(int[] a) => $"{a[0]},{a[1]},{a[2]}";
    }
}
