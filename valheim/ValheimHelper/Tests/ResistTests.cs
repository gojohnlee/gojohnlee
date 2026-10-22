using ValheimHelper.Core.Npc;
using Xunit;

public class ResistTests
{
    static bool No(string k) => false;
    static bool Yes(string k) => true;

    [Theory]
    [InlineData("Swamp", "Poison")]
    [InlineData("Mountain", "Frost")]
    [InlineData("DeepNorth", "Frost")]
    [InlineData("AshLands", "Fire")]
    [InlineData("Meadows", null)]
    [InlineData("BlackForest", null)]
    public void BiomeNeeds(string biome, string kind) => Assert.Equal(kind, Resist.ForBiome(biome));

    [Fact]
    public void DrinksWhatTheFoeDeals()
    {
        Assert.Equal("Poison", Resist.ToDrink(new[] { "Poison" }, "Meadows", No, Yes));
        Assert.Equal("Fire", Resist.ToDrink(new[] { "Fire" }, "Swamp", No, Yes));
    }

    [Fact]
    public void FoeWithoutElementNeedsNothingEvenInSwamp()
    {
        Assert.Null(Resist.ToDrink(new string[0], "Swamp", No, Yes));
    }

    [Fact]
    public void UnknownFoeFallsBackToBiome()
    {
        Assert.Equal("Poison", Resist.ToDrink(null, "Swamp", No, Yes));
        Assert.Null(Resist.ToDrink(null, "Meadows", No, Yes));
    }

    [Fact]
    public void SkipsWhenResistedOrMissing()
    {
        Assert.Null(Resist.ToDrink(new[] { "Poison" }, "Swamp", Yes, Yes));
        Assert.Null(Resist.ToDrink(new[] { "Poison" }, "Swamp", No, No));
        Assert.Null(Resist.ToDrink(null, "Mountain", k => k == "Frost", Yes));
        Assert.Equal("Fire", Resist.ToDrink(new[] { "Frost", "Fire" }, "Mountain", k => k == "Frost", Yes));
    }
}
