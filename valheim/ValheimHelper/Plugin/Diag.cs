using System;

namespace ValheimHelper
{
    /// <summary>
    /// 진단 로그(줄 머리 "DIAG "): 시험 모드(ValheimHelperTest.dll)가 Sink를 걸 때만 로그에 남는다(사용자 11-15) — 본 모드만 깔면 DIAG 줄 0.
    /// </summary>
    static class Diag
    {
        internal static Action<string> Sink;
        internal static void Log(string line) => Sink?.Invoke(line);
    }
}
