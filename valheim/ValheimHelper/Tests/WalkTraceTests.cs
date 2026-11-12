using System;
using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using Xunit;

public class WalkTraceTests
{
    static WalkTrace.Sample S(float t, float x, float z, float goal, float speed = 1.6f, string job = "unload",
        float wall = 1f, int door = 0, bool seen = false, float body = -1f) =>
        new WalkTrace.Sample { T = t, X = x, Z = z, Goal = goal, Speed = speed, Job = job, Wall = wall, Door = door, Seen = seen, Body = body };

    static List<WalkTrace.Sample> Straight(int n, float step = 0.4f)
    {
        var l = new List<WalkTrace.Sample>();
        for (int i = 0; i <= n; i++) l.Add(S(i * 0.25f, i * step, 0f, (n - i) * step + 0.5f));
        return l;
    }

    [Fact]
    public void StraightWalk_PassesAll()
    {
        var r = WalkTrace.Analyze(Straight(20));
        Assert.Equal(1, r.Legs);
        Assert.InRange(r.WorstRatio, 0.99f, 1.01f);
        Assert.Equal(0, r.NoProgress);
        Assert.Equal(0, r.SharpTurns);
        Assert.True(r.Pass, r.ToString());
    }

    [Fact]
    public void RecordEndsMidLeg_StillCounts()
    {
        var l = Straight(20);
        l.RemoveAt(l.Count - 1); // 시험이 닿기 직전에 기록을 멈춤(남은 길 > Arrive)
        var r = WalkTrace.Analyze(l);
        Assert.Equal(1, r.Legs);
        Assert.InRange(r.WorstRatio, 0.99f, 1.01f);
    }

    [Fact]
    public void OtherFloor_BaselineIsPathAtStart()
    {
        // 계단을 꺾어 4m 위로: 걸음 10m, 수평 직선 7.1m(1.4배), 시작 때 남은 길 10.5m → 1.0 아래
        List<WalkTrace.Sample> Leg(float rise)
        {
            var l = new List<WalkTrace.Sample>();
            for (int i = 0; i <= 25; i++)
            {
                var a = S(i * 0.25f, System.Math.Min(i, 12) * 0.4f, System.Math.Max(0, i - 12) * 0.4f, 10.5f - i * 0.4f);
                a.Y = i * rise / 25f; l.Add(a);
            }
            return l;
        }
        var r = WalkTrace.Analyze(Leg(4f));
        Assert.Equal(1, r.Legs);
        Assert.True(r.WorstRatio < 1.3f, r.ToString());
        Assert.True(WalkTrace.Analyze(Leg(0f)).WorstRatio > 1.3f); // 같은 층이면 그대로 직선 기준
    }

    [Fact]
    public void Detour_RatioAndSharpTurn()
    {
        // 목표 (8,0) 곁을 z=3으로 지나쳐 x=11.2까지 갔다 되돌아옴 → 되돌기 1번(멀어진 3m 남짓)
        var l = new List<WalkTrace.Sample>();
        float t = 0f;
        void Go(float x, float z) { l.Add(S(t, x, z, (float)Math.Sqrt((x - 8f) * (x - 8f) + z * z), 3.2f)); t += 0.25f; }
        for (int i = 0; i <= 14; i++) Go(i * 0.8f, 3f);
        for (int i = 1; i <= 5; i++) Go(11.2f - i * 0.64f, 3f - i * 0.6f);
        var r = WalkTrace.Analyze(l);
        Assert.Equal(1, r.Legs);
        Assert.True(r.WorstRatio > 1.3f, r.ToString());
        Assert.Equal(1, r.SharpTurns);
        Assert.Single(r.TurnAt);
        Assert.StartsWith("3.50@11.2,3.0", r.TurnAt[0]);
        Assert.False(r.Pass);
    }

