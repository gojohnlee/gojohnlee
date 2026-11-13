using ValheimHelper.Core.Work;
using Xunit;

namespace ValheimHelper.Tests
{
    public class RecoveryTests
    {
        [Fact]
        public void HurtAndReadyThresholds()
        {
            Assert.True(Recovery.IsHurt(39f, 100f));
            Assert.False(Recovery.IsHurt(40f, 100f));
            Assert.False(Recovery.IsReady(90f, 100f));
            Assert.True(Recovery.IsReady(95f, 100f));
        }

        [Fact]
        public void RetreatBelowFullIsHurt()
        {
            Assert.True(Recovery.RetreatIsHurt(41f, 71f));
            Assert.False(Recovery.RetreatIsHurt(70f, 71f));
        }

        [Fact]
        public void ThirdBigHurtAtSamePlaceGivesUp()
        {
            var r = new Recovery();
            Assert.False(r.Record(0f, 0f));
            Assert.False(r.Record(200f, 0f));
            Assert.False(r.Record(10f, 10f));
            Assert.True(r.Record(-20f, 5f));
            Assert.Equal(1, r.CountAt(200f, 0f));
        }

        [Fact]
        public void NearbyOnlyWithSpaceSafeGatherAndNoNewHit()
        {
            Assert.Equal(RecoverWay.Nearby, Recovery.Choose(false, true, true, false));
            Assert.Equal(RecoverWay.Home, Recovery.Choose(false, false, true, false));
            Assert.Equal(RecoverWay.Home, Recovery.Choose(false, true, false, false));
            Assert.Equal(RecoverWay.Home, Recovery.Choose(false, true, true, true));
            Assert.Equal(RecoverWay.GiveUp, Recovery.Choose(true, true, true, false));
        }

        [Fact]
        public void SaveLoadKeepsCountsAndSkipsBroken()
        {
            var r = new Recovery();
            r.Record(1.25f, -3f); r.Record(1f, -3f); r.Record(500f, 500f);
            var back = Recovery.Load(r.Save() + ";bad;1,2;x,1,1;3,3,0");
            Assert.Equal(2, back.CountAt(1f, -3f));
            Assert.Equal(1, back.CountAt(500f, 500f));
            Assert.Equal(0, back.CountAt(300f, -300f));
            Assert.True(back.Record(0f, -3f));
        }
    }
}
