using ValheimHelper.Core.Storage;
using Xunit;

namespace ValheimHelper.Tests
{
    public class StockTakeTests
    {
        [Fact]
        public void BagFirstThenNearestChest()
        {
            Assert.Equal(new[] { 3, 0 }, StockTake.Plan(5, 2, new[] { 4, 9 }));
            Assert.Equal(new[] { 4, 2 }, StockTake.Plan(8, 2, new[] { 4, 9 }));
        }

        [Fact]
        public void BagEnoughTakesNothing()
        {
            Assert.Equal(new[] { 0, 0 }, StockTake.Plan(3, 5, new[] { 4, 9 }));
            Assert.Equal(new int[0], StockTake.Plan(0, 0, new int[0]));
        }

        [Fact]
        public void NotEnoughTakesNothing()
        {
            Assert.Null(StockTake.Plan(20, 2, new[] { 4, 9 }));
            Assert.False(StockTake.Enough(20, 2, 13));
            Assert.True(StockTake.Enough(15, 2, 13));
            Assert.True(StockTake.Enough(0, 0, 0));
        }

        [Fact]
        public void SkipsEmptyChests()
        {
            Assert.Equal(new[] { 0, 3, 1 }, StockTake.Plan(4, -1, new[] { 0, 3, 5 }));
        }

        [Fact]
        public void ReservedLeavesOnlySpare()
        {
            Assert.Equal(5, StockTake.Spare(10, 0, 5));
            Assert.Equal(3, StockTake.Spare(10, 7, 5));
            Assert.Equal(0, StockTake.Spare(6, 7, 5));
            Assert.Equal(0, StockTake.Spare(10, 3, 0));
            Assert.Equal(4, StockTake.Spare(10, 3, 4));
        }
    }
}
