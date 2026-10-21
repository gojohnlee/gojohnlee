using System;
using ValheimHelper.Core.Work;
using Xunit;

public class TestSpotsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void NeighborsAtLeastSpacingApart(int n)
    {
        var s = TestSpots.Ring(n, 1000f);
        Assert.Equal(n, s.Count);
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                float d = (float)Math.Sqrt(Math.Pow(s[i].X - s[j].X, 2) + Math.Pow(s[i].Z - s[j].Z, 2));
                Assert.True(d >= 999f, $"{i}-{j}: {d}");
            }
    }

    [Fact]
    public void ZeroGivesEmpty() => Assert.Empty(TestSpots.Ring(0, 1000f));
}
