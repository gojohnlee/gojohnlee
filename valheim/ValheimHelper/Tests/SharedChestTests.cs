using ValheimHelper.Core.Storage;
using Xunit;

namespace ValheimHelper.Tests
{
    public class SharedChestTests
    {
        static (int, int)[] Steps(System.Collections.Generic.List<PutStep> plan) =>
            plan.ConvertAll(s => (s.Slot, s.N)).ToArray();

        [Fact]
        public void TakeOnlySameItem()
        {
            Assert.Equal(5, ChestDeal.Take("Wood|1|0", 30, "Wood|1|0", 5));
            Assert.Equal(12, ChestDeal.Take("Wood|1|0", 12, "Wood|1|0", 20)); // 있는 만큼만
            Assert.Equal(0, ChestDeal.Take("Stone|1|0", 30, "Wood|1|0", 5)); // 그사이 다른 물건(호스트가 먼저 잡음)
            Assert.Equal(0, ChestDeal.Take(null, 0, "Wood|1|0", 5));
            Assert.Equal(0, ChestDeal.Take("Wood|2|0", 30, "Wood|1|0", 5)); // 품질 다름
        }

        [Fact]
        public void PutPrefersDroppedSlotThenStacksThenEmpty()
        {
            var keys = new[] { "Wood", null, "Wood", "Stone" };
            var amounts = new[] { 45, 0, 10, 50 };
            var plan = ChestDeal.Put(keys, amounts, 50, 1, "Wood", 20, out int left);
            Assert.Equal(new[] { (1, 20) }, Steps(plan));
            Assert.Equal(0, left);

            plan = ChestDeal.Put(keys, amounts, 50, 3, "Wood", 60, out left); // 놓은 칸이 다른 물건 → 같은 물건 칸 먼저
            Assert.Equal(new[] { (0, 5), (2, 40), (1, 15) }, Steps(plan));
            Assert.Equal(0, left);
        }

        [Fact]
        public void PutFullChestReturnsLeftover()
        {
            var keys = new[] { "Wood", "Stone" };
            var amounts = new[] { 48, 50 };
            var plan = ChestDeal.Put(keys, amounts, 50, -1, "Wood", 10, out int left);
            Assert.Equal(new[] { (0, 2) }, Steps(plan));
            Assert.Equal(8, left); // 손님에게 돌려줌
            plan = ChestDeal.Put(keys, amounts, 50, 0, "Resin", 3, out left);
            Assert.Empty(plan);
            Assert.Equal(3, left);
        }

        [Fact]
        public void LedgerCorrectsMismatch()
        {
            var g = new GuestLedger();
            int take = g.Begin(false, "Wood", 10, 0f);
            int put = g.Begin(true, "Stone", 8, 0f);
            Assert.Equal(10, g.Locked("Wood"));
            Assert.Equal(0, g.Locked("Stone")); // 보낸 것은 가방에 없음
            Assert.Equal(-4, g.Reply(take, 6, out string k1)); // 6개만 꺼내짐 → 가방에서 4 뺌
            Assert.Equal("Wood", k1);
            Assert.Equal(0, g.Locked("Wood"));
            Assert.Equal(3, g.Reply(put, 5, out _)); // 5개만 들어감 → 3개 돌려받음
            Assert.Equal(0, g.Reply(put, 0, out string k3)); // 두 번 답 → 무시
            Assert.Null(k3);
            Assert.Equal(0, g.Count);
        }

        [Fact]
        public void LedgerExpireReturnsSentKeepsTaken()
        {
            var g = new GuestLedger();
            int a = g.Begin(false, "Wood", 10, 0f);
            Assert.Equal(0, g.Reply(a, 10, out _));
            int b = g.Begin(true, "Stone", 8, 1f);
            g.Begin(false, "Resin", 2, 4f);
            var back = new System.Collections.Generic.List<PutStepBack>();
            Assert.Equal(1, g.Expire(6f, 3f, back)); // b만 시간 넘김 → 보낸 돌 8개 돌려받음
            Assert.Single(back);
            Assert.Equal("Stone", back[0].Key);
            Assert.Equal(8, back[0].N);
            Assert.Equal(0, g.Reply(b, 0, out _)); // 늦게 온 답은 무시(가방 그대로)
            Assert.Equal(2, g.Locked("Resin"));
            Assert.Equal(1, g.Count);
        }
    }
}
