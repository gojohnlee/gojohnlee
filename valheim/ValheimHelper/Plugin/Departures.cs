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
        /// <summary>시험 중에도 쉴 때 먹거리·같이 캐기를 할지(사용자 11-09 기능). 옛 시험이 흔들리지 않게 기본은 끔 — 그 기능을 보는 시험만 켬.</summary>
        public static bool TestIdleChores;
        static bool ChoresOn => !TestRunner.Running || TestIdleChores;

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
            if (Farming(npc, cmd)) return;
            HashSet<ToolKind> ways = GatherJob.WaysFor(npc, cmd.Item);
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
                case DepartAction.WaitGuard:
                    // 위험한 곳인데 호위병이 바쁘다: 거점으로 돌아와 기다렸다가 호위병이 쉬게 되면 같이 출발(사용자 10-26, 대기열에 저장됨)
                    if (r.Interrupts) { npc.Ai.Interrupt(); npc.Say("dropJob", args); }
                    npc.Ai.Queue.Add(WorkSaves.ToQueued(npc, WorkSaves.GatherItem(who, cmd, args["item"] + " " + cmd.Amount, ReadyWhen.GuardIdle)));
                    if (npc.Ai.Current != WorkerAI.Mode.Work && Utils.DistanceXZ(npc.transform.position, npc.Home) > 5f) npc.Ai.GoHome(false);
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
            Start(npc, cmd, ways, r.Avoid);
            if (r.Action == DepartAction.GoTogether)
            {
                // 남은 곳이 다 위험: 호위병을 데려간다(사용자 결정 2026-10-26), 호위병이 없거나 바쁘면 쉬는 동료 일꾼(사용자 11-13)
                npc.Ai.Risky = true;
                WorkerNpc partner = GoWithGuard(npc) ?? GoWithMate(npc);
                args["partner"] = partner != null ? partner.NpcName : "";
                if (partner == null) r.Key = "depart.danger";
            }
            if (r.Key != null) npc.Say(r.Key, args);
            else npc.Say(input.Places.Count > 0 ? "gatherKnown" : "gatherStart", args);
        }

        /// <summary>
        /// 농사로만 나는 물건(당근 — 들에는 없음)이면 밭에 심는다(구상서 6-3 "당근이 없어요. 밭에 심어 둘게요. 1시간쯤"): 그 작물이 자라는 첫 밭에서
        /// 다 자란 것을 거두고 Farm.Plan만큼 심는 밭일(씨앗 유지 포함). 들에서도 나는 것(보리·씨앗)은 false → 채집. 밭이 없거나 씨앗·거둘 것이 없으면 말만 한다.
        /// </summary>
        static bool Farming(WorkerNpc npc, ChatCommand cmd, Dictionary<string, string> forCraft = null)
        {
            Crop crop = Crops.ForProduce(cmd.Item);
            if (crop == null || !FarmOnly(cmd.Item)) return false;
            var args = new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(cmd.Item), ["n"] = cmd.Amount.ToString() };
            FarmField field = FieldFor(crop);
            if (field == null) { npc.Say("farmNoField", args); return true; }
            FarmJob job = FarmJob.ForWant(npc, field, cmd.Item, cmd.Amount, out FarmPlan plan);
            if (plan.Plant.Count == 0 && FarmJob.Ripe(field).Count == 0) { npc.Say("farmNoSeedFor", args); return true; }
            args["minutes"] = Departure.Minutes5((float)Farm.GrowSeconds(Crops.All, plan)).ToString();
            Plugin.Log.LogInfo($"{npc.NpcName} 농사로 {cmd.Item} {cmd.Amount}: 밭 ({field.X:0},{field.Z:0}) 심기 [{string.Join(", ", plan.Plant.ConvertAll(p => p.Item + " " + p.Amount).ToArray())}], 모자람 {plan.Missing.Count}");
            if (!Commands.StartFarm(npc, field, cmd.Urgent, job)) return true;
            if (forCraft != null && plan.Plant.Count > 0)
            {
                forCraft["what"] = args["item"];
                forCraft["minutes"] = args["minutes"];
                npc.Say("craft.farm", forCraft);
            }
            else npc.Say(plan.Plant.Count > 0 ? "farmPlantFor" : "farmHarvestFor", args);
            return true;
        }

        /// <summary>그 작물이 자라는 첫 밭(없으면 null).</summary>
        static FarmField FieldFor(Crop crop)
        {
            foreach (var f in Brain.Farms.Fields)
                if (Farm.CanGrow(crop, Scout.BiomeAt(new Vector3(f.X, 0f, f.Z)))) return f;
            return null;
        }

        /// <summary>밭에서만 나는 물건(당근 — 들에는 없음)인지.</summary>
        static bool FarmOnly(string item) => Crops.ForProduce(item) != null && !ResourceCatalog.AnySourceBut(item, Crops.IsGrown);

        /// <summary>
        /// 제작 계획 재료가 밭에서만 나는 것이면(당근 수프의 당근, ⑨ 4d) 계획은 시작하지 않고 첫 농사 재료의 모자란 만큼 밭에 심는다
        /// ("당근 수프 1 만들려면 당근이 있어야 해서 밭에 심어 둘게요. N분쯤 뒤에 다시 시켜 주쇼"). 자라는 데 오래 걸려 PlanJob이 기다리지 않는다.
        /// 범위 밖·모름이 있으면 그 말이 먼저(false).
        /// </summary>
        static bool FarmFor(WorkerNpc npc, Plan plan, Dictionary<string, string> args)
        {
            if (plan.OutOfScope.Count > 0 || plan.Unknown.Count > 0) return false;
            PlanStep farm = plan.Steps.Find(s => s.Kind == StepKind.Farm);
            if (farm == null) return false;
            Plugin.Log.LogInfo($"{npc.NpcName} 제작 {args["item"]}: 재료 {farm.Item} {farm.Amount}은 농사 → 밭일");
            return Farming(npc, new ChatCommand { Kind = CommandKind.Item, Item = farm.Item, Amount = farm.Amount }, args);
        }

        /// <summary>시험용: 지금 그 명령을 받으면 어떻게 판단할지(말하거나 움직이지 않음).</summary>
        internal static DepartureResult JudgeFor(WorkerNpc npc, ChatCommand cmd)
        {
            HashSet<ToolKind> ways = GatherJob.WaysFor(npc, cmd.Item);
            return ways.Count == 0 ? null : Departure.Judge(Input(npc, cmd, ways, false));
        }

        /// <summary>시험용: 기다리는 질문을 seconds만큼 오래전에 물은 것으로(3분 보류 시험).</summary>
        internal static void AgeQuestion(WorkerNpc npc, double seconds)
        {
            if (s_pending.TryGetValue(npc, out var p)) p.AskedAt -= seconds;
        }

        static void Start(WorkerNpc npc, ChatCommand cmd, HashSet<ToolKind> ways, List<PlaceOption> avoid)
        {
            var job = new GatherJob(npc, cmd.Item, cmd.Amount, ways);
            if (avoid != null) foreach (var o in avoid) job.AvoidZone(o.Zone); // 덜 위험한 곳으로 가니 위험한 곳은 이번 일에서 안 고름
            npc.Ai.StartJob(job);
        }

        /// <summary>호위병(사용자 결정 2026-10-26 — 일꾼끼리는 같이 가지 않음). 없으면 null.</summary>
        internal static WorkerNpc GuardOf() => WorkerNpc.TheGuard;

        /// <summary>호위병을 지금 데려갈 수 있나: 살아 있고 쉬는 중(거점에서 쉼·집에 감·장비 챙기기). 다른 일꾼을 따라가는 중·묘비 회수 중이면 아님.</summary>
        internal static bool GuardFree(WorkerNpc g) => g != null && !g.IsDead()
            && (g.Ai.Current == WorkerAI.Mode.Idle || g.Ai.Current == WorkerAI.Mode.Home || g.Ai.Current == WorkerAI.Mode.Work && g.Ai.Job is GuardReadyJob);

        /// <summary>대기열 "호위병이 쉴 때"(ReadyWhen.GuardIdle).</summary>
        internal static bool GuardIdle() => GuardFree(GuardOf());

        /// <summary>
        /// 호위병을 지금 일에 붙인다: 호위병은 따라오고, 이 일꾼은 호위병이 8m 안에 올 때까지 기다렸다가 앞장선다(처음 거리만큼 기다림).
        /// StartJob 뒤에 부른다. 붙였으면 호위병, 못 붙였으면(없음·바쁨) null.
        /// </summary>
        internal static WorkerNpc GoWithGuard(WorkerNpc npc)
        {
            WorkerNpc g = GuardOf();
            if (!GuardFree(g) || npc.Ai.Current != WorkerAI.Mode.Work || npc.Ai.Job == null || npc.Ai.EscortName != null) return null;
            float d = Vector3.Distance(g.transform.position, npc.transform.position);
            g.Ai.Follow(npc.GetZDOID(), npc.NpcName);
            npc.Ai.EscortBy(g.GetZDOID(), g.NpcName, Escort.ForMate(d));
            Plugin.Log.LogInfo($"{npc.NpcName}: 호위병 {g.NpcName}과 같이 감({d:0}m)");
            return g;
        }

        /// <summary>동료가 위험한 데 혼자 갈 때 같이 갈 수 있는 거리(m) — 더 멀면 따라가는 쪽이 혼자 위험한 길을 걷고 앞장선 쪽도 오래 기다린다.</summary>
        internal const float MateJoinRange = 100f;

        /// <summary>
        /// 쉬는 일꾼 없게 ①(사용자 11-13 "동료가 위험한 데 가면 같이"): 남은 곳이 다 위험한 일(WorkerAI.Risky)을 동행 없이 하는 동료가 MateJoinRange 안이면
        /// 쉬는 이 일꾼이 호위병처럼 따라간다(Follow + 앞장선 쪽 EscortBy — 기다렸다가 같이, 20m 안 유지). 앞장선 쪽 일이 끝나면 집으로(Follow의 동료 규칙).
        /// 명령이 오면 바로 그 일로(앞장선 쪽은 혼자 감). 같이 간 동료 일꾼, 못 붙였으면 null.
        /// </summary>
        internal static WorkerNpc JoinRisky(WorkerNpc npc)
        {
            if (!ChoresOn || npc == null || npc.IsGuard || !FreeForChores(npc)) return null;
            foreach (var lead in WorkerNpc.All)
            {
                if (lead == npc || lead == null || lead.IsDead() || lead.IsGuard || lead.Ai.Current != WorkerAI.Mode.Work || lead.Ai.Job == null
                    || !lead.Ai.Risky || lead.Ai.EscortName != null || lead.InInterior()) continue;
                float d = Vector3.Distance(lead.transform.position, npc.transform.position);
                if (d > MateJoinRange) continue;
                npc.Ai.Follow(lead.GetZDOID(), lead.NpcName);
                lead.Ai.EscortBy(npc.GetZDOID(), npc.NpcName, Escort.ForMate(d));
                Plugin.Log.LogInfo($"{npc.NpcName}: 동료 {lead.NpcName}이 위험한 데 혼자 감 → 같이 감({d:0}m)");
                npc.Say("mateJoin", new Dictionary<string, string> { ["partner"] = lead.NpcName });
                return lead;
            }
            return null;
        }

        /// <summary>호위병을 못 붙였을 때 위험한 일을 막 시작한 이 일꾼에게 쉬는 동료 일꾼을 붙인다(JoinRisky를 그 동료 쪽에서). 붙였으면 그 동료, 없으면 null.</summary>
        internal static WorkerNpc GoWithMate(WorkerNpc npc)
        {
            if (!npc.Ai.Risky || npc.Ai.EscortName != null) return null;
            foreach (var mate in new List<WorkerNpc>(WorkerNpc.All))
                if (mate != npc && JoinRisky(mate) == npc) return mate;
            return null;
        }

        /// <summary>제작 계획의 채집 단계 출발 판단(재고·밤 질문 없이 위험만 — 직접 명령과 같은 규칙, 외부 검토 10-26). 채집으로 안 나면 null.</summary>
        internal static DepartureResult JudgeStep(WorkerNpc npc, string item, int amount)
        {
            HashSet<ToolKind> ways = GatherJob.WaysFor(npc, item);
            if (ways.Count == 0) return null;
            DepartureInput input = Input(npc, new ChatCommand { Kind = CommandKind.Item, Item = item, Amount = amount }, ways, true);
            input.Stock = 0;
            input.SecondsToNight = float.PositiveInfinity;
            return Departure.Judge(input);
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
                CurrentJob = !fromQueue && npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null && !npc.Ai.Yields ? npc.Ai.Job.Label : null,
                Tool = GatherJob.ToolFor(npc, ways, cmd.Item),
                // 지하묘지 진흙 더미에도 Beacon이 붙어 숨는 것으로 잡히지만, 던전 물건은 들어가서 캔다(회차 164: crypttest가 needFinder로 거절됨)
                NeedFinder = !Dungeons.InsideOnly(cmd.Item) && ResourceCatalog.FromHidden(cmd.Item) && !HiddenOres.HaveFinder(),
                Fed = npc.Meals.Count > 0,
                SecondsPerUnit = ways.Contains(ToolKind.Hunt) ? 40f : ways.Count == 1 && ways.Contains(ToolKind.None) ? 1.5f : 4f,
            };
            if (ways.Contains(ToolKind.Axe)) input.Skill = (int)npc.SkillLevels.Level((int)Skills.SkillType.WoodCutting);
            else if (ways.Contains(ToolKind.Pickaxe)) input.Skill = (int)npc.SkillLevels.Level((int)Skills.SkillType.Pickaxes);
            input.FoodInChest = Chests.Any(home, WorkerNpc.HasFood, ChestRole.Food);
            input.HealthMeads = CountMeads(npc.GetInventory()) + Chests.CountAll(home, CountMeads, ChestRole.Food);
            foreach (string k in Resist.Kinds) if (HasResistMead(npc, k)) input.ResistMeads.Add(k);
            input.Stock = Stock(cmd.Item, home);
            GameObject prefab = ObjectDB.instance.GetItemPrefab(cmd.Item);
            int stack = prefab != null ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize : 50;
            input.CarryPerTrip = Mathf.Max(1, npc.GetInventory().GetEmptySlots()) * Mathf.Max(1, stack);
            input.SecondsToNight = SecondsToNight();

            // 후보 장소: 아는 곳 가까운 순, 각 곳의 위험을 지금 내 상태(+동료)로 — 장비는 출발 준비에서 챙길 상자의 더 좋은 것까지(⑫ 다)
            float health = npc.GetMaxHealth() + (!input.Fed && input.FoodInChest ? FoodHealthGuess : 0f);
            Prep.Ready(npc, out float armor, out ItemDrop.ItemData weapon, out ItemDrop.ItemData shieldItem);
            armor = Mathf.Max(armor, npc.GetBodyArmor());
            float hit = Mathf.Max(Guard.HitWith(npc, weapon), Guard.HitOf(npc));
            Shield? shield = npc.BlockStats(shieldItem) ?? npc.BlockStats(); // 막기는 그 무리 적 수에 따라(Block.MaxFoes)
            System.Func<IList<Foe>, System.Func<float, float>> taken = f => Guard.TakenFor(npc, armor, shield, f.Count);
            System.Func<GroupFoe, Foe> resolve = Guard.ResolveFor(npc, Prep.Arsenal(npc)); // 적 약점·저항을 내 무기로(전투 보강 나)
            PlaceBook book = Brain.Places;
            double now = Now;
            var biomes = new Dictionary<ZoneXY, int>();
            Knowledge known = Brain.Get();
            System.Func<ZoneXY, int> biomeOf = z => biomes.TryGetValue(z, out int b) ? b : biomes[z] = known.BiomeOf(z) != 0 ? known.BiomeOf(z) : Scout.BiomeAt(Senses.ZoneCenter(z));
            var gearBy = new Dictionary<int, PlaceRisk>();
            int homeBiome = biomeOf(Senses.ZoneOf(home));
            var zones = Brain.Get().ZonesWith(cmd.Item, Senses.ZoneOf(home));
            if (Dungeons.InsideOnly(cmd.Item))
            {
                // 던전 안 물건(검은 핵)은 구역 지식에 안 쌓인다 → 갈 수 있는 던전 입구 구역도 후보로(⑬ 바-5: 시커에게서 도망친 광산에 혼자 다시 들어감)
                foreach (var d in Dungeons.All(cmd.Item))
                {
                    ZoneXY z = Senses.ZoneOf(d.Pos);
                    if (d.Left > 0 && (d.Open || d.CanOpen) && !zones.Exists(x => x.Zone.Equals(z))) zones.Add(new ZoneMemory { Zone = z });
                }
                zones.Sort((a, b) => Utils.DistanceXZ(home, Senses.ZoneCenter(a.Zone)).CompareTo(Utils.DistanceXZ(home, Senses.ZoneCenter(b.Zone))));
            }
            foreach (ZoneMemory m in zones)
            {
                if (input.Places.Count >= MaxPlaces) break;
                PlaceRisk alone = book.Risk(m.Zone, now, f => Danger.Ratio(f, health, hit, taken(f)), resolve);
                var o = new PlaceOption
                {
                    Zone = m.Zone,
                    Distance = Utils.DistanceXZ(home, Senses.ZoneCenter(m.Zone)),
                    Ratio = alone.Ratio,
                    RatioWithMead = book.Risk(m.Zone, now, f => Danger.Ratio(f, health + MeadHeal, hit, taken(f)), resolve).Ratio,
                    DeathWeight = alone.DeathWeight,
                    Boss = alone.Boss,
                    TopFoe = alone.TopFoe,
                    Resist = NeedsResist(npc, m.Biome),
                };
                int biome = m.Biome != 0 ? m.Biome : biomeOf(m.Zone);
                // 거점 바이옴(처음엔 초원)은 장비 검사 안 함: 줄 수 있는 더 좋은 장비가 없어 초반에 일을 못 했다(사용자 결정 2026-10-25)
                if (biome == homeBiome) gearBy[biome] = new PlaceRisk();
                if (!gearBy.TryGetValue(biome, out PlaceRisk land))
                    gearBy[biome] = land = book.BiomeRisk(biome, biomeOf, f => Danger.Ratio(f, health, hit, taken(f)), resolve, NativeTo);
                o.GearRatio = land.Ratio;
                o.GearFoe = land.TopFoe;
                o.BiomeName = BiomeNames.Of(biome);
                o.InBase = BaseArea.Contains(o.Distance);
                input.Places.Add(o);
            }
            input.Places.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            WorkerNpc guard = GuardOf();
            input.Guard = guard?.NpcName;
            input.GuardBusy = guard != null && !GuardFree(guard);
            if (cmd.Item == Dungeons.ScrapItem && input.Places.Count == 0) // 거절 대사가 지하묘지 말이라 고철만(서리 동굴은 못 찾으면 일이 찾기·탐험으로)
            {
                input.NoCrypt = !Dungeons.TryChoose(home, out _, out bool minedOut);
                input.CryptsMinedOut = minedOut;
            }
            return input;
        }

        static Dictionary<string, int> s_native;

        /// <summary>
        /// 그 적이 그 바이옴에 나는가(게임 생성 표 ZoneSystem.m_zoneCtrlPrefab의 SpawnSystem.m_spawnLists — 켜진 것만).
        /// 출발 판단의 "이 장비론 평원 무리"가 떠돌다 넘어온 적(초원의 트롤)으로 그 땅 전체를 막지 않게. 표를 못 읽으면 다 셈.
        /// </summary>
        internal static bool NativeTo(string kind, int biome)
        {
            if (s_native == null)
            {
                var map = new Dictionary<string, int>();
                SpawnSystem ss = ZoneSystem.instance != null && ZoneSystem.instance.m_zoneCtrlPrefab != null ? ZoneSystem.instance.m_zoneCtrlPrefab.GetComponentInChildren<SpawnSystem>(true) : null;
                if (ss != null)
                    foreach (SpawnSystemList list in ss.m_spawnLists)
                        foreach (SpawnSystem.SpawnData d in list.m_spawners)
                            if (d.m_enabled && d.m_prefab != null)
                                map[d.m_prefab.name] = (map.TryGetValue(d.m_prefab.name, out int m) ? m : 0) | (int)d.m_biome;
                if (map.Count == 0) return true; // 아직 못 읽음 — 다음에 다시
                s_native = map;
                Plugin.Log.LogInfo($"생성 표: 적 {map.Count}종 바이옴 읽음");
            }
            return s_native.TryGetValue(kind, out int mask) && (mask & biome) != 0;
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
            if (!args.ContainsKey("partner")) args["partner"] = GuardOf()?.NpcName ?? "";
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

        /// <summary>그 저항 미드를 짐이나 음식·미드 상자에 가졌는지(Prep이 거기서 챙김).</summary>
        static bool HasResistMead(WorkerNpc npc, string kind) =>
            npc.HasResistMead(kind) || Chests.Any(npc.Home, inv => inv.GetAllItems().Exists(it => Gear.ResistOf(it) == kind), ChestRole.Food);

        /// <summary>그 구역 바이옴(기억, 게임 Heightmap.Biome 비트)에서 막아야 할 피해. 모르거나 장비로 이미 막으면 null.</summary>
        static string NeedsResist(WorkerNpc npc, int biome)
        {
            for (int bit = 1; bit <= biome && bit > 0; bit <<= 1)
            {
                if ((biome & bit) == 0) continue;
                string k = Resist.ForBiome(((Heightmap.Biome)bit).ToString());
                if (k != null && !npc.Resists(k)) return k;
            }
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
            float f = DayFraction(env);
            if (f <= 0.25f || f >= 0.75f) return 0f;
            return Mathf.Max(0f, (0.75f - f) * env.m_dayLengthSec);
        }

        /// <summary>
        /// 지금 하루 비율(바닐라 EnvMan.RescaleDayFraction과 같은 꼴). GetDayFraction은 매 틱 1%씩 따라가는 부드러운 값이라 잠자기 등으로
        /// 시각이 뛰면 몇 초 동안 옛 저녁 값을 준다(회차 90 departtest: 아침에 꺼낸 일을 "다녀오면 깜깜" 하고 다시 물음) → 서버 시각에서 바로 센다.
        /// 시험의 시각 고정(m_debugTimeOfDay)은 그 값(m_debugTime)을 바로 쓴다.
        /// </summary>
        static float DayFraction(EnvMan env)
        {
            if (env.m_debugTimeOfDay) return env.m_debugTime; // GetDayFraction은 다음 FixedUpdate에야 바뀐다(프레임이 빠르면 한 프레임 늦음)
            if (ZNet.instance == null || env.m_dayLengthSec <= 0) return env.GetDayFraction();
            double len = env.m_dayLengthSec;
            float raw = (float)(ZNet.instance.GetTimeSeconds() % len / len);
            if (raw >= 0.15f && raw <= 0.85f) return 0.25f + (raw - 0.15f) / 0.7f * 0.5f;
            return raw < 0.5f ? raw / 0.15f * 0.25f : 0.75f + (raw - 0.85f) / 0.15f * 0.25f;
        }

        // ---- 제작 계획 (설계 C, 계획을 말한 뒤 PlanJob으로 실행) ----

        /// <summary>시험용(vh_restartcraft): 이번 실행에서 저장한 계획으로 이어 간 수, 계획을 다시 세운 수.</summary>
        internal static int ResumedPlans, ReplannedPlans;

        /// <summary>
        /// 저장에서 불러온 제작 칸을 다시 시작. 저장한 계획(w.Plan)이 있으면 그 단계·상태·진행으로 이어 간다(외부 검토 + 사용자 승인 10-26):
        /// 설비에 넣은 것·나오는 중인 것은 이 작업의 남은 기다림·꺼내기로 남아 재고로 다시 세거나 다른 작업과 나눠 세지 않는다.
        /// 없거나(옛 저장) 깨졌으면 현재 재고로 새로 세운다.
        /// </summary>
        public static void ResumeMake(WorkerNpc npc, WorkItem w)
        {
            PlanRunner saved = Features.RunPlans ? PlanSave.Load(w.Plan, s => MachineStep.AlreadyCooking(npc.Home, s)) : null;
            if (saved != null)
            {
                ResumedPlans++;
                Plugin.Log.LogInfo($"{npc.NpcName} 계획 이어 함(저장): {w.Label} — 끝 {saved.Count(StepState.Done)}/{saved.Plan.Steps.Count}, 돌던 기다림 {saved.Count(StepState.Running)}");
                npc.Ai.StartJob(new PlanJob(npc, saved, w.Label) { Goal = w });
                return;
            }
            ReplannedPlans++;
            if (!string.IsNullOrEmpty(w.Plan)) Plugin.Log.LogWarning($"{npc.NpcName} 저장된 계획을 못 읽음 — {w.Label} 다시 세움");
            else Plugin.Log.LogInfo($"{npc.NpcName} 저장된 계획 없음 — {w.Label} 다시 세움");
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
            if (FarmFor(npc, plan, args)) return;
            SayPlan(npc, plan, args);
            StartPlan(npc, plan, args["item"], new WorkItem { Kind = WorkKind.Make, Resume = true, Label = args["item"], Item = cmd.Item, Amount = cmd.Amount });
        }

        /// <summary>
        /// 계획이 끝까지 할 수 있거나 모자란 게 설비뿐이면(StationsOnly — 재료는 모아 두고 끝에 지어 달라고 함, 10-26) 실제로 시작(6번 실행기).
        /// 범위 밖·모름이 끼면 시작하지 않음. 일하는 중이면 시작하지 않음(대기열은 데이터 없는 PlanJob을 못 저장).
        /// </summary>
        static void StartPlan(WorkerNpc npc, Plan plan, string label, WorkItem goal)
        {
            if (!Features.RunPlans || plan.Steps.Count == 0 || (plan.Partial && !plan.StationsOnly) || plan.TooLong) return;
            var mode = npc.Ai.Current;
            // Work인데 하던 일이 없으면 대기열에서 꺼내는 중(앞 일이 막 끝남, WorkerAI.NextQueued) — 쉬는 것과 같다(회차 128: 죽은 뒤 이어 할 계획이 여기서 버려짐)
            bool busy = mode == WorkerAI.Mode.Work ? npc.Ai.Job != null && !(npc.Ai.Job is TidyJob) : mode != WorkerAI.Mode.Idle && mode != WorkerAI.Mode.Home;
            if (busy)
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
            if (FarmFor(npc, plan, args)) return;
            SayPlan(npc, plan, args);
            StartPlan(npc, plan, args["item"], new WorkItem { Kind = WorkKind.Make, Resume = true, Label = args["item"], SetNo = number, Amount = amount });
        }

        static Plan PlanFor(WorkerNpc npc, List<Ingredient> targets)
        {
            RecipeBook book = Recipes.Book();
            Vector3 home = npc.Home;
            var stock = new Dictionary<string, int>();
            foreach (var c in Chests.NearBase(home, ChestRole.Ingredients))
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
            if (plan.MissingStations.Count > 0)
            {
                args["station"] = Recipes.StationName(plan.MissingStations[0]);
                // 설비만 없고 모을 재료가 있으면 모아 둔다(StartPlan이 시작) — 끝나면 PlanJob이 craft.missingDone
                npc.Say(plan.StationsOnly && plan.Steps.Count > 0 && !plan.TooLong && Features.RunPlans ? "craft.missingGather" : "craft.missing", args);
                return;
            }
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
            HashSet<ToolKind> ways = GatherJob.WaysFor(npc, p.Cmd.Item);
            var args = Args(npc, r);
            switch (act)
            {
                case ReplyAction.GoAlternative: // 옛 저장의 대안 질문(이제 묻지 않음): Place가 원래 곳
                    s_pending.Remove(npc);
                    Start(npc, p.Cmd, ways, r.Place != null ? new List<PlaceOption> { r.Place } : null);
                    npc.Say("answer.alternative", args);
                    break;
                case ReplyAction.GoAlone:
                case ReplyAction.GoGrumble:
                case ReplyAction.GoEscort:
                {
                    s_pending.Remove(npc);
                    Start(npc, p.Cmd, ways, null);
                    // "같이 가자": 호위병이 와서 같이 간다(사용자 결정 2026-10-26 — 일꾼끼리는 같이 가지 않음)
                    WorkerNpc mate = act == ReplyAction.GoEscort ? GoWithGuard(npc) : null;
                    if (mate != null) args["partner"] = mate.NpcName;
                    npc.Say(mate != null ? "answer.escort" : act == ReplyAction.GoGrumble ? "answer.grumble" : "answer.go", args);
                    break;
                }
                case ReplyAction.Recheck:
                    if (!ConditionMet(npc, p)) { p.AskedAt = Now; npc.Say(r.Condition == "gear" ? "answer.gearMissing" : "answer.conditionMissing", args); break; }
                    s_pending.Remove(npc);
                    Start(npc, p.Cmd, ways, null);
                    npc.Say(r.Condition == "gear" ? "answer.gearMet" : "answer.conditionMet", args);
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

        static bool ConditionMet(WorkerNpc npc, Pending p)
        {
            string condition = p.Result.Condition;
            if (condition == "gear") // 상자의 더 좋은 장비까지 넣어 그 곳(없으면 가장 가까운 곳)을 다시 셈
            {
                DepartureInput input = Input(npc, p.Cmd, GatherJob.WaysFor(npc, p.Cmd.Item), true);
                PlaceOption at = p.Result.Place != null ? input.Places.Find(o => o.Zone.Equals(p.Result.Place.Zone)) : null;
                if (at == null && input.Places.Count > 0) at = input.Places[0];
                return at == null || !at.GearShort;
            }
            string kind = Resist.KindOf(condition);
            if (kind != null) return HasResistMead(npc, kind);
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
                        if (ConditionMet(npc, p))
                        {
                            s_pending.Remove(npc);
                            Start(npc, p.Cmd, GatherJob.WaysFor(npc, p.Cmd.Item), null);
                            npc.Say(p.Result.Condition == "gear" ? "answer.gearMet" : "answer.conditionMet", Args(npc, p.Result));
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
            foreach (var npc in new List<WorkerNpc>(WorkerNpc.All))
            {
                if (!FreeForChores(npc)) continue;
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

        // ---- 쉴 때 먹거리 (사용자 11-09: "당장 음식 없으면 베리라도") ----

        /// <summary>거점 음식 상자의 먹을 것 합이 이보다 적으면 쉬는 일꾼이 따 온다.</summary>
        internal const int IdleFoodBelow = 10;
        /// <summary>한 번에 따 올 수.</summary>
        internal const int IdleFoodAmount = 10;
        static int BaseFood(Vector3 home)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(home, ChestRole.Food))
                foreach (var it in c.GetInventory().GetAllItems())
                    if (Gear.IsFood(it)) n += it.m_stack;
            return n;
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
        internal static bool IdleFarm(WorkerNpc npc)
        {
            if (EnvMan.IsNight() || Brain.Farms.Fields.Count == 0) return false;
            foreach (var field in new List<FarmField>(Brain.Farms.Fields))
            {
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

        // ---- 쉴 때 꿀 거두기 (사용자 11-09 요청 가 "벌통 꿀") ----

        /// <summary>일꾼 집마다 다음에 볼 때(Time.time). 밭과 같게 10분 간격.</summary>
        static readonly Dictionary<string, float> s_honeyNext = new Dictionary<string, float>();

        internal static void TestHoneyNow() => s_honeyNext.Clear();

        /// <summary>쉬는 일꾼(낮): 거점 안 벌통에 꿀이 HoneyJob.MinHoney 이상이면 거두러 간다. 다른 일꾼이 꿀 거두기 중이면 건너뜀. 시작했으면 true.</summary>
        internal static bool IdleHoney(WorkerNpc npc)
        {
            if (EnvMan.IsNight()) return false;
            string key = $"{npc.Home.x:0},{npc.Home.z:0}";
            if (s_honeyNext.TryGetValue(key, out float next) && Time.time < next) return false;
            foreach (var n in WorkerNpc.All) if (n.Ai.Job is HoneyJob) return false;
            s_honeyNext[key] = Time.time + IdleFarmEvery;
            List<Beehive> hives = HoneyJob.Ready(npc.Home);
            Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 벌통: 꿀 찬 것 {hives.Count}");
            if (hives.Count == 0) return false;
            npc.Ai.StartJob(new HoneyJob(npc, hives));
            npc.Say("idleHoney", new Dictionary<string, string>());
            return true;
        }

        // ---- 쉴 때 우리 먹이 (사용자 11-09 요청 다-2) ----

        /// <summary>일꾼 집마다 다음에 볼 때(Time.time). 배고파지는 600초보다 짧게 1분 간격.</summary>
        static readonly Dictionary<string, float> s_feedNext = new Dictionary<string, float>();
        const float IdleFeedEvery = 60f;

        internal static void TestFeedNow() => s_feedNext.Clear();

        /// <summary>쉬는 일꾼(낮): "가축" 상자 둘레의 배고픈 길들인 가축에게 상자 먹이를 떨궈 주러 간다(LivestockJob). 다른 일꾼이 먹이 주는 중이면 건너뜀. 시작했으면 true.</summary>
        internal static bool IdleFeed(WorkerNpc npc)
        {
            if (EnvMan.IsNight()) return false;
            string key = $"{npc.Home.x:0},{npc.Home.z:0}";
            if (s_feedNext.TryGetValue(key, out float next) && Time.time < next) return false;
            foreach (var n in WorkerNpc.All) if (n.Ai.Job is LivestockJob) return false;
            s_feedNext[key] = Time.time + IdleFeedEvery;
            LivestockJob job = LivestockJob.For(npc);
            if (job == null) return false;
            npc.Ai.StartJob(job);
            npc.Say("idleFeed", new Dictionary<string, string>());
            return true;
        }

        // ---- 쉴 때 우리 솎기 (사용자 11-09 요청 다-4) ----

        static readonly Dictionary<string, float> s_cullNext = new Dictionary<string, float>();

        internal static void TestCullNow() => s_cullNext.Clear();

        /// <summary>쉬는 일꾼(낮): "가축" 상자 둘레 길들인 것이 CullJob.PenMax를 넘으면 다 자란 것을 잡아 고기를 거점 상자로(CullJob). 1분 간격. 시작했으면 true.</summary>
        internal static bool IdleCull(WorkerNpc npc)
        {
            if (EnvMan.IsNight()) return false;
            string key = $"{npc.Home.x:0},{npc.Home.z:0}";
            if (s_cullNext.TryGetValue(key, out float next) && Time.time < next) return false;
            foreach (var n in WorkerNpc.All) if (n.Ai.Job is CullJob) return false;
            s_cullNext[key] = Time.time + IdleFeedEvery;
            CullJob job = CullJob.For(npc);
            if (job == null) return false;
            npc.Ai.StartJob(job);
            npc.Say("idleCull", new Dictionary<string, string>());
            return true;
        }

        // ---- 쉴 때 야생 가축 길들이기 (사용자 11-09 요청 다-3) ----

        /// <summary>일꾼 집마다 다음에 볼 때(Time.time). 우리 먹이와 같게 1분 간격(배부름 600초 — 배고파지면 다시 줌).</summary>
        static readonly Dictionary<string, float> s_tameNext = new Dictionary<string, float>();

        internal static void TestTameNow() => s_tameNext.Clear();

        /// <summary>쉬는 일꾼(낮): 집 TameJob.TameFar 안 배고픈 야생 짐승에게 "가축" 상자 먹이를 떨궈 길들이러 간다. 다른 일꾼이 길들이는 중이면 건너뜀. 시작했으면 true.</summary>
        internal static bool IdleTame(WorkerNpc npc)
        {
            if (EnvMan.IsNight()) return false;
            string key = $"{npc.Home.x:0},{npc.Home.z:0}";
            if (s_tameNext.TryGetValue(key, out float next) && Time.time < next) return false;
            foreach (var n in WorkerNpc.All) if (n.Ai.Job is TameJob) return false;
            s_tameNext[key] = Time.time + IdleFeedEvery;
            TameJob job = TameJob.For(npc);
            if (job == null) return false;
            npc.Ai.StartJob(job);
            npc.Say("idleTame", new Dictionary<string, string>());
            return true;
        }

        // ---- 쉴 때 낚시 (사용자 11-09 요청 나 "사냥이 위험할 때 대신") ----

        /// <summary>일꾼 집마다 다음에 볼 때(Time.time). 10분 간격.</summary>
        static readonly Dictionary<string, float> s_fishNext = new Dictionary<string, float>();

        internal static void TestFishNow() => s_fishNext.Clear();

        /// <summary>
        /// 쉬는 일꾼(낮): 상자 음식이 IdleFoodBelow 밑인데 쉴 때 먹거리가 못 떠났으면(IdleFood false — 사냥감이 위험·약함·멀거나 채집거리 없음)
        /// 낚싯대·둘레 물고기가 무는 미끼가 있고 집 FishJob.FishFar 안에 물고기가 보이면 낚으러 간다. 다른 일꾼이 낚시 중이면 건너뜀. 시작했으면 true.
        /// </summary>
        internal static bool IdleFish(WorkerNpc npc)
        {
            if (EnvMan.IsNight()) return false;
            string key = $"{npc.Home.x:0},{npc.Home.z:0}";
            if (s_fishNext.TryGetValue(key, out float next) && Time.time < next) return false;
            foreach (var n in WorkerNpc.All) if (n.Ai.Job is FishJob) return false;
            int have = BaseFood(npc.Home);
            if (have >= IdleFoodBelow) return false;
            s_fishNext[key] = Time.time + IdleFarmEvery;
            HashSet<string> baits = FishJob.BaitsNear(npc.Home);
            bool can = FishJob.CanFish(npc, baits);
            Plugin.Log.LogInfo($"{npc.NpcName} 쉴 때 낚시(상자 음식 {have}): 둘레 물고기 미끼 {baits.Count}종, 낚싯대·미끼 {(can ? "있음" : "없음")}");
            if (!can) return false;
            npc.Ai.StartJob(new FishJob(npc));
            npc.Say("idleFish", new Dictionary<string, string>());
            return true;
        }

        // ---- 같이 캐기 (사용자 11-09: "나무 캐 와" 하면 한 명만 가고 하나는 멍하니 → 둘이 출발하면 합쳐서 목표면 복귀) ----

        /// <summary>
        /// 이름 없이 시킨 채집: 그 일을 맡은 일꾼(lead)이 바로 떠났으면 그 순간 쉬던 다른 일꾼도 같은 물건을 캐러 간다(각자 따로 다님 — 일꾼끼리 같이 가지는 않음, 9절).
        /// 목표는 나누지 않고 같이 센다(GatherJob.Team): 둘이 넣은 것 + 들고 있는 것이 목표면 둘 다 복귀. 나중에 쉬게 된 일꾼은 JoinGather가 남은 몫이 클 때만 끼움.
        /// 동료는 묻지 않고 출발 판단이 Go일 때만 간다.
        /// </summary>
        internal static void ShareGather(ChatCommand cmd, WorkerNpc lead)
        {
            if (!ChoresOn || !(lead.Ai.Job is GatherJob g) || g.Item != cmd.Item || g.FromRefill || g.Delivered > 0 || g.Target < 2) return;
            foreach (var n in new List<WorkerNpc>(WorkerNpc.All))
            {
                if (n == lead || !(FreeForChores(n) || n.Ai.Current == WorkerAI.Mode.Work && n.Ai.Yields && n.Ai.Queue.Count == 0 && !s_pending.ContainsKey(n))) continue; // 스스로 하던 일은 접고 같이 감
                HashSet<ToolKind> ways = GatherJob.WaysFor(n, cmd.Item);
                if (ways.Count == 0) continue;
                var c = new ChatCommand { Kind = CommandKind.Item, Item = cmd.Item, Amount = g.Target };
                DepartureInput input = Input(n, c, ways, true);
                input.Stock = 0;
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{n.NpcName} 같이 캐기 {cmd.Item} {g.Target} ({lead.NpcName}와 합쳐서): {r.Action} {r.Question} ({r.Reason})");
                if (r.Action != DepartAction.Go) continue;
                var job = new GatherJob(n, cmd.Item, g.Target, ways);
                foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
                if (g.Team == null) g.Team = new List<GatherJob> { g };
                g.Team.Add(job);
                job.Team = g.Team;
                n.Ai.StartJob(job);
                n.Say("gatherHelp", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(cmd.Item), ["n"] = g.Target.ToString(), ["partner"] = lead.NpcName });
            }
        }


        /// <summary>
        /// 쉬는 일꾼 없게 ①(사용자 11-13): 나중에 쉬게 된 일꾼도 동료가 명령받아 캐는 물건의 남은 몫이 크면(Core TeamJoin.Worth) 같은 팀으로 낀다.
        /// 자동 보충·갚기·쉴 때 일은 안 낌(그건 각자 맡음). 출발 판단이 Go일 때만.
        /// </summary>
        internal static bool JoinGather(WorkerNpc npc)
        {
            if (!ChoresOn) return false;
            foreach (var lead in WorkerNpc.All)
            {
                if (lead == npc || lead == null || lead.IsDead() || !(lead.Ai.Job is GatherJob g) || g.FromRefill || g.Repay || g.Target < 2) continue;
                int team = g.Team?.Count ?? 1;
                if (g.Team != null && g.Team.Exists(o => o.Npc == npc)) continue;
                if (!TeamJoin.Worth(g.Remaining() - g.Carried(), g.Target, team)) continue;
                HashSet<ToolKind> ways = GatherJob.WaysFor(npc, g.Item);
                if (ways.Count == 0) continue;
                var c = new ChatCommand { Kind = CommandKind.Item, Item = g.Item, Amount = g.Target };
                DepartureInput input = Input(npc, c, ways, true);
                input.Stock = 0;
                DepartureResult r = Departure.Judge(input);
                Plugin.Log.LogInfo($"{npc.NpcName} 늦게 같이 캐기 {g.Item} 남은 {g.Remaining() - g.Carried()}/{g.Target} ({lead.NpcName} 팀 {team}): {r.Action} ({r.Reason})");
                if (r.Action != DepartAction.Go) continue;
                var job = new GatherJob(npc, g.Item, g.Target, ways);
                foreach (var o in r.Avoid) job.AvoidZone(o.Zone);
                if (g.Team == null) g.Team = new List<GatherJob> { g };
                g.Team.Add(job);
                job.Team = g.Team;
                npc.Ai.StartJob(job);
                npc.Say("gatherHelp", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(g.Item), ["n"] = g.Target.ToString(), ["partner"] = lead.NpcName });
                return true;
            }
            return false;
        }
        /// <summary>
        /// 쉬는 일꾼 없게 ②(사용자 11-13, decisions 7절 "Next 소비처 기본안"): 지원 대기열 맨 앞 요청을 올린 기다리는 호위 곁으로 손 빈 일꾼의 호위가 달려간다(Summon).
        /// 그 호위를 못 찾거나 바쁘면 미룸(Postpone). 도착 확인은 Guard.CheckHelpArrived.
        /// </summary>
        internal static bool JoinHelp(WorkerNpc npc)
        {
            if (!ChoresOn || npc.Ai.Guard == null) return false;
            HelpQueue.Ask ask = Brain.Help.Next();
            if (ask == null) return false;
            WorkerNpc waiting = WorkerNpc.All.Find(w => w != null && w != npc && !w.IsDead() && w.Ai.Guard != null && w.Ai.Guard.HelpTask == ask.Task);
            if (waiting == null || !npc.Ai.Guard.Summon(waiting))
            {
                Brain.Help.Postpone(ask.Task);
                return false;
            }
            Plugin.Log.LogInfo($"{npc.NpcName} 지원 요청 '{ask.Task}' → {waiting.NpcName} 쪽으로 달려감(대기열 맨 앞)");
            return true;
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
