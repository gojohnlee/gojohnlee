using System.Collections.Generic;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;
using Xunit;

public class DepartureTests
{
    static PlaceOption P(float dist, float ratio = 0f, float withMead = float.NaN, float death = 0f, bool boss = false, int x = 0) =>
        new PlaceOption
        {
            Zone = new ZoneXY(x, 0), Distance = dist, Ratio = ratio, RatioWithMead = float.IsNaN(withMead) ? ratio : withMead,
            DeathWeight = death, Boss = boss, TopFoe = "트롤",
        };

    static DepartureInput In(params PlaceOption[] places) => new DepartureInput
    {
        Personality = Personality.Ragnar, Item = "나무", Amount = 20, Fed = true, Places = new List<PlaceOption>(places),
    };

    [Fact]
    public void RelaxedJustGoes()
    {
        var r = Departure.Judge(In(P(100)));
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Null(r.Key);
        Assert.Equal(Question.None, r.Question);
    }

    [Fact]
    public void BlockersRefuse()
    {
        var i = In(P(100)); i.Tool = ToolState.None;
        Assert.Equal("noTool", Departure.Judge(i).Key);
        i.Tool = ToolState.Weak;
        Assert.Equal("toolTooWeak", Departure.Judge(i).Key);
        i.Tool = ToolState.Ok; i.OutOfScope = true;
        Assert.Equal(DepartAction.Refuse, Departure.Judge(i).Action);
    }

    [Fact]
    public void BuriedOnlyNeedsFinder()
    {
        // ⑪ 3: 묻힌 은만 있고 위시본이 없으면 거절, 드러난 곳을 알면 간다, 연장 단계가 모자라면 그 말이 먼저
        var i = In(); i.NeedFinder = true;
        Assert.Equal("depart.needFinder", Departure.Judge(i).Key);
        i.Tool = ToolState.Weak;
        Assert.Equal("toolTooWeak", Departure.Judge(i).Key);
        var known = In(P(100)); known.NeedFinder = true;
        Assert.Equal(DepartAction.Go, Departure.Judge(known).Action);
    }

    [Fact]
    public void NoCryptRefusesWithReason()
    {
        var i = In(); i.Tool = ToolState.Ok; i.NoCrypt = true;
        Assert.Equal("depart.noCrypt", Departure.Judge(i).Key);
        i.CryptsMinedOut = true;
        var r = Departure.Judge(i);
        Assert.Equal(DepartAction.Refuse, r.Action);
        Assert.Equal("depart.cryptsEmpty", r.Key);
    }

    [Fact]
    public void BusyQueuesUnlessUrgent()
    {
        var i = In(P(100)); i.CurrentJob = "돌 30";
        var r = Departure.Judge(i);
        Assert.Equal(DepartAction.Queue, r.Action);
        Assert.Equal("depart.busy", r.Key);
        Assert.Equal("돌 30", r.Args["job"]);
        i.Urgent = true;
        r = Departure.Judge(i);
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.True(r.Interrupts);
    }

    [Fact]
    public void CautionWarnsAndGoes()
    {
        var r = Departure.Judge(In(P(100, 0.3f)));
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.warn", r.Key);
        Assert.Equal("트롤", r.Args["foe"]);
    }

    // 위험은 묻거나 거절하지 않고 경고만 하고 간다(사용자 결정 2026-10-25)
    [Fact]
    public void TightWithoutMeadWarnsAndGoes()
    {
        var r = Departure.Judge(In(P(100, 0.9f, withMead: 0.5f)));
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Null(r.Condition);
        Assert.Equal("depart.danger", r.Key);
    }

    [Fact]
    public void WeakGearForBiomeWarnsAndGoes()
    {
        // ⑫ 다: 평원 적 하나도 못 당하는 장비 → "이 장비론 평원 무리인데… 그래도 가지"
        var o = P(100); o.GearRatio = 1.4f; o.GearFoe = "데스킷"; o.BiomeName = "평원";
        var r = Departure.Judge(In(o));
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.gear", r.Key);
        Assert.Equal("평원", r.Args["biome"]);
        Assert.Equal("데스킷", r.Args["foe"]);
        o.GearRatio = 0.9f;
        Assert.Null(Departure.Judge(In(o)).Key);
        o.GearRatio = 1.4f; o.Boss = true;
        r = Departure.Judge(In(o));
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.danger", r.Key);
    }

