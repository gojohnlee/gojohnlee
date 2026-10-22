using System;
using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;

public class FarmTests
{
    const int Meadows = 1, BlackForest = 8, Plains = 16;

    static Crop Carrot => new Crop { Sapling = "sapling_carrot", Seed = "CarrotSeeds", Produce = "Carrot", Yield = 1, Biomes = Meadows | BlackForest | Plains, GrowRadius = 0.5f, GrowMin = 4000, GrowMax = 5000 };
    static Crop SeedCarrot => new Crop { Sapling = "sapling_seedcarrot", Seed = "Carrot", Produce = "CarrotSeeds", Yield = 3, Biomes = Meadows | BlackForest | Plains, GrowRadius = 0.5f, GrowMin = 4000, GrowMax = 6000 };
    static Crop Barley => new Crop { Sapling = "sapling_barley", Seed = "Barley", Produce = "Barley", Yield = 2, Biomes = Plains, GrowRadius = 0.5f, GrowMin = 4000, GrowMax = 5000 };

    static List<Crop> All => new List<Crop> { Carrot, SeedCarrot, Barley };
    static Func<string, int> Stock(params (string, int)[] s)
    {
        var d = new Dictionary<string, int>();
        foreach (var (k, v) in s) d[k] = v;
        return k => d.TryGetValue(k, out var v) ? v : 0;
    }
    static List<Ingredient> Want(string item, int n) => new List<Ingredient> { new Ingredient(item, n) };

    [Fact]
    public void RadiusDefaultsAndCaps()
    {
        Assert.Equal(Farm.DefaultRadius, Farm.ClampRadius(0));
        Assert.Equal(Farm.MaxRadius, Farm.ClampRadius(50));
        Assert.Equal(5f, Farm.ClampRadius(5));
    }

    [Fact]
    public void SpotsStayInsideAndKeepSpacing()
    {
        float sp = Farm.Spacing(0.5f);
        var spots = Farm.Spots(100, -50, 5, sp);
        Assert.NotEmpty(spots);
        foreach (var s in spots)
            Assert.True(Math.Sqrt((s.X - 100) * (s.X - 100) + (s.Z + 50) * (s.Z + 50)) <= 5 - sp * 0.5f + 1e-3);
        for (int i = 0; i < spots.Count; i++)
            for (int j = i + 1; j < spots.Count; j++)
            {
                float dx = spots[i].X - spots[j].X, dz = spots[i].Z - spots[j].Z;
                Assert.True(Math.Sqrt(dx * dx + dz * dz) >= sp - 1e-3);
            }
    }

    [Fact]
    public void SpotsWalkInSnakeOrder()
    {
        var spots = Farm.Spots(0, 0, 3, 1f);
        // 이웃한 자리끼리는 한 칸(대각 포함) 안: 줄 끝에서 다음 줄로 넘어갈 때도 멀리 뛰지 않음
        int far = 0;
        for (int i = 1; i < spots.Count; i++)
        {
            float dx = spots[i].X - spots[i - 1].X, dz = spots[i].Z - spots[i - 1].Z;
            if (dx * dx + dz * dz > 2.5f * 2.5f) far++;
        }
        Assert.Equal(0, far);
    }

    [Fact]
    public void TinyFieldHasNoSpots()
    {
        Assert.Empty(Farm.Spots(0, 0, 0.1f, 1f));
    }

    [Fact]
    public void BiomeAndColdRules()
    {
        Assert.True(Farm.CanGrow(Carrot, Meadows));
        Assert.False(Farm.CanGrow(Barley, Meadows));
        var cold = new Crop { Biomes = Farm.Mountain };
        Assert.False(Farm.CanGrow(cold, Farm.Mountain));
        cold.TolerateCold = true;
        Assert.True(Farm.CanGrow(cold, Farm.Mountain));
        var hot = new Crop { Biomes = Farm.AshLands };
        Assert.False(Farm.CanGrow(hot, Farm.AshLands));
    }

    [Fact]
    public void PlantsWantedWithEnoughSeedsNoUpkeep()
    {
        var p = Farm.Plan(All, Meadows, 100, Stock(("CarrotSeeds", 30)), Want("Carrot", 8));
        Assert.Equal(8, p.Count("sapling_carrot"));
        Assert.Equal(0, p.Count("sapling_seedcarrot"));
        Assert.Empty(p.Missing);
    }

    [Fact]
    public void FewSeedsLimitAndReportMissing()
    {
        var p = Farm.Plan(All, Meadows, 100, Stock(("CarrotSeeds", 3)), Want("Carrot", 8));
        Assert.Equal(3, p.Count("sapling_carrot"));
        Assert.Single(p.Missing);
        Assert.Equal(5, p.Missing[0].Amount);
    }

    [Fact]
    public void SeedUpkeepPlantsCarrotsForSeeds()
    {
        // 씨앗 12 → 당근 8 심으면 4 남음 → 10까지 6 모자람 → 씨앗 당근(3개씩) 2그루, 당근 2개 씀
        var p = Farm.Plan(All, Meadows, 100, Stock(("CarrotSeeds", 12), ("Carrot", 5)), Want("Carrot", 8));
        Assert.Equal(8, p.Count("sapling_carrot"));
        Assert.Equal(2, p.Count("sapling_seedcarrot"));
    }

    [Fact]
    public void UpkeepNeedsProduceInStock()
    {
        var p = Farm.Plan(All, Meadows, 100, Stock(("CarrotSeeds", 12)), Want("Carrot", 8));
        Assert.Equal(0, p.Count("sapling_seedcarrot"));
    }

    [Fact]
    public void SelfSeedingCropHasNoUpkeep()
    {
        var p = Farm.Plan(All, Plains, 100, Stock(("Barley", 6)), Want("Barley", 10));
        Assert.Equal(5, p.Count("sapling_barley"));
        Assert.Single(p.Plant);
    }

    [Fact]
    public void SlotsLimitEverything()
    {
        var p = Farm.Plan(All, Meadows, 4, Stock(("CarrotSeeds", 12), ("Carrot", 5)), Want("Carrot", 8));
        Assert.Equal(4, p.Count("sapling_carrot"));
        Assert.Equal(0, p.Count("sapling_seedcarrot"));
        Assert.Equal(4, p.Missing[0].Amount);
    }

    [Fact]
    public void WrongBiomeIsMissing()
    {
        var p = Farm.Plan(All, Meadows, 100, Stock(("Barley", 20)), Want("Barley", 4));
        Assert.Empty(p.Plant);
        Assert.Equal(4, p.Missing[0].Amount);
    }

    [Fact]
    public void YieldRoundsUpPlants()
    {
        var p = Farm.Plan(All, Plains, 100, Stock(("Barley", 20)), Want("Barley", 5));
        Assert.Equal(3, p.Count("sapling_barley"));
        Assert.Empty(p.Missing);
    }

    [Fact]
    public void GrowSecondsIsSlowestMax()
    {
        var crops = All;
        var p = Farm.Plan(crops, Meadows, 100, Stock(("CarrotSeeds", 12), ("Carrot", 5)), Want("Carrot", 8));
        Assert.Equal(6000, Farm.GrowSeconds(crops, p));
        Assert.Equal(0, Farm.GrowSeconds(crops, new FarmPlan()));
    }
}
