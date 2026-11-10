using ValheimHelper.Core.Work;
using Xunit;

namespace ValheimHelper.Tests
{
    public class DebtBookTests
    {
        [Fact]
        public void AddRepayAndRemoveWhenPaid()
        {
            var b = new DebtBook();
            Assert.Equal(5, b.Add("Wood", 5));
            Assert.Equal(8, b.Add("Wood", 3));
            b.Repaid("Wood", 6);
            Assert.Equal(2, b.Get("Wood"));
            b.Repaid("Wood", 9);
            Assert.Equal(0, b.Count);
            b.Repaid("Stone", 1); // 없는 것은 그대로
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void SaveLoadRoundTrip()
        {
            var b = new DebtBook();
            b.Add("Wood", 12);
            b.Add("CopperOre", 4);
            var c = DebtBook.Load(b.Save());
            Assert.Equal(12, c.Get("Wood"));
            Assert.Equal(4, c.Get("CopperOre"));
            Assert.False(c.Dirty);
        }

        [Fact]
        public void BadInputIgnored()
        {
            var b = DebtBook.Load("Wood=3;;=4;Stone=x;Flint=-2");
            Assert.Equal(1, b.Count);
            Assert.Equal(3, b.Get("Wood"));
            Assert.Equal(0, b.Add("a;b", 2));
            Assert.Equal(0, DebtBook.Load(null).Count);
        }
    }
}
