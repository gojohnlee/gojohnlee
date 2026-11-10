using ValheimHelper.Core.Work;
using Xunit;

namespace ValheimHelper.Tests
{
    public class HerdTests
    {
        [Theory]
        [InlineData(8, 2, 6, 0)] // 성체 2 + 새끼 6: 짝은 남기니 지금은 안 잡음
        [InlineData(8, 8, 6, 2)] // 새끼가 다 자람 → 넘는 2마리
        [InlineData(8, 3, 6, 1)] // 성체 3: 1마리만(짝 남김)
        [InlineData(6, 6, 6, 0)] // 넘지 않음
        [InlineData(3, 3, 2, 1)]
        [InlineData(2, 2, 1, 0)] // 짝은 max보다 먼저
        [InlineData(0, 0, 6, 0)]
        public void KeepsPairAndCullsOver(int total, int adults, int max, int cull) => Assert.Equal(cull, Herd.ToCull(total, adults, max));
    }
}
