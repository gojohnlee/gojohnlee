using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>쉴 때 일(사용자 11-09): 먹거리·갚기·밭·꿀·먹이·솎기·길들이기·낚시.</summary>
    public static partial class Departures
    {
        // ---- 쉴 때 먹거리 (사용자 11-09: "당장 음식 없으면 베리라도") ----

        /// <summary>거점 음식 상자의 먹을 것 합이 이보다 적으면 쉬는 일꾼이 따 온다.</summary>
        internal const int IdleFoodBelow = 10;
        /// <summary>한 번에 따 올 수.</summary>
        internal const int IdleFoodAmount = 10;
        /// <summary>거점 음식 상자의 일꾼 음식 합 — 시킨 음식 몫(Prep.Fresh)은 뺌(사용자 11-16: 몫을 넉넉으로 보면 안 만들고 몫이라 못 먹어 굶음).</summary>
        internal static int BaseFood(Vector3 home)
        {
            var haves = new Dictionary<string, int>();
            foreach (var c in Chests.NearBase(home, ChestRole.Food))
                foreach (var it in c.GetInventory().GetAllItems())
                    if (Gear.IsFood(it)) { haves.TryGetValue(it.m_shared.m_name, out int h); haves[it.m_shared.m_name] = h + it.m_stack; }
            return Prep.Fresh.FreeTotal(haves, ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time);
        }

        /// <summary>
        /// 쉴 때 먹거리 후보(사용자 11-09 "진행도에 따라"): 게임 음식 중 구할 길(채집·제작법·가공)이 있는 것, 체력 높은 순.
        /// 진행도는 따로 세지 않음 — 설비·재료 나는 곳·안전한 길이 갖춰져야 계획·출발 판단이 서므로 진행할수록 위쪽 음식이 저절로 됨.
        /// 10분마다 다시 셈(채집 목록·제작법이 늘어남).
        /// </summary>
        static List<string> s_foodRank;
        static float s_foodRankAt = -1e9f;

        internal static List<string> FoodRank()
        {
            if (s_foodRank != null && Time.time - s_foodRankAt < 600f) return s_foodRank;
            s_foodRankAt = Time.time;
            var list = new List<KeyValuePair<string, float>>();
            RecipeBook book = Features.RunPlans ? Recipes.Book() : null;
            if (ObjectDB.instance != null)
                foreach (GameObject go in ObjectDB.instance.m_items)
                {
                    ItemDrop d = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (d == null || !Gear.IsFood(d.m_itemData) || d.m_itemData.m_shared.m_food <= 0f) continue;
                    string name = go.name;
                    bool gather = ResourceCatalog.WaysFor(name).Count > 0;
                    bool make = book != null && (book.RecipesFor(name).Count > 0 || book.ProcessesFor(name).Count > 0);
                    if (gather || make) list.Add(new KeyValuePair<string, float>(name, d.m_itemData.m_shared.m_food));
                }
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            s_foodRank = new List<string>();
            foreach (var kv in list) s_foodRank.Add(kv.Key);
            Plugin.Log.LogInfo($"쉴 때 먹거리 후보 {s_foodRank.Count}개(체력순): {string.Join(", ", s_foodRank.ToArray())}");
            return s_foodRank;
        }

        /// <summary>쉴 때 만들 음식 수(모자라면 반으로 다시)와 계획 길이 상한(쉬는 틈에 할 일이라 짧게).</summary>
        internal const int IdleCookAmount = 6;
        internal const float IdleCookMaxSeconds = 20 * 60f;
        /// <summary>한 번 확인에 세워 볼 제작 계획 수(무겁지 않게 — 안 된 것은 10분 쉬니 다음 확인 때 다음 후보).</summary>
        const int IdlePlansPerCheck = 4;

        /// <summary>
        /// 쉬는 일꾼: 거점 음식 상자에 먹을 것이 IdleFoodBelow 밑이면 FoodRank 위에서부터 되는 첫 음식을 구한다(묻지 않음).
        /// 채집 음식(베리·버섯)은 출발 판단 Go면 IdleFoodAmount개, 만드는 음식은 TryCook. 안 되면 그 물건은 10분 쉼. 시작했으면 true.
        /// </summary>
        internal static bool IdleFood(WorkerNpc npc)
        {
            int have = BaseFood(npc.Home);
            if (have >= IdleFoodBelow) return false;
            int plans = 0;
            foreach (string item in FoodRank())
            {
                if (RefillSkipped(item) || NightSkipped(item) || RefillBusy(item) || FarmOnly(item)) continue;
                if (ResourceCatalog.WaysFor(item).Count == 0)
                {
                    if (EnvMan.IsNight() || plans >= IdlePlansPerCheck) continue;
                    plans++;
                    if (TryCook(npc, item, have)) return true;
                    continue;
                }
                HashSet<ToolKind> ways = GatherJob.WaysFor(npc, item);
                if (ways.Count == 0 || ways.Contains(ToolKind.Hunt)) { s_refillSkipUntil[item] = Time.time + 600f; continue; }
                var cmd = new ChatCommand { Kind = CommandKind.Item, Item = item, Amount = IdleFoodAmount };
                DepartureInput input = Input(npc, cmd, ways, true);
                input.Stock = 0;
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 먹거리 {item} {IdleFoodAmount}(상자 음식 {have}): {r.Action} {r.Question} ({r.Reason})");
                if (r.Action != DepartAction.Go) { s_refillSkipUntil[item] = Time.time + 600f; continue; }
                var job = new GatherJob(npc, item, IdleFoodAmount, ways) { FromRefill = true };
                foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
                npc.Ai.StartJob(job);
                npc.Say("idleFood", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(item), ["n"] = IdleFoodAmount.ToString() });
                return true;
            }
            return false;
        }

        /// <summary>
        /// 만드는 음식 하나: 제작 계획이 끝까지 되고(설비·재료 나는 곳 앎, 밭 재료 없음) IdleCookMaxSeconds 안이며(6개가 길면 3개),
        /// 사냥·채집 단계마다 출발 판단이 Go이고 혼자 약했던 사냥감이 아닐 때만. 안 되면 10분 쉼, 계획이 실패로 끝나도 10분 쉼(PlanJob.FromIdle).
        /// </summary>
        static bool TryCook(WorkerNpc npc, string item, int have)
        {
            Plan plan = null;
            int amount = 0;
            string why = null;
            foreach (int n in new[] { IdleCookAmount, IdleCookAmount / 2 })
            {
                amount = n;
                plan = PlanFor(npc, new List<Ingredient> { new Ingredient(item, n) });
                why = plan.Steps.Count == 0 ? "단계 없음" : plan.Partial ? "설비·출처 없음" : plan.TooLong || plan.Seconds > IdleCookMaxSeconds ? $"너무 김 {plan.Seconds:0}초" : null;
                if (why == null || !why.StartsWith("너무 김")) break;
            }
            if (why == null)
                foreach (var st in plan.Steps)
                {
                    if (st.Kind == StepKind.Farm) { why = "밭 재료"; break; }
                    if (st.Kind != StepKind.Gather && st.Kind != StepKind.Hunt) continue;
                    DepartureResult j = JudgeStep(npc, st.Item, st.Amount);
                    if (j == null || j.Action != DepartAction.Go) { why = $"{st.Item} 판단 {(j != null ? j.Action.ToString() : "방법 없음")}"; break; }
                    if (GatherJob.WaysFor(npc, st.Item).Contains(ToolKind.Hunt) && HuntWeak(npc, st.Item)) { why = $"{st.Item} 혼자 약함"; break; }
                }
            Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 만들기 {item} {amount}(상자 음식 {have}): 단계 {plan.Steps.Count}, {plan.Seconds:0}초 → {why ?? "감"}");
            if (why != null) { s_refillSkipUntil[item] = Time.time + 600f; return false; }
            string label = GatherJob.ItemLabel(item);
            npc.Ai.StartJob(new PlanJob(npc, plan, label) { FromIdle = true, Goal = new WorkItem { Kind = WorkKind.Make, Label = label, Item = item, Amount = amount } });
            npc.Say("idleCook", new Dictionary<string, string> { ["item"] = label, ["n"] = amount.ToString(), ["minutes"] = Departure.Minutes5(plan.Seconds).ToString() });
            return true;
        }

        // ---- 쓴 재료 다시 캐기 (사용자 11-09 "지가 쓴 재료는 다시 캐면") ----

        // 쉴 때 만들기가 상자에서 쓴 캘 수 있는 재료 → 아직 안 채운 수: Brain.Debt(일꾼 ZDO vh_debt에 저장, 사용자 11-10 — 다시 켜도 기억, 월드마다 따로)

        internal static void AddDebt(WorkerNpc npc, Dictionary<string, int> used)
        {
            foreach (var kv in used)
            {
                if (kv.Value <= 0 || ResourceCatalog.WaysFor(kv.Key).Count == 0 || FarmOnly(kv.Key)) continue; // 캘 수 있는 것만(밭 작물은 밭일이 채움)
                int now = Brain.Debt.Add(kv.Key, kv.Value);
                Plugin.Log.LogInfo($"{npc.NpcName} 쓴 재료 다시 캘 몫: {kv.Key} +{kv.Value} → {now}");
            }
        }

        internal static void Repaid(string item, int n) => Brain.Debt.Repaid(item, n);

        /// <summary>쉬는 일꾼: 갚을 재료가 있으면 그것부터 캐러 감(출발 판단 Go일 때만, 안 되면 그 물건 10분 쉼 — 몫은 남음). 시작했으면 true.</summary>
        internal static bool Repay(WorkerNpc npc)
        {
            foreach (var kv in new List<KeyValuePair<string, int>>(Brain.Debt.Items))
            {
                string item = kv.Key;
                if (RefillSkipped(item) || NightSkipped(item) || RefillBusy(item)) continue;
                HashSet<ToolKind> ways = GatherJob.WaysFor(npc, item);
                if (ways.Count == 0 || ways.Contains(ToolKind.Hunt) && HuntWeak(npc, item)) { s_refillSkipUntil[item] = Time.time + 600f; continue; }
                var cmd = new ChatCommand { Kind = CommandKind.Item, Item = item, Amount = kv.Value };
                DepartureInput input = Input(npc, cmd, ways, true);
                input.Stock = 0;
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{npc.NpcName} 쓴 재료 다시 캐기 {item} {kv.Value}: {r.Action} {r.Question} ({r.Reason})");
                if (r.Action != DepartAction.Go) { s_refillSkipUntil[item] = Time.time + 600f; continue; }
                var job = new GatherJob(npc, item, kv.Value, ways) { FromRefill = true, Repay = true };
                foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
                npc.Ai.StartJob(job);
                npc.Say("repay", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(item), ["n"] = kv.Value.ToString() });
                return true;
            }
            return false;
        }

        // ---- 쉴 때 밭일 (사용자 11-09 "밭도 알아서") ----

        /// <summary>밭마다 다음에 볼 때(Time.time). 볼 게 없었어도 10분은 다시 안 봄 — 거둘 것·빈자리 세기가 무겁지 않게, 헛걸음 되풀이 없게.</summary>
        static readonly Dictionary<string, float> s_farmNext = new Dictionary<string, float>();
        internal const float IdleFarmEvery = 600f;

        internal static void TestFarmNow() => s_farmNext.Clear();

        /// <summary>
        /// 쉬는 일꾼(낮): !밭으로 정한 밭에 다 자란 것이 있거나, 빈자리가 있고 상자에 심을 씨앗이 있으면 "!밭"과 같은 일(거두고 Farm.Fill만큼 심기)을 한다.
        /// 다른 일꾼이 그 밭일 중이면 건너뜀. 시작했으면 true.
        /// </summary>
        internal static bool IdleFarm(WorkerNpc npc, float maxDist = float.MaxValue)
        {
            if (EnvMan.IsNight() || Brain.Farms.Fields.Count == 0) return false;
            foreach (var field in new List<FarmField>(Brain.Farms.Fields))
            {
                if (Utils.DistanceXZ(npc.Home, new Vector3(field.X, 0f, field.Z)) > maxDist) continue; // 호위병은 거점 범위 안 밭만
                string key = $"{field.X:0},{field.Z:0}";
                if (s_farmNext.TryGetValue(key, out float next) && Time.time < next) continue;
                bool taken = false;
                foreach (var n in WorkerNpc.All) if (n.Ai.Job is FarmJob fj && fj.Field.Contains(field.X, field.Z)) taken = true;
                if (taken) continue;
                s_farmNext[key] = Time.time + IdleFarmEvery;
                int ripe = FarmJob.Ripe(field).Count;
                int free = FarmJob.FreeSpots(field);
                FarmJob job = FarmJob.ForField(npc, field);
                job.Quiet = true;
                bool plant = free > 0 && job.HasPlanting;
                Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 밭 ({field.X:0},{field.Z:0}): 다 자람 {ripe}, 빈자리 {free}, 씨앗 계획 {job.HasPlanting} → {(ripe > 0 || plant ? "감" : "볼 것 없음")}");
                if (ripe == 0 && !plant) continue;
                if (!Commands.StartFarm(npc, field, false, job)) continue;
                npc.Say("idleFarm", new Dictionary<string, string> { ["ripe"] = ripe.ToString(), ["free"] = free.ToString() });
                return true;
            }
            return false;
        }

        // ---- 쉴 때 일 표: 꿀·먹이·솎기·길들이기·낚시 (사용자 11-09 요청 가·다-2·다-3·다-4·나) ----

        /// <summary>쉴 때 일 하나: 일꾼 집마다 다음에 볼 때(Time.time)·간격·그 일 종류(다른 일꾼이 하는 중이면 건너뜀)·말.</summary>
        sealed class Chore
        {
            public readonly float Every;
            public readonly string Say;
            public readonly System.Func<IWorkerJob, bool> Is;
            public readonly Dictionary<string, float> Next = new Dictionary<string, float>();
            public Chore(float every, string say, System.Func<IWorkerJob, bool> isJob) { Every = every; Say = say; Is = isJob; }
        }

        /// <summary>배고파지는 600초보다 짧게 1분 간격(먹이·솎기·길들이기 — 배부름 600초, 배고파지면 다시 줌).</summary>
        const float IdleFeedEvery = 60f;
        static readonly Chore s_honey = new Chore(IdleFarmEvery, "idleHoney", j => j is HoneyJob);
        static readonly Chore s_feed = new Chore(IdleFeedEvery, "idleFeed", j => j is LivestockJob);
        static readonly Chore s_cull = new Chore(IdleFeedEvery, "idleCull", j => j is CullJob);
        static readonly Chore s_tame = new Chore(IdleFeedEvery, "idleTame", j => j is TameJob);
        static readonly Chore s_egg = new Chore(IdleFeedEvery, "idleEgg", j => j is EggJob);
        static readonly Chore s_fish = new Chore(IdleFarmEvery, "idleFish", j => j is FishJob);

        internal static void TestHoneyNow() => s_honey.Next.Clear();
        internal static void TestFeedNow() => s_feed.Next.Clear();
        internal static void TestCullNow() => s_cull.Next.Clear();
        internal static void TestTameNow() => s_tame.Next.Clear();
        internal static void TestEggNow() => s_egg.Next.Clear();
        internal static void TestFishNow() => s_fish.Next.Clear();

        /// <summary>볼 때인가: 낮이고, 이 집 간격이 지났고, 다른 일꾼이 그 일 중이 아님. key = 일꾼 집.</summary>
        static bool Due(WorkerNpc npc, Chore c, out string key)
        {
            key = $"{npc.Home.x:0},{npc.Home.z:0}";
            if (EnvMan.IsNight()) return false;
            if (c.Next.TryGetValue(key, out float next) && Time.time < next) return false;
            foreach (var n in WorkerNpc.All) if (c.Is(n.Ai.Job)) return false;
            return true;
        }

        /// <summary>이 집은 간격만큼 다시 안 봄(볼 게 없었어도 — 헛걸음 되풀이 없게).</summary>
        static void Stamp(Chore c, string key) => c.Next[key] = Time.time + c.Every;

        /// <summary>일이 있으면 시작하고 말함. 시작했으면 true.</summary>
        static bool Begin(WorkerNpc npc, Chore c, IWorkerJob job)
        {
            if (job == null) return false;
            npc.Ai.StartJob(job);
            npc.Say(c.Say, new Dictionary<string, string>());
            return true;
        }

        /// <summary>쉬는 일꾼(낮): 거점 안 벌통에 꿀이 HoneyJob.MinHoney 이상이면 거두러 간다. 10분 간격. 시작했으면 true.</summary>
        internal static bool IdleHoney(WorkerNpc npc)
        {
            if (!Due(npc, s_honey, out string key)) return false;
            Stamp(s_honey, key);
            List<Beehive> hives = HoneyJob.Ready(npc.Home);
            Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 벌통: 꿀 찬 것 {hives.Count}");
            return Begin(npc, s_honey, hives.Count == 0 ? null : new HoneyJob(npc, hives));
        }

        /// <summary>쉬는 일꾼(낮): "가축" 상자 둘레의 배고픈 길들인 가축에게 상자 먹이를 떨궈 주러 간다(LivestockJob). 시작했으면 true.</summary>
        internal static bool IdleFeed(WorkerNpc npc)
        {
            if (!Due(npc, s_feed, out string key)) return false;
            Stamp(s_feed, key);
            return Begin(npc, s_feed, LivestockJob.For(npc));
        }

        /// <summary>쉬는 일꾼(낮): "가축" 상자 둘레 길들인 것이 CullJob.PenMax를 넘으면 다 자란 것을 잡아 고기를 거점 상자로(CullJob). 시작했으면 true.</summary>
        internal static bool IdleCull(WorkerNpc npc)
        {
            if (!Due(npc, s_cull, out string key)) return false;
            Stamp(s_cull, key);
            return Begin(npc, s_cull, CullJob.For(npc));
        }

        /// <summary>쉬는 일꾼(낮): 가축 상자의 알 하나를 거점 불 곁(닭 알은 지붕 아래)에 놓아 깨게 한다(EggJob). 시작했으면 true.</summary>
        internal static bool IdleEgg(WorkerNpc npc)
        {
            if (!Due(npc, s_egg, out string key)) return false;
            Stamp(s_egg, key);
            return Begin(npc, s_egg, EggJob.For(npc));
        }

        /// <summary>쉬는 일꾼(낮): 집 TameJob.TameFar 안 배고픈 야생 짐승에게 "가축" 상자 먹이를 떨궈 길들이러 간다. 시작했으면 true.</summary>
        internal static bool IdleTame(WorkerNpc npc)
        {
            if (!Due(npc, s_tame, out string key)) return false;
            Stamp(s_tame, key);
            TameJob job = TameJob.For(npc);
            if (!Begin(npc, s_tame, job)) return false;
            if (TameJob.NeedsGuard(job.Animal))
            {
                // 센 야생(늑대·록스·아스크스빈)은 위험한 일처럼 호위병, 없거나 바쁘면 쉬는 동료(Departures 위험 규칙과 같음)
                npc.Ai.Risky = true;
                WorkerNpc partner = GoWithGuard(npc) ?? GoWithMate(npc);
                Plugin.Log.LogInfo($"{npc.NpcName} 길들이기: 센 {job.Animal.m_name} → 동행 {(partner != null ? partner.NpcName : "없음")}");
            }
            return true;
        }

        /// <summary>
        /// 쉬는 일꾼(낮): 상자 음식이 IdleFoodBelow 밑인데 쉴 때 먹거리가 못 떠났으면(IdleFood false — 사냥감이 위험·약함·멀거나 채집거리 없음)
        /// 낚싯대·둘레 물고기가 무는 미끼가 있고 집 FishJob.FishFar 안에 물고기가 보이면 낚으러 간다. 10분 간격(음식이 넉넉하면 간격을 안 씀). 시작했으면 true.
        /// </summary>
        internal static bool IdleFish(WorkerNpc npc)
        {
            if (!Due(npc, s_fish, out string key)) return false;
            int have = BaseFood(npc.Home);
            if (have >= IdleFoodBelow) return false;
            Stamp(s_fish, key);
            HashSet<string> baits = FishJob.BaitsNear(npc.Home);
            bool can = FishJob.CanFish(npc, baits);
            Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 낚시(상자 음식 {have}): 둘레 물고기 미끼 {baits.Count}종, 낚싯대·미끼 {(can ? "있음" : "없음")}");
            return Begin(npc, s_fish, can ? new FishJob(npc) : null);
        }
    }
}
