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
            if (Farming(npc, cmd)) return;
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
            Prep.Ready(npc, out float armor, out ItemDrop.ItemData weapon);
            armor = Mathf.Max(armor, npc.GetBodyArmor());
            float hit = Mathf.Max(Guard.HitWith(npc, weapon), Guard.HitOf(npc));
            System.Func<float, float> taken = Guard.TakenFor(npc, armor);
            WorkerNpc partner = IdlePartner(npc);
            float partnerHit = partner != null ? Guard.HitOf(partner) : 0f;
            PlaceBook book = Brain.Places;
            double now = Now;
            var biomes = new Dictionary<ZoneXY, int>();
            Knowledge known = Brain.Get();
            System.Func<ZoneXY, int> biomeOf = z => biomes.TryGetValue(z, out int b) ? b : biomes[z] = known.BiomeOf(z) != 0 ? known.BiomeOf(z) : Scout.BiomeAt(Senses.ZoneCenter(z));
            var gearBy = new Dictionary<int, PlaceRisk>();
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
                    Resist = NeedsResist(npc, m.Biome),
                };
                if (partner != null) o.RatioTogether = book.Risk(m.Zone, now, f => Danger.Ratio(f, health, hit + partnerHit, taken), Guard.Resolve).Ratio;
                int biome = m.Biome != 0 ? m.Biome : biomeOf(m.Zone);
                if (!gearBy.TryGetValue(biome, out PlaceRisk land))
                    gearBy[biome] = land = book.BiomeRisk(biome, biomeOf, f => Danger.Ratio(f, health, hit, taken), Guard.Resolve, NativeTo);
                o.GearRatio = land.Ratio;
                o.GearFoe = land.TopFoe;
                o.BiomeName = BiomeNames.Of(biome);
                input.Places.Add(o);
            }
            input.Places.Sort((a, b) => a.Distance.CompareTo(b.Distance));
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
        static bool NativeTo(string kind, int biome)
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
            if (FarmFor(npc, plan, args)) return;
            SayPlan(npc, plan, args);
            StartPlan(npc, plan, args["item"], new WorkItem { Kind = WorkKind.Make, Resume = true, Label = args["item"], Item = cmd.Item, Amount = cmd.Amount });
        }

        /// <summary>계획이 끝까지 할 수 있는 것이면 실제로 시작(6번 실행기). 일하는 중이면 시작하지 않음(대기열은 데이터 없는 PlanJob을 못 저장).</summary>
        static void StartPlan(WorkerNpc npc, Plan plan, string label, WorkItem goal)
        {
            if (!Features.RunPlans || plan.Steps.Count == 0 || plan.Partial || plan.TooLong) return;
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
                    if (act == ReplyAction.GoEscort) npc.Ai.EscortBy(who.CharacterId, who.Name); // 기다렸다가 앞장섬(구상서 8-1)
                    npc.Say(act == ReplyAction.GoEscort ? "answer.escort" : act == ReplyAction.GoGrumble ? "answer.grumble" : "answer.go", args);
                    break;
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
                DepartureInput input = Input(npc, p.Cmd, ResourceCatalog.WaysFor(p.Cmd.Item), true);
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
                            Start(npc, p.Cmd, ResourceCatalog.WaysFor(p.Cmd.Item), null);
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

        static void TickRefill(float dt)
        {
            s_refillTimer -= dt;
            if (s_refillTimer > 0f) return;
            s_refillTimer = (float)RefillBook.CheckEvery;
            RefillBook refill = Brain.Refill;
            if ((refill.Count == 0 && Brain.Personal.Count == 0) || WorkerNpc.All.Count == 0) return;
            WorkerNpc npc = null;
            foreach (var n in WorkerNpc.All)
                if (n.Ai.Current == WorkerAI.Mode.Idle && n.Ai.Queue.Count == 0 && !s_pending.ContainsKey(n) && !n.Ai.HasChores) { npc = n; break; }
            if (npc == null) return;
            GatherJob job = NextRefillJob(npc, 0);
            if (job == null) { StartRefillMake(npc); return; }
            npc.Ai.StartJob(job);
            npc.Say("refillGo", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(job.Item), ["n"] = job.Target.ToString() });
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
        static int RefillStock(string prefab, Vector3 home)
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
        internal static GatherJob NextRefillJob(WorkerNpc npc, int maxAmount)
        {
            RefillBook refill = Brain.Refill;
            if (refill.Count == 0 && Brain.Personal.Count == 0) return null;
            Vector3 home = npc.Home;
            foreach (Ingredient due in RefillDue(home))
            {
                if (s_refillSkipUntil.TryGetValue(due.Item, out float until) && Time.time < until) continue;
                HashSet<ToolKind> ways = ResourceCatalog.WaysFor(due.Item);
                if (ways.Count == 0 || RefillBusy(due.Item) || FarmOnly(due.Item)) continue; // 밭 물건은 StartRefillMake → RefillFarm
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
