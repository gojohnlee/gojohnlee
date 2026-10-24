using ValheimHelper.Core.Storage;
using Xunit;

namespace ValheimHelper.Tests
{
    public class ChestTidyTests
    {
        static readonly bool[] All3 = { true, true, true };

        [Fact]
        public void MostFilledChestWins()
        {
            Assert.Equal(1, ChestTidy.Target(new[] { 10, 30, 5 }, All3));
            Assert.Equal(0, ChestTidy.Target(new[] { 7, 7, 1 }, All3)); // 같으면 앞(가까운) 상자
        }

        [Fact]
        public void NothingToMove()
        {
            Assert.Equal(-1, ChestTidy.Target(new[] { 0, 12, 0 }, All3));
            Assert.Equal(-1, ChestTidy.Target(new[] { 0, 0, 0 }, All3));
            Assert.Equal(-1, ChestTidy.Target(new int[0], new bool[0]));
            Assert.Equal(-1, ChestTidy.Target(new[] { 3, 4 }, new[] { false, false }));
        }

        [Fact]
        public void RoleChestEvenIfEmpty()
        {
            // 상자 2만 역할 상자(고를 수 있음) → 비어 있어도 거기로
            Assert.Equal(2, ChestTidy.Target(new[] { 10, 30, 0 }, new[] { false, false, true }));
            // 역할 상자에만 있으면 옮길 것 없음
            Assert.Equal(-1, ChestTidy.Target(new[] { 0, 0, 9 }, new[] { false, false, true }));
        }

        [Fact]
        public void StableAfterGathering()
        {
            var counts = new[] { 10, 30, 5 };
            int t = ChestTidy.Target(counts, All3);
            counts[t] += counts[0] + counts[2];
            counts[0] = counts[2] = 0;
            Assert.Equal(-1, ChestTidy.Target(counts, All3));
            // 자리가 모자라 일부만 옮겨져도 같은 상자가 그대로 가장 많음
            Assert.Equal(1, ChestTidy.Target(new[] { 4, 41, 0 }, All3));
        }
    }
}
