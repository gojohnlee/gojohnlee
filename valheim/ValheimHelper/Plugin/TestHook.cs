using System;

namespace ValheimHelper
{
    /// <summary>
    /// 시험 모드(ValheimHelperTest.dll — 따로 깔 때만) 연결 지점(사용자 11-15). 본 모드에는 시험 코드가 없고 여기 값은 기본(false·null) 그대로라
    /// 시험 모드 없이 깔면 시험을 안 돌릴 때와 똑같이 돈다. 시험 모드가 올라오면 TestRunner가 채운다.
    /// </summary>
    static class TestHook
    {
        internal static bool Running;      // 시험 도는 중(TestHook.Running)
        internal static bool AutoSession;  // autotest.txt로 켠 시험 실행
        internal static Action<string> Log;   // 시험 diag 줄
        internal static Action First, Last;   // Plugin.Update 맨 앞·맨 뒤
        internal static Action<string> E072;  // E072 저장 전 줄(e072-save.txt)
    }
}
