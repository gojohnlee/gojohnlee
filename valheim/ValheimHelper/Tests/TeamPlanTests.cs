using ValheimHelper.Core.Npc;
using Xunit;

namespace ValheimHelper.Tests
{
    public class TeamPlanTests
    {
        [Fact]
        public void NeededPicksSmallestTeam()
        {
            var hits = new[] { 30f, 20f };
            Assert.Equal(1, TeamPlan.Needed(x => x >= 0f, hits));
            Assert.Equal(2, TeamPlan.Needed(x => x >= 25f, hits));
            Assert.Equal(3, TeamPlan.Needed(x => x >= 45f, hits));
            Assert.Equal(0, TeamPlan.Needed(x => x >= 60f, hits));
        }

        [Fact]
        public void NeededWithoutHelpers()
        {
            Assert.Equal(0, TeamPlan.Needed(x => x >= 10f, new float[0]));
            Assert.Equal(2, TeamPlan.Needed(x => x >= 10f, new[] { 10f }));
            Assert.Equal(0, TeamPlan.Needed(x => x >= 10f, new[] { -5f, 5f }));
        }

        [Fact]
        public void NextStepsUpToThree()
        {
            Assert.Equal(1, TeamPlan.Next(0));
            Assert.Equal(2, TeamPlan.Next(1));
            Assert.Equal(3, TeamPlan.Next(2));
            Assert.Equal(0, TeamPlan.Next(3));
        }

        [Fact]
        public void StartKnownFoeUsesNeeded()
        {
            Assert.Equal(2, TeamPlan.Start(true, 2, 0));
            Assert.Equal(0, TeamPlan.Start(true, 0, 0));
            Assert.Equal(1, TeamPlan.Start(false, 0, 0));
            Assert.Equal(3, TeamPlan.Start(false, 0, 2));
        }
    }
}
