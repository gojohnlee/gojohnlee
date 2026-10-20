using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;
using Xunit;

public class JosaTests
{
    [Theory]
    [InlineData("나무", "는", "는")]
    [InlineData("철", "는", "은")]
    [InlineData("구리", "이", "가")]
    [InlineData("돌", "가", "이")]
    [InlineData("돌", "으로", "로")]
    [InlineData("석탄", "로", "으로")]
    [InlineData("나무", "으로", "로")]
    [InlineData("주석", "요", "이요")]
    [InlineData("나무", "이요", "요")]
    [InlineData("10", "이라", "이라")] // 십
    [InlineData("5", "이라", "라")]
    [InlineData("8", "으로", "로")] // 팔
    [InlineData("(돌)", "가", "이")] // 괄호 건너뜀
    public void PicksByBatchim(string word, string josa, string expected) => Assert.Equal(expected, Josa.For(word, josa));

    [Fact]
    public void UnknownWordShowsBoth() => Assert.Equal("은(는)", Josa.For("", "는"));

    [Fact]
    public void SpeechAppliesJosa()
    {
        string line = new Speech().Say(Personality.Alarak, "notFound", new Dictionary<string, string> { ["item"] = "철" });
        Assert.StartsWith("철은 ", line);
        line = new Speech().Say(Personality.Alarak, "notFound", new Dictionary<string, string> { ["item"] = "나무" });
        Assert.StartsWith("나무는 ", line);
    }
}

public class ExploreTests
{
    // 거점 (0,0) 둘레 반경 1 구역만 밝혀진 지도
    static bool Explored1(ZoneXY z) => System.Math.Abs(z.X) <= 1 && System.Math.Abs(z.Y) <= 1;
    static bool AllLand(ZoneXY z) => true;

    [Fact]
    public void PicksFrontierNextToExplored()
    {
        Assert.True(ExplorePlan.Next(new ZoneXY(0, 0), new ZoneXY(0, 0), 10, new Knowledge(), Explored1, AllLand, 0, 0, null, out var z));
        Assert.Equal(2, z.Ring(new ZoneXY(0, 0)));
    }

    [Fact]
    public void PrefersSideNearNpc()
    {
        ExplorePlan.Next(new ZoneXY(0, 0), new ZoneXY(1, 0), 10, new Knowledge(), Explored1, AllLand, 0, 0, null, out var z);
        Assert.Equal(new ZoneXY(2, 0), z);
    }

    [Fact]
    public void FollowsDirection()
    {
        ExplorePlan.Next(new ZoneXY(0, 0), new ZoneXY(0, 0), 10, new Knowledge(), Explored1, AllLand, 0, 1, null, out var z);
        Assert.Equal(new ZoneXY(0, 2), z);
    }

    [Fact]
    public void SkipsWaterAndLooked()
    {
        var k = new Knowledge();
        k.MarkLooked(new ZoneXY(2, 0));
        bool land(ZoneXY z) => z.X <= 2; // 동쪽 3부터 바다
        ExplorePlan.Next(new ZoneXY(0, 0), new ZoneXY(1, 0), 10, k, Explored1, land, 1, 0, null, out var z);
        Assert.NotEqual(new ZoneXY(2, 0), z);
        Assert.True(z.X <= 2);
    }

    [Fact]
    public void NothingLeft()
    {
        bool land(ZoneXY z) => Explored1(z); // 섬
        Assert.False(ExplorePlan.Next(new ZoneXY(0, 0), new ZoneXY(0, 0), 10, new Knowledge(), Explored1, land, 0, 0, null, out _));
    }

    [Fact]
    public void PreferredBiomeSideFirst()
    {
        // 서쪽 끝(-1,*)이 늪이면 서쪽 경계로
        ExplorePlan.Next(new ZoneXY(0, 0), new ZoneXY(1, 0), 10, new Knowledge(), Explored1, AllLand, 0, 0, n => n.X == -1 && n.Y == 0, out var z);
        Assert.Equal(-2, z.X);
    }

    [Fact]
    public void CompassWords()
    {
        Assert.Equal("북쪽", Bearing.Of(0, 5));
        Assert.Equal("남동쪽", Bearing.Of(3, -3));
        Assert.Equal("서쪽", Bearing.Of(-4, 0.5));
        Assert.True(Bearing.Vector("northeast", out int dx, out int dy));
        Assert.Equal(1, dx);
        Assert.Equal(1, dy);
    }

    [Fact]
    public void BiomeNameMask() => Assert.Equal("늪·검은 숲", BiomeNames.Of(BiomeNames.FromGame("Swamp") | BiomeNames.FromGame("BlackForest")));

    [Fact]
    public void KnowledgeSummaryAndBiomeSaved()
    {
        var k = new Knowledge();
        k.Saw(new Dictionary<ZoneXY, Dictionary<string, int>>
        {
            [new ZoneXY(5, 0)] = new Dictionary<string, int> { ["Wood"] = 3, ["Stone"] = 1 },
            [new ZoneXY(1, 0)] = new Dictionary<string, int> { ["Wood"] = 2 },
        }, new List<ZoneXY>(), 10);
        k.SetBiome(new ZoneXY(5, 0), 8);
        k.SetBiome(new ZoneXY(1, 0), 1);
        var s = k.Summary(new ZoneXY(0, 0));
        Assert.Equal("Wood", s[0].Item);
        Assert.Equal(2, s[0].Zones);
        Assert.Equal(new ZoneXY(1, 0), s[0].Nearest);
        Assert.Equal(9, s[0].Biomes);
        var k2 = Knowledge.Load(k.Save());
        Assert.Equal(8, k2.BiomeOf(new ZoneXY(5, 0)));
        Assert.Equal(8, k2.BiomesOf("Stone"));
        // 0.3.5 저장 형식(바이옴 없음)도 읽는다
        Assert.Equal(3, Knowledge.Load("5,0,1,10;Wood=3\n").Get(new ZoneXY(5, 0)).Items["Wood"]);
    }
}
