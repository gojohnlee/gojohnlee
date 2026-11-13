using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using Xunit;

public class NightFoesTests
{
    // 종류별 능력치: greyling 체력 30·한 방 5, wolf 체력 60·한 방 12.
    static Foe Resolve(GroupFoe g)
    {
        float hp = g.Kind == "wolf" ? 60 : 30, dmg = g.Kind == "wolf" ? 12 : 5;
        return new Foe { Kind = g.Kind, Health = hp, Damage = dmg };
    }

    static float NoArmor(float d) => d;

    [Fact]
    public void NoNightSpawnsKeepsListAsIs()
    {
        var day = new List<Foe> { Resolve(new GroupFoe { Kind = "greyling", Count = 1 }) };
        Assert.Equal(1, NightFoes.Plus(day, null, Resolve).Count);
        Assert.Equal(1, NightFoes.Plus(day, new List<NightSpawn>(), Resolve).Count);
    }

    [Fact]
    public void NightSpawnAddsItsCountOfTheSameKind()
    {
        var night = new List<NightSpawn> { new NightSpawn { Kind = "wolf", Count = 3 } };
        var all = NightFoes.Plus(new List<Foe>(), night, Resolve);
        Assert.Equal(3, all.Count);
        Assert.All(all, f => { Assert.Equal("wolf", f.Kind); Assert.Equal(12, f.Damage); Assert.Equal(60, f.Health); });
    }

    [Fact]
    public void ZeroCountIsSkipped()
    {
        var night = new List<NightSpawn> { new NightSpawn { Kind = "wolf", Count = 0 } };
        Assert.Empty(NightFoes.Plus(new List<Foe>(), night, Resolve));
    }

    [Fact]
    public void NightShareRaisesRiskForSameWeaponOnlyByItsFoes()
    {
        // 밤 몫은 적 쪽만 셈: 같은 내 무기·체력이면 밤 적이 더해진 만큼만 비율이 오른다(장비와 무관한 고정 가산 없음).
        var meadow = new List<Foe> { Resolve(new GroupFoe { Kind = "greyling", Count = 1 }) };
        var night = new List<NightSpawn> { new NightSpawn { Kind = "wolf", Count = 2 } };
        float before = Danger.Ratio(meadow, 100, 20, NoArmor);
        float after = Danger.Ratio(NightFoes.Plus(meadow, night, Resolve), 100, 20, NoArmor);
        Assert.True(after > before);
        Assert.Equal(0f, Danger.Ratio(NightFoes.Plus(new List<Foe>(), null, Resolve), 100, 20, NoArmor));
    }
}
