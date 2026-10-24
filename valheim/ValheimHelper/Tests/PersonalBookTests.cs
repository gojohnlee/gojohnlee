using System.Collections.Generic;
using ValheimHelper.Core.Storage;
using Xunit;

namespace ValheimHelper.Tests
{
    public class PersonalBookTests
    {
        [Fact]
        public void OnceIsRemovedWhenFull()
        {
            var b = new PersonalBook();
            b.Set(1, "MeadHealthMinor", 5, once: true);
            var stock = new Dictionary<string, int> { ["MeadHealthMinor"] = 2 };
            var due = b.Due((o, it) => stock.TryGetValue(it, out int n) ? n : 0);
            Assert.Single(due);
            Assert.Equal(3, due[0].Missing);
            stock["MeadHealthMinor"] = 5;
            Assert.Empty(b.Due((o, it) => stock[it]));
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void KeepStaysAndUsesSlack()
        {
            var b = new PersonalBook();
            b.Set(1, "ArrowWood", 100, once: false);
            Assert.Empty(b.Due((o, it) => 95)); // 10% 안쪽은 안 나섬
            var due = b.Due((o, it) => 80);
            Assert.Equal(20, due[0].Missing);
            Assert.Empty(b.Due((o, it) => 100));
            Assert.Equal(1, b.Count);
        }

        [Fact]
        public void OwnersAreSeparate()
        {
            var b = new PersonalBook();
            b.Set(1, "Wood", 5, true);
            b.Set(2, "Wood", 7, false);
            b.Set(1, "Wood", 9, false); // 같은 사람·물건은 바꿈
            Assert.Equal(2, b.Count);
            Assert.Equal(9, b.Find(1, "Wood").Amount);
            Assert.False(b.Find(1, "Wood").Once);
            Assert.Equal(1, b.Off(1));
            Assert.Null(b.Find(1, "Wood"));
            Assert.NotNull(b.Find(2, "Wood"));
            b.Set(0, "Wood", 5, true); // 모르는 사람은 안 적음
            Assert.Equal(1, b.Count);
        }

        [Fact]
        public void SaveLoadRoundTrip()
        {
            var b = new PersonalBook();
            b.Set(123456789012L, "MeadHealthMinor", 5, true);
            b.Set(42, "ArrowWood", 100, false);
            var c = PersonalBook.Load(b.Save());
            Assert.False(c.Dirty);
            Assert.Equal(2, c.Count);
            Assert.True(c.Find(123456789012L, "MeadHealthMinor").Once);
            Assert.Equal(100, c.Find(42, "ArrowWood").Amount);
            Assert.Equal(0, PersonalBook.Load("엉망|x;1|Wood|abc|o").Count);
        }
    }
}
