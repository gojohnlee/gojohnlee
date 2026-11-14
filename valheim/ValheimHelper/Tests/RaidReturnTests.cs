using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;
using Xunit;

namespace ValheimHelper.Tests
{
    public class RaidReturnTests
    {
        [Fact]
        public void GoesHomeWhenAtLeastHalfLeftOnArrival()
        {
            Assert.True(RaidReturn.GoHome(60, 0, 0));
            Assert.True(RaidReturn.GoHome(60, 0, 100));
        }

        [Fact]
        public void KeepsWorkingWhenLessThanHalfLeftOnArrival()
        {
            Assert.False(RaidReturn.GoHome(60, 40, 0));
            Assert.False(RaidReturn.GoHome(60, 0, 160));
        }

        [Fact]
        public void ExactlyHalfCountsAsGoHome()
        {
            Assert.True(RaidReturn.GoHome(60, 30, 0));
            Assert.True(RaidReturn.GoHome(60, 0, 150));
        }

        [Fact]
        public void ArrivalTimeUsesWalkSpeed()
        {
            // 도착 시간 = 거리 ÷ Departure.WalkSpeed(=5m/s) → 150m면 30초. 남은 59초 − 30초 = 29초라 절반(30초) 미달
            Assert.Equal(30.0, 150 / Departure.WalkSpeed, 6);
            Assert.False(RaidReturn.GoHome(60, 1, 150));
        }
    }
}
