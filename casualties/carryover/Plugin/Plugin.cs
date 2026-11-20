using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Carryover.Core;
using HarmonyLib;

namespace CasualtiesCarryover
{
    /// <summary>
    /// 로그라이트 모드:
    /// - 능력치 이어받기: 직전 회차에서 새로 얻은 STR·RES·INT만큼 다음 새 회차를 높여 시작(누적 없음). 상태 BepInEx/config/casualties.carryover.txt
    /// - 영구 퍽: 회차 성과로 포인트를 받아 메뉴에서 퍽을 산다(모든 회차에 계속). 상태 BepInEx/config/casualties.perks.txt, 아공간 저장분 casualties.subspace.json
    /// 게임 세이브(save.sv)는 건드리지 않는다.
    /// </summary>
    [BepInPlugin(Guid, "Casualties Carryover", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gojohnlee.casualties.carryover";
        public const string Version = "1.2.0";

        internal static ManualLogSource Log;
        internal static string StatePath, PerksPath;
        internal static CarryoverState State;
        internal static PerkState Perks;

        private void Awake()
        {
            Log = Logger;
            // 시험 때는 CU_CARRYOVER_STATE·CU_CARRYOVER_PERKS로 다른 파일을 써서 사용자 상태를 건드리지 않는다.
            StatePath = Env("CU_CARRYOVER_STATE") ?? Path.Combine(Paths.ConfigPath, "casualties.carryover.txt");
            PerksPath = Env("CU_CARRYOVER_PERKS") ?? Path.Combine(Paths.ConfigPath, "casualties.perks.txt");
            Subspace.FilePath = Env("CU_CARRYOVER_SUBSPACE") ?? Path.Combine(Paths.ConfigPath, "casualties.subspace.json");
            State = Load(StatePath, CarryoverState.Parse) ?? new CarryoverState();
            Perks = Load(PerksPath, PerkState.Parse) ?? new PerkState();
            var h = new Harmony(Guid);
            h.PatchAll(typeof(Patches));
            h.PatchAll(typeof(PerkPatches));
            h.PatchAll(typeof(Subspace));
            gameObject.AddComponent<PerkMenu>();
            gameObject.AddComponent<RunHud>();
            var test = Env("CU_CARRYOVER_TEST");
            if (test == "1") gameObject.AddComponent<SelfTest>();
            else if (test == "perk") gameObject.AddComponent<PerkTest>();
            else if (test == "lang") gameObject.AddComponent<LangCheck>();
            Log.LogInfo($"loaded, state {StatePath}: runActive={State.RunActive} start={Fmt(State.Start)} pending={Fmt(State.Pending)}; perks {PerksPath}: {Perks.Serialize().Replace('\n', ' ')}");
        }

        private static string Env(string k) { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? null : v; }

        private static T Load<T>(string path, Func<string, T> parse) where T : class
        {
            try { return File.Exists(path) ? parse(File.ReadAllText(path)) : null; }
            catch (Exception e) { Log.LogWarning($"read {path} failed, starting fresh: {e.Message}"); return null; }
        }

        private static void Write(string path, string text)
        {
            try
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, text);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Log.LogError($"write {path} failed: {e.Message}"); }
        }

        internal static void SaveState() => Write(StatePath, State.Serialize());
        internal static void SavePerks() => Write(PerksPath, Perks.Serialize());

        internal static string Fmt(int[] a) => $"{a[0]},{a[1]},{a[2]}";
    }
}
