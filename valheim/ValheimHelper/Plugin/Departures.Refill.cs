using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>자동 보충(구상서 11장): 쉬는 일꾼 차례(TickRefill)·채울 것·채집/제작/밭 보충·쉬는 물건.</summary>
    public static partial class Departures
    {
        // ---- 자동 보충 (구상서 11장) ----

        static float s_refillTimer = (float)RefillBook.CheckEvery;
        static readonly Dictionary<string, float> s_refillSkipUntil = new Dictionary<string, float>();

        /// <summary>
        /// 1분마다: 기준치 아래인 물건을 한가한 NPC 하나가 채우러 간다(직접 명령·묘비 회수가 없을 때만).
        /// 자동 보충은 묻지 않는다 — 출발 전 판단이 질문을 낼 상황(위험·재고·밤 등)이면 그 물건은 10분 쉰다.
        /// 채집으로 나는 물건은 채집 일, 못 나는 물건은 제작 계획(StartRefillMake, ⑩).
        /// </summary>
        internal static void TestRefillNow() { s_refillTimer = 0f; TickRefill(0f); }

        /// <summary>!계속채워를 받으면 곧바로(2초 안) 확인한다(사용자 11-09: 대답만 하고 한참 뒤에 움직이던 것).</summary>
        internal static void RefillSoon(string item)
        {
            if (item != null) s_refillSkipUntil.Remove(item); // 다시 시켰으니 전에 실패해 쉬던 것도 바로 해 본다
            if (s_refillTimer > 2f) s_refillTimer = 2f;
        }

        /// <summary>새로 쉬게 된 일꾼을 5초마다 본다 — 일 끝나고 돌아오면 1분을 기다리지 않고 바로 다음 보충·먹거리.</summary>
        static float s_idlePoll;
        static readonly HashSet<WorkerNpc> s_idleSeen = new HashSet<WorkerNpc>();

        /// <summary>스스로 일을 받을 수 있게 쉬는 중: 거점에서 쉼, 대기열·질문·묘비 회수 없음.</summary>
        static bool FreeForChores(WorkerNpc n) => n != null && !n.IsDead() && n.Ai.Current == WorkerAI.Mode.Idle && n.Ai.Queue.Count == 0 && !s_pending.ContainsKey(n) && !n.Ai.HasChores;

        static void TickRefill(float dt)
        {
            s_refillTimer -= dt;
            s_idlePoll -= dt;
            if (s_idlePoll <= 0f)
            {
                s_idlePoll = 5f;
                bool fresh = false;
                foreach (var n in WorkerNpc.All)
                {
                    if (!FreeForChores(n)) { s_idleSeen.Remove(n); continue; }
                    if (s_idleSeen.Add(n) && !n.Ai.Resting) fresh = true;
                }
                s_idleSeen.RemoveWhere(n => n == null);
                if (fresh && s_refillTimer > 1f) s_refillTimer = 1f;
            }
            if (s_refillTimer > 0f) return;
            s_refillTimer = (float)RefillBook.CheckEvery;
            if (WorkerNpc.All.Count == 0) return;
            // 쉬는 일꾼 모두 차례로(사용자 11-09): 같은 물건은 RefillBusy가 막으니 각자 다른 것을 맡는다
            // 호위병 먼저(사용자 11-14 "비욘도 쉴 때 거점 안 집안일"): 밭·꿀·먹이를 호위병이 맡으면 일꾼은 채집·보충에 남는다
            var order = new List<WorkerNpc>(WorkerNpc.All);
            order.Sort((x, y) => (y != null && y.IsGuard).CompareTo(x != null && x.IsGuard));
            foreach (var npc in order)
            {
                if (!FreeForChores(npc)) continue;
                if (Commands.RunLaterChore(npc)) continue; // 바쁠 때 받은 !가축·!정리(회차 662)
                if (npc.IsGuard)
                {
                    // 거점(125m) 안 집안일만: 밭(거두기·심기)·꿀·먹이. 순찰 중엔 안 함(순찰 먼저), 채집·먼 길·보충은 안 함.
                    // 동행 요청이 오면 GuardFree가 집안일 중도 쉬는 것으로 보아 GoWithGuard의 Follow가 바로 놓게 한다.
                    if (!npc.Ai.Resting && !npc.Ai.Patrolling && ChoresOn && Features.IdleChores && !IdleFarm(npc, BaseArea.Radius) && !IdleHoney(npc)) IdleFeed(npc);
                    continue;
                }
                if (JoinRisky(npc) != null) continue; // 쉬는 일꾼 없게 ① 동료가 위험한 데 혼자 가면 같이(사용자 11-13)
                if (!npc.Ai.Resting && JoinGather(npc)) continue; // 쉬는 일꾼 없게 ① 동료 일 돕기 먼저(사용자 11-13)
                if (!npc.Ai.Resting && JoinHelp(npc)) continue; // 쉬는 일꾼 없게 ② 지원 요청 대기열 맨 앞(decisions 7절)
                if (Brain.Refill.Count > 0 || Brain.Personal.Count > 0)
                {
                    GatherJob job = NextRefillJob(npc, 0, true);
                    if (job != null)
                    {
                        npc.Ai.StartJob(job);
                        if (job.WantGuard) GoWithGuard(npc);
                        npc.Say("refillGo", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(job.Item), ["n"] = job.Target.ToString() });
                        continue;
                    }
                    if (StartRefillMake(npc)) continue;
                }
                if (!npc.Ai.Resting && ChoresOn && Features.IdleChores && !IdleFood(npc) && !IdleFish(npc) && !Repay(npc) && !IdleFarm(npc) && !IdleHoney(npc) && !IdleFeed(npc) && !IdleCull(npc)) IdleTame(npc);
            }
        }

        /// <summary>자동 보충할 것: !채워 기준치 아래인 것 + 개인 상자 준비(상자 기능 ③ 나-2)에서 거점 상자로도 모자란 몫(같은 물건이면 더함).</summary>
        static List<Ingredient> RefillDue(Vector3 home)
        {
            List<Ingredient> due = Brain.Refill.Count > 0 ? Brain.Refill.Due(item => RefillStock(item, home), Brain.Sets) : new List<Ingredient>();
            foreach (Ingredient s in PersonalChests.Shortages(home))
            {
                Ingredient same = due.Find(d => d.Item == s.Item);
                if (same != null) same.Amount += s.Amount;
                else due.Add(s);
            }
            return due;
        }

        /// <summary>자동 보충으로 구할 수 있는 물건인지(채집·밭·제작법·가공). 출발 판단은 따로(RefillSkipped).</summary>
        internal static bool Obtainable(string item)
        {
            if (ResourceCatalog.WaysFor(item).Count > 0 || Crops.ForProduce(item) != null) return true;
            if (!Features.RunPlans) return false;
            RecipeBook book = Recipes.Book();
            return book.RecipesFor(item).Count > 0 || book.ProcessesFor(item).Count > 0;
        }

        /// <summary>자동 보충이 그 물건을 쉬는 중(출발 판단이 막음·계획이 안 됨 등, 10분).</summary>
        internal static bool RefillSkipped(string item) => s_refillSkipUntil.TryGetValue(item, out float until) && Time.time < until;

        /// <summary>혼자 사냥하기엔 약했던 물건 → 그때 전력(Strength). 지금 전력이 그보다 높아지면(장비·음식) 잊는다.</summary>
        static readonly Dictionary<string, float> s_huntWeak = new Dictionary<string, float>();

        internal static void TestForgetWeak(string item) => s_huntWeak.Remove(item);

        /// <summary>밤이라 못 구한 자동 보충 물건(해가 뜨면 비움).</summary>
        static readonly HashSet<string> s_refillNight = new HashSet<string>();

        static bool NightSkipped(string item)
        {
            if (!EnvMan.IsNight()) { s_refillNight.Clear(); return false; }
            return s_refillNight.Contains(item);
        }

        /// <summary>일꾼 전력: 최대 체력 + 갑옷×2 + 한 방×3(장비·음식이 좋아지면 오른다).</summary>
        internal static float Strength(WorkerNpc npc) => npc.GetMaxHealth() + npc.GetBodyArmor() * 2f + Guard.HitOf(npc) * 3f;

        internal static void NoteHuntWeak(WorkerNpc npc, string item)
        {
            float s = Strength(npc);
            if (s_huntWeak.TryGetValue(item, out float old) && old >= s) return;
            s_huntWeak[item] = s;
            Plugin.Log.LogInfo($"{npc.NpcName} 사냥 {item}: 혼자는 약함 기억(전력 {s:0})");
        }

        internal static bool HuntWeak(WorkerNpc npc, string item)
        {
            if (!s_huntWeak.TryGetValue(item, out float at)) return false;
            if (Strength(npc) <= at + 0.5f) return true;
            s_huntWeak.Remove(item); // 강해졌다: 다시 혼자 해 본다
            return false;
        }

        /// <summary>
        /// 자동 보충 일이 실패로 끝남(사용자 11-07 무한 반복): 그 물건은 10분 쉰다. 사냥감이 세서(무리·센 것·호위병 바쁨) 그만뒀으면 약함도 기억.
        /// </summary>
        internal static void RefillJobFailed(WorkerNpc npc, string item, string key)
        {
            s_refillSkipUntil[item] = Time.time + 600f;
            if (key == "nightNoExplore" || key == "nightBack") s_refillNight.Add(item); // 밤에 못 감: 아침까지 쉼(그동안 요리 같은 제작 보충이 차례를 받음)
            if (key == "preyPack" || key == "preyStrong" || key == "preyWaitGuard") NoteHuntWeak(npc, item);
            Plugin.Log.LogInfo($"{npc.NpcName} 자동 보충 {item}: 실패({key}) → 10분 쉼");
        }

        /// <summary>시험용: 출발 판단이 막은 것처럼 그 물건을 쉬게 하거나 풀기.</summary>
        internal static void TestSkip(string item, bool on)
        {
            if (on) s_refillSkipUntil[item] = Time.time + 600f;
            else s_refillSkipUntil.Remove(item);
        }

        /// <summary>그 물건을 이미 누가 채우거나 만드는 중이면(두 일꾼이 같은 걸 겹쳐 만들어 재료를 두 번 쓰지 않게).</summary>
        internal static bool RefillBusy(string item)
        {
            foreach (var n in WorkerNpc.All)
            {
                IWorkerJob j = n.Ai.Job;
                if (j is GatherJob g && g.Item == item) return true;
                if (j is PlanJob p && p.Goal != null && p.Goal.Item == item) return true;
            }
            return false;
        }

        /// <summary>자동 보충 재고: 채집 물건은 채집이 넣는 재료 상자(Stock), 만드는 물건은 만든 것이 놓이는 역할 상자(음식 → 음식, 장비 → 장비, 나머지 → 재료).</summary>
        internal static int RefillStock(string prefab, Vector3 home)
        {
            if (ResourceCatalog.WaysFor(prefab).Count > 0) return Stock(prefab, home);
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null) return 0;
            int n = 0;
            foreach (var c in Chests.NearBase(home, Chests.RoleFor(drop.m_itemData))) n += c.GetInventory().CountItems(drop.m_itemData.m_shared.m_name);
            return n;
        }

        /// <summary>
        /// 기준치 아래인 물건 중 갈 만한 첫 채집 일(없으면 null). 시작은 부르는 쪽 몫.
        /// maxAmount가 0보다 크면 한 번에 그만큼만(Wait 중 짧게 다녀오기).
        /// </summary>
        /// <param name="canEscort">호위병을 데려갈 수 있는 자리(거점에서 바로 시작) — 제작 계획 Wait 중 끼워 넣기는 false(위험한 곳만 남으면 그 물건은 미룸).</param>
        internal static GatherJob NextRefillJob(WorkerNpc npc, int maxAmount, bool canEscort = false)
        {
            RefillBook refill = Brain.Refill;
            if (refill.Count == 0 && Brain.Personal.Count == 0) return null;
            Vector3 home = npc.Home;
            foreach (Ingredient due in RefillDue(home))
            {
                if (s_refillSkipUntil.TryGetValue(due.Item, out float until) && Time.time < until) continue;
                if (NightSkipped(due.Item)) continue;
                HashSet<ToolKind> ways = GatherJob.WaysFor(npc, due.Item);
                if (ways.Count == 0 || RefillBusy(due.Item) || FarmOnly(due.Item)) continue; // 밭 물건은 StartRefillMake → RefillFarm
                int amount = maxAmount > 0 ? System.Math.Min(due.Amount, maxAmount) : due.Amount;
                var cmd = new ChatCommand { Kind = CommandKind.Item, Item = due.Item, Amount = amount };
                DepartureInput input = Input(npc, cmd, ways, true);
                input.Stock = 0; // 모자란 만큼만 시켰으니 재고 질문은 하지 않는다
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{npc.NpcName} 자동 보충 {due.Item} {amount}: {r.Action} {r.Question} ({r.Reason})");
                // 자동 보충도 같은 판단: 덜 위험한 곳으로 바꿔 가고, 다 위험하면 호위병과(바쁘면 기다리지 않고 그 물건은 10분 뒤로 — 사용자 10-26)
                bool together = r.Action == DepartAction.GoTogether && canEscort && GuardIdle();
                if (r.Action != DepartAction.Go && !together) { s_refillSkipUntil[due.Item] = Time.time + 600f; continue; }
                // 출발 전 전력 판단(사용자 11-07): 이 사냥감을 혼자 못 잡았고 그 뒤로 강해지지 않았으면 처음부터 쉬는 호위병과, 호위병이 바쁘면 10분 뒤로
                if (!together && ways.Contains(ToolKind.Hunt) && HuntWeak(npc, due.Item))
                {
                    if (!canEscort || !GuardIdle()) { s_refillSkipUntil[due.Item] = Time.time + 600f; Plugin.Log.LogInfo($"{npc.NpcName} 자동 보충 {due.Item}: 혼자는 약함·호위병 못 데려감 → 10분 뒤"); continue; }
                    together = true;
                    Plugin.Log.LogInfo($"{npc.NpcName} 자동 보충 {due.Item}: 혼자는 약함(전력 {Strength(npc):0}) → 처음부터 호위병과");
                }
                var job = new GatherJob(npc, due.Item, amount, ways) { WantGuard = together, FromRefill = true };
                foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
                return job;
            }
            return null;
        }

        /// <summary>
        /// ⑩ 만드는 물건 자동 보충("!채워 구리주괴 20", 세트의 음식): 채집으로 안 나는 첫 모자란 물건을 제작 계획으로 만든다(묻지 않음).
        /// 계획이 끝까지 되는 것만 — 범위 밖·모름·없는 설비·너무 김·밭 재료(⑩ 다음 조각)면 그 물건은 10분 쉰다. 시작했으면 true.
        /// </summary>
        static bool StartRefillMake(WorkerNpc npc)
        {
            if (!Features.RunPlans) return false;
            Vector3 home = npc.Home;
            RecipeBook book = Recipes.Book();
            foreach (Ingredient due in RefillDue(home))
            {
                if (s_refillSkipUntil.TryGetValue(due.Item, out float until) && Time.time < until) continue;
                if (RefillBusy(due.Item)) continue;
                if (FarmOnly(due.Item))
                {
                    if (RefillFarm(npc, due.Item, due.Amount)) return true;
                    continue;
                }
                if (ResourceCatalog.WaysFor(due.Item).Count > 0) continue;
                if (book.RecipesFor(due.Item).Count == 0 && book.ProcessesFor(due.Item).Count == 0) { s_refillSkipUntil[due.Item] = Time.time + 600f; continue; }
                Plan plan = PlanFor(npc, new List<Ingredient> { due });
                bool farm = plan.Steps.Exists(s => s.Kind == StepKind.Farm);
                Plugin.Log.LogInfo($"{npc.NpcName} 자동 보충 만들기 {due.Item} {due.Amount}: 단계 {plan.Steps.Count}, {plan.Seconds:0}초, 부분 {plan.Partial}, 김 {plan.TooLong}, 밭 {farm}");
                if (farm && plan.OutOfScope.Count == 0 && plan.Unknown.Count == 0)
                {
                    // 밭 재료가 낀 계획(당근 수프): 그 재료를 먼저 밭에 심는다. 다 자라 상자에 들어오면 다음 확인 때 계획이 끝까지 된다.
                    PlanStep f = plan.Steps.Find(s => s.Kind == StepKind.Farm);
                    if (!RefillBusy(f.Item) && RefillFarm(npc, f.Item, f.Amount)) return true;
                    s_refillSkipUntil[due.Item] = Time.time + 600f;
                    continue;
                }
                if (plan.Steps.Count == 0 || plan.Partial || plan.TooLong || farm) { s_refillSkipUntil[due.Item] = Time.time + 600f; continue; }
                string label = GatherJob.ItemLabel(due.Item);
                npc.Ai.StartJob(new PlanJob(npc, plan, label) { Goal = new WorkItem { Kind = WorkKind.Make, Label = label, Item = due.Item, Amount = due.Amount } });
                npc.Say("refillMake", new Dictionary<string, string> { ["item"] = label, ["n"] = due.Amount.ToString(), ["minutes"] = Departure.Minutes5(plan.Seconds).ToString() });
                return true;
            }
            return false;
        }

        /// <summary>
        /// ⑩ 밭 물건 자동 보충(5b): 그 작물이 자라는 첫 밭에서 다 자란 것을 거두고 모자란 만큼(밭에서 자라는 중인 것은 빼고, FarmJob.Coming) 심는다.
        /// 밭이 없거나·씨앗도 거둘 것도 없거나·이미 자라는 중이면 그 물건은 10분 쉰다(말 없음). 시작했으면 true.
        /// </summary>
        static bool RefillFarm(WorkerNpc npc, string item, int amount)
        {
            Crop crop = Crops.ForProduce(item);
            FarmField field = crop != null ? FieldFor(crop) : null;
            bool taken = false;
            if (field != null)
                foreach (var n in WorkerNpc.All)
                    if (n.Ai.Job is FarmJob fj && fj.Field.Contains(field.X, field.Z)) taken = true;
            if (taken) return false; // 누가 그 밭일 중: 쉬게 하지 않고 다음 확인 때 다시 셈
            int coming = field != null ? FarmJob.Coming(field, item) : 0, ripe = field != null ? FarmJob.Ripe(field).Count : 0;
            int need = amount - coming;
            FarmJob job = null;
            FarmPlan plan = null;
            if (field != null && !taken && (need > 0 || ripe > 0)) job = FarmJob.ForWant(npc, field, item, System.Math.Max(0, need), out plan);
            bool go = job != null && (plan.Plant.Count > 0 || ripe > 0);
            Plugin.Log.LogInfo($"{npc.NpcName} 자동 보충 밭 {item} {amount}: 밭 {(field != null ? $"({field.X:0},{field.Z:0})" : "없음")}, 맡음 {taken}, 자라는 중 {coming}, 다 자람 {ripe}, 심기 {(plan != null ? plan.Plant.Count : 0)} → {(go ? "감" : "쉼")}");
            if (!go || !Commands.StartFarm(npc, field, false, job)) { s_refillSkipUntil[item] = Time.time + 600f; return false; }
            var args = new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(item), ["n"] = amount.ToString() };
            if (plan.Plant.Count > 0)
            {
                args["minutes"] = Departure.Minutes5((float)Farm.GrowSeconds(Crops.All, plan)).ToString();
                npc.Say("refillFarm", args);
            }
            else npc.Say("farmHarvestFor", args);
            return true;
        }
    }
}
