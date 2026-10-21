using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 출발 전 판단과 대답 받기 (구상서 8-1장, 설계 docs/valheim/design-depart-craft.txt B장). 판단은 Core Departure,
    /// 여기서는 게임 값을 모아 넣고 결과대로 말하고 움직인다. [게임 시험 안 됨 — 2026-10-21 클라우드 세션에서 작성]
    /// </summary>
    public static class Departures
    {
        /// <summary>[기본안] 체력 미드 하나로 더 버티는 체력(바닐라 작은 체력 미드 회복량쯤).</summary>
        const float MeadHeal = 50f;
        /// <summary>[기본안] 아직 안 먹었지만 음식 상자에 먹을 게 있으면 출발 준비에서 먹을 테니 더해 주는 체력.</summary>
        const float FoodHealthGuess = 50f;
        const int MaxPlaces = 5;

        sealed class Pending
        {
            public DepartureResult Result;
            public ChatCommand Cmd;
            public Speaker Who;
            public double AskedAt;
            public float RecheckTimer;
        }

        static readonly Dictionary<WorkerNpc, Pending> s_pending = new Dictionary<WorkerNpc, Pending>();

        public static bool Awaiting => s_pending.Count > 0;

        /// <summary>
        /// 시험 중(TestRunner.Running)에는 질문하면 바로 "그래도 가"로 대답한 것으로 친다 — 예전 채집·준비 시험이
        /// 쌓인 재고·죽은 기록·밤 때문에 질문에서 멈추지 않게. 출발 전 판단 시험은 이걸 끄고 돈다.
        /// </summary>
        public static bool TestAutoGo = true;

        public static double AskedAt(WorkerNpc npc) => s_pending.TryGetValue(npc, out var p) ? p.AskedAt : -1;

        public static void Forget(WorkerNpc npc) => s_pending.Remove(npc);

        /// <summary>저장용(WorkSaves → ZDO vh_pending). 기다리는 질문이 없으면 null.</summary>
        public static PendingData Snapshot(WorkerNpc npc)
        {
            if (!s_pending.TryGetValue(npc, out var p)) return null;
            DepartureResult r = p.Result;
            var d = new PendingData
            {
                Question = r.Question, Condition = r.Condition, AskedAt = p.AskedAt, Speaker = p.Who.Name,
                Item = p.Cmd.Item, Amount = p.Cmd.Amount, Urgent = p.Cmd.Urgent, Target = p.Cmd.Target,
            };
            if (r.Place != null) { d.HasPlace = true; d.Place = r.Place.Zone; }
            if (r.Alternative != null) { d.HasAlternative = true; d.Alternative = r.Alternative.Zone; }
            foreach (var kv in r.Args) d.Args[kv.Key] = kv.Value;
            return d;
        }

        /// <summary>
        /// 불러온 질문 대기를 되살린다. 장소는 구역만(대답 처리에 구역만 쓴다). 3분이 넘었으면 다음 Tick이 보류 처리.
        /// 말한 사람은 이름만(대답 받을 NPC는 Dispatch.Answerer가 물은 시각·이름으로 고르므로 캐릭터 ZDOID가 필요 없다).
        /// </summary>
        public static void Restore(WorkerNpc npc, PendingData d)
        {
            var r = new DepartureResult { Action = DepartAction.Ask, Question = d.Question, Condition = d.Condition };
            if (d.HasPlace) r.Place = new PlaceOption { Zone = d.Place };
            if (d.HasAlternative) r.Alternative = new PlaceOption { Zone = d.Alternative };
            foreach (var kv in d.Args) r.Args[kv.Key] = kv.Value;
            var cmd = new ChatCommand { Kind = CommandKind.Item, Item = d.Item, Amount = d.Amount, Urgent = d.Urgent, Target = d.Target };
            s_pending[npc] = new Pending { Result = r, Cmd = cmd, Who = new Speaker(d.Speaker ?? "", ZDOID.None, ZDOID.None), AskedAt = d.AskedAt };
        }

        static double Now => ZNet.instance.GetTimeSeconds();

        // ---- 명령 받기 ----

        /// <summary>!물건 수량: 판단하고 결과대로. fromQueue면 대기열에서 꺼낸 것(하던 일 없음).</summary>
        public static void Gather(Speaker who, ChatCommand cmd, WorkerNpc npc, bool fromQueue = false)
        {
            HashSet<ToolKind> ways = ResourceCatalog.WaysFor(cmd.Item);
            if (ways.Count == 0) { Craft(npc, cmd); return; }
            s_pending.Remove(npc);
            DepartureInput input = Input(npc, cmd, ways, fromQueue);
            DepartureResult r = Departure.Judge(input);
            Plugin.Log.LogInfo($"{npc.NpcName} 출발 전 판단 {cmd.Item} {cmd.Amount}: {r.Action} {r.Question} ({r.Reason})");
            var args = Args(npc, r);
            switch (r.Action)
            {
                case DepartAction.Refuse:
                    npc.Say(r.Key, args);
                    return;
                case DepartAction.Queue:
                    npc.Ai.Queue.Add(WorkSaves.ToQueued(npc, WorkSaves.GatherItem(who, cmd, args["item"] + " " + cmd.Amount, ReadyWhen.Always)));
                    npc.Say(r.Key, args);
                    return;
                case DepartAction.Ask:
                    if (r.Interrupts) { npc.Ai.Interrupt(); npc.Ai.GoHome(false); npc.Say("dropJob", args); }
                    s_pending[npc] = new Pending { Result = r, Cmd = cmd, Who = who, AskedAt = Now };
                    args["extra"] = r.ExtraKey != null ? npc.Line(r.ExtraKey, args) : "";
                    npc.Say(r.Key, args);
                    if (TestRunner.Running && TestAutoGo)
                    {
                        Plugin.Log.LogInfo($"{npc.NpcName} 시험 중이라 질문 {r.Question}에 '그래도 가'로 대답");
                        Answer(who, Reply.GoAnyway, npc.NpcName);
                    }
                    return;
            }
            if (r.Interrupts) { npc.Ai.Interrupt(); npc.Say("dropJob", args); }
            Start(npc, cmd, ways, null);
            if (r.Action == DepartAction.GoTogether)
            {
                WorkerNpc partner = IdlePartner(npc);
                if (partner != null) partner.Ai.Follow(npc.GetZDOID(), npc.NpcName);
                args["partner"] = partner != null ? partner.NpcName : "";
            }
            if (r.Key != null) npc.Say(r.Key, args);
            else npc.Say(input.Places.Count > 0 ? "gatherKnown" : "gatherStart", args);
        }

        /// <summary>시험용: 지금 그 명령을 받으면 어떻게 판단할지(말하거나 움직이지 않음).</summary>
        internal static DepartureResult JudgeFor(WorkerNpc npc, ChatCommand cmd)
        {
            HashSet<ToolKind> ways = ResourceCatalog.WaysFor(cmd.Item);
            return ways.Count == 0 ? null : Departure.Judge(Input(npc, cmd, ways, false));
        }

        /// <summary>시험용: 기다리는 질문을 seconds만큼 오래전에 물은 것으로(3분 보류 시험).</summary>
        internal static void AgeQuestion(WorkerNpc npc, double seconds)
        {
            if (s_pending.TryGetValue(npc, out var p)) p.AskedAt -= seconds;
        }

        static void Start(WorkerNpc npc, ChatCommand cmd, HashSet<ToolKind> ways, PlaceOption avoid)
        {
            var job = new GatherJob(npc, cmd.Item, cmd.Amount, ways);
            if (avoid != null) job.AvoidHere(Senses.ZoneCenter(avoid.Zone)); // 대안으로: 원래 곳은 이번 일에서 버림
            npc.Ai.StartJob(job);
        }

        static DepartureInput Input(WorkerNpc npc, ChatCommand cmd, HashSet<ToolKind> ways, bool fromQueue)
        {
            Vector3 home = npc.Home;
            var input = new DepartureInput
            {
                Personality = npc.Personality,
                Item = GatherJob.ItemLabel(cmd.Item),
                Amount = cmd.Amount,
                Urgent = cmd.Urgent,
                CurrentJob = !fromQueue && npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null && !(npc.Ai.Job is GraveJob) ? npc.Ai.Job.Label : null,
                Tool = GatherJob.CanStart(npc, ways) ? ToolState.Ok : ToolState.None,
                Fed = npc.Meals.Count > 0,
                SecondsPerUnit = ways.Count == 1 && ways.Contains(ToolKind.None) ? 1.5f : 4f,
            };
            if (ways.Contains(ToolKind.Axe)) input.Skill = (int)npc.SkillLevels.Level((int)Skills.SkillType.WoodCutting);
            else if (ways.Contains(ToolKind.Pickaxe)) input.Skill = (int)npc.SkillLevels.Level((int)Skills.SkillType.Pickaxes);
            input.FoodInChest = Chests.Any(home, WorkerNpc.HasFood, ChestRole.Food);
            input.HealthMeads = CountMeads(npc.GetInventory()) + Chests.CountAll(home, CountMeads, ChestRole.Food);
            input.Stock = Stock(cmd.Item, home);
            GameObject prefab = ObjectDB.instance.GetItemPrefab(cmd.Item);
            int stack = prefab != null ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize : 50;
            input.CarryPerTrip = Mathf.Max(1, npc.GetInventory().GetEmptySlots()) * Mathf.Max(1, stack);
            input.SecondsToNight = SecondsToNight();

            // 후보 장소: 아는 곳 가까운 순, 각 곳의 위험을 지금 내 상태(+동료)로
            float health = npc.GetMaxHealth() + (!input.Fed && input.FoodInChest ? FoodHealthGuess : 0f);
            float hit = Guard.HitOf(npc);
            System.Func<float, float> taken = Guard.TakenFor(npc);
            WorkerNpc partner = IdlePartner(npc);
            float partnerHit = partner != null ? Guard.HitOf(partner) : 0f;
            PlaceBook book = Brain.Places;
            double now = Now;
            foreach (ZoneMemory m in Brain.Get().ZonesWith(cmd.Item, Senses.ZoneOf(home)))
            {
                if (input.Places.Count >= MaxPlaces) break;
                PlaceRisk alone = book.Risk(m.Zone, now, f => Danger.Ratio(f, health, hit, taken), Guard.Resolve);
                var o = new PlaceOption
                {
                    Zone = m.Zone,
                    Distance = Utils.DistanceXZ(home, Senses.ZoneCenter(m.Zone)),
                    Ratio = alone.Ratio,
                    RatioWithMead = book.Risk(m.Zone, now, f => Danger.Ratio(f, health + MeadHeal, hit, taken), Guard.Resolve).Ratio,
                    DeathWeight = alone.DeathWeight,
                    Boss = alone.Boss,
                    TopFoe = alone.TopFoe,
                };
                if (partner != null) o.RatioTogether = book.Risk(m.Zone, now, f => Danger.Ratio(f, health, hit + partnerHit, taken), Guard.Resolve).Ratio;
                input.Places.Add(o);
            }
            input.Places.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return input;
        }

        static Dictionary<string, string> Args(WorkerNpc npc, DepartureResult r)
        {
            var args = new Dictionary<string, string>(r.Args);
            if (r.Args.TryGetValue("foe", out string kind)) args["foe"] = FoeName(kind);
            if (r.Alternative != null)
            {
                Scout.FromHome(npc.Home, Senses.ZoneCenter(r.Alternative.Zone), out string dir, out string dist);
                args["altDir"] = dir;
                args["altDist"] = dist;
            }
            if (!args.ContainsKey("partner")) args["partner"] = IdlePartner(npc)?.NpcName ?? "";
            if (!args.ContainsKey("extra")) args["extra"] = "";
            return args;
        }

        public static string FoeName(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "";
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(kind) : null;
            Character c = prefab != null ? prefab.GetComponent<Character>() : null;
            return c != null ? Localization.instance.Localize(c.m_name) : kind;
        }

        static WorkerNpc IdlePartner(WorkerNpc npc)
        {
            foreach (var o in WorkerNpc.All)
                if (o != npc && (o.Ai.Current == WorkerAI.Mode.Idle || o.Ai.Current == WorkerAI.Mode.Home) && o.Ai.Queue.Count == 0 && !s_pending.ContainsKey(o))
                    return o;
            return null;
        }

        static int CountMeads(Inventory inv)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems()) if (Gear.IsHealthMead(it)) n += it.m_stack;
            return n;
        }

        static int Stock(string prefab, Vector3 home)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return 0;
            string name = go.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            int n = 0;
            foreach (var c in Chests.NearBase(home, ChestRole.Mats)) n += c.GetInventory().CountItems(name);
            return n;
        }

        /// <summary>밤까지 남은 초(바닐라 EnvMan: 하루의 0.75부터 0.25까지 밤). 이미 밤이면 0.</summary>
        static float SecondsToNight()
        {
            EnvMan env = EnvMan.instance;
            if (env == null) return float.PositiveInfinity;
            if (EnvMan.IsNight()) return 0f;
            return Mathf.Max(0f, (0.75f - env.GetDayFraction()) * env.m_dayLengthSec);
        }

        // ---- 제작 계획 (설계 C, 계획을 말한 뒤 PlanJob으로 실행) ----

        /// <summary>저장에서 불러온 제작 칸을 다시 시작(계획을 현재 재고로 새로 세움).</summary>
        public static void ResumeMake(WorkerNpc npc, WorkItem w)
        {
            if (w.SetNo > 0) MakeSet(npc, w.SetNo, w.Amount);
            else Craft(npc, new ChatCommand { Kind = CommandKind.Item, Item = w.Item, Amount = w.Amount });
        }

        static void Craft(WorkerNpc npc, ChatCommand cmd)
        {
            RecipeBook book = Recipes.Book();
            if (book.RecipesFor(cmd.Item).Count == 0 && book.ProcessesFor(cmd.Item).Count == 0) { npc.Say("notYet"); return; }
            Plan plan = PlanFor(npc, new List<Ingredient> { new Ingredient(cmd.Item, cmd.Amount) });
            var args = new Dictionary<string, string>
            {
                ["item"] = GatherJob.ItemLabel(cmd.Item), ["n"] = cmd.Amount.ToString(), ["minutes"] = Departure.Minutes5(plan.Seconds).ToString(),
            };
            SayPlan(npc, plan, args);
            StartPlan(npc, plan, args["item"], new WorkItem { Kind = WorkKind.Make, Resume = true, Label = args["item"], Item = cmd.Item, Amount = cmd.Amount });
        }

        /// <summary>계획이 끝까지 할 수 있는 것이면 실제로 시작(6번 실행기). 일하는 중이면 시작하지 않음(대기열은 데이터 없는 PlanJob을 못 저장).</summary>
        static void StartPlan(WorkerNpc npc, Plan plan, string label, WorkItem goal)
        {
            if (!Features.RunPlans || plan.Steps.Count == 0 || plan.Partial || plan.TooLong) return;
            var mode = npc.Ai.Current;
            if (mode != WorkerAI.Mode.Idle && mode != WorkerAI.Mode.Home)
            {
                Plugin.Log.LogInfo($"{npc.NpcName} 계획 {label}: 지금 {mode}라 시작 안 함");
                return;
            }
            npc.Ai.StartJob(new PlanJob(npc, plan, label) { Goal = goal });
        }

        /// <summary>!세트 n 수량: 세트 안의 물건마다 수량만큼(6-4). 범위 밖 물건은 빼고 계획.</summary>
        public static void MakeSet(WorkerNpc npc, int number, int amount)
        {
            var targets = Brain.Sets.Targets(number, amount);
            var args = new Dictionary<string, string> { ["no"] = number.ToString(), ["item"] = "세트 " + number + "번", ["n"] = amount.ToString() };
            if (targets.Count == 0) { npc.Say("setNone", args); return; }
            Plan plan = PlanFor(npc, targets);
            if (plan.OutOfScope.Count > 0)
            {
                targets.RemoveAll(t => plan.OutOfScope.Contains(t.Item));
                if (targets.Count == 0) { npc.Say("depart.outOfScope", args); return; }
                plan = PlanFor(npc, targets);
            }
            args["minutes"] = Departure.Minutes5(plan.Seconds).ToString();
            SayPlan(npc, plan, args);
            StartPlan(npc, plan, args["item"], new WorkItem { Kind = WorkKind.Make, Resume = true, Label = args["item"], SetNo = number, Amount = amount });
        }

        static Plan PlanFor(WorkerNpc npc, List<Ingredient> targets)
        {
            RecipeBook book = Recipes.Book();
            Vector3 home = npc.Home;
            var stock = new Dictionary<string, int>();
            foreach (var c in Chests.NearBase(home, ChestRole.Mats))
                foreach (var it in c.GetInventory().GetAllItems())
                {
                    if (it.m_dropPrefab == null) continue;
                    string key = Utils.GetPrefabName(it.m_dropPrefab);
                    stock.TryGetValue(key, out int n);
                    stock[key] = n + it.m_stack;
                }
            ZoneXY homeZone = Senses.ZoneOf(home);
            var planner = new CraftPlanner(book, Recipes.At(home), (item, n) =>
            {
                var zones = Brain.Get().ZonesWith(item, homeZone);
                if (zones.Count == 0) return float.NaN; // 어디 있는지 모름: 찾기 상한으로 셈
                float dist = Utils.DistanceXZ(home, Senses.ZoneCenter(zones[0].Zone));
                return dist * 2f / Departure.WalkSpeed + n * 4f;
            });
            Plan plan = planner.Expand(targets, stock);
            Plugin.Log.LogInfo($"{npc.NpcName} 제작 계획 {targets[0].Item} {targets[0].Amount}(목표 {targets.Count}개): 단계 {plan.Steps.Count}, {plan.Seconds:0}초, 모을 것 [" +
                string.Join(", ", new List<string>(Keys(plan.Gathers)).ToArray()) + $"], 없는 설비 [{string.Join(", ", plan.MissingStations.ToArray())}], " +
                $"범위 밖 [{string.Join(", ", plan.OutOfScope.ToArray())}], 모름 [{string.Join(", ", plan.Unknown.ToArray())}]");
            return plan;
        }

        static void SayPlan(WorkerNpc npc, Plan plan, Dictionary<string, string> args)
        {
            if (plan.OutOfScope.Count > 0) { npc.Say("depart.outOfScope", args); return; }
            if (plan.Steps.Count == 0 && !plan.Partial) { npc.Say("craft.inStock", args); return; }
            if (plan.MissingStations.Count > 0) { args["station"] = Recipes.StationName(plan.MissingStations[0]); npc.Say("craft.missing", args); return; }
            if (plan.Unknown.Count > 0) { args["what"] = GatherJob.ItemLabel(plan.Unknown[0]); npc.Say("craft.unknown", args); return; }
            if (plan.TooLong) { npc.Say("craft.tooLong", args); return; }
            var shortNames = new List<string>();
            foreach (var s in plan.Short) shortNames.Add(GatherJob.ItemLabel(s));
            args["short"] = shortNames.Count == 2 ? shortNames[0] + Josa.For(shortNames[0], "이랑") + " " + shortNames[1] : shortNames.Count == 1 ? shortNames[0] : args["item"];
            npc.Say("craft.plan", args);
        }

        static IEnumerable<string> Keys(Dictionary<string, int> d)
        {
            foreach (var kv in d) yield return kv.Key + " " + kv.Value;
        }

        // ---- 대답 받기 (설계 B-7) ----

        public static Reply ReplyOf(AnswerKind a)
        {
            switch (a)
            {
                case AnswerKind.Yes: return Reply.Yes;
                case AnswerKind.GoAnyway: return Reply.GoAnyway;
                case AnswerKind.Escort: return Reply.Escort;
                case AnswerKind.Elsewhere: return Reply.Elsewhere;
                default: return Reply.No;
            }
        }

        /// <summary>기다리는 NPC가 대답을 받는다. 받은 NPC가 없으면 false(일반 명령으로 처리).</summary>
        public static bool Answer(Speaker who, Reply a, string target)
        {
            var npcs = WorkerNpc.All;
            var infos = new List<NpcInfo>(npcs.Count);
            foreach (var n in npcs) infos.Add(new NpcInfo { Name = n.NpcName, Personality = n.Personality, AskedAt = AskedAt(n) });
            int i = Dispatch.Answerer(infos, target, Now);
            if (i < 0) return false;
            WorkerNpc npc = npcs[i];
            Pending p = s_pending[npc];
            DepartureResult r = p.Result;
            ReplyAction act = Replies.Resolve(r.Question, r.Alternative != null, a);
            Plugin.Log.LogInfo($"{npc.NpcName} 대답 {a} → {act} (질문 {r.Question})");
            if (act == ReplyAction.NotApplicable) return true;
            HashSet<ToolKind> ways = ResourceCatalog.WaysFor(p.Cmd.Item);
            var args = Args(npc, r);
            switch (act)
            {
                case ReplyAction.GoAlternative:
                    s_pending.Remove(npc);
                    Start(npc, p.Cmd, ways, r.Place);
                    npc.Say("answer.alternative", args);
                    break;
                case ReplyAction.GoAlone:
                case ReplyAction.GoGrumble:
                case ReplyAction.GoEscort:
                    s_pending.Remove(npc);
                    Start(npc, p.Cmd, ways, null); // 동행: 대답한 사람이 같이 오면 Guard가 20m 안 플레이어 힘을 합쳐 셈
                    npc.Say(act == ReplyAction.GoEscort ? "answer.escort" : act == ReplyAction.GoGrumble ? "answer.grumble" : "answer.go", args);
                    break;
                case ReplyAction.Recheck:
                    if (!ConditionMet(npc)) { p.AskedAt = Now; npc.Say("answer.conditionMissing", args); break; }
                    s_pending.Remove(npc);
                    Start(npc, p.Cmd, ways, null);
                    npc.Say("answer.conditionMet", args);
                    break;
                case ReplyAction.WaitMorning:
                    s_pending.Remove(npc);
                    ChatCommand cmd = p.Cmd;
                    npc.Ai.Queue.Add(WorkSaves.ToQueued(npc, WorkSaves.GatherItem(p.Who, cmd, args["item"] + " " + cmd.Amount, ReadyWhen.Morning)));
                    npc.Say("answer.morning", args);
                    break;
                case ReplyAction.Hold:
                    s_pending.Remove(npc);
                    npc.Say("answer.hold", args);
                    break;
                case ReplyAction.Cancel:
                    s_pending.Remove(npc);
                    npc.Say("answer.cancel", args);
                    break;
            }
            return true;
        }

        static bool ConditionMet(WorkerNpc npc)
        {
            if (CountMeads(npc.GetInventory()) > 0) return true;
            return Chests.CountAll(npc.Home, CountMeads, ChestRole.Food) > 0;
        }

        /// <summary>매 프레임: 3분 지난 질문은 보류, 조건 질문은 5초마다 상자를 다시 보고 채워졌으면 출발.</summary>
        public static void Tick(float dt)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            TickRefill(dt);
            if (s_pending.Count == 0) return;
            double now = Now;
            foreach (var kv in new List<KeyValuePair<WorkerNpc, Pending>>(s_pending))
            {
                WorkerNpc npc = kv.Key;
                Pending p = kv.Value;
                if (npc == null) { s_pending.Remove(npc); continue; }
                if (p.Result.Question == Question.Condition)
                {
                    p.RecheckTimer -= dt;
                    if (p.RecheckTimer <= 0f)
                    {
                        p.RecheckTimer = 5f;
                        if (ConditionMet(npc))
                        {
                            s_pending.Remove(npc);
                            Start(npc, p.Cmd, ResourceCatalog.WaysFor(p.Cmd.Item), null);
                            npc.Say("answer.conditionMet", Args(npc, p.Result));
                            continue;
                        }
                    }
                }
                if (now - p.AskedAt > Replies.Timeout)
                {
                    s_pending.Remove(npc);
                    npc.Say("answer.timeout");
                }
            }
        }

        // ---- 자동 보충 (구상서 11장) ----

        static float s_refillTimer = (float)RefillBook.CheckEvery;
        static readonly Dictionary<string, float> s_refillSkipUntil = new Dictionary<string, float>();

        /// <summary>
        /// 1분마다: 기준치 아래인 물건을 한가한 NPC 하나가 채우러 간다(직접 명령·묘비 회수가 없을 때만).
        /// 자동 보충은 묻지 않는다 — 출발 전 판단이 질문을 낼 상황(위험·재고·밤 등)이면 그 물건은 10분 쉰다.
        /// 채집으로 나는 물건만(만드는 물건은 ⑥ 실행기 다음).
        /// </summary>
        static void TickRefill(float dt)
        {
            s_refillTimer -= dt;
            if (s_refillTimer > 0f) return;
            s_refillTimer = (float)RefillBook.CheckEvery;
            RefillBook refill = Brain.Refill;
            if (refill.Count == 0 || WorkerNpc.All.Count == 0) return;
            WorkerNpc npc = null;
            foreach (var n in WorkerNpc.All)
                if (n.Ai.Current == WorkerAI.Mode.Idle && n.Ai.Queue.Count == 0 && !s_pending.ContainsKey(n) && !n.Ai.HasChores) { npc = n; break; }
            if (npc == null) return;
            GatherJob job = NextRefillJob(npc, 0);
            if (job == null) return;
            npc.Ai.StartJob(job);
            npc.Say("refillGo", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(job.Item), ["n"] = job.Target.ToString() });
        }

        /// <summary>
        /// 기준치 아래인 물건 중 갈 만한 첫 채집 일(없으면 null). 시작은 부르는 쪽 몫.
        /// maxAmount가 0보다 크면 한 번에 그만큼만(Wait 중 짧게 다녀오기).
        /// </summary>
        internal static GatherJob NextRefillJob(WorkerNpc npc, int maxAmount)
        {
            RefillBook refill = Brain.Refill;
            if (refill.Count == 0) return null;
            Vector3 home = npc.Home;
            foreach (Ingredient due in refill.Due(item => Stock(item, home), Brain.Sets))
            {
                if (s_refillSkipUntil.TryGetValue(due.Item, out float until) && Time.time < until) continue;
                HashSet<ToolKind> ways = ResourceCatalog.WaysFor(due.Item);
                if (ways.Count == 0) continue;
                int amount = maxAmount > 0 ? System.Math.Min(due.Amount, maxAmount) : due.Amount;
                var cmd = new ChatCommand { Kind = CommandKind.Item, Item = due.Item, Amount = amount };
                DepartureInput input = Input(npc, cmd, ways, true);
                input.Stock = 0; // 모자란 만큼만 시켰으니 재고 질문은 하지 않는다
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{npc.NpcName} 자동 보충 {due.Item} {amount}: {r.Action} {r.Question} ({r.Reason})");
                if (r.Action != DepartAction.Go && r.Action != DepartAction.GoTogether) { s_refillSkipUntil[due.Item] = Time.time + 600f; continue; }
                return new GatherJob(npc, due.Item, amount, ways);
            }
            return null;
        }

        // ---- !기억 ----

        public static void TellMemory(WorkerNpc npc)
        {
            float health = npc.GetMaxHealth(), hit = Guard.HitOf(npc);
            System.Func<float, float> taken = Guard.TakenFor(npc);
            double now = Now;
            var top = Brain.Places.Top(5, now, f => Danger.Ratio(f, health, hit, taken), Guard.Resolve);
            if (top.Count == 0) { npc.Say("memoryEmpty"); return; }
            var parts = new List<string>();
            foreach (var m in top)
            {
                Scout.FromHome(npc.Home, Senses.ZoneCenter(m.Zone), out string dir, out string dist);
                int biome = Brain.Get().BiomeOf(m.Zone);
                string where = (biome != 0 ? BiomeNames.Of(biome) + " " : "") + dir + " " + dist + "m";
                var what = new List<string>();
                foreach (var g in m.Group) what.Add(FoeName(g.Kind) + " " + g.Count);
                if (m.Deaths > 0) what.Add("죽음 " + m.Deaths + "번");
                if ((m.Features & PlaceFeature.DeepWater) != 0) what.Add("깊은 물");
                if ((m.Features & PlaceFeature.Pit) != 0) what.Add("구덩이");
                parts.Add(where + (what.Count > 0 ? ": " + string.Join("·", what.ToArray()) : ""));
            }
            npc.Say("memory", new Dictionary<string, string> { ["list"] = string.Join(", ", parts.ToArray()) });
        }
    }
}
