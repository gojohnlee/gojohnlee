using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 일 사이 빈틈 재기(사용자 11-12 16:24 "최종 실전 시험 덧붙임"): 이 PC가 맡은 일꾼마다 0.1초에 한 번 지금 동작(일.단계, 없으면 쉼)과
    /// 가만히 섬(거의 안 움직이고 휘두르지도 않음)을 Core GapLog에 넘긴다. 합계만 두므로 크기가 안 늘어난다.
    /// 실플레이: 설정 Features.GapLog(기본 켬) — 5분마다 gap-stats.txt에 표(최악 1초 넘는 줄만). 시험은 Log에서 바로 표를 뽑는다.
    /// </summary>
    static class GapWatch
    {
        const float Check = 0.1f, SaveEvery = 300f, StillSpeed = 0.3f;
        const string FileName = "gap-stats.txt";

        public static readonly GapLog Log = new GapLog();
        static readonly HashSet<string> s_seen = new HashSet<string>(), s_last = new HashSet<string>();
        static float s_tick, s_nextSave;

        public static void Tick(float dt)
        {
            if (!Features.GapLog) return;
            s_tick += dt;
            if (s_tick < Check) return;
            s_tick = 0f;
            double now = Time.time;
            s_seen.Clear();
            foreach (var n in WorkerNpc.All)
            {
                if (n == null || n.Ai == null || n.Zdo == null || !n.Zdo.IsOwner() || n.IsDead()) continue;
                s_seen.Add(n.NpcName);
                Log.Tick(n.NpcName, Step(n.Ai), n.GetVelocity().magnitude < StillSpeed && !n.InAttack(), now);
            }
            foreach (var name in s_last)
                if (!s_seen.Contains(name)) Log.Close(name, now); // 죽음·구역 내림: 서 있던 것을 여기까지로
            s_last.Clear();
            s_last.UnionWith(s_seen);
            if (TestHook.Running || Time.realtimeSinceStartup < s_nextSave) return;
            s_nextSave = Time.realtimeSinceStartup + SaveEvery;
            Save();
        }

        static string Step(WorkerAI ai)
        {
            if (ai.GuardBusy) return "싸움";
            var job = ai.Job;
            if (job == null) return "쉼(" + ai.Current + ")";
            string name = job.GetType().Name;
            if (name.EndsWith("Job")) name = name.Substring(0, name.Length - 3);
            // 멀리선 이동 생략(모든 플레이어 64m 밖)으로 서서 기다림은 아무도 못 보는 설계대로의 기다림 — 따로 센다(회차 703)
            if (job is GatherJob g && g.FarMapPos != null) return name + ".먼길생략";
            return name + "." + (job is PlanJob plan && plan.InnerStep != null ? plan.InnerStep : job.StepName);
        }

        /// <summary>지금까지의 표(서 있는 것도 지금까지로 닫아 넣음). minWorst초보다 최악이 짧은 줄은 뺀다.</summary>
        public static List<string> Table(double minWorst)
        {
            Log.CloseAll(Time.time);
            return GapLog.Table(Log.Rows(), minWorst);
        }

        static void Save()
        {
            var rows = Log.Rows();
            if (rows.Count == 0) return;
            try
            {
                var lines = new List<string> { $"일 사이 빈틈(일꾼이 가만히 선 시간, 최악 1초 넘는 동작만) — {DateTime.Now:MM-dd HH:mm}", "" };
                lines.AddRange(GapLog.Table(rows, 1.0));
                File.WriteAllLines(Path.Combine(Plugin.Dir, FileName), lines);
            }
            catch (Exception e) { Plugin.Log.LogWarning(FileName + " 저장 실패: " + e.Message); }
        }
    }
}
