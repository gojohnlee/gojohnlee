using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 집 안 걸음 기록(사용자 11-11 "집 안에서 사람처럼" ①): 0.25초마다 한 줄(behavior-house.txt) → 숫자 요약.
    /// 기준: 길/직선 ≤1.3·진척 없음 0·벽 ≤5%·급회전 0·열린 문 멈칫 ≤0.4초·보는 중 순간이동 0.
    /// </summary>
    public static class WalkTrace
    {
        public const float StandSpeed = 0.3f, RunSpeed = 2.5f, WallNear = 0.3f, TeleportStep = 3f,
            StuckSecs = 1f, SharpTurn = 90f, TurnStep = 0.1f, DoorPauseMax = 0.4f, MinLeg = 2f, Arrive = 1f;

        /// <summary>한 줄. Goal = 남은 길 길이(m, 목표 없으면 음수), Wall = 가장 가까운 벽(m, 모르면 음수), Door 0 없음·1 열린 문 곁·2 닫힌 문 곁.</summary>
        public struct Sample
        {
            public float T, X, Y, Z, Speed, Goal, Wall;
            public int Door;
            public bool Seen;
            public string Job;
        }

        public sealed class Report
        {
            public int Legs, NoProgress, SharpTurns, DoorPauses, TeleSeen, TeleUnseen, Moving;
            public float WorstRatio, MeanRatio, WallFrac, DoorPauseLongest, Stand, Walk, Run;

            public bool Pass => WorstRatio <= 1.3f && NoProgress == 0 && WallFrac <= 0.05f && SharpTurns == 0
                && DoorPauseLongest <= DoorPauseMax && TeleSeen == 0;

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "길/직선 최악 {0:0.00}·평균 {1:0.00}({2}구간) 진척없음 {3} 벽 {4:0.0}% 급회전 {5} 문멈칫 {6}번·최장 {7:0.00}초 " +
                "서기 {8:0}%·걷기 {9:0}%·뛰기 {10:0}% 순간이동 봄 {11}·안봄 {12} → {13}",
                WorstRatio, MeanRatio, Legs, NoProgress, WallFrac * 100f, SharpTurns, DoorPauses, DoorPauseLongest,
                Stand * 100f, Walk * 100f, Run * 100f, TeleSeen, TeleUnseen, Pass ? "기준 통과" : "기준 밖");
        }

        /// <summary>한 줄 꼴: t x y z speed goal wall door seen(0/1) job(빈칸은 _, 없으면 -).</summary>
        public static string Line(Sample s) => string.Format(CultureInfo.InvariantCulture,
            "{0:0.00} {1:0.00} {2:0.00} {3:0.00} {4:0.00} {5:0.00} {6:0.00} {7} {8} {9}",
            s.T, s.X, s.Y, s.Z, s.Speed, s.Goal, s.Wall, s.Door, s.Seen ? 1 : 0,
            string.IsNullOrEmpty(s.Job) ? "-" : s.Job.Replace(' ', '_'));

        public static bool TryParse(string line, out Sample s)
        {
            s = default;
            if (string.IsNullOrWhiteSpace(line) || line[0] == '#') return false;
            var p = line.Split(' ');
            if (p.Length < 10) return false;
            var f = new float[7];
            for (int i = 0; i < 7; i++)
                if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return false;
            if (!int.TryParse(p[7], out int door)) return false;
            s = new Sample { T = f[0], X = f[1], Y = f[2], Z = f[3], Speed = f[4], Goal = f[5], Wall = f[6],
                Door = door, Seen = p[8] == "1", Job = p[9] == "-" ? "" : p[9] };
            return true;
        }

        public static Report Analyze(IList<Sample> s)
        {
            var r = new Report();
            if (s.Count == 0) return r;
            int stand = 0, walk = 0, run = 0, wall = 0;
            foreach (var a in s)
            {
                if (a.Speed < StandSpeed) stand++;
                else
                {
                    if (a.Speed <= RunSpeed) walk++; else run++;
                    if (a.Wall >= 0f && a.Wall < WallNear) wall++;
                }
            }
            r.Moving = walk + run;
            r.Stand = stand / (float)s.Count; r.Walk = walk / (float)s.Count; r.Run = run / (float)s.Count;
            r.WallFrac = r.Moving > 0 ? wall / (float)r.Moving : 0f;

            // 구간: 같은 일·목표 있음, 닿거나 일이 바뀌거나 순간이동하면 끝. 진척 = 남은 길이 가장 짧았던 값보다 줄어듦.
            int legStart = -1;
            float legPath = 0f, ratioSum = 0f, best = 0f, bestT = 0f, pauseStart = -1f;
            bool stuckNow = false;
            double lastDir = double.NaN;
            void CloseLeg(Sample end)
            {
                float straight = Flat(s[legStart], end);
                if (straight < MinLeg) return;
                float ratio = legPath / straight;
                r.Legs++; ratioSum += ratio;
                if (ratio > r.WorstRatio) r.WorstRatio = ratio;
            }
            for (int i = 0; i < s.Count; i++)
            {
                var a = s[i];
                bool tele = false;
                if (i > 0)
                {
                    var b = s[i - 1];
                    float step = Flat(a, b);
                    if (step > TeleportStep)
                    {
                        tele = true;
                        if (a.Seen || b.Seen) r.TeleSeen++; else r.TeleUnseen++;
                        lastDir = double.NaN;
                    }
                    else if (step >= TurnStep)
                    {
                        double dir = Math.Atan2(a.Z - b.Z, a.X - b.X) * 180.0 / Math.PI;
                        if (!double.IsNaN(lastDir))
                        {
                            double d = Math.Abs(dir - lastDir) % 360.0;
                            if (d > 180.0) d = 360.0 - d;
                            if (d > SharpTurn) r.SharpTurns++;
                        }
                        lastDir = dir;
                    }
                    if (legStart >= 0 && !tele) legPath += step;
                }

                bool inLeg = a.Goal >= 0f && !string.IsNullOrEmpty(a.Job);
                if (legStart >= 0 && (tele || !inLeg || a.Job != s[legStart].Job || a.Goal < Arrive)) { if (!tele) CloseLeg(a); legStart = -1; }
                if (legStart < 0 && inLeg && a.Goal >= Arrive)
                {
                    legStart = i; legPath = 0f; best = a.Goal; bestT = a.T; stuckNow = false;
                }
                else if (legStart >= 0)
                {
                    if (a.Goal < best - 0.05f) { best = a.Goal; bestT = a.T; stuckNow = false; }
                    else if (!stuckNow && a.T - bestT > StuckSecs) { r.NoProgress++; stuckNow = true; }
                }

                if (a.Door == 1 && a.Speed < StandSpeed && inLeg)
                {
                    if (pauseStart < 0f) pauseStart = a.T;
                }
                else if (pauseStart >= 0f) EndPause(r, a.T - pauseStart, ref pauseStart);
            }
            // 기록이 구간 중에 끝남(시험이 닿음·시간 끝으로 기록을 멈춤 — 회차 404 집 시험은 그래서 0구간)도 그 자리까지 한 구간으로 셈
            if (legStart >= 0) CloseLeg(s[s.Count - 1]);
            if (pauseStart >= 0f) EndPause(r, s[s.Count - 1].T - pauseStart, ref pauseStart);
            r.MeanRatio = r.Legs > 0 ? ratioSum / r.Legs : 0f;
            return r;
        }

        static void EndPause(Report r, float len, ref float start)
        {
            start = -1f;
            if (len <= 0f) return;
            if (len > r.DoorPauseLongest) r.DoorPauseLongest = len;
            if (len > DoorPauseMax) r.DoorPauses++;
        }

        static float Flat(Sample a, Sample b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
