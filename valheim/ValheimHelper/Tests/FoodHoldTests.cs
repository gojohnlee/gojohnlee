using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using Xunit;

namespace ValheimHelper.Tests
{
    public class FoodHoldTests
    {
        static List<FoodKind> Kinds(params (string n, float f, int h)[] k) { var l = new List<FoodKind>(); foreach (var x in k) l.Add(new FoodKind(x.n, x.f, x.h)); return l; }

        [Fact]
        public void 몫은_다른_음식이_있으면_뒤로()
        {
            var hold = new FoodHold();
            hold.Add("cooked", 4, 0);
            var pick = FoodHold.Pick(Kinds(("cooked", 30, 4), ("berry", 10, 5), ("honey", 8, 1)), 3, n => hold.Held(n, 10));
            Assert.Equal(new[] { "berry", "honey", "berry" }, pick);
        }

        [Fact]
        public void 다른_음식이_없어도_몫은_안_씀()
        {
            var hold = new FoodHold();
            hold.Add("cooked", 4, 0);
            var pick = FoodHold.Pick(Kinds(("cooked", 30, 4), ("berry", 10, 1)), 3, n => hold.Held(n, 10));
            Assert.Equal(new[] { "berry" }, pick);
            Assert.Equal(0, hold.Free("cooked", 4, 10));
            Assert.Equal(2, hold.Free("cooked", 6, 10));
            Assert.Equal(4, hold.Free("cooked", 4, FoodHold.HoldTime));
        }

        [Fact]
        public void 몫보다_많으면_넘는_수는_먼저()
        {
            var hold = new FoodHold();
            hold.Add("cooked", 4, 0);
            var pick = FoodHold.Pick(Kinds(("cooked", 30, 6), ("berry", 10, 5)), 3, n => hold.Held(n, 10));
            Assert.Equal(new[] { "cooked", "berry", "cooked" }, pick);
        }

        [Fact]
        public void 시간이_지나면_보통_음식()
        {
            var hold = new FoodHold();
            hold.Add("cooked", 4, 0);
            Assert.Equal(4, hold.Held("cooked", FoodHold.HoldTime - 1));
            Assert.Equal(0, hold.Held("cooked", FoodHold.HoldTime));
            var pick = FoodHold.Pick(Kinds(("cooked", 30, 4), ("berry", 10, 5)), 2, n => hold.Held(n, FoodHold.HoldTime + 1));
            Assert.Equal(new[] { "cooked", "berry" }, pick);
        }

        [Fact]
        public void 주문마다_만료가_따로()
        {
            var hold = new FoodHold();
            hold.Add("cooked", 4, 0);
            hold.Add("cooked", 3, 1000);
            Assert.Equal(7, hold.Held("cooked", 2000));
            Assert.Equal(3, hold.Held("cooked", FoodHold.HoldTime));
            Assert.Equal(0, hold.Held("cooked", FoodHold.HoldTime + 1000));
        }

        [Fact]
        public void 가져가면_몫이_줄고_새로_생긴_것은_보통_음식()
        {
            var hold = new FoodHold();
            hold.Add("cooked", 4, 0);
            hold.Add("cooked", 3, 100);
            hold.Trim("cooked", 5, 200); // 플레이어가 2개 가져감 → 오래된 주문부터 줄임
            Assert.Equal(5, hold.Held("cooked", 200));
            Assert.Equal(3, hold.Held("cooked", FoodHold.HoldTime + 50)); // 옛 주문 2개 남은 것만 만료
            Assert.Equal(2, hold.Free("cooked", 5, FoodHold.HoldTime + 50));
            hold.Trim("cooked", 9, 300); // 늘어난 건 몫이 안 늘음
            Assert.Equal(3, hold.Held("cooked", FoodHold.HoldTime + 50));
        }

        [Fact]
        public void 시킨_음식만_있으면_일꾼_음식은_0()
        {
            var hold = new FoodHold();
            hold.Add("cooked", 12, 0);
            var haves = new Dictionary<string, int> { ["cooked"] = 12, ["berry"] = 3 };
            Assert.Equal(3, hold.FreeTotal(haves, 10));
            haves["cooked"] = 14; // 시킨 수보다 더 있는 것은 보통 음식
            Assert.Equal(5, hold.FreeTotal(haves, 10));
            haves["cooked"] = 8; // 가져감
            Assert.Equal(3, hold.FreeTotal(haves, 10));
            Assert.Equal(8, hold.Held("cooked", 10));
        }
    }
}
