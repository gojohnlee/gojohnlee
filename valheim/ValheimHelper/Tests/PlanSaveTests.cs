using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;

// 제작 재시작 복원(외부 검토 + 사용자 승인 10-26): 계획을 다시 세우지 않고 저장한 단계·상태·진행으로 이어 간다
public class PlanSaveTests
{
    static Plan Copper(int n = 5)
    {
        var b = new RecipeBook();
        b.SetSource("Wood", SourceKind.Gather);
        b.SetSource("CopperOre", SourceKind.Gather);
        b.Add(new Process { Station = "kiln", Input = "Wood", Output = "Coal", SecondsPerProduct = 15, Capacity = 25 });
        b.Add(new Process { Station = "smelter", Input = "CopperOre", Output = "Copper", Fuel = "Coal", FuelPerProduct = 2, SecondsPerProduct = 30, Capacity = 10 });
        var f = new Facilities();
        f.Processors["kiln"] = 1;
        f.Processors["smelter"] = 1;
        return new CraftPlanner(b, f).Expand("Copper", n, null);
    }

    static PlanStep First(Plan p, StepKind k, string item = null) => p.Steps.First(s => s.Kind == k && (item == null || s.Item == item));

    [Fact]
    public void RoundTripKeepsStepsAndStates()
    {
        var plan = Copper();
        var r = new PlanRunner(plan);
        var gatherOre = First(plan, StepKind.Gather, "CopperOre");
        r.Begin(gatherOre.Id, 0f); r.Complete(gatherOre.Id);
        string text = PlanSave.Save(r, id => 0);
        var back = PlanSave.Load(text);
        Assert.NotNull(back);
        Assert.Equal(plan.Steps.Count, back.Plan.Steps.Count);
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            PlanStep a = plan.Steps[i], b = back.Plan.Steps[i];
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Item, b.Item);
            Assert.Equal(a.Amount, b.Amount);
            Assert.Equal(a.Station, b.Station);
            Assert.Equal(a.Fuel, b.Fuel);
            Assert.Equal(a.FuelAmount, b.FuelAmount);
            Assert.Equal(a.Seconds, b.Seconds);
            Assert.Equal(a.After, b.After);
            Assert.Equal(r.StateOf(i), back.StateOf(i));
        }
    }

    [Fact]
    public void LoadedSmelterIsNotLoadedAgainAndWaitRestarts()
    {
        // 넣은 직후 재시작: 넣기는 끝(다시 넣지 않음 → 광석을 또 모으지 않음), 기다림은 지금부터 다시, 꺼내기는 남음
        var plan = Copper();
        var r = new PlanRunner(plan);
        float now = 0f;
        PlanStep s = null;
        for (int guard = 0; guard < 10000 && !r.Over; guard++)
        {
            s = r.Next(now);
            if (s == null) { now += 1f; continue; } // 숯가마 기다림
            if (s.Kind == StepKind.Load && s.Station == "smelter") break;
            r.Begin(s.Id, now); r.Complete(s.Id); now += 1f;
        }
        Assert.True(s != null && s.Kind == StepKind.Load && s.Station == "smelter");
        r.Begin(s.Id, now); r.Complete(s.Id);
        PlanStep wait = plan.Steps.First(x => x.Kind == StepKind.Wait && x.After.Contains(s.Id));
        Assert.Null(r.Next(now + 1f)); // 기다림이 돌기 시작
        Assert.Equal(StepState.Running, r.StateOf(wait.Id));
        var back = PlanSave.Load(PlanSave.Save(r, id => 0));
        Assert.Equal(StepState.Done, back.StateOf(s.Id));
        Assert.Equal(StepState.Running, back.StateOf(wait.Id));
        Assert.All(back.Plan.Steps.Where(x => x.Kind == StepKind.Gather), g => Assert.Equal(StepState.Done, back.StateOf(g.Id)));
        Assert.Null(back.Next(1f)); // 기다리는 동안 다른 할 일 없음(광석 모으기 다시 안 함)
        Assert.True(back.NextWaitEnds(0f) > 0f);
        Assert.Equal(1f, back.RunningFor(wait.Id, 1f));
        PlanStep unload = plan.Steps.First(x => x.Kind == StepKind.Unload && x.After.Contains(wait.Id));
        Assert.Equal(StepState.Pending, back.StateOf(unload.Id));
    }

    [Fact]
    public void RunningGatherResumesWithWhatIsLeft()
    {
        var plan = Copper();
        var r = new PlanRunner(plan);
        var ore = First(plan, StepKind.Gather, "CopperOre");
        r.Begin(ore.Id, 0f);
        var back = PlanSave.Load(PlanSave.Save(r, id => id == ore.Id ? 3 : 0));
        Assert.Equal(StepState.Pending, back.StateOf(ore.Id));
        Assert.Equal(ore.Amount - 3, back.Plan.Steps[ore.Id].Amount);
        back = PlanSave.Load(PlanSave.Save(r, id => id == ore.Id ? ore.Amount : 0)); // 다 넣었으면 끝
        Assert.Equal(StepState.Done, back.StateOf(ore.Id));
    }

    [Fact]
    public void RunningUnloadAndCraftAreRedone_AttendedLoadDependsOnStation()
    {
        var plan = Copper();
        var r = new PlanRunner(plan);
        var unload = First(plan, StepKind.Unload);
        r.Begin(unload.Id, 0f);
        var back = PlanSave.Load(PlanSave.Save(r, id => 0));
        Assert.Equal(StepState.Pending, back.StateOf(unload.Id));
        // 곁에서 지키는 설비(조리대) 넣기가 진행 중: 이미 올라가 있으면 끝(꺼내기가 마저), 아니면 다시
        var load = First(plan, StepKind.Load);
        load.AttendSeconds = 20f;
        r.Begin(load.Id, 0f);
        string text = PlanSave.Save(r, id => 0);
        Assert.Equal(StepState.Done, PlanSave.Load(text, st => true).StateOf(load.Id));
        Assert.Equal(StepState.Pending, PlanSave.Load(text, st => false).StateOf(load.Id));
        Assert.Equal(StepState.Pending, PlanSave.Load(text).StateOf(load.Id));
    }

    [Fact]
    public void FailedAndBlockedStay()
    {
        var plan = Copper();
        var r = new PlanRunner(plan);
        var wood = First(plan, StepKind.Gather, "Wood");
        r.Fail(wood.Id);
        var back = PlanSave.Load(PlanSave.Save(r, id => 0));
        for (int i = 0; i < plan.Steps.Count; i++) Assert.Equal(r.StateOf(i), back.StateOf(i));
        Assert.True(back.Count(StepState.Blocked) > 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v2~0/0/Wood/5////0/0/0/0//0/0")]
    [InlineData("v1~0/0/Wood/5")]
    [InlineData("v1~1/0/Wood/5////0/0/0/0//0/0")]
    [InlineData("v1~0/0/Wood/5////0/0/0/0/3/0/0")]
    [InlineData("v1~0/9/Wood/5////0/0/0/0//0/0")]
    public void BrokenTextGivesNull(string text) => Assert.Null(PlanSave.Load(text));

    [Fact]
    public void WorkItemKeepsPlanThroughQueueSave()
    {
        var r = new PlanRunner(Copper());
        string plan = PlanSave.Save(r, id => 0);
        var w = new WorkItem { Kind = WorkKind.Make, Resume = true, Label = "구리 주괴 5", Item = "Copper", Amount = 5, Plan = plan };
        var back = WorkState.LoadQueue(WorkState.SaveQueue(new[] { w })).Single();
        Assert.Equal(plan, back.Plan);
        Assert.NotNull(PlanSave.Load(back.Plan));
    }
}
