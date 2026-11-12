using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 집 안 걸음 기록(사용자 11-11 "집 안에서 사람처럼" ①): 0.25초마다 한 줄(behavior-house.txt) → 숫자 요약.
    /// 기준: 길/직선 ≤1.3·진척 없음 0·짧은 충돌 0·벽에 대고 달리기 0(벽 0.3m 안 걷기는 괜찮음, 벽 %는 참고 — 사용자 11-12)·급회전 0(= 헛걸음 되돌기: 남은 길이 늘어나는 쪽으로 0.5m 넘게 걸은 뒤 다시 줄어듦 — 홱 도는 각도·속도는 안 셈, 사용자 11-11)·열린 문 멈칫 ≤0.4초·보는 중 순간이동 0.
    /// </summary>
    public static class WalkTrace
    {
        public const float StandSpeed = 0.3f, RunSpeed = 2.5f, WallNear = 0.3f, TeleportStep = 3f,
            StuckSecs = 1f, Touch = 0.1f, BumpFrom = 1f, BumpTurn = 60f, WallRunSecs = 1f, AwayMin = 0.5f, TurnStep = 0.1f, DoorPauseMax = 0.4f, MinLeg = 2f, Arrive = 1f, FloorRise = 1.3f;

        /// <summary>한 줄. Goal = 남은 길 길이(m, 목표 없으면 음수), Wall = 가장 가까운 벽(m, 모르면 음수), Body = 가장 가까운 다른 일꾼·플레이어와 몸 사이(m, 없으면 음수), Door 0 없음·1 열린 문 곁·2 닫힌 문 곁.</summary>
        public struct Sample
        {
            public float T, X, Y, Z, Speed, Goal, Wall, Body;
            public int Door;
            public bool Seen;
            public string Job;
        }

        public sealed class Report
        {
            public int Legs, NoProgress, Bumps, WallRuns, SharpTurns, DoorPauses, TeleSeen, TeleUnseen, Moving;
            public float WorstRatio, MeanRatio, WallFrac, DoorPauseLongest, Stand, Walk, Run;
            /// <summary>급회전(되돌기) 자리 "t@x,z,y 멀어진m"(앞 8개, t = 되돌기 시작).</summary>
            public readonly List<string> TurnAt = new List<string>();
            public readonly List<string> StuckAt = new List<string>(); // 진척 없음 자리 8개까지

            /// <summary>짧은 충돌 자리 "t@x,z,y 벽|몸 속도→속도 방향°"(앞 8개).</summary>
            public readonly List<string> BumpAt = new List<string>();

            public bool Pass => WorstRatio <= 1.3f && NoProgress == 0 && Bumps == 0 && WallRuns == 0 && SharpTurns == 0
                && DoorPauseLongest <= DoorPauseMax && TeleSeen == 0;

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "길/직선 최악 {0:0.00}·평균 {1:0.00}({2}구간) 진척없음 {3} 충돌 {14} 벽달리기 {15} 벽 {4:0.0}%(참고) 급회전 {5} 문멈칫 {6}번·최장 {7:0.00}초 " +
                "서기 {8:0}%·걷기 {9:0}%·뛰기 {10:0}% 순간이동 봄 {11}·안봄 {12} → {13}",
                WorstRatio, MeanRatio, Legs, NoProgress, WallFrac * 100f, SharpTurns, DoorPauses, DoorPauseLongest,
                Stand * 100f, Walk * 100f, Run * 100f, TeleSeen, TeleUnseen, Pass ? "기준 통과" : "기준 밖", Bumps, WallRuns);
        }

        /// <summary>한 줄 꼴: t x y z speed goal wall door seen(0/1) job(빈칸은 _, 없으면 -) body(11-12부터, 옛 기록엔 없음).</summary>
        public static string Line(Sample s) => string.Format(CultureInfo.InvariantCulture,
            "{0:0.00} {1:0.00} {2:0.00} {3:0.00} {4:0.00} {5:0.00} {6:0.00} {7} {8} {9} {10:0.00}",
            s.T, s.X, s.Y, s.Z, s.Speed, s.Goal, s.Wall, s.Door, s.Seen ? 1 : 0,
            string.IsNullOrEmpty(s.Job) ? "-" : s.Job.Replace(' ', '_'), s.Body);

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
                Door = door, Seen = p[8] == "1", Job = p[9] == "-" ? "" : p[9], Body = -1f };
            if (p.Length > 10 && float.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float body)) s.Body = body;
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
            bool stuckNow = false, bumpNow = false, runNow = false;
            float runStart = -1f;
            float away = 0f; // 구간 안에서 남은 길이 늘어나는 쪽으로 걸은 거리(되돌기 셈)
            void CloseLeg(Sample end)
            {
                float straight = Flat(s[legStart], end);
                if (straight < MinLeg) return;
                // 다른 층(높이 차 1.3m 넘음)이면 기준 = 시작 때 남은 길(계단 따라가는 길) — 직선으론 계단을 돌아가는 오름이 늘 5~6배(회차 406)
                if (Math.Abs(end.Y - s[legStart].Y) > FloorRise && s[legStart].Goal > straight) straight = s[legStart].Goal;
                float ratio = legPath / straight;
                r.Legs++; ratioSum += ratio;
                if (ratio > r.WorstRatio) r.WorstRatio = ratio;
            }
            // 짧은 충돌(사용자 11-12): 걷던(1m/s 넘게) 몸이 무언가에 닿은(0.1m 안) 순간 속도가 절반 밑으로 줄거나 방향이 60° 넘게 튐 — 닿음이 풀릴 때까지 한 번.
            // 벽에 대고 달리기: 벽에 닿은 채 움직이는데 남은 길이 걸은 거리의 30%도 안 줄기를 1초 넘게.
            void Contact(int i, float step)
            {
                Sample a = s[i], b = s[i - 1];
                bool wallTouch = a.Wall >= 0f && a.Wall < Touch, bodyTouch = a.Body >= 0f && a.Body < Touch;
                if (!wallTouch && !bodyTouch) bumpNow = false;
                else if (!bumpNow && a.Door == 0 && b.Speed >= BumpFrom)
                {
                    float turn = 0f;
                    if (i >= 2 && step >= TurnStep && Flat(b, s[i - 2]) >= TurnStep)
                    {
                        double h0 = Math.Atan2(b.Z - s[i - 2].Z, b.X - s[i - 2].X), h1 = Math.Atan2(a.Z - b.Z, a.X - b.X);
                        turn = (float)(Math.Abs((h1 - h0 + 3 * Math.PI) % (2 * Math.PI) - Math.PI) * 180.0 / Math.PI);
                    }
                    if (a.Speed < b.Speed * 0.5f || turn > BumpTurn)
                    {
                        r.Bumps++; bumpNow = true;
                        if (r.BumpAt.Count < 8)
                            r.BumpAt.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00}@{1:0.0},{2:0.0},{3:0.0} {4} {5:0.0}→{6:0.0} {7:0}°",
                                a.T, a.X, a.Z, a.Y, bodyTouch ? "몸" : "벽", b.Speed, a.Speed, turn));
                    }
                }
                bool sliding = wallTouch && a.Speed >= StandSpeed && a.Goal >= 0f && b.Goal >= 0f && b.Goal - a.Goal < step * 0.3f;
                if (!sliding) { runStart = -1f; runNow = false; }
                else if (runStart < 0f) runStart = b.T;
                else if (!runNow && a.T - runStart > WallRunSecs) { r.WallRuns++; runNow = true; }
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
                    }
                    else if (legStart >= 0 && step >= TurnStep && a.Job == b.Job && a.Goal >= 0f && b.Goal >= 0f)
                    {
                        // 남은 길이 걸은 거리보다 훨씬 늘면 길을 새로 받은 것(걸음 탓 아님) — 셈에서 뺌
                        float dg = a.Goal - b.Goal;
                        if (dg > 0.05f && dg <= step + 0.3f) away += step;
                        else if (dg < -0.05f)
                        {
                            if (away > AwayMin)
                            {
                                r.SharpTurns++;
                                if (r.TurnAt.Count < 8)
                                    r.TurnAt.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00}@{1:0.0},{2:0.0},{3:0.0} {4:0.0}m", b.T, b.X, b.Z, b.Y, away));
                            }
                            away = 0f;
                        }
                    }
                    if (legStart >= 0 && !tele) { legPath += step; Contact(i, step); }
                }

                bool inLeg = a.Goal >= 0f && !string.IsNullOrEmpty(a.Job);
                if (legStart >= 0 && (tele || !inLeg || a.Job != s[legStart].Job || a.Goal < Arrive)) { if (!tele) CloseLeg(a); legStart = -1; }
                if (legStart < 0 && inLeg && a.Goal >= Arrive)
                {
                    legStart = i; legPath = 0f; best = a.Goal; bestT = a.T; stuckNow = false; away = 0f; bumpNow = runNow = false; runStart = -1f;
                }
                else if (legStart >= 0)
                {
                    if (a.Goal < best - 0.05f) { best = a.Goal; bestT = a.T; stuckNow = false; }
                    // 남은 길이 걸은 거리보다 훨씬 늘면 길을 새로 받은 것 — 그 값부터 다시 잼(회차 450: 뛰는 중 새 길을 진척 없음으로 셌음)
                    else if (a.Goal - s[i - 1].Goal > Flat(a, s[i - 1]) + 0.3f) { best = a.Goal; bestT = a.T; stuckNow = false; }
                    else if (!stuckNow && a.T - bestT > StuckSecs)
                    {
                        r.NoProgress++; stuckNow = true;
                        if (r.StuckAt.Count < 8)
                            r.StuckAt.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00}~{1:0.00}@{2:0.0},{3:0.0},{4:0.0} 남은 길 {5:0.0} 속도 {6:0.0} {7}", bestT, a.T, a.X, a.Z, a.Y, a.Goal, a.Speed, a.Job));
                    }
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
