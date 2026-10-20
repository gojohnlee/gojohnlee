using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;

public class KnowledgeTests
{
    static Dictionary<ZoneXY, Dictionary<string, int>> Seen(int x, int y, string item, int n) =>
        new Dictionary<ZoneXY, Dictionary<string, int>> { [new ZoneXY(x, y)] = new Dictionary<string, int> { [item] = n } };

    [Fact]
    public void RemembersWhatItSaw()
    {
        var k = new Knowledge();
        k.Saw(Seen(1, 2, "Wood", 5), new List<ZoneXY>(), 10);
        var zones = k.ZonesWith("Wood", new ZoneXY(0, 0));
        Assert.Single(zones);
        Assert.Equal(5, zones[0].Items["Wood"]);
        Assert.Empty(k.ZonesWith("Stone", new ZoneXY(0, 0)));
    }

    [Fact]
    public void PartialViewKeepsLargerCount()
    {
        var k = new Knowledge();
        k.Saw(Seen(0, 0, "Wood", 8), new List<ZoneXY>(), 10);
        k.Saw(Seen(0, 0, "Wood", 3), new List<ZoneXY>(), 20);
        Assert.Equal(8, k.Get(new ZoneXY(0, 0)).Items["Wood"]);
    }

    [Fact]
    public void FullViewForgetsWhatIsGone()
    {
        var k = new Knowledge();
        k.Saw(Seen(0, 0, "Wood", 8), new List<ZoneXY>(), 10);
        k.Saw(new Dictionary<ZoneXY, Dictionary<string, int>>(), new List<ZoneXY> { new ZoneXY(0, 0) }, 20);
        Assert.Empty(k.ZonesWith("Wood", new ZoneXY(0, 0)));
    }

    [Fact]
    public void NearestFirst()
    {
        var k = new Knowledge();
        k.Saw(Seen(5, 5, "Wood", 1), new List<ZoneXY>(), 1);
        k.Saw(Seen(1, 0, "Wood", 1), new List<ZoneXY>(), 1);
        Assert.Equal(new ZoneXY(1, 0), k.ZonesWith("Wood", new ZoneXY(0, 0))[0].Zone);
    }

    [Fact]
    public void SaveLoadRoundTrip()
    {
        var k = new Knowledge();
        k.Saw(Seen(-3, 7, "FineWood", 4), new List<ZoneXY>(), 123);
        k.MarkLooked(new ZoneXY(-3, 7));
        k.MarkLooked(new ZoneXY(2, 2));
        var k2 = Knowledge.Load(k.Save());
        Assert.Equal(2, k2.Count);
        Assert.True(k2.IsLooked(new ZoneXY(-3, 7)));
        Assert.Equal(4, k2.Get(new ZoneXY(-3, 7)).Items["FineWood"]);
        Assert.Equal(123, k2.Get(new ZoneXY(-3, 7)).SeenAt);
    }

    [Fact]
    public void LoadIgnoresGarbage() => Assert.Equal(0, Knowledge.Load("garbage\n;;\n1,2").Count);
}

public class SearchPlanTests
{
    [Fact]
    public void StartsAtHomeThenRings()
    {
        var k = new Knowledge();
        var home = new ZoneXY(0, 0);
        Assert.True(SearchPlan.Next(home, home, 3, k, z => true, out var next));
        Assert.Equal(home, next);
        k.MarkLooked(home);
        Assert.True(SearchPlan.Next(home, new ZoneXY(1, 1), 3, k, z => true, out next));
        Assert.Equal(1, next.Ring(home));
        Assert.Equal(new ZoneXY(1, 1), next); // 같은 고리면 지금 위치에서 가까운 곳
    }

    [Fact]
    public void SkipsLookedAndUnexplored()
    {
        var k = new Knowledge();
        var home = new ZoneXY(0, 0);
        k.MarkLooked(home);
        // 동쪽만 밝혀짐
        Assert.True(SearchPlan.Next(home, home, 2, k, z => z.X > 0, out var next));
        Assert.True(next.X > 0);
        Assert.Equal(1, next.Ring(home));
    }

    [Fact]
    public void NothingLeft()
    {
        var k = new Knowledge();
        var home = new ZoneXY(0, 0);
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) k.MarkLooked(new ZoneXY(x, y));
        Assert.False(SearchPlan.Next(home, home, 1, k, z => true, out _));
    }
}
