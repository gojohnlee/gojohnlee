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
        internal static BepInEx.Logging.ManualLogSource Log;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            // 한 단계가 실패해도 나머지는 올라오게 한다(0.1.0은 시험 명령 등록 오류로 자동 시험까지 멈췄다).
            Step("Harmony 패치", () => new Harmony(Guid).PatchAll());
            Step("NPC 설정·명령", () => Workers.Init(Config));
            Step("기능 스위치", () => Features.Init(Config));
            Step("vh_fartest", FarZoneTest.Register);
            Step("vh_npctest", NpcTest.Register);
            Step("vh_flagtest", FlagTest.Register);
            Step("vh_woodtest·vh_stonetest", GatherTest.Register);
            Step("vh_exploretest", ExploreTest.Register);
            Step("vh_preptest", PrepTest.Register);
            Step("vh_bagtest", BagTest.Register);
            Step("vh_settletest", SettleTest.Register);
            Step("vh_deathtest", DeathTest.Register);
            Step("vh_fighttest", FightTest.Register);
            Step("vh_pittest", PitTest.Register);
            Step("vh_departtest", DepartTest.Register);
            Step("vh_peertest", PeerTest.Register);
            Step("vh_crafttest", CraftTest.Register);
            Step("vh_claudetest", ClaudeTest.Register);
            Step("vh_nametest", NameTest.Register);
            Step("vh_bantertest", BanterTest.Register);
            Step("vh_stocktest", StockTest.Register);
            Step("vh_tidytest", TidyTest.Register);
            Step("vh_tidywalktest", TidyWalkTest.Register);
            Step("vh_mychesttest", MyChestTest.Register);
            Step("vh_sharetest", ShareTest.Register);
            Step("vh_guesttest", GuestTest.Register);
            Step("vh_jointest", JoinTest.Register);
            Step("vh_guestcmdtest", GuestCmdTest.Register);
            Step("vh_guestchesttest", GuestChestTest.Register);
            Step("vh_guestrestart", GuestRestartTest.Register);
            Step("vh_skilltest", SkillTest.Register);
            Step("vh_refilltest", RefillTest.Register);
            Step("vh_chorestest", ChoresTest.Register);
            Step("vh_fishtest", FishTest.Register);
            Step("vh_livestocktest", LivestockTest.Register);
            Step("vh_raidtest", RaidTest.Register);
            Step("vh_helptest", HelpTest.Register);
            Step("vh_teamtest", TeamTest.Register);
            Step("vh_matetest", MateTest.Register);
            Step("vh_helpjointest", HelpJoinTest.Register);
            Step("vh_gatherhelptest", HelpGatherTest.Register);
            Step("vh_waitjointest", WaitJoinTest.Register);
            Step("vh_settest", SetTest.Register);
            Step("vh_portaltest", PortalTest.Register);
            Step("vh_portalbuildtest", PortalBuildTest.Register);
            Step("vh_stucktest", StuckTest.Register);
            Step("vh_hoptest", HopTest.Register);
            Step("vh_skiptest", SkipTest.Register);
            Step("vh_resisttest", ResistTest.Register);
            Step("vh_repairtest", RepairTest.Register);
            Step("vh_crypttest", CryptTest.Register);
            Step("vh_frostcavetest", FrostCaveTest.Register);
            Step("vh_hunttest", HuntTest.Register);
            Step("vh_cooktest", CookTest.Register);
            Step("vh_farmtest", FarmTest.Register);
            Step("vh_bowtest", BowTest.Register);
            Step("vh_basetest", BaseTest.Register);
            Step("vh_escorttest", EscortTest.Register);
            Step("vh_posttest", PostTest.Register);
            Step("vh_guardbowtest", GuardBowTest.Register);
            Step("vh_silvertest", SilverTest.Register);
            Step("vh_plainstest", PlainsTest.Register);
            Step("vh_mistlandstest", MistlandsTest.Register);
            Step("vh_deepnorthtest", DeepNorthTest.Register);
            Step("vh_ashlandstest", AshlandsTest.Register);
            Step("vh_squitotest", SquitoTest.Register);
            Step("vh_mistfoetest", MistFoeTest.Register);
            Step("vh_ashfoetest", AshFoeTest.Register);
            Step("vh_deepfoetest", DeepFoeTest.Register);
            Step("vh_fulingtest", FulingTest.Register);
            Step("vh_tartest", TarTest.Register);
            Step("vh_plainsfarmtest", PlainsFarmTest.Register);
            Step("vh_restart1·2", RestartTest.Register);
            Step("vh_restartcraft", RestartCraftTest.Register);
            Step("vh_machinestucktest", MachineStuckTest.Register);
            Step("vh_unloadreachtest", UnloadReachTest.Register);
            Step("vh_houselogtest", HouseLogTest.Register);
            Step("vh_chainfalltest", ChainFallTest.Register);
            Step("vh_housetest", HouseTest.Register);
            Step("vh_newerahouse", NewEraHouseTest.Register);
            Step("vh_houseexport", HouseCopy.Register);
            Step("vh_houseimport", HouseCopy.RegisterImport);
            Step("vh_househere", HouseCopy.RegisterHere);
            Step("vh_houseground", HouseCopy.RegisterGround);
            Step("vh_blocktest", BlockTest.Register);
            Step("vh_weaktest", WeakTest.Register);
            Step("자동 시험", TestRunner.CheckAutoRun);
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
            try { RefillWindow.OnGUI(); }
            catch (Exception e)
            {
                if (!m_guiFailed) Log.LogError("OnGUI 오류(같은 오류는 다시 적지 않음): " + e);
                m_guiFailed = true;
            }
        }

        void OnApplicationQuit() => SpeechCount.Save();

        void Update()
        {
            // 기능마다 따로 try: 한 기능 오류가 뒤 기능(WorkSaves·MorningSettle 등)을 멈추지 않게(사용자 11-10)
            Run("Minimized", () => Minimized.Update()); // 먼저: 다른 오류가 나도 창은 내려 둔다
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
            Run("TestRunner", () => TestRunner.UpdateAutoRun());
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
