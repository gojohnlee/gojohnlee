using System;
using System.Collections.Generic;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 모드 부하 재기(사용자 11-13 00:15): VH_FRAMESTATS=1을 켠 판에서만 프레임 시간을 모아
    /// 30초마다 BepInEx 로그에 "프레임 n장 · 평균 · 최악 · 1% 최악"을 한 줄 남긴다.
    /// 같은 판을 모드 끔·켬으로 두 번 재서 비교한다. 끄면 Tick은 아무 일도 안 한다.
    /// </summary>
    public static class FrameLog
    {
        const double ReportSecs = 30.0;
        static readonly bool On = Environment.GetEnvironmentVariable("VH_FRAMESTATS") == "1";
        static readonly List<double> Frames = new List<double>();
        static double sinceReport;

        public static void Tick()
        {
            if (!On) return;
            double dt = UnityEngine.Time.unscaledDeltaTime;
            Frames.Add(dt);
            sinceReport += dt;
            if (sinceReport < ReportSecs) return;
            Plugin.Log?.LogInfo("[모드 부하] " + FrameStats.ToText(FrameStats.Summarize(Frames)));
            Frames.Clear();
            sinceReport = 0;
        }
    }
}
