using ValheimHelper.Core.Npc;
using Xunit;

public class SaluteTests
{
    [Fact]
    public void Near_SalutesOnce_ThenAgainAfterMinute()
    {
        var s = new Salute();
        Assert.False(s.Try(1, Salute.Range + 0.5f, 0f, false)); // 멂
        Assert.True(s.Try(1, 3f, 1f, false));
        Assert.True(s.Busy(1f + Salute.Hold - 0.1f));
        Assert.False(s.Try(1, 3f, 10f, false)); // 같은 사람 60초 안
        Assert.False(s.Try(1, 3f, 1f + Salute.Again - 1f, false));
        Assert.True(s.Try(1, 3f, 1f + Salute.Again, false));
    }

    [Fact]
    public void OtherPerson_AfterHold_FoeBlocks()
    {
        var s = new Salute();
        Assert.True(s.Try(1, 2f, 0f, false));
        Assert.False(s.Try(2, 2f, 1f, false)); // 하는 중
        Assert.False(s.Try(2, 2f, Salute.Hold + 1f, true)); // 적이 보임
        Assert.True(s.Try(2, 2f, Salute.Hold + 1f, false));
        Assert.Equal(2, s.Who);
        s.Stop();
        Assert.False(s.Busy(Salute.Hold + 1.5f));
    }
}