    [Fact]
    public void TightSwampWithoutPoisonMeadGrumblesAndGoes()
    {
        var o = P(100, 0.9f, withMead: 0.5f); o.Resist = "Poison";
        var i = In(o); i.HealthMeads = 2;
        var r = Departure.Judge(i);
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Null(r.Condition);
        Assert.Equal("depart.noResist", r.Key);
        Assert.Equal("독 저항 미드", r.Args["mead"]);
    }

    [Fact]
    public void TightSwampWithPoisonMeadUsesHealthMeadRule()
    {
        var o = P(100, 0.9f, withMead: 0.5f); o.Resist = "Poison";
        var i = In(o); i.HealthMeads = 2; i.ResistMeads.Add("Poison");
        var r = Departure.Judge(i);
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.warn", r.Key);
        Assert.Null(Resist.KindOf("healthMead"));
    }

    [Fact]
    public void CautionSwampWithoutResistGrumblesButGoes()
    {
        var o = P(100, 0.3f); o.Resist = "Poison";
        var r = Departure.Judge(In(o));
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.noResist", r.Key);
    }

    [Fact]
    public void TightWithOwnMeadsJustWarns()
    {
        var i = In(P(100, 0.9f, withMead: 0.5f)); i.HealthMeads = 2;
        var r = Departure.Judge(i);
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.warn", r.Key);
    }

    [Fact]
    public void AlarakIsBolder()
    {
        var i = In(P(100, 0.9f, withMead: 0.9f)); i.Personality = Personality.Floki;
        Assert.Equal("depart.warn", Departure.Judge(i).Key);
        i.Personality = Personality.Ragnar;
        Assert.Equal("depart.danger", Departure.Judge(i).Key);
    }

    // 위험한 곳이면 묻지 않고 조용히 덜 위험한 곳으로(거리 무관), 그런 곳이 없으면 동료와 같이(사용자 결정 2026-10-26)
    [Fact]
    public void HardQuietlyGoesToSaferPlaceAtAnyDistance()
    {
        var r = Departure.Judge(In(P(100, 1.3f, x: 1), P(3000, 0.1f, x: 2), P(200, 2.5f, x: 3)));
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.far", r.Key); // 바꾼 걸 말하지 않는다(덧붙이는 말은 새 곳 기준 — 멀다)
        Assert.Equal(2, r.Place.Zone.X);
        Assert.Same(r.Place, r.Alternative);
        Assert.Equal(new[] { 1, 3 }, r.Avoid.ConvertAll(o => o.Zone.X).ToArray());
    }

    [Fact]
    public void SafeNearestStillAvoidsRiskyOthers()
    {
        var r = Departure.Judge(In(P(100, 0.05f, x: 1), P(150, 1.3f, x: 2)));
        Assert.Null(r.Alternative);
        Assert.Equal(1, r.Place.Zone.X);
        Assert.Equal(2, Assert.Single(r.Avoid).Zone.X);
    }

    [Fact]
    public void RiskyPlaceInsideBaseIsNotSwappedOrAvoided()
    {
        var home = P(60, 0.05f, death: 0.6f, x: 1); home.InBase = true; // 습격 때 죽은 기록
        var i = In(home, P(500, 0.05f, x: 2), P(300, 1.3f, x: 3)); i.Guard = "호위병";
        var r = Departure.Judge(i);
        Assert.Equal(1, r.Place.Zone.X);
        Assert.Equal(DepartAction.GoTogether, r.Action); // 바꿔 가진 않고, 위험하니 호위병과
        var safe = P(60, 0.05f, x: 1); safe.InBase = true;
        var inBaseRisky = P(80, 1.3f, x: 4); inBaseRisky.InBase = true;
        r = Departure.Judge(In(safe, inBaseRisky, P(300, 1.3f, x: 3)));
        Assert.Equal(3, Assert.Single(r.Avoid).Zone.X); // 거점 안 위험한 곳은 피할 곳에 안 넣음
    }

