using ValheimHelper.Core.Npc;
using Xunit;

public class SkillBookTests
{
    const int Wood = 13, Pick = 12;

    [Fact]
    public void StartsAtTen()
    {
        var b = new SkillBook();
        Assert.Equal(10f, b.Level(Wood));
        Assert.Equal(0.1f, b.Factor(Wood), 3);
        Assert.Equal(0f, b.Level(0));
    }

    [Fact]
    public void RaisesLikeVanilla()
    {
        var b = new SkillBook();
        // 10 → 11 요구치: 11^1.5 × 0.5 + 0.5 = 18.74
        Assert.Equal(18.74f, SkillBook.NextRequirement(10), 2);
        int hits = 0;
        while (b.Level(Wood) < 11f) { b.Raise(Wood, 1f, 1f, 1f); hits++; }
        Assert.Equal(19, hits);
        // 세계 스킬 배율 2배면 절반
        var fast = new SkillBook();
        hits = 0;
        while (fast.Level(Wood) < 11f) { fast.Raise(Wood, 1f, 1f, 2f); hits++; }
        Assert.Equal(10, hits);
    }

    [Fact]
    public void AnnouncesEveryFiveAndCapsAtSeventy()
    {
        var b = SkillBook.Load("13=14:0");
        int said = 0;
        for (int i = 0; i < 200 && said == 0; i++) said = b.Raise(Wood, 1f, 1f, 1f);
        Assert.Equal(15, said);
        var top = SkillBook.Load("12=69:0");
        int last = 0;
        for (int i = 0; i < 1000; i++) { int s = top.Raise(Pick, 1f, 1f, 1f); if (s > 0) last = s; }
        Assert.Equal(70f, top.Level(Pick));
        Assert.Equal(70, last);
        Assert.Equal(0, top.Raise(Pick, 1f, 1f, 1f));
    }

    [Fact]
    public void RandomFactorFollowsSkill()
    {
        var b = new SkillBook();
        // 스킬 10: 0.4 + 0.06 = 0.46, ±0.15
        Assert.Equal(0.31f, b.RandomFactor(Wood, 0), 3);
        Assert.Equal(0.61f, b.RandomFactor(Wood, 1), 3);
        Assert.Equal(0.46f, b.MeanFactor(Wood), 3);
        var hi = SkillBook.Load("13=70:0");
        Assert.Equal(0.82f, hi.MeanFactor(Wood), 3);
    }

    [Fact]
    public void SaveLoadAndRaisedList()
    {
        var b = new SkillBook();
        for (int i = 0; i < 25; i++) b.Raise(Wood, 1f, 1f, 1f);
        b.Raise(Pick, 1f, 0.5f, 1f);
        var c = SkillBook.Load(b.Save());
        Assert.Equal(b.Save(), c.Save());
        Assert.Equal(11f, c.Level(Wood));
        var raised = c.Raised();
        Assert.Single(raised);
        Assert.Equal(Wood, raised[0].Key);
        Assert.Equal(10f, SkillBook.Load("12=3:0").Level(Pick)); // 시작값 아래는 시작값으로
        Assert.Equal(0, SkillBook.Load("garbage;x=y").Raised().Count);
    }
}
