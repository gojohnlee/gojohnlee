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
    public void StaleSignalsOnceAtLimit()
    {
        var q = new HelpQueue();
        q.Add("창자 20개", 1);
        Assert.False(q.Postpone("없는 일"));
        for (int i = 1; i < HelpQueue.StaleAfter; i++) Assert.False(q.Postpone("창자 20개"));
        Assert.True(q.Postpone("창자 20개")); // 막 닿을 때 한 번
        Assert.False(q.Postpone("창자 20개")); // 그 뒤로는 안 되풀이
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

public class HelpQueueSaveTests
{
    [Fact]
    public void SaveLoadKeepsOrderNeededAndPostponed()
    {
        var q = new HelpQueue();
        q.Add("창자 20개", 2);
        q.Add("돌 20개", 1);
        q.Postpone("돌 20개");
        q.Postpone("돌 20개");
        var back = HelpQueue.Load(q.Save());
        Assert.Equal(2, back.Count);
        Assert.Equal("돌 20개", back.Next().Task); // 두 번 미룬 일이 먼저
        back.Remove("돌 20개");
        var a = back.Next();
        Assert.Equal("창자 20개", a.Task);
        Assert.Equal(2, a.Needed);
        back.Served("창자 20개", 1);
        Assert.Equal(1, back.Next().Needed);
    }

    [Fact]
    public void EmptyTextLoadsEmptyQueue()
    {
        Assert.Equal(0, HelpQueue.Load(null).Count);
        Assert.Equal(0, HelpQueue.Load("").Count);
    }

    [Fact]
    public void BrokenPiecesAreSkipped()
    {
        var q = HelpQueue.Load("창자 20개=x/0;돌=1/0;bad;=3/1;나무=2/0");
        Assert.Equal(2, q.Count);
        Assert.Equal("돌", q.Next().Task);
    }

    [Fact]
    public void SeparatorsInTaskAreRefused()
    {
        var q = new HelpQueue();
        q.Add("a;b", 1);
        q.Add("c/d", 1);
        Assert.Equal(0, q.Count);
    }
}
