using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;
using Xunit;

public class PlaceBookTests
{
    // 종류별 능력치: troll 체력 600·한 방 70, greyling 체력 30·한 방 5. 레벨마다 ×1.5 한 방.
    static Foe Resolve(GroupFoe g)
    {
        float hp = g.Kind == "troll" ? 600 : 30, dmg = g.Kind == "troll" ? 70 : 5;
        return new Foe { Kind = g.Kind, Health = hp, Damage = dmg * (1 + 0.5f * (g.Level - 1)) };
    }

    static GroupFoe[] G(string kind, int n, int level = 1, bool boss = false) =>
        new[] { new GroupFoe { Kind = kind, Count = n, Level = level, Boss = boss } };

    // 체력 100, 한 방 20, 갑옷 없음
    static float Weak(IList<Foe> f) => Danger.Ratio(f, 100, 20, d => d);
    // 체력 200, 한 방 60, 피해 절반
    static float Strong(IList<Foe> f) => Danger.Ratio(f, 200, 60, d => d * 0.5f);

    static readonly ZoneXY Z = new ZoneXY(3, -2);

    [Fact]
    public void RiskUsesCurrentStrength()
    {
        var b = new PlaceBook();
        b.Record(Z, G("troll", 1), Outcome.Back, 0, Resolve);
        float weak = b.Risk(Z, 0, Weak, Resolve).Ratio;
        float strong = b.Risk(Z, 0, Strong, Resolve).Ratio;
        Assert.True(strong < weak / 2, $"{strong} vs {weak}");
        Assert.Equal("troll", b.Risk(Z, 0, Weak, Resolve).TopFoe);
    }

    [Fact]
    public void GroupDecaysByHalfLife()
    {
        var b = new PlaceBook();
        b.Record(Z, G("greyling", 3), Outcome.Won, 0, Resolve);
        float now = b.Risk(Z, 0, Weak, Resolve).Ratio;
        float later = b.Risk(Z, PlaceBook.HalfLife, Weak, Resolve).Ratio;
        Assert.Equal(now / 2, later, 3);
    }

    [Fact]
    public void DeathLastsLonger()
    {
        var b = new PlaceBook();
        b.Record(Z, G("troll", 1), Outcome.Died, 0, Resolve, "troll");
        var r = b.Risk(Z, 10 * PlaceBook.Day, Weak, Resolve);
        Assert.True(r.DeathWeight > 0.6f, r.DeathWeight.ToString());
        Assert.True(r.Ratio < b.Risk(Z, 0, Weak, Resolve).Ratio * 0.2f);
        Assert.Equal(1, b.Get(Z).Deaths);
        Assert.Equal("troll", b.Get(Z).Killer);
    }

    [Fact]
    public void HeavierGroupReplacesOnlyIfHeavierThanDecayedOld()
    {
        var b = new PlaceBook();
        b.Record(Z, G("troll", 1), Outcome.Back, 0, Resolve);
        b.Record(Z, G("greyling", 1), Outcome.Won, 10, Resolve); // 훨씬 약함 → 트롤 유지
        Assert.Equal("troll", b.Get(Z).Group[0].Kind);
        b.Record(Z, G("greyling", 1), Outcome.Won, 40 * PlaceBook.Day, Resolve); // 트롤 기억은 거의 사라짐
        Assert.Equal("greyling", b.Get(Z).Group[0].Kind);
    }

    [Fact]
    public void NeighborsCountHalf()
    {
        var b = new PlaceBook();
        b.Record(new ZoneXY(Z.X + 1, Z.Y), G("greyling", 3), Outcome.Won, 0, Resolve);
        var own = b.Risk(new ZoneXY(Z.X + 1, Z.Y), 0, Weak, Resolve);
        var near = b.Risk(Z, 0, Weak, Resolve);
        var far = b.Risk(new ZoneXY(Z.X - 1, Z.Y), 0, Weak, Resolve);
        Assert.Equal(own.Ratio / 2, near.Ratio, 3);
        Assert.True(near.Known);
        Assert.False(far.Known);
        Assert.Equal(0f, near.DeathWeight);
    }

