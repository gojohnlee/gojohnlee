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
    public void DrinksForBiomeOnlyWhenFoesInView()
    {
        Assert.Equal("Poison", Resist.ToDrink("Swamp", true, false, No, Yes));
        Assert.Null(Resist.ToDrink("Swamp", false, false, No, Yes));
        Assert.Null(Resist.ToDrink("Meadows", true, false, No, Yes));
    }

    [Fact]
    public void PoisonedDrinksPoisonAnywhere()
    {
        Assert.Equal("Poison", Resist.ToDrink("Meadows", false, true, No, Yes));
        Assert.Equal("Poison", Resist.ToDrink("Mountain", true, true, No, Yes));
    }

    [Fact]
    public void SkipsWhenResistedOrMissing()
    {
        Assert.Null(Resist.ToDrink("Swamp", true, false, Yes, Yes));
        Assert.Null(Resist.ToDrink("Swamp", true, false, No, No));
        Assert.Null(Resist.ToDrink("Mountain", true, false, k => k == "Frost", Yes));
        Assert.Equal("Frost", Resist.ToDrink("Mountain", true, false, k => k == "Poison", Yes));
    }
}
