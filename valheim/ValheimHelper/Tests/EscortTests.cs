using ValheimHelper.Core.Npc;
using Xunit;

public class EscortTests
{
    [Fact]
    public void WaitsUntilNearThenLeads()
    {
        var e = new Escort();
        Assert.Equal(EscortAct.Wait, e.Tick(1f, 30f));
        Assert.Equal("escort.wait", e.Say);
        Assert.Equal(EscortAct.Wait, e.Tick(1f, 15f));
        Assert.Null(e.Say); // 한 번만 말함
        Assert.Equal(EscortAct.Go, e.Tick(1f, 7f));
        Assert.Equal(Escort.Phase.Leading, e.Now);
    }

    [Fact]
    public void GivesUpWhenNobodyComes()
    {
        var e = new Escort();
        for (int i = 0; i < 60; i++) Assert.Equal(EscortAct.Wait, e.Tick(1f, 50f));
        Assert.Equal(EscortAct.GiveUp, e.Tick(1f, 50f));
        Assert.Equal("escort.giveup", e.Say);
        Assert.Equal(EscortAct.Go, e.Tick(1f, 50f)); // 그 뒤로는 혼자 감
        Assert.Null(e.Say);
    }

    [Fact]
    public void PausesPastFarAndResumesWhenClose()
    {
        var e = new Escort();
        e.Tick(1f, 5f);
        Assert.Equal(EscortAct.Go, e.Tick(1f, 19f));
        Assert.Equal(EscortAct.Wait, e.Tick(1f, 21f));
        Assert.Equal("escort.far", e.Say);
        Assert.Equal(EscortAct.Wait, e.Tick(1f, 15f)); // 12m 안까지 와야 다시 감
        Assert.Null(e.Say);
        Assert.Equal(EscortAct.Go, e.Tick(1f, 11f));
        Assert.Equal(EscortAct.Wait, e.Tick(1f, 25f));
        Assert.Null(e.Say); // 30초 안엔 다시 안 말함
    }

    [Fact]
    public void PausedTooLongGivesUp()
    {
        var e = new Escort();
        e.Tick(1f, 5f);
        for (int i = 0; i < 91; i++) Assert.Equal(EscortAct.Wait, e.Tick(1f, 30f)); // 첫 칸은 멈춤으로 바뀌기만
        Assert.Equal(EscortAct.GiveUp, e.Tick(1f, 30f));
    }

    [Fact]
    public void GoneGivesUpAtOnce()
    {
        var e = new Escort();
        e.Tick(1f, 5f);
        Assert.Equal(EscortAct.GiveUp, e.Tick(1f, -1f));
        e = new Escort();
        e.Tick(1f, 5f);
        Assert.Equal(EscortAct.GiveUp, e.Tick(1f, 250f)); // 같이 가다 200m 넘게 벌어짐(포탈 등)
    }

    // 동료 일꾼은 거리와 상관없이 부른다(사용자 결정 2026-10-26): 출발 전엔 멀어도 기다리고, 기다림은 처음 거리만큼 늘어난다
    [Fact]
    public void FarMateIsWaitedForByDistance()
    {
        var e = Escort.ForMate(600f); // 600 / 4 + 30 = 180초
        for (int i = 0; i < 180; i++) Assert.Equal(EscortAct.Wait, e.Tick(1f, 600f - i * 3f));
        Assert.Equal(EscortAct.GiveUp, e.Tick(1f, 50f));
        e = Escort.ForMate(600f);
        Assert.Equal(EscortAct.Wait, e.Tick(1f, 600f));
        Assert.Equal(EscortAct.Go, e.Tick(1f, 6f));
        Assert.Equal(Escort.Phase.Leading, e.Now);
        Assert.Equal(EscortAct.Wait, Escort.ForMate(10f).Tick(1f, 10f)); // 가까워도 최소 60초
    }

    [Fact]
    public void PausedForCountsOnlyThisPause()
    {
        var e = new Escort();
        e.Tick(1f, 5f);
        e.Tick(1f, 30f);
        Assert.Equal(Escort.Phase.Paused, e.Now);
        Assert.Equal(0f, e.PausedFor);
        e.Tick(2f, 30f); e.Tick(3f, 30f);
        Assert.Equal(5f, e.PausedFor);
        e.Tick(1f, 5f);
        Assert.Equal(0f, e.PausedFor);
    }
}
