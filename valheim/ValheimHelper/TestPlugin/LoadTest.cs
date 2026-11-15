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
    /// 배속이 부하를 바꾸므로 VH_SPEED=1로, 최소화 판은 30fps 고정이라 VH_FULLFPS=1로 돌린다. 일꾼 수는 월드에 있는 그대로.
    /// VH_LOAD_WORK=1이면 쉬고 난 뒤 일꾼마다 채집(나무·돌 번갈아 200)을 시키고 10초 뒤부터 잰다(일함 판, 끝에 "!멈춰").
    /// </summary>
    static class LoadTest
    {
        public static void Register() => TestRunner.Register("vh_loadtest", "모드 부하: 모드 켬 판 프레임 재기", Run);

        static readonly bool Work = Environment.GetEnvironmentVariable("VH_LOAD_WORK") == "1";

        static IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(Bare.WaitSecs);
            if (Work)
            {
                int i = 0;
                foreach (var npc in new List<WorkerNpc>(WorkerNpc.All))
                    GatherTest.Say($"!{npc.NpcName} {(i++ % 2 == 0 ? "나무" : "돌")} 200", null);
                yield return new WaitForSecondsRealtime(10f);
            }
            int busyStart = Busy();
            var frames = new List<double>();
            float measured = 0f;
            while (measured < Bare.MeasureSecs)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime);
                measured += Time.unscaledDeltaTime;
            }
            string text = FrameStats.ToText(FrameStats.Summarize(frames));
            string who = $"일꾼 {WorkerNpc.All.Count}명 {(Work ? "일함" : "쉼")}(일 중 시작 {busyStart}·끝 {Busy()})";
            Plugin.Log.LogInfo($"[모드 부하] 모드 켬 {who} " + text);
            TestRunner.Log($"프레임: {who} · {text}");
            // 일함 판은 재는 동안 일꾼이 일하고 있어야 비교가 맞다
            bool ok = frames.Count > 0 && (!Work || busyStart > 0);
            TestRunner.Log($"판정: {(ok ? "PASS" : "FAIL")} (1/1)");
            if (Work) GatherTest.Say("!멈춰", null);
            TestRunner.Finish();
        }

        static int Busy()
        {
            int n = 0;
            foreach (var npc in WorkerNpc.All)
                if (npc != null && npc.Ai?.Job != null) n++;
            return n;
        }
    }
}
