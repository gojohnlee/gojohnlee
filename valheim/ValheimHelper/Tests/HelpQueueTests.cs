using ValheimHelper.Core.Work;
using Xunit;

public class HelpQueueTests
{
    [Fact]
    public void EmptyQueueHasNoNext()
    {
        var q = new HelpQueue();
        Assert.Null(q.Next());
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void FirstInFirstServed()
    {
        var q = new HelpQueue();
        q.Add("창자 20개", 1);
        q.Add("돌 20개", 1);
        Assert.Equal("창자 20개", q.Next().Task);
    }

    [Fact]
    public void PostponedTwiceGoesFirst()
    {
        var q = new HelpQueue();
        q.Add("창자 20개", 1);
        q.Add("돌 20개", 1);
        q.Postpone("창자 20개");
        Assert.Equal("창자 20개", q.Next().Task); // 한 번은 아직 들어온 순서
        q.Postpone("창자 20개");
        q.Add("나무 20개", 1);
        Assert.Equal("창자 20개", q.Next().Task); // 두 번 미뤄져 맨 앞
    }

    [Fact]
    public void AddSameTaskKeepsOneAndTakesLarger()
    {
        var q = new HelpQueue();
        q.Add("창자 20개", 1);
        q.Add("창자 20개", 2);
        q.Add("창자 20개", 1);
        Assert.Equal(1, q.Count);
        Assert.Equal(2, q.Next().Needed);
    }

    [Fact]
    public void ServedRemovesWhenFilled()
    {
        var q = new HelpQueue();
        q.Add("창자 20개", 2);
        Assert.False(q.Served("창자 20개", 1));
        Assert.Equal(1, q.Next().Needed);
        Assert.True(q.Served("창자 20개", 1));
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void IgnoresBadInput()
    {
        var q = new HelpQueue();
        q.Add("", 1);
        q.Add(null, 1);
        q.Add("돌 20개", 0);
        Assert.Equal(0, q.Count);
        q.Postpone("없는 일");
        Assert.False(q.Served("없는 일", 1));
    }

    [Fact]
    public void RemoveDropsTask()
    {
        var q = new HelpQueue();
        q.Add("창자 20개", 1);
        q.Remove("창자 20개");
        Assert.Null(q.Next());
    }
}
