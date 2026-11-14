using BepInEx.Configuration;

namespace ValheimHelper
{
    /// <summary>
    /// 기능 스위치(BepInEx 설정 [Features], PlayWalkLog 말고 기본 켬 — 친구와 하다 문제 나면 모드를 통째로 빼지 않고 그것만 끔). 집 시험에서 하나가 터지면 그것만 끄고 나머지를 본다.
    /// 끄면 예전 동작 그대로. 시험이 잠깐 바꿀 때는 Set으로(설정 파일에는 안 씀).
    /// </summary>
    public static class Features
    {
        static ConfigEntry<bool> s_saveWork, s_knowExplored, s_claudeChat, s_runPlans, s_playWalkLog, s_sharedChests, s_farZones, s_idleChores, s_tidy, s_settle;
        static bool? s_knowExploredOverride;

        public static void Init(ConfigFile cfg)
        {
            s_saveWork = cfg.Bind("Features", "SaveWork", true, "대기열·질문 대기·하던 일을 NPC ZDO에 저장하고 NPC가 생길 때 불러옴");
            s_knowExplored = cfg.Bind("Features", "KnowExplored", true, "지도에 밝혀진 구역의 자원은 가 보지 않아도 앎");
            s_claudeChat = cfg.Bind("Features", "ClaudeChat", true, "정해진 명령이 아닌 채팅을 호스트 PC의 claude 명령줄로 해석");
            s_runPlans = cfg.Bind("Features", "RunPlans", true, "제작 명령(!구리주괴 20, !세트)을 계획만 말하지 않고 실제로 실행(PlanJob)");
            s_playWalkLog = cfg.Bind("Features", "PlayWalkLog", false, "실플레이 집 안 걸음 기록: 일꾼이 집 40m 안에서 일하는 동안 0.25초 줄을 behavior-house-play.txt에(2MB 넘으면 .old로 돌림)");
            s_sharedChests = cfg.Bind("Features", "SharedChests", true, "사람끼리 상자 같이 열기(호스트 상자를 손님이 열어 둔 동안 다른 사람도 열기·손님 제작이 호스트 상자 재료 씀). 끄면 바닐라(한 사람만)");
            s_farZones = cfg.Bind("Features", "FarZones", true, "먼 구역 유지: 호스트가 일꾼 둘레 구역을 만들고 돌림. 끄면 사람 곁 구역만 돎(멀리 간 일꾼은 멈춤)");
            s_idleChores = cfg.Bind("Features", "IdleChores", true, "쉴 때 하는 일(먹거리·낚시·쓴 재료 다시 캐기·밭·꿀·먹이·솎기·길들이기)");
            s_tidy = cfg.Bind("Features", "Tidy", true, "거점 상자 정리(같은 것끼리 모으기)");
            s_settle = cfg.Bind("Features", "Settle", true, "밤사이(잠으로 건너뛴 시간) 정산: 그동안 했을 일을 아침에 셈");
        }

        public static bool SaveWork => s_saveWork?.Value ?? true;
        public static bool ClaudeChat => s_claudeChat?.Value ?? true;
        public static bool RunPlans => s_runPlans?.Value ?? true;
        public static bool PlayWalkLog => s_playWalkLog?.Value ?? false;
        public static bool SharedChests => s_sharedChests?.Value ?? true;
        public static bool FarZones => s_farZones?.Value ?? true;
        public static bool IdleChores => s_idleChores?.Value ?? true;
        public static bool Tidy => s_tidy?.Value ?? true;
        public static bool Settle => s_settle?.Value ?? true;
        public static bool KnowExplored => s_knowExploredOverride ?? s_knowExplored?.Value ?? true;

        /// <summary>시험용: null이면 설정값으로 돌아감.</summary>
        public static void OverrideKnowExplored(bool? on) => s_knowExploredOverride = on;
    }
}
