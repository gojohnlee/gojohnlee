using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using Xunit;

public class MealsTests
{
    static FoodInfo F(string n, float hp, float st, float burn = 1200f, float regen = 2f) =>
        new FoodInfo { Name = n, Health = hp, Stamina = st, BurnTime = burn, Regen = regen };

    [Fact]
    public void BaseValuesWithoutFood()
    {
        var m = new Meals();
        Assert.Equal(25f, m.MaxHealth);
        Assert.Equal(75f, m.MaxStamina);
    }

    [Fact]
    public void ThreeKindsAtMost()
    {
        var m = new Meals();
        Assert.True(m.Eat(F("a", 10, 10)));
        Assert.True(m.Eat(F("b", 10, 10)));
        Assert.True(m.Eat(F("c", 10, 10)));
        Assert.False(m.Eat(F("d", 10, 10))); // 다 배부름
        Assert.False(m.Eat(F("a", 10, 10))); // 같은 것도 절반 지나야
        Assert.Equal(55f, m.MaxHealth);
    }

    [Fact]
    public void FadesAndRunsOut()
    {
        var m = new Meals();
        m.Eat(F("a", 20, 0, burn: 100f));
        m.Tick(50f);
        Assert.InRange(m.MaxHealth, 25f + 20f * 0.8f, 25f + 20f * 0.82f); // 0.5^0.3 = 0.812
        m.Tick(50f);
        Assert.Equal(0, m.Count);
    }

    [Fact]
    public void ReplacesHalfEatenFood()
    {
        var m = new Meals();
        m.Eat(F("a", 10, 10, burn: 100f));
        m.Eat(F("b", 10, 10, burn: 1000f));
        m.Eat(F("c", 10, 10, burn: 1000f));
        m.Tick(60f); // a만 절반 넘게 지남
        Assert.True(m.Eat(F("d", 30, 30)));
        Assert.Contains("d", m.Names);
        Assert.DoesNotContain("a", m.Names);
    }

    [Fact]
    public void RegenEveryTenSeconds()
    {
        var m = new Meals();
        m.Eat(F("a", 10, 10, regen: 2f));
        m.Eat(F("b", 10, 10, regen: 3f));
        Assert.Equal(0f, m.Tick(9f));
        Assert.Equal(5f, m.Tick(1f));
    }

    [Fact]
    public void EatsBestDistinctFoods()
    {
        var m = new Meals();
        var picked = m.EatBest(new[] { F("berry", 7, 20), F("meat", 30, 10), F("meat", 30, 10), F("soup", 20, 20), F("honey", 8, 35) });
        Assert.Equal(3, picked.Count); // 꿀 43, 고기 40, 수프 40 > 베리 27
        Assert.Equal("honey", picked[0]);
        Assert.Contains("meat", picked);
        Assert.Contains("soup", picked);
        Assert.Empty(m.EatBest(new[] { F("berry", 7, 20) })); // 배부름
    }

    [Fact]
    public void SaveLoadKeepsTimeLeft()
    {
        var m = new Meals();
        m.Eat(F("a", 10, 10, burn: 100f));
        m.Tick(30f);
        var foods = new Dictionary<string, FoodInfo> { ["a"] = F("a", 10, 10, burn: 100f) };
        var m2 = Meals.Load(m.Save() + ";gone:50;bad", n => foods.TryGetValue(n, out var f) ? f : null);
        Assert.Equal(1, m2.Count);
        Assert.Equal(m.MaxHealth, m2.MaxHealth, 3);
    }
}