    // 덜 위험한 곳이 없으면 호위병을 데려간다, 바쁘면 거점에서 기다렸다 같이(사용자 결정 2026-10-26)
    [Fact]
    public void AllRiskyTakesGuardOrWaitsForIt()
    {
        var i = In(P(100, 1.3f, x: 1), P(400, 0.9f, death: 0.6f, x: 2)); i.Guard = "호위병";
        var r = Departure.Judge(i);
        Assert.Equal(DepartAction.GoTogether, r.Action);
        Assert.Equal("depart.together", r.Key);
        Assert.Equal("호위병", r.Args["partner"]);
        Assert.Equal(1, r.Place.Zone.X);
        Assert.Empty(r.Avoid);
        i.GuardBusy = true;
        r = Departure.Judge(i);
        Assert.Equal(DepartAction.WaitGuard, r.Action);
        Assert.Equal("depart.waitGuard", r.Key);
        i = In(P(100, 2.5f, boss: true)); i.Guard = "호위병"; // 보스·불가도 호위병과
        Assert.Equal(DepartAction.GoTogether, Departure.Judge(i).Action);
        r = Departure.Judge(In(P(100, 1.3f))); // 호위병이 없으면 경고만 하고 혼자
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.danger", r.Key);
        i = In(P(100, 0.05f), P(200, 1.3f, x: 2)); i.Guard = "호위병"; i.GuardBusy = true; // 안전한 곳이면 호위병과 상관없이 감
        Assert.Equal(DepartAction.Go, Departure.Judge(i).Action);
    }

    [Fact]
    public void GuardUsesBoldLine()
    {
        Assert.Equal(Danger.FightFloki, Departure.FightLine(Personality.Guard));
        Assert.Equal(Departure.FightLine(Personality.Floki), Departure.FightLine(Personality.Guard));
        Assert.True(Speech.Has(Personality.Guard, "fightStart")); // 대사는 임시로 라그나 것
    }

    [Fact]
    public void WeakGearPlaceSwapsToSaferBiome()
    {
        var plains = P(100, x: 1); plains.GearRatio = 1.4f;
        var meadow = P(900, 0.1f, x: 2);
        var r = Departure.Judge(In(plains, meadow));
        Assert.Equal(2, r.Place.Zone.X);
        Assert.Equal("depart.far", r.Key);
    }

    [Fact]
    public void DiedThereWarnsEvenIfSafeNow()
    {
        var r = Departure.Judge(In(P(100, 0.05f, death: 0.6f)));
        Assert.Equal(DepartAction.Go, r.Action);
        Assert.Equal("depart.danger", r.Key);
        var i = In(P(100, 0.05f, death: 0.4f)); i.Personality = Personality.Floki;
        Assert.Equal(DepartAction.Go, Departure.Judge(i).Action);
    }

    [Fact]
    public void NightAndStockQuestions()
    {
        var i = In(P(500)); i.SecondsToNight = 60;
        Assert.Equal(Question.Night, Departure.Judge(i).Question);
        i = In(P(50)); i.SecondsToNight = 0; // 거점 근처는 밤이어도 묻지 않음
        Assert.Equal(DepartAction.Go, Departure.Judge(i).Action);
        i = In(P(100)); i.Stock = 300;
        var r = Departure.Judge(i);
        Assert.Equal(Question.Stock, r.Question);
        Assert.Equal("300", r.Args["stock"]);
        i = In(); i.Stock = 300; // 모르는 물건이어도 재고는 묻는다
        Assert.Equal(Question.Stock, Departure.Judge(i).Question);
    }

