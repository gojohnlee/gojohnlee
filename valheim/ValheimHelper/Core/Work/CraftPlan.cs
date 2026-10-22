using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    public sealed class Ingredient
    {
        public string Item;
        public int Amount;
        public Ingredient() { }
        public Ingredient(string item, int amount) { Item = item; Amount = amount; }
    }

    /// <summary>제작법(바닐라 Recipe, 품질 1 기준).</summary>
    public sealed class Recipe
    {
        public string Output;
        /// <summary>한 번 만들 때 나오는 수(m_amount, 화살 20 등).</summary>
        public int OutputAmount = 1;
        public readonly List<Ingredient> Inputs = new List<Ingredient>();
        /// <summary>제작대 이름. null이면 손으로.</summary>
        public string Station;
        public int StationLevel = 1;
        /// <summary>바닐라 m_requireOnlyOneIngredient: 재료 중 하나만 있으면 된다(첫 재료로 셈).</summary>
        public bool OnlyOne;
    }

    /// <summary>가공 설비 변환 하나(제련소·숯가마·발효통 등): 넣는 것 → 나오는 것, 연료, 시간, 한 번에 넣을 수 있는 양.</summary>
    public sealed class Process
    {
        public string Station;
        public string Input;
        public int InputAmount = 1;
        public string Output;
        public int OutputAmount = 1;
        /// <summary>연료(없으면 null)와 나오는 것 한 번당 연료 수.</summary>
        public string Fuel;
        public int FuelPerProduct;
        public float SecondsPerProduct;
        /// <summary>한 번에 넣을 수 있는 재료 수. 0이면 제한 없음.</summary>
        public int Capacity;
    }

    public enum SourceKind { None, Gather, Hunt, Farm, OutOfScope }

    /// <summary>게임 데이터에서 만든 제작법·가공표·채집 출처 (설계 C-1). 플러그인이 월드 불러올 때 한 번 채운다.</summary>
    public sealed class RecipeBook
    {
        readonly Dictionary<string, List<Recipe>> _recipes = new Dictionary<string, List<Recipe>>();
        readonly Dictionary<string, List<Process>> _processes = new Dictionary<string, List<Process>>();
        readonly Dictionary<string, SourceKind> _sources = new Dictionary<string, SourceKind>();

        public void Add(Recipe r)
        {
            if (!_recipes.TryGetValue(r.Output, out var list)) _recipes[r.Output] = list = new List<Recipe>();
            list.Add(r);
        }

        public void Add(Process p)
        {
            if (!_processes.TryGetValue(p.Output, out var list)) _processes[p.Output] = list = new List<Process>();
            list.Add(p);
        }

        public void SetSource(string item, SourceKind kind) => _sources[item] = kind;

        public SourceKind SourceOf(string item) => _sources.TryGetValue(item, out var k) ? k : SourceKind.None;
        public IList<Recipe> RecipesFor(string item) => _recipes.TryGetValue(item, out var l) ? l : (IList<Recipe>)new Recipe[0];
        public IList<Process> ProcessesFor(string item) => _processes.TryGetValue(item, out var l) ? l : (IList<Process>)new Process[0];
    }

    /// <summary>거점 설비: 제작대 이름 → 레벨, 가공 설비 이름 → 대수.</summary>
    public sealed class Facilities
    {
        public readonly Dictionary<string, int> Stations = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Processors = new Dictionary<string, int>();

        public bool HasStation(string name, int level) => name == null || Stations.TryGetValue(name, out int l) && l >= level;
        public int ProcessorCount(string name) => Processors.TryGetValue(name, out int n) ? n : 0;
    }

    public enum StepKind { Gather, Hunt, Farm, Load, Wait, Unload, Craft }

    /// <summary>계획의 단계 하나. After의 단계가 다 끝나야 시작할 수 있다.</summary>
    public sealed class PlanStep
    {
        public int Id;
        public StepKind Kind;
        /// <summary>Gather·Hunt·Farm: 모을 물건. Load: 넣을 재료. Unload: 꺼낼 것. Craft: 만들 것.</summary>
        public string Item;
        public int Amount;
        public string Station;
        public string Fuel;
        public int FuelAmount;
        /// <summary>Craft: 만드는 횟수.</summary>
        public int Crafts;
        /// <summary>Wait: 설비가 도는 시간. 그 밖의 단계는 예상 시간(시뮬레이션이 채움).</summary>
        public float Seconds;
        public readonly List<int> After = new List<int>();
    }

    public sealed class Plan
    {
        public readonly List<PlanStep> Steps = new List<PlanStep>();
        /// <summary>모아야 할 원재료 합(물건 → 수).</summary>
        public readonly Dictionary<string, int> Gathers = new Dictionary<string, int>();
        /// <summary>계획이 쓰기로 한 상자 재고.</summary>
        public readonly Dictionary<string, int> UsedStock = new Dictionary<string, int>();
        /// <summary>목표 바로 아래 재료 중 재고로 모자라 만들거나 모아야 하는 것(대사 "구리 광석이랑 숯이 없어서", 앞에서 2개).</summary>
        public readonly List<string> Short = new List<string>();
        /// <summary>없는 설비: "제작대이름:레벨" 또는 가공 설비 이름.</summary>
        public readonly List<string> MissingStations = new List<string>();
        public readonly List<string> OutOfScope = new List<string>();
        public readonly List<string> Unknown = new List<string>();
        /// <summary>일꾼 1명 기준 예상 초.</summary>
        public float Seconds;
        public bool TooLong => Seconds > CraftPlanner.MaxSeconds;
        /// <summary>설비가 없거나 범위 밖·모르는 재료가 있어 끝까지는 못 한다(할 수 있는 단계까지만).</summary>
        public bool Partial => MissingStations.Count > 0 || OutOfScope.Count > 0 || Unknown.Count > 0;
    }

    /// <summary>
    /// 제작 계획 (구상서 6-1장, 설계 docs/valheim/design-depart-craft.txt C장). 목표에서 재료를 거꾸로 펼치고(재고 먼저, 예약),
    /// 단계 DAG를 만들고, 일꾼 1명이 기다리는 동안 다른 일을 하는 시뮬레이션으로 예상 시간을 낸다. 토큰 0.
    /// 제작법은 NPC가 전부 안다(사용자 확정 2026-10-21), 거점 설비·레벨 조건은 바닐라 그대로.
    /// </summary>
    public sealed class CraftPlanner
    {
        public const int MaxDepth = 8;
        public const float MaxSeconds = 2 * 3600f;
        /// <summary>[기본안] 제작 1회, 설비에 싣기·꺼내기 1회 초.</summary>
        public const float CraftSeconds = 3f, LoadSeconds = 10f;
        /// <summary>[기본안] 위치를 모르는 물건 찾기 상한(구상서 8-2: 찾기 15분).</summary>
        public const float UnknownSearchSeconds = 15 * 60f;

        readonly RecipeBook _book;
        readonly Facilities _fac;
        readonly Func<string, int, float> _gatherSeconds;
        Dictionary<string, int> _available;
        HashSet<string> _visiting;
        Dictionary<string, PlanStep> _gatherSteps;
        Dictionary<string, PlanStep> _lastUnload;
        Plan _plan;

        /// <param name="gatherSeconds">물건·수 → 모으는 데 드는 초(이동 포함). null이면 개당 4초.</param>
        public CraftPlanner(RecipeBook book, Facilities fac, Func<string, int, float> gatherSeconds = null)
        {
            _book = book;
            _fac = fac;
            _gatherSeconds = gatherSeconds ?? ((item, n) => n * 4f);
        }

        public Plan Expand(IList<Ingredient> targets, IDictionary<string, int> stock)
        {
            _plan = new Plan();
            _available = new Dictionary<string, int>();
            if (stock != null) foreach (var kv in stock) _available[kv.Key] = kv.Value;
            _visiting = new HashSet<string>();
            _gatherSteps = new Dictionary<string, PlanStep>();
            _lastUnload = new Dictionary<string, PlanStep>();
            foreach (var t in targets) Need(t.Item, t.Amount, 0);
            _plan.Seconds = Simulate(_plan.Steps);
            return _plan;
        }

        public Plan Expand(string item, int amount, IDictionary<string, int> stock) =>
            Expand(new[] { new Ingredient(item, amount) }, stock);

        /// <returns>이 물건을 만들어 내는 단계들(부모가 기다릴 것). 재고로 다 되면 비어 있다.</returns>
        List<int> Need(string item, int amount, int depth)
        {
            var produced = new List<int>();
            if (amount <= 0) return produced;
            _available.TryGetValue(item, out int have);
            int take = Math.Min(have, amount);
            if (take > 0)
            {
                _available[item] = have - take;
                _plan.UsedStock.TryGetValue(item, out int used);
                _plan.UsedStock[item] = used + take;
                amount -= take;
            }
            if (amount <= 0) return produced;
            if (depth == 1 && _plan.Short.Count < 2 && !_plan.Short.Contains(item)) _plan.Short.Add(item);
            if (depth > MaxDepth || _visiting.Contains(item)) { AddOnce(_plan.Unknown, item); return produced; }

            _visiting.Add(item);
            try
            {
                Process proc = PickProcess(item);
                if (proc != null) return Processed(proc, amount, depth);
                Recipe recipe = PickRecipe(item, true);
                if (recipe != null) return Crafted(recipe, amount, depth);
                SourceKind src = _book.SourceOf(item);
                if (src == SourceKind.Gather || src == SourceKind.Hunt || src == SourceKind.Farm)
                {
                    produced.Add(Gathered(item, amount, src).Id);
                    return produced;
                }
                // 설비만 없음: 없다고 적고, 그 아래 재료는 그래도 모아 둘 수 있게 펼친다(할 수 있는 단계까지)
                IList<Process> procs = _book.ProcessesFor(item);
                if (procs.Count > 0)
                {
                    Process p = procs[0];
                    AddOnce(_plan.MissingStations, p.Station);
                    int batches = (amount + p.OutputAmount - 1) / p.OutputAmount;
                    Need(p.Input, batches * p.InputAmount, depth + 1);
                    if (p.Fuel != null) Need(p.Fuel, batches * p.FuelPerProduct, depth + 1);
                    return produced;
                }
                Recipe any = PickRecipe(item, false);
                if (any != null)
                {
                    AddOnce(_plan.MissingStations, any.Station + ":" + any.StationLevel);
                    int crafts = (amount + any.OutputAmount - 1) / any.OutputAmount;
                    foreach (var ing in Inputs(any)) Need(ing.Item, ing.Amount * crafts, depth + 1);
                    return produced;
                }
                AddOnce(src == SourceKind.OutOfScope ? _plan.OutOfScope : _plan.Unknown, item);
                return produced;
            }
            finally { _visiting.Remove(item); }
        }

        static void AddOnce(List<string> list, string s)
        {
            if (!list.Contains(s)) list.Add(s);
        }

        static IEnumerable<Ingredient> Inputs(Recipe r)
        {
            if (r.OnlyOne && r.Inputs.Count > 0) { yield return r.Inputs[0]; yield break; }
            foreach (var i in r.Inputs) yield return i;
        }

        /// <summary>쓸 수 있는 설비의 가공 중 입력이 거점 재고에 있는 것을 먼저(철: 고철·철 광석 둘 다 철 주괴가 되는데 고철만 있을 수 있음), 없으면 먼저 것.</summary>
        Process PickProcess(string item)
        {
            Process first = null;
            foreach (var p in _book.ProcessesFor(item))
            {
                if (_fac.ProcessorCount(p.Station) <= 0) continue;
                if (first == null) first = p;
                if (_available.TryGetValue(p.Input, out int have) && have >= p.InputAmount) return p;
            }
            return first;
        }

        /// <summary>그 물건의 제작법: 설비가 되는 것 중 재료 종류가 적은 것(같으면 먼저 것). usableOnly가 아니면 아무거나.</summary>
        Recipe PickRecipe(string item, bool usableOnly)
        {
            Recipe best = null;
            foreach (var r in _book.RecipesFor(item))
            {
                if (usableOnly && !_fac.HasStation(r.Station, r.StationLevel)) continue;
                if (best == null || r.Inputs.Count < best.Inputs.Count) best = r;
            }
            return best;
        }

        PlanStep Add(StepKind kind, string item, int amount)
        {
            var s = new PlanStep { Id = _plan.Steps.Count, Kind = kind, Item = item, Amount = amount };
            _plan.Steps.Add(s);
            return s;
        }

        PlanStep Gathered(string item, int amount, SourceKind src)
        {
            _plan.Gathers.TryGetValue(item, out int n);
            _plan.Gathers[item] = n + amount;
            if (_gatherSteps.TryGetValue(item, out var s)) { s.Amount += amount; return s; } // 같은 물건은 한 번에 모은다
            s = Add(src == SourceKind.Hunt ? StepKind.Hunt : src == SourceKind.Farm ? StepKind.Farm : StepKind.Gather, item, amount);
            _gatherSteps[item] = s;
            return s;
        }

        List<int> Crafted(Recipe r, int amount, int depth)
        {
            int crafts = (amount + r.OutputAmount - 1) / r.OutputAmount;
            var deps = new List<int>();
            foreach (var ing in Inputs(r)) deps.AddRange(Need(ing.Item, ing.Amount * crafts, depth + 1));
            PlanStep s = Add(StepKind.Craft, r.Output, crafts * r.OutputAmount);
            s.Crafts = crafts;
            s.Station = r.Station;
            s.After.AddRange(deps);
            var left = crafts * r.OutputAmount - amount; // 남는 건 상자로(다른 갈래가 쓸 수 있게)
            if (left > 0) { _available.TryGetValue(r.Output, out int a); _available[r.Output] = a + left; }
            return new List<int> { s.Id };
        }

        /// <summary>가공: 용량만큼 나눠 싣기 → 기다리기 → 꺼내기. 설비가 여러 대면 번갈아 쓴다.</summary>
        List<int> Processed(Process p, int amount, int depth)
        {
            int batches = (amount + p.OutputAmount - 1) / p.OutputAmount;
            var deps = new List<int>();
            deps.AddRange(Need(p.Input, batches * p.InputAmount, depth + 1));
            if (p.Fuel != null && p.FuelPerProduct > 0) deps.AddRange(Need(p.Fuel, batches * p.FuelPerProduct, depth + 1));
            int perLoad = p.Capacity > 0 ? Math.Max(1, p.Capacity / Math.Max(1, p.InputAmount)) : batches;
            int machines = Math.Max(1, _fac.ProcessorCount(p.Station));
            var result = new List<int>();
            var lastOnMachine = new PlanStep[machines];
            int chunk = 0;
            for (int done = 0; done < batches; chunk++)
            {
                int n = Math.Min(perLoad, batches - done);
                done += n;
                int m = chunk % machines;
                PlanStep load = Add(StepKind.Load, p.Input, n * p.InputAmount);
                load.Station = p.Station;
                load.Fuel = p.Fuel;
                load.FuelAmount = p.Fuel != null ? n * p.FuelPerProduct : 0;
                load.After.AddRange(deps);
                if (lastOnMachine[m] != null) load.After.Add(lastOnMachine[m].Id);
                else if (_lastUnload.TryGetValue(p.Station + "#" + m, out var prev)) load.After.Add(prev.Id); // 다른 갈래가 쓰던 같은 설비
                PlanStep wait = Add(StepKind.Wait, p.Output, n * p.OutputAmount);
                wait.Station = p.Station;
                wait.Seconds = n * p.SecondsPerProduct;
                wait.After.Add(load.Id);
                PlanStep unload = Add(StepKind.Unload, p.Output, n * p.OutputAmount);
                unload.Station = p.Station;
                unload.After.Add(wait.Id);
                lastOnMachine[m] = unload;
                _lastUnload[p.Station + "#" + m] = unload;
                result.Add(unload.Id);
            }
            int left = batches * p.OutputAmount - amount;
            if (left > 0) { _available.TryGetValue(p.Output, out int a); _available[p.Output] = a + left; }
            return result;
        }

        float LaborSeconds(PlanStep s)
        {
            switch (s.Kind)
            {
                case StepKind.Gather:
                case StepKind.Hunt:
                case StepKind.Farm:
                    float g = _gatherSeconds(s.Item, s.Amount);
                    return float.IsNaN(g) || float.IsInfinity(g) ? UnknownSearchSeconds + s.Amount * 4f : g;
                case StepKind.Craft: return CraftSeconds * s.Crafts;
                case StepKind.Load:
                case StepKind.Unload: return LoadSeconds;
            }
            return 0f;
        }

        /// <summary>
        /// 일꾼 1명 시뮬레이션(설계 C-4): 몸 쓰는 단계는 하나씩, 설비 기다리기는 동시에 흐른다.
        /// 고르는 순서: 싣기(기다리기를 먼저 풀어 줌) → 꺼내기 → 남은 길이 가장 긴 것(설비를 오래 돌릴 쪽부터) → 계획 순서.
        /// 실행기(⑥)도 같은 규칙을 쓸 것.
        /// </summary>
        float Simulate(List<PlanStep> steps)
        {
            int n = steps.Count;
            var finish = new float[n];
            var done = new bool[n];
            float t = 0f;
            int left = 0;
            foreach (var s in steps) if (s.Kind != StepKind.Wait) { s.Seconds = LaborSeconds(s); left++; }
            float[] tail = Tails(steps);
            while (left > 0)
            {
                // 앞 단계가 다 끝난 기다리기는 끝나는 시각이 정해진다
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var s in steps)
                    {
                        if (s.Kind != StepKind.Wait || done[s.Id] || !DepsDone(s, done)) continue;
                        finish[s.Id] = DepsFinish(s, finish) + s.Seconds;
                        done[s.Id] = true;
                        changed = true;
                    }
                }
                PlanStep pick = null;
                float nextReady = float.MaxValue;
                foreach (var s in steps)
                {
                    if (s.Kind == StepKind.Wait || done[s.Id] || !DepsDone(s, done)) continue;
                    float ready = DepsFinish(s, finish);
                    if (ready > t) { nextReady = Math.Min(nextReady, ready); continue; }
                    if (pick == null || Better(s, pick, tail)) pick = s;
                }
                if (pick == null)
                {
                    if (nextReady == float.MaxValue) break; // 막힘(순환 등) — 계획에는 없어야 함
                    t = nextReady;
                    continue;
                }
                t += pick.Seconds;
                finish[pick.Id] = t;
                done[pick.Id] = true;
                left--;
            }
            float total = t;
            for (int i = 0; i < n; i++) if (done[i]) total = Math.Max(total, finish[i]);
            return total;
        }

        static int Rank(PlanStep s) => s.Kind == StepKind.Load ? 0 : s.Kind == StepKind.Unload ? 1 : 2;

        static bool Better(PlanStep a, PlanStep b, float[] tail) =>
            Rank(a) != Rank(b) ? Rank(a) < Rank(b) : tail[a.Id] > tail[b.Id] + 0.001f;

        /// <summary>단계마다 그 단계부터 끝까지 가장 긴 길(자기 시간 포함). 단계는 앞 단계보다 뒤에 만들어지므로 뒤에서부터.</summary>
        static float[] Tails(List<PlanStep> steps)
        {
            var tail = new float[steps.Count];
            for (int i = steps.Count - 1; i >= 0; i--)
            {
                tail[i] += steps[i].Seconds;
                foreach (int d in steps[i].After) tail[d] = System.Math.Max(tail[d], tail[i]);
            }
            return tail;
        }

        static bool DepsDone(PlanStep s, bool[] done)
        {
            foreach (int d in s.After) if (!done[d]) return false;
            return true;
        }

        static float DepsFinish(PlanStep s, float[] finish)
        {
            float f = 0f;
            foreach (int d in s.After) f = Math.Max(f, finish[d]);
            return f;
        }
    }
}
