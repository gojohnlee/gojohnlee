using ValheimHelper.Core.Npc;
using Xunit;

public class BlockTests
{
    static Shield Wood => new Shield { Power = 10f, StaggerLimit = 40f };

    [Fact]
    public void ArmorMatchesVanillaFormula()
    {
        Assert.Equal(6f, Block.Armor(10f, 4f), 3);      // 갑옷이 한 방의 반보다 작음 → 뺌
        Assert.Equal(2.5f, Block.Armor(10f, 10f), 3);   // 크면 dmg²/(4ac)
        Assert.Equal(10f, Block.Armor(10f, 0f), 3);
    }

    [Fact]
    public void ShieldCutsOneFoeHit()
    {
        // 한 방 15, 막기 10 → 5.625, 갑옷 6 → 1.318, 덜 휘두름 1.3배 → 1.71 (갑옷만이면 9)
        Assert.Equal(9f, Block.Taken(15f, 6f, null, 1), 3);
        Assert.Equal(1.714f, Block.Taken(15f, 6f, Wood, 1), 2);
    }

    [Fact]
    public void StaggerBreaksTheBlock()
    {
        // 막고 남은 90이 비틀 한계 40의 반(20)을 넘음 → 막기 실패, 갑옷만
        Assert.False(Block.Holds(100f, Wood));
        Assert.Equal(Block.Armor(100f, 6f), Block.Taken(100f, 6f, Wood, 1), 3);
    }

    [Fact]
    public void NoStaggerLimitAlwaysHolds()
    {
        Assert.True(Block.Holds(100f, new Shield { Power = 10f, StaggerLimit = 0f }));
    }

    [Fact]
    public void ManyFoesCountNoBlock()
    {
        Assert.Equal(9f, Block.Taken(15f, 6f, Wood, Block.MaxFoes + 1), 3);
        Assert.True(Block.Taken(15f, 6f, Wood, Block.MaxFoes) < 9f);
    }

    [Fact]
    public void NeverWorseThanNoShield()
    {
        var weak = new Shield { Power = 0.5f, StaggerLimit = 0f };
        Assert.Equal(Block.Armor(15f, 6f), Block.Taken(15f, 6f, weak, 1), 3);
        Assert.False(Block.Holds(15f, new Shield { Power = 0f, StaggerLimit = 40f }));
    }
}
