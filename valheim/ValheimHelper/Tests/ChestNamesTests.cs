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
    }
}