    [Fact]
    public void BossFlagFadesWithWeight()
    {
        var b = new PlaceBook();
        b.Record(Z, G("eikthyr", 1, 1, true), Outcome.Fled, 0, Resolve);
        Assert.True(b.Risk(Z, 0, Weak, Resolve).Boss);
        Assert.False(b.Risk(Z, 3 * PlaceBook.HalfLife, Weak, Resolve).Boss);
    }

    [Fact]
    public void KeepsHundredDroppingOldestButDeathsLater()
    {
        var b = new PlaceBook();
        b.Record(new ZoneXY(0, 0), G("troll", 1), Outcome.Died, 0, Resolve); // 가장 오래됐지만 죽음
        for (int i = 1; i <= PlaceBook.MaxPlaces; i++) b.Record(new ZoneXY(i, 0), G("greyling", 1), Outcome.Won, i, Resolve);
        Assert.Equal(PlaceBook.MaxPlaces, b.Count);
        Assert.NotNull(b.Get(new ZoneXY(0, 0)));
        Assert.Null(b.Get(new ZoneXY(1, 0)));
    }

    [Fact]
    public void FeaturesAreKept()
    {
        var b = new PlaceBook();
        b.AddFeature(Z, PlaceFeature.DeepWater, 5);
        b.AddFeature(Z, PlaceFeature.Pit, 6);
        Assert.Equal(PlaceFeature.DeepWater | PlaceFeature.Pit, b.Risk(Z, 6, Weak, Resolve).Features);
        Assert.Equal(0f, b.Risk(Z, 6, Weak, Resolve).Ratio);
    }

    [Fact]
    public void TopSortsByCurrentDanger()
    {
        var b = new PlaceBook();
        b.Record(new ZoneXY(0, 0), G("greyling", 1), Outcome.Won, 0, Resolve);
        b.Record(new ZoneXY(5, 5), G("troll", 1), Outcome.Back, 0, Resolve);
        b.Record(new ZoneXY(9, 9), new GroupFoe[0], Outcome.Died, 0, Resolve, "fall");
        var top = b.Top(2, 0, Weak, Resolve);
        Assert.Equal(2, top.Count);
        Assert.Equal(new ZoneXY(5, 5), top[0].Zone);
    }

    [Fact]
    public void SaveLoadRoundTrip()
    {
        var b = new PlaceBook();
        b.Record(Z, new[] { new GroupFoe { Kind = "troll", Count = 1, Level = 2 }, new GroupFoe { Kind = "greyling", Count = 4 } }, Outcome.Died, 123.5, Resolve, "troll");
        b.AddFeature(Z, PlaceFeature.Cold, 130);
        b.Record(new ZoneXY(-7, 8), G("eikthyr", 1, 1, true), Outcome.Fled, 50, Resolve);
        var c = PlaceBook.Load(b.Save());
        Assert.Equal(2, c.Count);
        Assert.False(c.Dirty);
        var m = c.Get(Z);
        Assert.Equal(2, m.Group.Count);
        Assert.Equal(2, m.Group[0].Level);
        Assert.Equal(4, m.Group[1].Count);
        Assert.Equal(1, m.Deaths);
        Assert.Equal(123.5, m.LastDeathAt, 3);
        Assert.Equal("troll", m.Killer);
        Assert.Equal(PlaceFeature.Cold, m.Features);
        Assert.Equal(Outcome.Died, m.LastOutcome);
        Assert.True(c.Get(new ZoneXY(-7, 8)).Group[0].Boss);
        Assert.Equal(-1, c.Get(new ZoneXY(-7, 8)).LastDeathAt);
        Assert.Equal(b.Save(), c.Save());
    }

    [Fact]
    public void LoadIgnoresGarbage()
    {
        var c = PlaceBook.Load("garbage\n1,2|x\n,,,|\n");
        Assert.Equal(0, c.Count);
    }
}
