using ValheimHelper.Core.Npc;
using Xunit;

public class WeaknessTests
{
    // 종류 순서: 둔기·베기·찌르기·불·냉기·번개·독·영
    static float[] Club => new[] { 12f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
    static float[] Sword => new[] { 0f, 20f, 0f, 0f, 0f, 0f, 0f, 0f };
    static float[] Skeleton => new[] { Weakness.Mult(6), Weakness.Mult(1), Weakness.Mult(5), 1f, 1f, 1f, Weakness.Mult(3), 1f }; // 둔기 매우 약함·베기 저항·찌르기 매우 저항·독 면역

    [Fact]
    public void ModifiersMatchVanilla()
    {
        Assert.Equal(1f, Weakness.Mult(0));
        Assert.Equal(0.5f, Weakness.Mult(1));
        Assert.Equal(1.5f, Weakness.Mult(2));
        Assert.Equal(0f, Weakness.Mult(3));
        Assert.Equal(0f, Weakness.Mult(4));
        Assert.Equal(0.25f, Weakness.Mult(5));
        Assert.Equal(2f, Weakness.Mult(6));
        Assert.Equal(0.75f, Weakness.Mult(7));
        Assert.Equal(1.25f, Weakness.Mult(8));
    }

    [Fact]
    public void ClubBeatsSwordOnSkeleton()
    {
        float club = Weakness.Hit(0f, Club, Skeleton), sword = Weakness.Hit(0f, Sword, Skeleton);
        Assert.Equal(24f, club);
        Assert.Equal(10f, sword);
        Assert.Equal(0, Weakness.Pick(new[] { club, sword }));
        // 배율 없으면 칼이 더 셈
        Assert.Equal(1, Weakness.Pick(new[] { Weakness.Hit(0f, Club, null), Weakness.Hit(0f, Sword, null) }));
    }

    [Fact]
    public void TrueDamageIgnoresResistance()
    {
        var immune = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        Assert.Equal(5f, Weakness.Hit(5f, Sword, immune));
    }

    [Fact]
    public void HealthScaleFollowsEffectiveHit()
    {
        Assert.Equal(0.5f, Weakness.HealthScale(12f, 24f));   // 약점이면 체력이 반인 셈
        Assert.Equal(2f, Weakness.HealthScale(20f, 10f));     // 저항이면 두 배
        Assert.Equal(Weakness.MaxScale, Weakness.HealthScale(20f, 0f)); // 면역이면 못 이김
        Assert.Equal(Weakness.MinScale, Weakness.HealthScale(1f, 100f));
        Assert.Equal(1f, Weakness.HealthScale(0f, 10f));
        Assert.Equal(-1, Weakness.Pick(new float[0]));
    }
}
