using ValheimHelper.Core.Work;
using Xunit;

namespace ValheimHelper.Tests
{
    public class PreyProgressTests
    {
        [Fact]
        public void KeepsWhileHealthDrops()
        {
            var p = new PreyProgress();
            p.Start(100, 0);
            for (int t = 50; t <= 300; t += 50)
                Assert.Equal(PreyProgress.Verdict.Keep, p.Tick(100 - t / 10.0, t));
        }

        [Fact]
        public void NoHarmGivesUp()
        {
            var p = new PreyProgress();
            p.Start(100, 0);
            Assert.Equal(PreyProgress.Verdict.Keep, p.Tick(90, 10));
            Assert.Equal(PreyProgress.Verdict.Keep, p.Tick(90, 69));
            Assert.Equal(PreyProgress.Verdict.NoHarm, p.Tick(90, 70));
        }

        [Fact]
        public void RegenIsNotProgress()
        {
            var p = new PreyProgress();
            p.Start(100, 0);
            p.Tick(80, 10);
            p.Tick(95, 30);
            Assert.Equal(PreyProgress.Verdict.Keep, p.Tick(85, 60)); // 되찬 뒤 다시 깎여도 최저 80 밑이 아니면 진척 아님
            Assert.Equal(PreyProgress.Verdict.NoHarm, p.Tick(85, 70));
        }

        [Fact]
        public void MaxEndsEvenWithProgress()
        {
            var p = new PreyProgress();
            p.Start(1000, 0);
            Assert.Equal(PreyProgress.Verdict.TooLong, p.Tick(1, PreyProgress.MaxSeconds));
        }

        [Fact]
        public void LongNotedOnce()
        {
            var p = new PreyProgress();
            p.Start(100, 0);
            Assert.False(p.NoteLong(119));
            Assert.True(p.NoteLong(121));
            Assert.False(p.NoteLong(200));
        }
    }
}