    [Fact]
    public void NightAndStockStillAskWhenDangerous()
    {
        var i = In(P(300, 1.3f)); i.Stock = 500; i.SecondsToNight = 0;
        var r = Departure.Judge(i);
        Assert.Equal(Question.Night, r.Question);
        Assert.Equal("note.stock", r.ExtraKey);
        i.SecondsToNight = float.PositiveInfinity;
        Assert.Equal(Question.Stock, Departure.Judge(i).Question);
        i.Stock = 0;
        Assert.Equal("depart.danger", Departure.Judge(i).Key);
    }

    [Fact]
    public void OneAddOnByPriority()
    {
        var i = In(P(800)); i.Fed = false; i.FoodInChest = false; i.Amount = 200; i.CarryPerTrip = 50; i.Skill = 10;
        Assert.Equal("depart.hungry", Departure.Judge(i).Key);
        i.Fed = true;
        Assert.Equal("depart.far", Departure.Judge(i).Key);
        i.KnowsPortal = true;
        var r = Departure.Judge(i);
        Assert.Equal("depart.trips", r.Key);
        Assert.Equal("4", r.Args["trips"]);
        i.Amount = 20;
        Assert.Equal("depart.lowSkill", Departure.Judge(i).Key);
    }

    [Fact]
    public void EstimateAndMinutes()
    {
        var i = In(P(300)); i.Amount = 50; i.SecondsPerUnit = 4; i.CarryPerTrip = 25;
        // 이동 300×2÷5 = 120초 × 2번 + 200초 = 440초
        Assert.Equal(440f, Departure.Estimate(i, i.Places[0]), 1);
        Assert.Equal(10, Departure.Minutes5(440));
        Assert.Equal(5, Departure.Minutes5(10));
        i.Skill = 70;
        Assert.Equal(380f, Departure.Estimate(i, i.Places[0]), 1);
    }

    [Theory]
    [InlineData(Question.Alternative, true, Reply.Yes, ReplyAction.GoAlternative)]
    [InlineData(Question.Alternative, true, Reply.GoAnyway, ReplyAction.GoGrumble)]
    [InlineData(Question.Alternative, true, Reply.Escort, ReplyAction.GoEscort)]
    [InlineData(Question.Alternative, true, Reply.No, ReplyAction.Hold)]
    [InlineData(Question.Escort, false, Reply.Yes, ReplyAction.GoEscort)]
    [InlineData(Question.Escort, false, Reply.Elsewhere, ReplyAction.NotApplicable)]
    [InlineData(Question.Escort, true, Reply.Elsewhere, ReplyAction.GoAlternative)]
    [InlineData(Question.DiedThere, false, Reply.GoAnyway, ReplyAction.GoGrumble)]
    [InlineData(Question.Condition, false, Reply.Yes, ReplyAction.Recheck)]
    [InlineData(Question.Night, false, Reply.Yes, ReplyAction.WaitMorning)]
    [InlineData(Question.Night, false, Reply.GoAnyway, ReplyAction.GoAlone)]
    [InlineData(Question.Night, false, Reply.No, ReplyAction.Cancel)]
    [InlineData(Question.Stock, false, Reply.Yes, ReplyAction.GoAlone)]
    [InlineData(Question.Stock, false, Reply.No, ReplyAction.Cancel)]
    [InlineData(Question.Stock, false, Reply.Timeout, ReplyAction.Hold)]
    public void ReplyTable(Question q, bool alt, Reply a, ReplyAction expected) =>
        Assert.Equal(expected, Replies.Resolve(q, alt, a));

    [Fact]
    public void AnswererPicksNamedOrLatest()
    {
        var npcs = new List<NpcInfo>
        {
            new NpcInfo { Name = "Claude", AskedAt = 100 },
            new NpcInfo { Name = "Alarak", AskedAt = 150 },
        };
        Assert.Equal(1, Dispatch.Answerer(npcs, null, 200));
        Assert.Equal(0, Dispatch.Answerer(npcs, "claude", 200));
        Assert.Equal(-1, Dispatch.Answerer(npcs, null, 400)); // 3분 넘음
        npcs[1].AskedAt = -1;
        Assert.Equal(0, Dispatch.Answerer(npcs, null, 200));
        Assert.Equal(-1, Dispatch.Answerer(npcs, "Alarak", 200));
    }

