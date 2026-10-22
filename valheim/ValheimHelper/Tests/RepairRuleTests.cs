using ValheimHelper.Core.Npc;
using Xunit;

public class RepairRuleTests
{
    [Theory]
    [InlineData(true, 0f, 100f, true)]     // 부러짐
    [InlineData(true, 49f, 100f, true)]
    [InlineData(true, 50f, 100f, false)]
    [InlineData(true, 99f, 100f, false)]   // 조금 닳은 건 안 감
    [InlineData(false, 0f, 100f, false)]   // 내구도 없는 물건
    [InlineData(true, 0f, 0f, false)]
    public void Needs(bool use, float dur, float max, bool expect) => Assert.Equal(expect, RepairRule.Needs(use, dur, max));
}
