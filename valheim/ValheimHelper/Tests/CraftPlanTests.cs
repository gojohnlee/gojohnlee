using System.Collections.Generic;
using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;

public class CraftPlanTests
{
    // 바닐라 비슷한 작은 세계: 숯가마(목재 1 → 숯 1, 15초), 제련소(구리 광석 1 + 숯 2 → 구리 1, 30초, 광석 10개까지)
    static RecipeBook Book()
    {
        var b = new RecipeBook();
        b.SetSource("Wood", SourceKind.Gather);
        b.SetSource("CopperOre", SourceKind.Gather);
        b.SetSource("TinOre", SourceKind.Gather);
        b.SetSource("Feathers", SourceKind.Hunt);
        b.SetSource("DragonTear", SourceKind.OutOfScope);
        b.Add(new Process { Station = "kiln", Input = "Wood", Output = "Coal", SecondsPerProduct = 15, Capacity = 25 });
        b.Add(new Process { Station = "smelter", Input = "CopperOre", Output = "Copper", Fuel = "Coal", FuelPerProduct = 2, SecondsPerProduct = 30, Capacity = 10 });
        b.Add(new Process { Station = "smelter", Input = "TinOre", Output = "Tin", Fuel = "Coal", FuelPerProduct = 2, SecondsPerProduct = 30, Capacity = 10 });
        var bronze = new Recipe { Output = "Bronze", Station = "forge", StationLevel = 1 };
        bronze.Inputs.Add(new Ingredient("Copper", 2));
        bronze.Inputs.Add(new Ingredient("Tin", 1));
        b.Add(bronze);
        var arrow = new Recipe { Output = "ArrowWood", OutputAmount = 20, Station = "workbench" };
        arrow.Inputs.Add(new Ingredient("Wood", 8));
        arrow.Inputs.Add(new Ingredient("Feathers", 2));
        b.Add(arrow);
        var club = new Recipe { Output = "Club", Station = null };
        club.Inputs.Add(new Ingredient("Wood", 6));
        b.Add(club);
        var odd = new Recipe { Output = "Odd", Station = "workbench" };
        odd.Inputs.Add(new Ingredient("DragonTear", 1));
        b.Add(odd);
        return b;
    }

    static Facilities Base(bool kiln = true, bool smelter = true, int forge = 1)
    {
        var f = new Facilities();
        f.Stations["workbench"] = 2;
        if (forge > 0) f.Stations["forge"] = forge;
        if (kiln) f.Processors["kiln"] = 1;
        if (smelter) f.Processors["smelter"] = 1;
        return f;
    }

    static Dictionary<string, int> Stock(params (string, int)[] items) => items.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void CopperBarsFromConcept()
    {
        // 구상서 6-1 예: 구리 주괴 20 = 구리 광석 20 + 숯 40, 숯 40 = 목재 40
        var plan = new CraftPlanner(Book(), Base()).Expand("Copper", 20, null);
        Assert.Equal(20, plan.Gathers["CopperOre"]);
        Assert.Equal(40, plan.Gathers["Wood"]);
        Assert.Equal(new[] { "CopperOre", "Coal" }, plan.Short);
        Assert.False(plan.Partial);
        // 제련소 용량 10 → 두 번 싣기
        Assert.Equal(2, plan.Steps.Count(s => s.Kind == StepKind.Load && s.Station == "smelter"));
        // 숯가마 용량 25 → 40은 두 번
        Assert.Equal(2, plan.Steps.Count(s => s.Kind == StepKind.Load && s.Station == "kiln"));
    }

    [Fact]
    public void StockIsUsedFirstAndReservedOnce()
    {
        // 숯 10이 있으면 숯가마는 목재 30. 청동은 구리·주석 둘 다 숯이 필요 → 같은 숯을 두 번 쓰지 않음
        var plan = new CraftPlanner(Book(), Base()).Expand("Copper", 20, Stock(("Coal", 10)));
        Assert.Equal(30, plan.Gathers["Wood"]);
        Assert.Equal(10, plan.UsedStock["Coal"]);
        var bronze = new CraftPlanner(Book(), Base()).Expand("Bronze", 2, Stock(("Coal", 6)));
        // 구리 4(숯 8) + 주석 2(숯 4) = 숯 12, 재고 6 → 목재 6
        Assert.Equal(6, bronze.Gathers["Wood"]);
        Assert.Equal(6, bronze.UsedStock["Coal"]);
    }

    [Fact]
    public void FullStockMeansNothingToDo()
    {
        var plan = new CraftPlanner(Book(), Base()).Expand("Copper", 5, Stock(("Copper", 9)));
        Assert.Empty(plan.Steps);
        Assert.Equal(0f, plan.Seconds);
    }

