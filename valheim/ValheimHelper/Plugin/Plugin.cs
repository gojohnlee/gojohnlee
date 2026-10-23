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
            Step("vh_skilltest", SkillTest.Register);
            Step("vh_refilltest", RefillTest.Register);
            Step("vh_raidtest", RaidTest.Register);
            Step("vh_helptest", HelpTest.Register);
            Step("vh_teamtest", TeamTest.Register);
            Step("vh_settest", SetTest.Register);
            Step("vh_portaltest", PortalTest.Register);
            Step("vh_portalbuildtest", PortalBuildTest.Register);
            Step("vh_stucktest", StuckTest.Register);
            Step("vh_hoptest", HopTest.Register);
            Step("vh_skiptest", SkipTest.Register);
            Step("vh_resisttest", ResistTest.Register);
            Step("vh_repairtest", RepairTest.Register);
            Step("vh_crypttest", CryptTest.Register);
            Step("vh_hunttest", HuntTest.Register);
            Step("vh_cooktest", CookTest.Register);
            Step("vh_farmtest", FarmTest.Register);
            Step("vh_bowtest", BowTest.Register);
            Step("vh_basetest", BaseTest.Register);
            Step("vh_escorttest", EscortTest.Register);
            Step("vh_silvertest", SilverTest.Register);
            Step("vh_restart1·2", RestartTest.Register);
            Step("자동 시험", TestRunner.CheckAutoRun);
            Logger.LogInfo($"Valheim Helper {Version} 로드됨");
        }

        static void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Log.LogError($"{what} 실패: {e}"); }
        }

        bool m_updateFailed;

        void Update()
        {
            try
            {
                Minimized.Update(); // 먼저: 다른 오류가 나도 창은 내려 둔다
                Workers.Update();
                WorkerBanter.Update();
                Brain.Update(UnityEngine.Time.deltaTime);
                Departures.Tick(UnityEngine.Time.deltaTime);
                ClaudeChat.Tick();
                WorkSaves.Tick(UnityEngine.Time.deltaTime);
                MorningSettle.Tick();
                MapSense.Tick(UnityEngine.Time.deltaTime);
                MapIcons.Update(UnityEngine.Time.deltaTime);
                TestRunner.UpdateAutoRun();
            }
            catch (Exception e)
            {
                if (!m_updateFailed) Log.LogError("Update 오류(같은 오류는 다시 적지 않음): " + e);
                m_updateFailed = true;
            }
        }
    }
}