    [Fact]
    public void FastSharpTurn_NotCountedWhenGoalKeepsShrinking()
    {
        // 홱 도는 것 자체는 괜찮음(사용자 11-11): 뛰다가 135° 꺾어도 남은 길이 계속 줄면 0
        var l = new List<WalkTrace.Sample>();
        float goal = 20f;
        for (int i = 0; i <= 8; i++) { l.Add(S(i * 0.25f, i * 0.8f, 0f, goal, 3.2f)); goal -= 0.8f; }
        for (int i = 1; i <= 8; i++) { l.Add(S(2f + i * 0.25f, 6.4f - i * 0.56f, i * 0.56f, goal, 3.2f)); goal -= 0.8f; }
        Assert.Equal(0, WalkTrace.Analyze(l).SharpTurns);
    }

    [Fact]
    public void SmallOvershootAndReplan_NotCounted()
    {
        // 0.4m 지나침은 봐줌, 걷지 않고 남은 길이 20m 뛴 것(길 새로 받음)은 셈에서 뺌
        var l = new List<WalkTrace.Sample>();
        float t = 0f;
        void Add(float x, float goal) { l.Add(S(t, x, 0f, goal)); t += 0.25f; }
        for (int i = 0; i <= 10; i++) Add(i * 0.4f, 10f - i * 0.4f);
        Add(4.4f, 6.4f); Add(4.0f, 6.0f);           // 0.4m 지나쳐 되돌기
        Add(4.4f, 26f); Add(4.8f, 25.6f);           // 길 새로 받음(+20m)
        for (int i = 1; i <= 5; i++) Add(4.8f + i * 0.4f, 25.6f - i * 0.4f);
        Assert.Equal(0, WalkTrace.Analyze(l).SharpTurns);
    }
    [Fact]
    public void SlowTurnAround_NotSharp()
    {
        // 계단 위 끝: 거꾸로 돌아서도 남은 길이 계속 줄면 되돌기 아님
        var l = new List<WalkTrace.Sample>();
        for (int i = 0; i <= 6; i++) l.Add(S(i * 0.25f, i * 0.35f, 0f, 10f - i * 0.1f));
        for (int i = 1; i <= 6; i++) l.Add(S(1.5f + i * 0.25f, 2.1f - i * 0.2f, i * 0.3f, 9.4f - i * 0.36f));
        Assert.Equal(0, WalkTrace.Analyze(l).SharpTurns);
    }

    [Fact]
    public void NoProgress_NewPathWhileRunningNotCounted()
    {
        // 회차 450 vh_housetest: 뛰는 중 길을 새로 받아 남은 길이 4.5→7.4로 튐 → 그 뒤 줄어도 옛 최솟값 밑으로 안 가 진척 없음으로 셌음
        var l = Straight(4);
        float t = l[l.Count - 1].T, x = 1.6f, g = l[l.Count - 1].Goal + 3f;
        for (int i = 1; i <= 8; i++) l.Add(S(t + i * 0.25f, x + i * 0.4f, 0f, g - i * 0.4f, 3.5f));
        var r = WalkTrace.Analyze(l);
        Assert.Equal(0, r.NoProgress);
        Assert.Empty(r.StuckAt);
    }

    [Fact]
    public void NoProgress_CountedOncePerEpisode()
    {
        var l = Straight(4);
        float t = l[l.Count - 1].T, g = l[l.Count - 1].Goal + 2f;
        l[l.Count - 1] = S(t, 1.6f, 0f, g);
        for (int i = 1; i <= 8; i++) l.Add(S(t + i * 0.25f, 1.6f, 0f, g, 0f)); // 2초 제자리
        for (int i = 1; i <= 8; i++) l.Add(S(t + 2f + i * 0.25f, 1.6f + i * 0.4f, 0f, g - i * 0.4f));
        var r = WalkTrace.Analyze(l);
        Assert.Equal(1, r.NoProgress);
        Assert.True(r.Stand > 0.2f);
    }

