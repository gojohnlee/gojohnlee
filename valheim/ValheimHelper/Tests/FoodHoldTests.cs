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
        public void 다른_음식이_없으면_몫에서도()
        {
            var hold = new FoodHold();
            hold.Add("cooked", 4, 0);
            var pick = FoodHold.Pick(Kinds(("cooked", 30, 4), ("berry", 10, 1)), 3, n => hold.Held(n, 10));
            Assert.Equal(new[] { "berry", "cooked", "cooked" }, pick);
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
    }
}
