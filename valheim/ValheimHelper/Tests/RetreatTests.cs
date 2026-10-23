using System;
using ValheimHelper.Core.Npc;
using Xunit;
using Xunit.Abstractions;

public class RetreatTests
{
    readonly ITestOutputHelper m_out;
    public RetreatTests(ITestOutputHelper output) { m_out = output; }

    static RetreatWay Way(double hx, double hz, double ax, double az, bool chased, out double dx, out double dz)
    {
        int side = 0;
        return Retreat.Way(hx, hz, ax, az, chased, ref side, out dx, out dz);
    }

    [Fact]
    public void HomeFarAndFoeElsewhere_GoesHome()
    {
        // 거점은 서쪽 100m, 적은 북쪽(→ 남쪽으로 피함): 거점 쪽(온 길)
        Assert.Equal(RetreatWay.Home, Way(-100, 0, 0, -1, false, out double dx, out double dz));
        Assert.Equal(-1, dx, 6);
        Assert.Equal(0, dz, 6);
    }

    [Fact]
    public void NearHome_RunsAwayFromFoe()
    {
        Assert.Equal(RetreatWay.Away, Way(-5, 0, 1, 0, false, out double dx, out double dz));
        Assert.Equal(1, dx, 6);
        Assert.Equal(0, dz, 6);
    }

    [Fact]
    public void FoeOnHomeSide_GoesAroundTowardHome()
    {
        // 거점 서쪽 100m, 적은 서쪽(거점 쪽) 조금 북쪽 → 동쪽으로 곧장 가지 않고 남쪽으로 돌아감
        double n = Math.Sqrt(0.98 * 0.98 + 0.2 * 0.2), ax = 0.98 / n, az = -0.2 / n;
        Assert.Equal(RetreatWay.Around, Way(-100, 0, ax, az, false, out double dx, out double dz));
        Assert.Equal(1, dx * dx + dz * dz, 6);
        Assert.Equal(Retreat.AroundMinDot, dx * ax + dz * az, 6);
        Assert.True(dz < -0.9, $"거점 쪽 옆(남쪽)으로 {dz}");
    }

    [Fact]
    public void Chased_TurnsLessSoTheFoeCannotCatchUp()
    {
        // 거점이 적 반대쪽에서 90° 옆: 쫓기지 않으면 거점 쪽 그대로, 쫓기면 적 반대쪽에서 41°까지만
        Assert.Equal(RetreatWay.Home, Way(0, 100, 1, 0, false, out _, out _));
        Assert.Equal(RetreatWay.Around, Way(0, 100, 1, 0, true, out double dx, out double dz));
        Assert.Equal(Retreat.ChasedMinDot, dx, 6);
        Assert.True(dz > 0);
        // 거점이 적 반대쪽과 거의 같으면 쫓겨도 거점 쪽
        Assert.Equal(RetreatWay.Home, Way(100, 10, 1, 0, true, out _, out _));
    }

    [Fact]
    public void FoeExactlyOnHomeSide_KeepsLastSide()
    {
        int side = -1;
        Assert.Equal(RetreatWay.Around, Retreat.Way(-100, 0, 1, 0, false, ref side, out _, out double dz));
        Assert.Equal(-1, side);
        Assert.True(dz < 0);
        side = 1;
        Retreat.Way(-100, 0, 1, 0, false, ref side, out _, out dz);
        Assert.Equal(1, side);
        Assert.True(dz > 0);
    }

