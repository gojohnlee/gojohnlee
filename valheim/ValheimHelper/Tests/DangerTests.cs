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
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Scv, weak, 70, 70, 20, NoArmor, false));       // 16/70
        var mid = new[] { F(100, 20) };                                                                      // 5방×20 = 100
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Scv, mid, 150, 150, 20, NoArmor, false));       // 0.67 > 0.6
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Alarak, mid, 150, 150, 20, NoArmor, false));   // ≤ 0.8
        var troll = new[] { F(600, 70) };
        Assert.Equal(Verdict.Flee, Danger.Judge(Personality.Alarak, troll, 70, 70, 20, NoArmor, false));
        Assert.Equal(Verdict.Flee, Danger.Judge(Personality.Scv, new[] { F(1, 1, boss: true) }, 70, 70, 20, NoArmor, false));
        Assert.Equal(Verdict.Calm, Danger.Judge(Personality.Scv, new Foe[0], 70, 70, 20, NoArmor, false));
    }

    [Fact]
    public void LowHealthDrinksOrBacksOff()
    {
        var weak = new[] { F(10, 2) };
        Assert.Equal(Verdict.Drink, Danger.Judge(Personality.Scv, weak, 25, 70, 20, NoArmor, true));   // 36% ≤ 40%
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Scv, weak, 25, 70, 20, NoArmor, false));
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Alarak, weak, 25, 70, 20, NoArmor, false)); // 30% 기준
    }

    [Fact]
    public void GuessGrowsWithSizeAndStars()
    {
        Danger.Guess(1.8f, 0.4f, 1, out float h1, out float d1);
        Assert.Equal(60f, h1, 1);
        Assert.Equal(12f, d1, 1);
        Danger.Guess(1.8f, 0.4f, 3, out float h3, out float d3);
        Assert.Equal(180f, h3, 1);
        Assert.Equal(24f, d3, 1);
        Danger.Guess(5f, 1.2f, 1, out float ht, out _);
        Assert.True(ht > 250f);
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
        Assert.Equal(Verdict.Drink, Danger.Judge(Personality.Scv, two, 18, 70, 13, d => d, true));
        // 미드가 없으면 물러남(가득한 체력으로는 이길 만한 상대라 도망까지는 아님)
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Scv, two, 18, 70, 13, d => d, false));
    }
}
