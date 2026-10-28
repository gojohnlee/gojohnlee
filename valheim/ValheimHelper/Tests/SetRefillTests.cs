using System.Collections.Generic;
using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;

public class SetMenuTests
{
    [Fact]
    public void SaveNextFreeAndMax()
    {
        var b = new SetMenuBook();
        Assert.Equal(1, b.Save(0, new[] { "MeadHealthMinor", "DeerStew", "Sausages" }, null, "lee"));
        Assert.Equal(2, b.Save(0, new[] { "Honey" }, "늪 세트", "lee"));
        for (int i = 3; i <= SetMenuBook.Max; i++) b.Save(i, new[] { "Honey" }, null, null);
        Assert.Equal(0, b.NextFree());
        Assert.Equal(0, b.Save(0, new[] { "Honey" }, null, null));
        Assert.Equal(0, b.Save(11, new[] { "Honey" }, null, null));
        Assert.Equal(0, b.Save(1, new string[0], null, null)); // 빈 세트는 안 됨
    }

    [Fact]
    public void EditAndTargets()
    {
        var b = new SetMenuBook();
        b.Save(1, new[] { "MeadHealthMinor", "DeerStew", "Sausages" }, null, null);
        Assert.True(b.Edit(1, new[] { "Sausages" }, new[] { "MeadStaminaMinor" }));
        var t = b.Targets(1, 20);
        Assert.Equal(new[] { "MeadHealthMinor", "DeerStew", "MeadStaminaMinor" }, t.Select(x => x.Item));
        Assert.All(t, x => Assert.Equal(20, x.Amount));
        Assert.Empty(b.Targets(2, 20));
        Assert.False(b.Edit(1, new[] { "Nothing" }, null));
    }

    [Fact]
    public void SaveLoad()
    {
        var b = new SetMenuBook();
        b.Save(3, new[] { "Honey", "Raspberry" }, "간식", "lee");
        var c = SetMenuBook.Load(b.Save());
        Assert.False(c.Dirty);
        Assert.Equal("간식", c.Get(3).Name);
        Assert.Equal("lee", c.Get(3).By);
        Assert.Equal(b.Save(), c.Save());
    }
}

public class RefillTests
{
    [Fact]
    public void DueBelowThresholdWithSlack()
    {
        var r = new RefillBook();
        r.Keep("Wood", 200);
        var stock = new Dictionary<string, int> { ["Wood"] = 190 };
        Assert.Empty(r.Due(i => stock.TryGetValue(i, out int n) ? n : 0, null)); // 10 모자람 < 20(10%)
        stock["Wood"] = 150;
        var due = r.Due(i => stock.TryGetValue(i, out int n) ? n : 0, null);
        Assert.Equal(50, due.Single().Amount);
    }

    [Fact]
    public void SetsUseLargestThreshold()
    {
        var sets = new SetMenuBook();
        sets.Save(1, new[] { "Honey", "DeerStew" }, null, null);
        var r = new RefillBook();
        r.KeepSet(1, 10);
        r.Keep("Honey", 30);
        var due = r.Due(i => 0, sets).ToDictionary(x => x.Item, x => x.Amount);
        Assert.Equal(30, due["Honey"]);
        Assert.Equal(10, due["DeerStew"]);
        r.KeepSet(5, 10); // 없는 세트는 무시
        Assert.Equal(2, r.Due(i => 0, sets).Count);
    }

    [Fact]
    public void OnceFillsWithoutSlackThenDrops()
    {
        var sets = new SetMenuBook();
        sets.Save(1, new[] { "Honey", "DeerStew" }, null, null);
        var r = new RefillBook();
        r.Keep("Wood", 100, once: true);
        r.KeepSet(1, 2, once: true);
        r.Keep("Stone", 100);
        var c = RefillBook.Load(r.Save()); // 한 번 채움 표시도 저장됨
        Assert.True(c.IsOnce("Wood") && c.IsOnceSet(1) && !c.IsOnce("Stone"));
        var stock = new Dictionary<string, int> { ["Wood"] = 99, ["Stone"] = 99, ["Honey"] = 2, ["DeerStew"] = 1 };
        var due = r.Due(i => stock.TryGetValue(i, out int n) ? n : 0, sets).ToDictionary(x => x.Item, x => x.Amount);
        Assert.Equal(1, due["Wood"]);           // 한 번 채움은 Slack 없이
        Assert.False(due.ContainsKey("Stone")); // 유지는 Slack(10%) 안이면 안 나감
        Assert.Equal(1, due["DeerStew"]);
        Assert.Equal(3, r.Count);
        stock["Wood"] = 100; stock["DeerStew"] = 2;
        r.Due(i => stock.TryGetValue(i, out int n) ? n : 0, sets);
        Assert.Equal(1, r.Count);               // 다 찬 한 번 채움은 지워지고 유지만 남음
        Assert.Equal("Stone", r.Items.Single().Key);
    }

    [Fact]
    public void OffAndSaveLoad()
    {
        var r = new RefillBook();
        r.Keep("Wood", 200);
        r.Keep("Stone", 100);
        r.KeepSet(2, 5);
        var c = RefillBook.Load(r.Save());
        Assert.Equal(r.Save(), c.Save());
        Assert.Equal(3, c.Count);
        c.Off("Wood");
        Assert.Equal(2, c.Count);
        c.Off();
        Assert.Equal(0, c.Count);
    }
}
