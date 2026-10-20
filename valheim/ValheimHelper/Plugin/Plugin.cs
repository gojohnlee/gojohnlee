using BepInEx;
using HarmonyLib;

namespace ValheimHelper
{
    [BepInPlugin(Guid, "Valheim Helper", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gojohnlee.valheimhelper";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            new Harmony(Guid).PatchAll();
            Workers.Init(Config);
            FarZoneTest.Register();
            NpcTest.Register();
            TestRunner.CheckAutoRun();
            Logger.LogInfo($"Valheim Helper {Version} 로드됨");
        }

        void Update()
        {
            Workers.Update();
            TestRunner.UpdateAutoRun();
        }
    }
}
