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
        var mid = new[] { F(100, 13) };                                                                      // 10방×13 = 130
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Ragnar, mid, 150, 150, 10, NoArmor, false));       // 0.87 > 0.85
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Floki, mid, 150, 150, 10, NoArmor, false));   // ≤ 0.95, 20 남음 > 13
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Ragnar, new[] { F(100, 20) }, 150, 150, 20, NoArmor, false)); // 0.67 — 전엔 물러남
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
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Ragnar, weak, 25, 70, 20, NoArmor, false)); // 미드 없어도 비율 0.08이면 싸움
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Ragnar, new[] { F(100, 6) }, 25, 70, 20, NoArmor, false)); // 30/25 > 0.85
        Assert.Equal(Verdict.Fight, Danger.Judge(Personality.Floki, weak, 25, 70, 20, NoArmor, false)); // 30% 기준
        // 비율 15/18 = 0.83 ≤ 0.85지만 끝나면 3 남음 < 적 한 방 5 → 물러남(회차 145 fighttest에서 이렇게 죽음)
        Assert.Equal(Verdict.Back, Danger.Judge(Personality.Ragnar, new[] { F(40, 5) }, 18, 71, 15, NoArmor, false));
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

public class MateCallTests
{
    static Foe F(float hp, float dmg) => new Foe { Kind = "x", Health = hp, Damage = dmg };

    [Fact]
    public void CallsMateWhenTogetherWins()
    {
        // 혼자면 물러남·도망이지만 동료 한 방을 합치면 싸움 → 불러 놓고 기다림
        Assert.Equal(MateCall.Wait, Danger.ShouldCallMate(Verdict.Back, Verdict.Fight, 1.2f, 2));
        Assert.Equal(MateCall.Wait, Danger.ShouldCallMate(Verdict.Flee, Verdict.Fight, 2f, 1));
        // 같이여도 안 되면(보스 등) 안 부름
        Assert.Equal(MateCall.None, Danger.ShouldCallMate(Verdict.Flee, Verdict.Flee, 3f, 1));
        Assert.Equal(MateCall.None, Danger.ShouldCallMate(Verdict.Drink, Verdict.Fight, 1f, 1));
        // 혼자도 싸움: 빡빡하거나 3마리 이상이면 같이 패기, 아니면 혼자
        Assert.Equal(MateCall.Join, Danger.ShouldCallMate(Verdict.Fight, Verdict.Fight, 0.7f, 1));
        Assert.Equal(MateCall.Join, Danger.ShouldCallMate(Verdict.Fight, Verdict.Fight, 0.2f, 3));
        Assert.Equal(MateCall.None, Danger.ShouldCallMate(Verdict.Fight, Verdict.Fight, 0.3f, 2));
    }

    [Fact]
    public void MateHitTurnsBackIntoFight()
    {
        // 혼자(한 방 8)면 물러나는 두 마리가 동료(한 방 8)를 합치면 싸움
        var two = new[] { F(40, 5), F(40, 5) };
        Verdict alone = Danger.Judge(Personality.Ragnar, two, 70, 70, 8, d => d, false);
        Verdict together = Danger.Judge(Personality.Ragnar, two, 70, 70, 16, d => d, false);
        Assert.NotEqual(Verdict.Fight, alone);
        Assert.Equal(Verdict.Fight, together);
    }
}
