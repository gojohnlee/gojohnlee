using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using Xunit;

public class WalkTraceTests
{
    static WalkTrace.Sample S(float t, float x, float z, float goal, float speed = 1.6f, string job = "unload",
        float wall = 1f, int door = 0, bool seen = false) =>
        new WalkTrace.Sample { T = t, X = x, Z = z, Goal = goal, Speed = speed, Job = job, Wall = wall, Door = door, Seen = seen };

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
        var l = new List<WalkTrace.Sample>();
        float t = 0f, goal = 20f;
        void Go(float x, float z) { l.Add(S(t, x, z, goal)); t += 0.25f; goal -= 0.4f; }
        for (int i = 0; i < 10; i++) Go(i * 0.4f, 0f);       // 동쪽
        for (int i = 0; i < 10; i++) Go(4f, i * 0.4f);       // 북쪽(90°는 급회전 아님)
        for (int i = 0; i < 6; i++) Go(4f - i * 0.4f, 4f);   // 서쪽
        for (int i = 0; i < 10; i++) Go(2f + i * 0.2f, 4f - i * 0.4f); // 남동쪽으로 꺾음(>90°)
        l.Add(S(t, 4f, 0f, 0.5f));
        var r = WalkTrace.Analyze(l);
        Assert.Equal(1, r.Legs);
        Assert.True(r.WorstRatio > 1.3f, r.ToString());
        Assert.True(r.SharpTurns >= 1, r.ToString());
        Assert.False(r.Pass);
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
}
