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
    /// 배속이 부하를 바꾸므로 VH_SPEED=1로, 최소화 판은 30fps 고정이라 VH_FULLFPS=1로 돌린다. TestKit.Setup으로 일꾼 둘+호위병(사용자 11-15), VH_LOAD_AT=house|forest면 그 자리(LoadSpot, 끔 판과 같은 자리) — house는 VH_HOUSE=1도 같이.
    /// VH_LOAD_RUNS=n이면 재기를 n번 되풀이해 칸별 중간값(회마다 diag 줄). VH_LOAD_WORK=1이면 쉬고 난 뒤 일꾼마다 채집(나무·돌 번갈아 200)을 시키고 10초 뒤부터 잰다(일함 판, 끝에 "!멈춰").
    /// </summary>
    static class LoadTest
    {
        public static void Register() => TestRunner.Register("vh_loadtest", "모드 부하: 모드 켬 판 프레임 재기", Run);

        static readonly bool Work = Environment.GetEnvironmentVariable("VH_LOAD_WORK") == "1";

        static IEnumerator Run()
        {
            if (LoadSpot.At == "house") TestRunner.House = true; // VH_HOUSE=1 없이도 집 곁 거점
            var w = new TestKit.World { House = LoadSpot.At == "house" };
            try { yield return Body(w); }
            finally { TestKit.Restore(w); }
        }

        static IEnumerator Body(TestKit.World w)
        {
            string where = "들어간 자리";
            Vector3 pos = default; Quaternion rot = default;
            if (LoadSpot.At != "" && !LoadSpot.Find(out pos, out rot, out where))
            {
                TestRunner.Log($"자리 못 찾음: {where}");
                TestRunner.Log("판정: FAIL (0/1)");
                TestRunner.Finish();
                yield break;
            }
            // 사용자 11-15: 일꾼 둘+호위병. 숲은 먼저 옮겨 거기에 거점, 집은 Setup이 집 곁 빈 땅에 거점을 잡은 뒤 재는 자리로
            if (LoadSpot.At == "forest") yield return LoadSpot.Go(pos, rot);
            yield return TestKit.Setup(w);
            yield return TestKit.SpawnGuard();
            if (LoadSpot.At != "") yield return LoadSpot.Go(pos, rot);
            TestRunner.Log($"재는 자리 {where} · 일꾼 {WorkerNpc.All.Count}명(호위병 {(WorkerNpc.TheGuard != null ? "있음" : "없음")})");
            yield return new WaitForSecondsRealtime(Bare.WaitSecs);
            if (Work)
            {
                int i = 0;
                foreach (var npc in new[] { w.Ragnar, w.Floki })
                    if (npc != null) GatherTest.Say($"!{npc.NpcName} {(i++ % 2 == 0 ? "나무" : "돌")} 200", null);
                yield return new WaitForSecondsRealtime(10f);
            }
            int busyStart = Busy();
            var runs = new List<FrameStats.Summary>();
            for (int run = 1; run <= Bare.Runs; run++)
            {
                var frames = new List<double>();
                float measured = 0f;
                while (measured < Bare.MeasureSecs)
                {
                    yield return null;
                    frames.Add(Time.unscaledDeltaTime);
                    measured += Time.unscaledDeltaTime;
                }
                runs.Add(FrameStats.Summarize(frames));
                if (Bare.Runs > 1) TestRunner.Log($"프레임 {run}/{Bare.Runs}번째: {FrameStats.ToText(runs[run - 1])}");
            }
            var med = FrameStats.Median(runs);
            string text = FrameStats.ToText(med) + (Bare.Runs > 1 ? $" (중간값 {Bare.Runs}번)" : "");
            string who = $"{where} 일꾼 {WorkerNpc.All.Count}명 {(Work ? "일함" : "쉼")}(일 중 시작 {busyStart}·끝 {Busy()})";
            Plugin.Log.LogInfo($"[모드 부하] 모드 켬 {who} " + text);
            TestRunner.Log($"프레임: {who} · {text}");
            // 일함 판은 재는 동안 일꾼이 일하고 있어야 비교가 맞다
            bool ok = med.Count > 0 && (!Work || busyStart > 0) && WorkerNpc.TheGuard != null;
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
