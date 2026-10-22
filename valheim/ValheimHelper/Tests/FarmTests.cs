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

    [Fact]
    public void FarmBookAddsMovesAndCaps()
    {
        var b = new FarmBook();
        Assert.Equal(FarmSetResult.Added, b.Set(0, 0, 10));
        Assert.Equal(FarmSetResult.Moved, b.Set(5, 0, 30)); // 첫 밭 안 → 옮김, 반경은 최대 20
        Assert.Single(b.Fields);
        Assert.Equal(5f, b.Fields[0].X);
        Assert.Equal(20, b.Fields[0].Radius);
        Assert.Equal(FarmSetResult.Added, b.Set(100, 0, 10));
        for (int i = 2; i < FarmBook.Max; i++) b.Set(1000 * i, 0, 10);
        Assert.Equal(FarmBook.Max, b.Fields.Count);
        Assert.Equal(FarmSetResult.Full, b.Set(-5000, 0, 10));
        Assert.True(b.Remove(101, 1));
        Assert.False(b.Remove(-5000, 0));
        Assert.Equal(FarmBook.Max - 1, b.Fields.Count);
    }

    [Fact]
    public void FarmBookSaveLoadRoundTrip()
    {
        var b = new FarmBook();
        b.Set(12.34f, -56.7f, 10);
        b.Set(300, 400, 15);
        var c = FarmBook.Load(b.Save());
        Assert.False(c.Dirty);
        Assert.Equal(2, c.Fields.Count);
        Assert.Equal(12.3f, c.Fields[0].X, 1);
        Assert.Equal(-56.7f, c.Fields[0].Z, 1);
        Assert.Equal(15, c.Fields[1].Radius);
        Assert.Empty(FarmBook.Load("x;1,2;3,4,-1;").Fields);
    }

    [Fact]
    public void FillPlantsStockedSeedsAndKeepsSeeds()
    {
        // 씨앗만큼(자리 3/4까지), 씨앗용 작물은 채우지 않음
        var a = Farm.Fill(All, Meadows, 20, Stock(("CarrotSeeds", 6)));
        Assert.Equal(6, a.Count("sapling_carrot"));
        Assert.Equal(0, a.Count("sapling_seedcarrot"));
        var b = Farm.Fill(All, Meadows, 20, Stock(("CarrotSeeds", 30), ("Carrot", 5)));
        Assert.Equal(15, b.Count("sapling_carrot"));
        Assert.Equal(0, b.Count("sapling_seedcarrot"));
        // 씨앗을 다 쓰면 남긴 자리에 당근 씨앗 심기(10개 되게 3개씩 → 4)
        var c = Farm.Fill(All, Meadows, 20, Stock(("CarrotSeeds", 12), ("Carrot", 4)));
        Assert.Equal(12, c.Count("sapling_carrot"));
        Assert.Equal(4, c.Count("sapling_seedcarrot"));
        // 평원: 돌아가며, 보리는 재고의 반까지
        var d = Farm.Fill(All, Plains, 40, Stock(("CarrotSeeds", 4), ("Barley", 9)));
        Assert.Equal(4, d.Count("sapling_carrot"));
        Assert.Equal(4, d.Count("sapling_barley"));
        // 씨앗 없음·안 자라는 바이옴
        Assert.Empty(Farm.Fill(All, Meadows, 20, Stock()).Plant);
        Assert.Empty(Farm.Fill(All, Farm.Mountain, 20, Stock(("CarrotSeeds", 6))).Plant);
    }
}