    [Fact]
    public void BatchesRoundUp()
    {
        // 화살 30 → 20개씩 2번: 목재 16, 깃털 2×2
        var plan = new CraftPlanner(Book(), Base()).Expand("ArrowWood", 30, null);
        var craft = plan.Steps.Single(s => s.Kind == StepKind.Craft);
        Assert.Equal(2, craft.Crafts);
        Assert.Equal(16, plan.Gathers["Wood"]);
        Assert.Equal(4, plan.Gathers["Feathers"]);
        Assert.Contains(plan.Steps, s => s.Kind == StepKind.Hunt && s.Item == "Feathers");
    }

    [Fact]
    public void LeftoverFromOneBranchServesAnother()
    {
        // 화살 10(20개 나옴) 두 번 요청 → 두 번째는 남은 10으로
        var plan = new CraftPlanner(Book(), Base()).Expand(new[] { new Ingredient("ArrowWood", 10), new Ingredient("ArrowWood", 10) }, null);
        Assert.Single(plan.Steps.Where(s => s.Kind == StepKind.Craft));
    }

    [Fact]
    public void MissingStationStillGathersWhatItCan()
    {
        // "광석은 다 모았는데 제련소가 없어요"
        var plan = new CraftPlanner(Book(), Base(smelter: false)).Expand("Copper", 10, null);
        Assert.Contains("smelter", plan.MissingStations);
        Assert.Equal(10, plan.Gathers["CopperOre"]);
        Assert.True(plan.Partial);
        var noForge = new CraftPlanner(Book(), Base(forge: 0)).Expand("Bronze", 1, null);
        Assert.Contains("forge:1", noForge.MissingStations);
        Assert.True(noForge.Gathers.ContainsKey("CopperOre"));
    }

    [Fact]
    public void OutOfScopeAndUnknown()
    {
        var plan = new CraftPlanner(Book(), Base()).Expand("Odd", 1, null);
        Assert.Contains("DragonTear", plan.OutOfScope);
        var unknown = new CraftPlanner(Book(), Base()).Expand("Nothing", 1, null);
        Assert.Contains("Nothing", unknown.Unknown);
    }

    [Fact]
    public void CyclesAreStopped()
    {
        var b = new RecipeBook();
        var a = new Recipe { Output = "A" }; a.Inputs.Add(new Ingredient("B", 1)); b.Add(a);
        var bb = new Recipe { Output = "B" }; bb.Inputs.Add(new Ingredient("A", 1)); b.Add(bb);
        var plan = new CraftPlanner(b, new Facilities()).Expand("A", 1, null);
        Assert.Contains("A", plan.Unknown);
    }

    [Fact]
    public void HandRecipesNeedNoStation()
    {
        var plan = new CraftPlanner(Book(), new Facilities()).Expand("Club", 1, null);
        Assert.False(plan.Partial);
        Assert.Equal(6, plan.Gathers["Wood"]);
    }

    [Fact]
    public void SimulationOverlapsWaitingWithGathering()
    {
        // 목재 4초/개, 광석 4초/개. 구리 10: 광석 10(40초) + 목재 20(80초) → 숯가마 20×15=300초 기다림
        // → 제련 10×30=300초. 싣기·꺼내기 10초씩. 기다리는 동안 할 일이 없으면 그냥 흐른다.
        var plan = new CraftPlanner(Book(), Base()).Expand("Copper", 10, null);
        // 순서상 하한: 목재 80 + 싣기 10 + 숯 300 + 꺼내기 10 + 싣기 10 + 제련 300 + 꺼내기 10 = 720, 광석 40초는 숯 기다리는 동안
        Assert.Equal(720f, plan.Seconds, 0);
        Assert.False(plan.TooLong);
    }

    [Fact]
    public void LoadsFirstSoMachinesRunWhileGathering()
    {
        // 구리 20: 숯가마 두 번(25+15). 첫 싣기를 먼저 하면 그동안 광석을 캔다 → 단순 합보다 짧다
        var plan = new CraftPlanner(Book(), Base()).Expand("Copper", 20, null);
        float serial = plan.Steps.Sum(s => s.Seconds);
        Assert.True(plan.Seconds < serial, $"{plan.Seconds} < {serial}");
    }

    [Fact]
    public void TooLongOverTwoHours()
    {
        var plan = new CraftPlanner(Book(), Base(), (item, n) => n * 60f).Expand("Copper", 100, null);
        Assert.True(plan.TooLong);
    }

    [Fact]
    public void OnlyOneIngredient()
    {
        var b = Book();
        var r = new Recipe { Output = "Mix", OnlyOne = true };
        r.Inputs.Add(new Ingredient("Wood", 2));
        r.Inputs.Add(new Ingredient("CopperOre", 2));
        b.Add(r);
        var plan = new CraftPlanner(b, Base()).Expand("Mix", 1, null);
        Assert.True(plan.Gathers.ContainsKey("Wood"));
        Assert.False(plan.Gathers.ContainsKey("CopperOre"));
    }
}
