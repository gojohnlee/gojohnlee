using BepInEx.Configuration;

namespace ValheimHelper
{
    /// <summary>
    /// 묶음 1 기능 스위치(BepInEx 설정 [Features], 기본 켬). 집 시험에서 하나가 터지면 그것만 끄고 나머지를 본다.
    /// 끄면 예전 동작 그대로. 시험이 잠깐 바꿀 때는 Set으로(설정 파일에는 안 씀).
    /// </summary>
    public static class Features
    {
        static ConfigEntry<bool> s_saveWork, s_knowExplored;
        static bool? s_knowExploredOverride;

        public static void Init(ConfigFile cfg)
        {
            s_saveWork = cfg.Bind("Features", "SaveWork", true, "대기열·질문 대기·하던 일을 NPC ZDO에 저장하고 NPC가 생길 때 불러옴");
            s_knowExplored = cfg.Bind("Features", "KnowExplored", true, "지도에 밝혀진 구역의 자원은 가 보지 않아도 앎");
        }

        public static bool SaveWork => s_saveWork?.Value ?? true;
        public static bool KnowExplored => s_knowExploredOverride ?? s_knowExplored?.Value ?? true;

        /// <summary>시험용: null이면 설정값으로 돌아감.</summary>
        public static void OverrideKnowExplored(bool? on) => s_knowExploredOverride = on;
    }
}
