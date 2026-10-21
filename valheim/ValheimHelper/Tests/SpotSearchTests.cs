using ValheimHelper.Core.Work;
using Xunit;

public class SpotSearchTests
{
    [Fact]
    public void CenterGoodReturnsCenter()
    {
        Assert.True(SpotSearch.Find(10f, 20f, 5f, 50f, 1f, (x, z) => 1f, out float x, out float z));
        Assert.Equal(10f, x); Assert.Equal(20f, z);
    }

    [Fact]
    public void NearestRingWinsAndBestWithinRing()
    {
        // 중심에서 10m(2칸) 떨어진 곳부터 쓸 만하고, 그 고리 안에서는 x가 큰 쪽이 점수가 높다
        Assert.True(SpotSearch.Find(0f, 0f, 5f, 100f, 1f,
            (x, z) => System.Math.Max(System.Math.Abs(x), System.Math.Abs(z)) >= 10f ? x : 0f, out float rx, out float rz));
        Assert.Equal(10f, rx);
    }

    [Fact]
    public void NothingFoundReturnsFalse()
    {
        Assert.False(SpotSearch.Find(0f, 0f, 5f, 30f, 1f, (x, z) => 0f, out float x, out float z));
        Assert.Equal(0f, x); Assert.Equal(0f, z);
    }

    [Fact]
    public void BadStepReturnsFalse() =>
        Assert.False(SpotSearch.Find(0f, 0f, 0f, 30f, 0f, (x, z) => 1f, out _, out _));
}