    static readonly ChatParser Parser = new ChatParser(new Dictionary<string, string> { ["Claude"] = "Claude" });

    [Theory]
    [InlineData("그래도 가", AnswerKind.GoAnyway)]
    [InlineData("같이 가자", AnswerKind.Escort)]
    [InlineData("딴 데로 가", AnswerKind.Elsewhere)]
    [InlineData("다른데!", AnswerKind.Elsewhere)]
    [InlineData("응", AnswerKind.Yes)]
    public void ParsesReplies(string line, AnswerKind kind)
    {
        var r = Parser.Parse(line, awaitingAnswer: true);
        Assert.Equal(ChatRoute.Answer, r.Route);
        Assert.Equal(kind, r.Answer);
    }

    [Theory]
    [InlineData("!나무 50 빨리", true)]
    [InlineData("!먼저 나무 50", true)]
    [InlineData("!나무 50", false)]
    public void UrgentWords(string line, bool urgent)
    {
        var r = Parser.Parse(line);
        Assert.Equal(ChatRoute.Command, r.Route);
        Assert.Equal(CommandKind.Item, r.Command.Kind);
        Assert.Equal(50, r.Command.Amount);
        Assert.Equal(urgent, r.Command.Urgent);
    }
}

public class DepartureSpeechTests
{
    static readonly string[] Keys =
    {
        "memory", "depart.warn", "depart.hungry", "depart.far", "depart.trips", "depart.lowSkill", "depart.busy", "depart.together", "depart.outOfScope", "depart.noCrypt", "depart.cryptsEmpty", "cryptOpen",
        "ask.alternative", "ask.escort", "ask.diedThere", "ask.condition", "ask.gear", "answer.gearMet", "answer.gearMissing", "depart.noResist", "ask.night", "ask.stock", "note.stock", "note.night",
        "craftChestFull", "answer.grumble", "answer.escort", "answer.alternative", "answer.conditionMet", "answer.conditionMissing", "answer.morning",
        "answer.go", "answer.hold", "answer.cancel", "answer.timeout", "skillUp", "skills", "skillsNone", "craft.plan", "craft.inStock", "craft.missing", "craft.tooLong", "craft.unknown", "setList", "setEmpty", "setNone", "keepSet", "keepOff", "prepOnce", "prepKeep", "prepOff", "prepNoChest", "prepShort", "state.prep", "refillGo", "refillMake", "refillFarm",
        "farmSet", "farmMoved", "farmFull", "farmNoGrow", "farmWhere", "farmNoTool", "farmNoSeed", "farmGo", "farmDone", "farmNoRoom", "state.farm", "farmHarvest", "farmPlantFor", "farmHarvestFor", "farmNoField", "farmNoSeedFor", "craft.farm",
    };

    [Fact]
    public void BothPersonalitiesHaveEveryKey()
    {
        foreach (var k in Keys)
        {
            Assert.True(Speech.Has(Personality.Ragnar, k), "SCV " + k);
            Assert.True(Speech.Has(Personality.Floki, k), "Alarak " + k);
        }
    }

    [Fact]
    public void JudgeKeysAllExist()
    {
        // Judge가 돌려줄 수 있는 키(ask.*는 질문 이름에서 만든다)
        foreach (Question q in System.Enum.GetValues(typeof(Question)))
        {
            if (q == Question.None) continue;
            string k = "ask." + char.ToLowerInvariant(q.ToString()[0]) + q.ToString().Substring(1);
            Assert.True(Speech.Has(Personality.Ragnar, k), k);
        }
    }
}

public class JosaRangTests
{
    [Fact]
    public void Rang()
    {
        Assert.Equal("이랑", Josa.For("구리 광석", "이랑"));
        Assert.Equal("랑", Josa.For("나무", "이랑"));
    }
}
