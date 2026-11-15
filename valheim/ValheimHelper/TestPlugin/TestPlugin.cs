using System;
using System.IO;
using BepInEx;
using HarmonyLib;

namespace ValheimHelper
{
    /// <summary>
    /// 시험 모드(사용자 11-15 "모드를 두 개로"): 시험 명령(vh_*test)·자동 시험·진단 패치·DIAG 로그는 전부 이 DLL에만 있다.
    /// 본 모드(ValheimHelper.dll)만 깔면 시험 코드가 아예 없다 — 본 모드는 TestHook·Diag.Sink 연결 지점만 두고(기본 null·false) 여기서 건다.
    /// </summary>
    [BepInPlugin(Guid, "Valheim Helper Test", Plugin.Version)]
    [BepInDependency(Plugin.Guid)]
    public class TestPlugin : BaseUnityPlugin
    {
        public const string Guid = "gojohnlee.valheimhelper.test";

        void Awake()
        {
            if (Bare.On)
            {
                // 모드 끔 판(부하 비교): 본 모드는 패치·일꾼·명령 하나도 안 켜고(Plugin.Off), 여기서 autotest.txt(월드·캐릭터)만 읽는다
                Step("자동 시험", TestRunner.CheckAutoRun);
                Logger.LogInfo("Valheim Helper Test 로드됨 — 모드 끔 판(VH_BARE=1), 패치 0");
                return;
            }
            TestHook.Log = TestRunner.Log;
            TestHook.First = () => Minimized.Update(); // 먼저: 다른 오류가 나도 창은 내려 둔다
            TestHook.Last = () => TestRunner.UpdateAutoRun();
            TestHook.E072 = line => File.AppendAllText(Path.Combine(Plugin.Dir, "e072-save.txt"), DateTime.Now.ToString("HH:mm:ss ") + line + "\n"); // 다음 실행(restart2)이 읽음
            Diag.Sink = line => Plugin.Log.LogInfo(line);
            Step("진단 패치", () => new Harmony(Guid).PatchAll());
            Step("vh_despawn", () => new Terminal.ConsoleCommand("vh_despawn", "[Valheim Helper] 일꾼 NPC를 모두 없앤다", args => { if (TestRunner.TestWorldOnly(args.Context)) args.Context.AddString($"NPC {Workers.DespawnAll()}명 없앰"); }));
            Step("vh_fartest", FarZoneTest.Register);
            Step("vh_npctest", NpcTest.Register);
            Step("vh_flagtest", FlagTest.Register);
            Step("vh_woodtest·vh_stonetest", GatherTest.Register);
            Step("vh_finaltest", FinalTest.Register);
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
            Step("vh_waitlatetest", WaitJoinTest.RegisterLate);
            Step("vh_settest", SetTest.Register);
            Step("vh_portaltest", PortalTest.Register);
            Step("vh_portalbuildtest", PortalBuildTest.Register);
            Step("vh_stucktest", StuckTest.Register);
            Step("vh_hoptest", HopTest.Register);
            Step("vh_skiptest", SkipTest.Register);
            Step("vh_pathforget", PathForgetTest.Register);
            Step("vh_resisttest", ResistTest.Register);
            Step("vh_repairtest", RepairTest.Register);
            Step("vh_crypttest", CryptTest.Register);
            Step("vh_frostcavetest", FrostCaveTest.Register);
            Step("vh_hunttest", HuntTest.Register);
            Step("vh_entrailstest", EntrailsTest.Register);
            Step("vh_cooktest", CookTest.Register);
            Step("vh_farmtest", FarmTest.Register);
            Step("vh_bowtest", BowTest.Register);
            Step("vh_basetest", BaseTest.Register);
            Step("vh_escorttest", EscortTest.Register);
            Step("vh_posttest", PostTest.Register);
            Step("vh_birdprobetest", BirdProbeTest.Register);
            Step("vh_birdshoottest", BirdShootTest.Register);
            Step("vh_nightexploretest", NightExploreTest.Register);
            Step("vh_nightretreattest", NightRetreatTest.Register);
            Step("vh_arrowtest", ArrowTest.Register);
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
            Step("vh_loadtest", LoadTest.Register);
            Step("자동 시험", TestRunner.CheckAutoRun);
            Logger.LogInfo("Valheim Helper Test 로드됨");
        }

        // 모드 끔 판은 본 모드 Update가 안 돌아 여기서 직접
        void Update()
        {
            if (!Bare.On) return;
            try { Minimized.Update(); Bare.Update(); }
            catch (Exception e) { if (!m_failed) Plugin.Log.LogError("모드 끔 판 Update 오류(다시 적지 않음): " + e); m_failed = true; }
        }

        bool m_failed;

        static void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError($"{what} 실패: {e}"); }
        }
    }
}
