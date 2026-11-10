using ValheimHelper.Core.Work;
using Xunit;

namespace ValheimHelper.Tests
{
    public class ChestNamesTests
    {
        [Theory]
        [InlineData("장비상자", ChestTags.Gear)]
        [InlineData("my_GEAR", ChestTags.Gear)]
        [InlineData("음식 미드", ChestTags.Food)]
        [InlineData("Food-Mats", ChestTags.Food | ChestTags.Mats)]
        [InlineData("재료", ChestTags.Mats)]
        [InlineData("개인_백산수", ChestTags.Private)]
        [InlineData("가축 먹이", ChestTags.Livestock)]
        [InlineData("Livestock", ChestTags.Livestock)]
        [InlineData("백산수개인", ChestTags.Private)]
        [InlineData("개인 장비", ChestTags.Private)]
        [InlineData("private food", ChestTags.Private)]
        [InlineData("잡동사니", ChestTags.None)]
        [InlineData("", ChestTags.None)]
        [InlineData(null, ChestTags.None)]
        public void Parse(string name, ChestTags want) => Assert.Equal(want, ChestNames.Parse(name));

        [Fact]
        public void RoleChestsOnly()
        {
            var ok = ChestNames.Select(new[] { ChestTags.Gear, ChestTags.None, ChestTags.Food }, ChestTags.Gear);
            Assert.Equal(new[] { true, false, false }, ok);
        }

        [Fact]
        public void NoRoleChestFallsBackToGeneral()
        {
            var ok = ChestNames.Select(new[] { ChestTags.Food, ChestTags.None, ChestTags.Private }, ChestTags.Gear);
            Assert.Equal(new[] { false, true, false }, ok);
        }

        [Fact]
        public void NoGeneralTakesAllButPrivate()
        {
            var ok = ChestNames.Select(new[] { ChestTags.Food, ChestTags.Private }, ChestTags.Gear);
            Assert.Equal(new[] { true, false }, ok);
        }

        [Fact]
        public void AnyRoleSkipsOnlyPrivate()
        {
            var ok = ChestNames.Select(new[] { ChestTags.Gear, ChestTags.None, ChestTags.Private }, ChestTags.None);
            Assert.Equal(new[] { true, true, false }, ok);
        }

        [Theory]
        [InlineData("늑대", "Wolf")]
        [InlineData("늑대 우리", "Wolf")]
        [InlineData("Wolf Pen", "Wolf")]
        [InlineData("멧돼지", "Boar")]
        [InlineData("돼지우리", "Boar")]
        [InlineData("pig", "Boar")]
        [InlineData("록스", "Lox")]
        [InlineData("닭", "Hen")]
        [InlineData("chicken", "Hen")]
        [InlineData("아스크스빈", "Asksvin")]
        [InlineData("늑대 가죽", null)] // 재료 상자는 우리 아님
        [InlineData("가축", null)]
        [InlineData("우리", null)]
        [InlineData("", null)]
        public void PenSpecies(string name, string prefab) => Assert.Equal(prefab, ChestNames.PenSpecies(name));

        [Fact]
        public void SpeciesPenIsLivestock()
        {
            Assert.Equal(ChestTags.Livestock, ChestNames.Parse("늑대"));
            Assert.Equal(ChestTags.None, ChestNames.Parse("늑대 가죽"));
        }
    }
}
