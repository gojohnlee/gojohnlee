using System;
using BepInEx;
using HarmonyLib;

namespace ValheimHelper
{
    [BepInPlugin(Guid, "Valheim Helper", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gojohnlee.valheimhelper";
        public const string Version = "0.3.8";
        // 시작 로그·!상태·diag 머리에 쓰는 판: "0.3.8+해시7 (빌드 시각)" — 해시는 빌드 때 HEAD, 고친 채면 *
        public static string FullVersion => $"{Version}+{BuildInfo.Commit} ({BuildInfo.Time})";

        internal static Plugin Instance;
        internal static string Dir => System.IO.Path.GetDirectoryName(Instance.Info.Location); // 모드 DLL 폴더(BepInEx/plugins/ValheimHelper)
        internal static BepInEx.Logging.ManualLogSource Log;
        // 모드 끔 판(시험 모드의 부하 비교 vh_bare가 VH_BARE=1로 켬 — 플레이에선 늘 false)
        internal static readonly bool Off = Environment.GetEnvironmentVariable("VH_BARE") == "1";

        void Awake()
        {
            Instance = this;
            Log = Logger;
            if (Off)
            {
                // 모드 끔 판(부하 비교, 시험 모드 Bare): 패치·일꾼·명령 하나도 안 켠다
                Logger.LogInfo($"Valheim Helper {FullVersion} 로드됨 — 모드 끔 판(VH_BARE=1), 패치 0");
                return;
            }
            // 한 단계가 실패해도 나머지는 올라오게 한다(0.1.0은 시험 명령 등록 오류로 자동 시험까지 멈췄다).
            Step("Harmony 패치", () => new Harmony(Guid).PatchAll());
            Step("NPC 설정·명령", () => Workers.Init(Config));
            Step("기능 스위치", () => Features.Init(Config));
            Step("게임 함수 변경 감지", () => GameCheck.Run());
            Logger.LogInfo($"Valheim Helper {FullVersion} 로드됨");
        }

        // 같이 깔린 BepInEx 플러그인 "이름 판, …" — 플레이 버그가 모드 조합 탓인지 가리려고 시작 로그·diag 머리에
        internal static string PluginList()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                if (info?.Metadata != null) names.Add($"{info.Metadata.Name} {info.Metadata.Version}");
            names.Sort(StringComparer.Ordinal);
            return string.Join(", ", names);
        }

        // Awake 땐 뒤 플러그인이 아직 안 올라와서 첫 프레임에 적는다
        void Start() => Step("플러그인 목록", () => Logger.LogInfo("같이 깔린 플러그인: " + PluginList()));

        static void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Log.LogError($"{what} 실패: {e}"); }
        }

        readonly System.Collections.Generic.HashSet<string> m_updateFailed = new System.Collections.Generic.HashSet<string>();

        bool m_guiFailed;

        void OnGUI()
        {
            if (Off) return;
            try { RefillWindow.OnGUI(); }
            catch (Exception e)
            {
                if (!m_guiFailed) Log.LogError("OnGUI 오류(같은 오류는 다시 적지 않음): " + e);
                m_guiFailed = true;
            }
        }

        void OnApplicationQuit() { if (!Off) SpeechCount.Save(); }

        void Update()
        {
            if (Off) return;
            // 기능마다 따로 try: 한 기능 오류가 뒤 기능(WorkSaves·MorningSettle 등)을 멈추지 않게(사용자 11-10)
            if (TestHook.First != null) Run("TestFirst", TestHook.First); // 시험 모드(맨 앞)
            Run("Workers", () => Workers.Update());
            Run("WorkerBanter", () => WorkerBanter.Update());
            Run("Brain", () => Brain.Update(UnityEngine.Time.deltaTime));
            Run("Departures", () => Departures.Tick(UnityEngine.Time.deltaTime));
            Run("FoodModWatch", () => FoodModWatch.Tick(UnityEngine.Time.deltaTime));
            Run("ClaudeChat", () => ClaudeChat.Tick());
            Run("SpeechCount", () => SpeechCount.Tick());
            Run("WorkSaves", () => WorkSaves.Tick(UnityEngine.Time.deltaTime));
            Run("MorningSettle", () => MorningSettle.Tick());
            Run("Tidy", () => Tidy.Tick(UnityEngine.Time.deltaTime));
            Run("PersonalChests", () => PersonalChests.Tick(UnityEngine.Time.deltaTime));
            Run("SharedChests", () => SharedChests.Tick());
            Run("SharedGuest", () => SharedGuest.Tick());
            Run("PlayWalkLog", () => PlayWalkLog.Tick(UnityEngine.Time.deltaTime));
            Run("SharedCraft", () => SharedCraft.Tick());
            Run("MapSense", () => MapSense.Tick(UnityEngine.Time.deltaTime));
            Run("MapIcons", () => MapIcons.Update(UnityEngine.Time.deltaTime));
            Run("RefillWindow", () => RefillWindow.Update(UnityEngine.Time.deltaTime));
            Run("GapWatch", () => GapWatch.Tick(UnityEngine.Time.deltaTime));
            Run("FrameLog", () => FrameLog.Tick()); // VH_FRAMESTATS=1일 때만 프레임 시간 기록
            if (TestHook.Last != null) Run("TestLast", TestHook.Last); // 시험 모드 자동 시험(맨 뒤)
        }

        // 캡처 없는 람다라 프레임마다 할당 없음. 오류 로그는 기능별로 처음 한 번.
        void Run(string name, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                if (m_updateFailed.Add(name)) Log.LogError("Update 오류 " + name + "(같은 기능 오류는 다시 적지 않음): " + e);
            }
        }
    }
}
