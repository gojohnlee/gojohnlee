using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// vh_loadtest (모드 부하 재기 모드 켬 판): 모드 끔 판(Bare, VH_BARE=1)과 같은 자리·같은 시간으로
    /// VH_BARE_WAIT초(기본 20) 쉬고 VH_BARE_SECS초(기본 60) 프레임 시간을 재 "[모드 부하] 모드 켬 …"과 diag "프레임:" 줄에 남긴다.
    /// 배속이 부하를 바꾸므로 VH_SPEED=1로, 최소화 판은 30fps 고정이라 VH_MIN=0으로 돌린다. 일꾼 수는 월드에 있는 그대로.
    /// </summary>
    static class LoadTest
    {
        public static void Register() => TestRunner.Register("vh_loadtest", "모드 부하: 모드 켬 판 프레임 재기", Run);

        static IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(Bare.WaitSecs);
            var frames = new List<double>();
            float measured = 0f;
            while (measured < Bare.MeasureSecs)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime);
                measured += Time.unscaledDeltaTime;
            }
            string text = FrameStats.ToText(FrameStats.Summarize(frames));
            Plugin.Log.LogInfo($"[모드 부하] 모드 켬 일꾼 {WorkerNpc.All.Count}명 " + text);
            TestRunner.Log($"프레임: 일꾼 {WorkerNpc.All.Count}명 · {text}");
            TestRunner.Log($"판정: {(frames.Count > 0 ? "PASS" : "FAIL")} (1/1)");
            TestRunner.Finish();
        }
    }
}
