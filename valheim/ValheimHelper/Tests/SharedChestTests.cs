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

        static System.Collections.Generic.Dictionary<string, int> D(params (string, int)[] kv)
        {
            var d = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        [Fact]
        public void LedgerCorrectsMismatch()
        {
            var g = new GuestLedger();
            int take = g.Begin(D(("Wood", 10)), 0f);
            int put = g.Begin(D(("Stone", -8)), 0f);
            Assert.Equal(10, g.Locked("Wood"));
            Assert.Equal(0, g.Locked("Stone")); // 보낸 것은 가방에 없음
            Assert.Equal(D(("Wood", -4)), g.Reply(take, D(("Wood", -6)))); // 6개만 꺼내짐 → 가방에서 4 뺌
            Assert.Equal(0, g.Locked("Wood"));
            Assert.Equal(D(("Stone", 3)), g.Reply(put, D(("Stone", 5)))); // 5개만 들어감 → 3개 돌려받음
            Assert.Null(g.Reply(put, D())); // 두 번 답 → 무시
            Assert.Equal(0, g.Count);
        }

        [Fact]
        public void LedgerSwapAndInsideMoveMatchExactly()
        {
            var g = new GuestLedger();
            int swap = g.Begin(D(("Wood", 5), ("Stone", -3)), 0f); // 상자 나무 5 ↔ 가방 돌 3
            Assert.Empty(g.Reply(swap, D(("Wood", -5), ("Stone", 3))));
            int inside = g.Begin(D(), 0f); // 상자 안 옮기기: 가방 변화 없음
            Assert.Empty(g.Reply(inside, D()));
            int half = g.Begin(D(), 0f); // 상자 안 옮기기 중 넣을 자리 없어 2개 남음 → 상자 -2 → 가방에 2
            Assert.Equal(D(("Wood", 2)), g.Reply(half, D(("Wood", -2))));
        }

        [Fact]
        public void LedgerExpireReturnsSentKeepsTaken()
        {
            var g = new GuestLedger();
            int a = g.Begin(D(("Wood", 10)), 0f);
            Assert.Empty(g.Reply(a, D(("Wood", -10))));
            int b = g.Begin(D(("Stone", -8), ("Wood", 1)), 1f);
            g.Begin(D(("Resin", 2)), 4f);
            var back = new System.Collections.Generic.List<PutStepBack>();
            Assert.Equal(1, g.Expire(6f, 3f, back)); // b만 시간 넘김 → 보낸 돌 8개 돌려받음, 꺼낸 나무는 둠
            Assert.Single(back);
            Assert.Equal("Stone", back[0].Key);
            Assert.Equal(8, back[0].N);
            Assert.Null(g.Reply(b, D())); // 늦게 온 답은 무시(가방 그대로)
            Assert.Equal(2, g.Locked("Resin"));
            Assert.Equal(1, g.Count);
        }

        static string Ops(System.Collections.Generic.List<SlotOp> ops) =>
            string.Join(" ", ops.ConvertAll(o => o.Slot + ":" + o.Key + "x" + o.N));

        [Fact]
        public void DiffFindsTakesAndPuts()
        {
            var takes = new System.Collections.Generic.List<SlotOp>();
            var puts = new System.Collections.Generic.List<SlotOp>();
            // 0: 나무 20 → 15(5 꺼냄), 1: 빈칸 → 돌 4(넣음), 2: 송진 3 → 가죽 1(바꿈), 3: 그대로
            ChestDiff.Diff(new[] { "Wood", null, "Resin", "Flint" }, new[] { 20, 0, 3, 7 },
                           new[] { "Wood", "Stone", "Leather", "Flint" }, new[] { 15, 4, 1, 7 }, takes, puts);
            Assert.Equal("0:Woodx5 2:Resinx3", Ops(takes));
            Assert.Equal("1:Stonex4 2:Leatherx1", Ops(puts));

            takes.Clear(); puts.Clear(); // 상자 안에서 0 → 3으로 옮김
            ChestDiff.Diff(new[] { "Wood", null, null, null }, new[] { 20, 0, 0, 0 },
                           new[] { null, null, null, "Wood" }, new[] { 0, 0, 0, 20 }, takes, puts);
            Assert.Equal("0:Woodx20", Ops(takes));
            Assert.Equal("3:Woodx20", Ops(puts));
        }

        [Fact]
        public void PutAllowedNeverCreates()
        {
            Assert.Equal(20, ChestDiff.PutAllowed(20, 0, 0)); // 상자 안 옮기기: 꺼낸 만큼
            Assert.Equal(0, ChestDiff.PutAllowed(0, 0, 0)); // 꺼내기 실패(그사이 누가 가져감) → 새로 만들지 않음
            Assert.Equal(3, ChestDiff.PutAllowed(0, 5, 2)); // 가방에서 보낸 5 중 2 넣음
            Assert.Equal(0, ChestDiff.PutAllowed(5, -5, 9));
        }
    }
}