    /// <summary>
    /// 쫓김 흉내(Guard 꼴): 0.5초마다 판단, 싸울 생각인 적이 10m 안이면 새로 고름(쫓김), 다 가면 기다림(물러나 기다리기) —
    /// arriveRepick이면 다 간 뒤 적이 30m 안에 있으면 한 번 새로 고름(도망 끝 → 다시 물러남, 쫓김 아님).
    /// 적은 일꾼에게 곧장 온다(회색난쟁이: 60m 도망에 10~18m 뒤처짐 → 일꾼 7m/s, 적 5.3m/s 쯤).
    /// 거점 (0,0), 일꾼 (30,0), 적은 그 사이 8m. oldRule = 고치기 전 규칙(거점 쪽이 아니면 적 반대쪽으로 곧장).
    /// </summary>
    static (double maxHome, double nearestHome, double minGap) Chase(bool oldRule, double foeSpeed, bool arriveRepick, double seconds = 300)
    {
        double nx = 30, nz = 0, fx = 22, fz = 0, tx = nx, tz = nz;
        const double npcSpeed = 7, leg = 50, dt = 0.05;
        bool moving = false, arrived = false;
        int side = 0;
        double maxHome = 0, nearestHome = double.MaxValue, minGap = double.MaxValue, judge = 0;
        for (double t = 0; t < seconds; t += dt)
        {
            double gx = nx - fx, gz = nz - fz, gap = Math.Sqrt(gx * gx + gz * gz);
            judge -= dt;
            if (judge <= 0)
            {
                judge = 0.5;
                bool chased = gap < 10;
                if (chased || (arrived && gap < 30))
                {
                    arrived = false;
                    double ax = gx / gap, az = gz / gap, hd = Math.Sqrt(nx * nx + nz * nz), dx, dz;
                    RetreatWay way;
                    if (oldRule)
                    {
                        bool home = hd > Retreat.HomeNear && (-nx * ax - nz * az) / hd > Retreat.HomeMinDot;
                        way = home ? RetreatWay.Home : RetreatWay.Away;
                        dx = home ? -nx / hd : ax; dz = home ? -nz / hd : az;
                    }
                    else way = Retreat.Way(-nx, -nz, ax, az, chased, ref side, out dx, out dz);
                    if (way == RetreatWay.Home && hd < leg) { tx = 0; tz = 0; }
                    else { tx = nx + dx * leg; tz = nz + dz * leg; }
                    moving = true;
                }
            }
            if (moving)
            {
                double mx = tx - nx, mz = tz - nz, md = Math.Sqrt(mx * mx + mz * mz);
                if (md < 3) { moving = false; arrived = arriveRepick; }
                else { nx += mx / md * npcSpeed * dt; nz += mz / md * npcSpeed * dt; }
            }
            gx = nx - fx; gz = nz - fz; gap = Math.Sqrt(gx * gx + gz * gz);
            if (gap > 1.5) { fx += gx / gap * foeSpeed * dt; fz += gz / gap * foeSpeed * dt; }
            double h = Math.Sqrt(nx * nx + nz * nz);
            maxHome = Math.Max(maxHome, h);
            if (t > 1) { nearestHome = Math.Min(nearestHome, h); minGap = Math.Min(minGap, gap); }
        }
        return (maxHome, nearestHome, minGap);
    }

    [Theory]
    [InlineData(5.3, false)]
    [InlineData(5.3, true)]
    [InlineData(6.3, false)]
    [InlineData(6.3, true)]
    public void ChasedFromHomeSide_OldRuleDrifts_NewRuleStaysNearHome(double foeSpeed, bool arriveRepick)
    {
        var old = Chase(true, foeSpeed, arriveRepick);
        var now = Chase(false, foeSpeed, arriveRepick);
        m_out.WriteLine($"적 {foeSpeed}m/s 다 가면 다시 {arriveRepick} — 예전: 거점에서 최대 {old.maxHome:0}m, 가장 가까이 {old.nearestHome:0}m, 적과 최소 {old.minGap:0.0}m / 지금: 최대 {now.maxHome:0}m, 가장 가까이 {now.nearestHome:0}m, 적과 최소 {now.minGap:0.0}m");
        Assert.True(old.maxHome > 300, "흉내가 버그를 되살림(예전 규칙은 계속 멀어짐)");
        Assert.True(now.maxHome < 120, $"거점에서 최대 {now.maxHome:0}m");
        Assert.True(now.nearestHome < 30, $"거점 가까이 돌아옴 {now.nearestHome:0}m"); // 적이 10~30m 뒤를 계속 따라오면 거점 둘레를 돈다
        Assert.True(now.minGap > 3, $"쫓아오는 적에게 붙잡히지 않음 {now.minGap:0.0}m");
    }
}
