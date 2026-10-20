using System;
using BepInEx;
using HarmonyLib;

namespace ValheimHelper
{
    [BepInPlugin(Guid, "Valheim Helper", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gojohnlee.valheimhelper";
        public const string Version = "0.3.7";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            // 한 단계가 실패해도 나머지는 올라오게 한다(0.1.0은 시험 명령 등록 오류로 자동 시험까지 멈췄다).
            Step("Harmony 패치", () => new Harmony(Guid).PatchAll());
            Step("NPC 설정·명령", () => Workers.Init(Config));
            Step("vh_fartest", FarZoneTest.Register);
            Step("vh_npctest", NpcTest.Register);
            Step("vh_flagtest", FlagTest.Register);
            Step("vh_woodtest·vh_stonetest", GatherTest.Register);
            Step("vh_exploretest", ExploreTest.Register);
            Step("vh_preptest", PrepTest.Register);
            Step("vh_deathtest", DeathTest.Register);
            Step("vh_fighttest", FightTest.Register);
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
                Brain.Update(UnityEngine.Time.deltaTime);
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
