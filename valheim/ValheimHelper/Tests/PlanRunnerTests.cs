using System.Linq;
using ValheimHelper.Core.Work;
using Xunit;

public class PlanRunnerTests
{
    static Plan Copper()
    {
        var b = new RecipeBook();
        b.SetSource("Wood", SourceKind.Gather);
        b.SetSource("CopperOre", SourceKind.Gather);
        b.Add(new Process { Station = "kiln", Input = "Wood", Output = "Coal", SecondsPerProduct = 15, Capacity = 25 });
        b.Add(new Process { Station = "smelter", Input = "CopperOre", Output = "Copper", Fuel = "Coal", FuelPerProduct = 2, SecondsPerProduct = 30, Capacity = 10 });
        var f = new Facilities();
        f.Processors["kiln"] = 1;
        f.Processors["smelter"] = 1;
        return new CraftPlanner(b, f).Expand("Copper", 5, null);
    }

    // 끝날 때까지 일 단계를 순서대로 하게 하며 시계를 1초씩 보낸다. 실제 한 일 순서를 돌려준다.
    static PlanStep[] RunAll(PlanRunner r, ref float now)
    {
        var done = new System.Collections.Generic.List<PlanStep>();
        for (int guard = 0; guard < 10000 && !r.Over; guard++)
        {
            var s = r.Next(now);
            if (s == null) { now += 1; continue; }
            r.Begin(s.Id, now);
            r.Complete(s.Id);
            done.Add(s);
            now += 2;
        }
        return done.ToArray();
    }

    [Fact]
    public void RunsEverythingInDependencyOrder()
    {
        var plan = Copper();
        var r = new PlanRunner(plan);
        float now = 0;
        var order = RunAll(r, ref now);
        Assert.True(r.Finished);
        foreach (var s in order)
            foreach (int d in s.After) Assert.True(System.Array.FindIndex(order, o => o.Id == d) < System.Array.IndexOf(order, s) || plan.Steps[d].Kind == StepKind.Wait);
    }

    [Fact]
    public void WaitFinishesByClockNotByWorker()
    {
        var plan = Copper();
        var r = new PlanRunner(plan);
        var wait = plan.Steps.First(s => s.Kind == StepKind.Wait);
        float now = 0;
        // 숯가마 Wait의 선행(채집·싣기)을 다 끝낸다
        while (true)
        {
            var s = r.Next(now);
            if (s == null || s.Kind == StepKind.Unload) break;
            r.Begin(s.Id, now); r.Complete(s.Id);
            if (r.StateOf(wait.Id) == StepState.Running) break;
        }
        Assert.Equal(StepState.Running, r.StateOf(wait.Id));
        Assert.True(r.NextWaitEnds(now) > 0);
        r.Tick(now + wait.Seconds - 0.5f);
        Assert.Equal(StepState.Running, r.StateOf(wait.Id));
        r.Tick(now + wait.Seconds + 0.5f);
        Assert.Equal(StepState.Done, r.StateOf(wait.Id));
    }

    [Fact]
    public void FailBlocksDependentsOnly()
    {
        var plan = Copper();
        var r = new PlanRunner(plan);
        var first = r.Next(0);
        r.Begin(first.Id, 0);
        r.Fail(first.Id);
        Assert.Equal(StepState.Failed, r.StateOf(first.Id));
        Assert.True(r.Count(StepState.Blocked) > 0);
        // 막히지 않은 단계가 있으면 계속 할 수 있고, 결국 Over지만 Finished는 아님
        float now = 0;
        RunAll(r, ref now);
        Assert.True(r.Over);
        Assert.False(r.Finished);
        Assert.Equal(0, r.Count(StepState.Pending));
    }

    [Fact]
    public void EmptyPlanIsFinished()
    {
        var plan = new CraftPlanner(new RecipeBook(), new Facilities()).Expand(new System.Collections.Generic.List<Ingredient>(), null);
        var r = new PlanRunner(plan);
        Assert.True(r.Finished);
        Assert.Null(r.Next(0));
    }
}