    [Fact]
    public void Wall_Door_Teleport_Gait()
    {
        var l = new List<WalkTrace.Sample>
        {
            S(0f, 0f, 0f, 10f, wall: 0.2f),
            S(0.25f, 0.4f, 0f, 9.6f, wall: 0.2f, door: 1),
            S(0.5f, 0.4f, 0f, 9.6f, 0f, door: 1),
            S(0.75f, 0.4f, 0f, 9.6f, 0f, door: 1),
            S(1.0f, 0.4f, 0f, 9.6f, 0f, door: 1),
            S(1.25f, 1.6f, 0f, 8.4f, 4.8f),
            S(1.5f, 6.6f, 0f, 3.4f, seen: true), // 보는 중 5m 뜀
            S(1.75f, 7f, 0f, 3.2f),
            S(2.0f, 12f, 0f, 3f),                // 안 볼 때
        };
        var r = WalkTrace.Analyze(l);
        Assert.Equal(1, r.TeleSeen);
        Assert.Equal(1, r.TeleUnseen);
        Assert.Equal(1, r.DoorPauses);
        Assert.InRange(r.DoorPauseLongest, 0.74f, 0.76f);
        Assert.True(r.WallFrac > 0.3f);
        Assert.True(r.Run > 0f && r.Walk > 0f && r.Stand > 0f);
        Assert.False(r.Pass);
    }

    [Fact]
    public void Line_RoundTrips()
    {
        var a = S(1.25f, -3959.5f, -5829.25f, 4.5f, job: "craft smelt", door: 2, seen: true);
        a.Y = 36.4f;
        Assert.True(WalkTrace.TryParse(WalkTrace.Line(a), out var b));
        Assert.Equal(a.X, b.X, 2);
        Assert.Equal(a.Y, b.Y, 2);
        Assert.Equal("craft_smelt", b.Job);
        Assert.Equal(2, b.Door);
        Assert.True(b.Seen);
        Assert.False(WalkTrace.TryParse("# 머리", out _));
    }

    // 사용자 11-12: 벽 0.3m 안 걷기는 기준 밖이 아님 — 닿아(0.1m 안) 속도가 확 줄거나 튀어야 충돌
    [Fact]
    public void NearWallWalk_Passes()
    {
        var l = Straight(20);
        for (int i = 0; i < l.Count; i++) { var a = l[i]; a.Wall = 0.15f; l[i] = a; }
        var r = WalkTrace.Analyze(l);
        Assert.Equal(0, r.Bumps);
        Assert.Equal(0, r.WallRuns);
        Assert.True(r.Pass, r.ToString());
    }

    [Fact]
    public void ShortBump_CountedOnce()
    {
        var l = Straight(20);
        for (int i = 8; i <= 9; i++) { var a = l[i]; a.Wall = 0.05f; a.Speed = 0.6f; l[i] = a; }
        var r = WalkTrace.Analyze(l);
        Assert.Equal(1, r.Bumps);
        Assert.Single(r.BumpAt);
        Assert.Contains("벽", r.BumpAt[0]);
        Assert.False(r.Pass);
    }

    [Fact]
    public void BodyBump_DirectionFlip()
    {
        var l = Straight(10);
        // 5번째에서 다른 일꾼에 닿아 옆으로 튐(속도는 그대로)
        var a = l[5]; a.X = l[4].X; a.Z = 0.4f; a.Body = 0.02f; l[5] = a;
        var r = WalkTrace.Analyze(l);
        Assert.Equal(1, r.Bumps);
        Assert.Contains("몸", r.BumpAt[0]);
    }

    [Fact]
    public void WallRun_SlideWithoutProgress()
    {
        var l = new List<WalkTrace.Sample>();
        for (int i = 0; i <= 12; i++) l.Add(S(i * 0.25f, i * 0.3f, 0f, 6f - (i < 4 ? i * 0.3f : 1.2f), wall: i >= 4 ? 0.02f : 1f));
        var r = WalkTrace.Analyze(l);
        Assert.Equal(1, r.WallRuns);
        Assert.False(r.Pass);
    }

    [Fact]
    public void Line_RoundTripsBody_OldLineHasNone()
    {
        var s = S(1f, 2f, 3f, 4f, body: 0.5f);
        Assert.True(WalkTrace.TryParse(WalkTrace.Line(s), out var p));
        Assert.InRange(p.Body, 0.49f, 0.51f);
        Assert.True(WalkTrace.TryParse("1.00 2.00 0.00 3.00 1.60 4.00 1.00 0 0 unload", out var old));
        Assert.True(old.Body < 0f);
    }
}
