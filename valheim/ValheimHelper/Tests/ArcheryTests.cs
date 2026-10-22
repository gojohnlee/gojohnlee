using ValheimHelper.Core.Npc;
using Xunit;

public class ArcheryTests
{
    [Fact]
    public void StillTargetAimsAboveByDrop()
    {
        Assert.True(Archery.Aim(0f, 0f, 20f, 0f, 0f, 0f, 40f, 10f, out float x, out float y, out float z));
        Assert.Equal(0f, x, 3);
        Assert.Equal(20f, z, 3);
        Assert.Equal(0.5f * 10f * 0.25f, y, 3); // 0.5초 비행 → 1.25m 위
    }

    [Fact]
    public void NoGravityStillTargetIsStraight()
    {
        Assert.True(Archery.Aim(3f, 1f, 10f, 0f, 0f, 0f, 50f, 0f, out float x, out float y, out float z));
        Assert.Equal(3f, x, 3);
        Assert.Equal(1f, y, 3);
        Assert.Equal(10f, z, 3);
    }

    [Fact]
    public void MovingTargetLeads()
    {
        // 20m 앞에서 오른쪽(+x)으로 8m/s 뛰는 사슴, 화살 40m/s → 0.5초쯤 → 4m쯤 앞을 겨눔
        Assert.True(Archery.Aim(0f, 0f, 20f, 8f, 0f, 0f, 40f, 0f, out float x, out float _, out float z));
        Assert.InRange(x, 3.9f, 4.3f);
        Assert.Equal(20f, z, 3);
    }

    [Fact]
    public void FleeingTargetLeadsFurther()
    {
        Assert.True(Archery.Aim(0f, 0f, 20f, 0f, 0f, 8f, 40f, 0f, out float _, out float _, out float z));
        Assert.InRange(z, 24.5f, 25.5f); // 20/(40-8)=0.625초 → 25m
    }

    [Fact]
    public void CannotAimWhenTooSlowOrFar()
    {
        Assert.False(Archery.Aim(0f, 0f, 20f, 0f, 0f, 0f, 0f, 10f, out _, out _, out _));
        Assert.False(Archery.Aim(0f, 0f, 100f, 0f, 0f, 0f, 40f, 10f, out _, out _, out _));
        Assert.False(Archery.Aim(0f, 0f, 20f, 0f, 0f, 45f, 40f, 0f, out _, out _, out _)); // 화살보다 빨리 도망
    }
}
