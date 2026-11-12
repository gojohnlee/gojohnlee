using ValheimHelper.Core.Work;
using Xunit;

public class TeamJoinTests
{
    [Fact]
    public void JoinsWhenMuchLeft() => Assert.True(TeamJoin.Worth(15, 20, 1));

    [Fact]
    public void SkipsWhenAlmostDone()
    {
        Assert.False(TeamJoin.Worth(3, 20, 1));  // 4 밑
        Assert.False(TeamJoin.Worth(5, 20, 1));  // 1/3 밑
        Assert.True(TeamJoin.Worth(7, 20, 1));
    }

    [Fact]
    public void SkipsFullTeam()
    {
        Assert.True(TeamJoin.Worth(30, 40, 2));
        Assert.False(TeamJoin.Worth(30, 40, TeamJoin.MaxTeam));
    }
}
