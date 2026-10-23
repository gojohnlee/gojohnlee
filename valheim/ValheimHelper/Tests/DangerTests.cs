using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using Xunit;

public class DangerTests
{
    static Foe F(float hp, float dmg, bool boss = false) => new Foe { Kind = "x", Health = hp, Damage = dmg, Boss = boss };
    static float NoArmor(float d) => d;

    [Fact]
    public void SingleFoeMatchesConceptFormula()
    {
        // 적 체력 40, 한 방 8 / 내 한 방 20 → 2방에 잡고 그동안 2번 맞음 = 16
        Assert.Equal(16f, Danger.ExpectedLoss(new[] { F(40, 8) }, 20f, NoArmor));
    }

    [Fact]
    public void GroupsHurtMoreThanTheSum()
    {
        // 같은 적 둘: 첫째 잡는 동안 둘 다 때리고(2방×16), 둘째 잡는 동안 하나(2방×8) = 48 > 16+16
        Assert.Equal(48f, Danger.ExpectedLoss(new[] { F(40, 8), F(40, 8) }, 20f, NoArmor));
    }

    [Fact]
    public void KillsWeakestFirst()
    {
        // 약한 것(체력 10, 한 방 30)부터 잡으면: 1방×(30+5) + 10방×5 = 85
        Assert.Equal(85f, Danger.ExpectedLoss(new[] { F(100, 5), F(10, 30) }, 10f, NoArmor));
    }

    [Fact]
    public void ArmorLowersLoss()
    {
        float armored = Danger.ExpectedLoss(new[] { F(40, 8) }, 20f, d => d * 0.25f);
        Assert.Equal(4f, armored);
    }

    [Fact]
    public void Verdicts()
    {
        var weak = new[] { F(40, 8) };
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Ragnar, weak, 70, 70, 20, NoArmor, false));       // 16/70
        var mid = new[] { F(100, 20) };                                                                      // 5방×20 = 100
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Ragnar, mid, 150, 150, 20, NoArmor, false));       // 0.67 > 0.6
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Floki, mid, 150, 150, 20, NoArmor, false));   // ≤ 0.8
        var troll = new[] { F(600, 70) };
        Assert.Equal(Verdict.Flee, Danger.Judge(Personality.Floki, troll, 70, 70, 20, NoArmor, false));
        Assert.Equal(Verdict.Flee, Danger.Judge(Personality.Ragnar, new[] { F(1, 1, boss: true) }, 70, 70, 20, NoArmor, false));
        Assert.Equal(Verdict.Calm, Danger.Judge(Personality.Ragnar, new Foe[0], 70, 70, 20, NoArmor, false));
    }

    [Fact]
    public void LowHealthDrinksOrBacksOff()
    {
        var weak = new[] { F(10, 2) };
        Assert.Equal(Verdict.Drink, Danger.Judge(Personality.Ragnar, weak, 25, 70, 20, NoArmor, true));   // 36% ≤ 40%
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Ragnar, weak, 25, 70, 20, NoArmor, false));
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Floki, weak, 25, 70, 20, NoArmor, false)); // 30% 기준
    }

    [Fact]
    public void GuessGrowsWithSizeAndStars()
    {
        Assert.Equal(10f, Danger.GuessDamage(1.8f, 0.4f, 1), 1);
        Assert.Equal(20f, Danger.GuessDamage(1.8f, 0.4f, 3), 1); // 별 2개: ×2
        Assert.True(Danger.GuessDamage(5f, 1.2f, 1) > 40f);     // 트롤만 한 몸집
        Assert.Equal(5f, Danger.GuessDamage(0.3f, 0.1f, 1), 1);  // 아주 작아도 0.5배까지만
    }

    [Fact]
    public void FoeBookLearnsAndSaves()
    {
        var b = new FoeBook();
        b.Learn("Greydwarf", 40, 8);
        b.Learn("Greydwarf", 40, 12);
        b.Learn("bad=name", 1, 1);
        Assert.True(b.TryGet("Greydwarf", out float h, out float d));
        Assert.Equal(12f, d);
        var b2 = FoeBook.Load(b.Save() + ";junk");
        Assert.Equal(1, b2.Count);
        Assert.True(b2.Knows("Greydwarf"));
    }
}

public class DangerOrderTests
{
    static Foe F(float hp, float dmg) => new Foe { Kind = "x", Health = hp, Damage = dmg };

    [Fact]
    public void WoundedDrinksBeforeFleeing()
    {
        // 체력 18/70: 지금 체력으로는 비율이 크지만, 미드가 있으면 마시는 게 먼저
        var two = new[] { F(40, 7), F(40, 7) };
        Assert.Equal(Verdict.Drink, Danger.Judge(Personality.Ragnar, two, 18, 70, 13, d => d, true));
        // 미드가 없으면 물러남(가득한 체력으로는 이길 만한 상대라 도망까지는 아님)
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Ragnar, two, 18, 70, 13, d => d, false));
    }
}
