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
    public static partial class Departures
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
        /// 시험 중(TestHook.Running)에는 질문하면 바로 "그래도 가"로 대답한 것으로 친다 — 예전 채집·준비 시험이
        /// 쌓인 재고·죽은 기록·밤 때문에 질문에서 멈추지 않게. 출발 전 판단 시험은 이걸 끄고 돈다.
        /// </summary>
        public static bool TestAutoGo = true;
        /// <summary>시험 중에도 쉴 때 먹거리·같이 캐기를 할지(사용자 11-09 기능). 옛 시험이 흔들리지 않게 기본은 끔 — 그 기능을 보는 시험만 켬.</summary>
        public static bool TestIdleChores;
        static bool ChoresOn => !TestHook.Running || TestIdleChores;

        public static double AskedAt(WorkerNpc npc) => s_pending.TryGetValue(npc, out var p) ? p.AskedAt : -1;

        public static void Forget(WorkerNpc npc) => s_pending.Remove(npc);

        /// <summary>시험용(vh_departtest E112): 질문하고 대답을 기다리는 상태를 바로 만든다.</summary>
        internal static void AskForTest(WorkerNpc npc, ChatCommand cmd, DepartureResult r) =>
            s_pending[npc] = new Pending { Result = r, Cmd = cmd, Who = new Speaker("vhtest", ZDOID.None, ZDOID.None), AskedAt = Now };

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
            if (ways.Count == 0) { Craft(npc, cmd, null, true); return; }
            s_pending.Remove(npc);
            DepartureInput input = Input(npc, cmd, ways, fromQueue);
            DepartureResult r = Departure.Judge(input);
            Plugin.Log.LogInfo($"{npc.NpcName} 출발 전 판단 {cmd.Item} {cmd.Amount}: {r.Action} {r.Question} ({r.Reason})");
            var args = Args(npc, r);
            switch (r.Action)
            {
                case DepartAction.Refuse:
                    // 연장이 없으면 맞는 연장 제작을 대기열에 걸고, 그 뒤에 이 일을 다시 건다(사용자 11-13 "연장 없을 때 맞는 연장 먼저 만들기").
                    // 다시 온 일(fromQueue)은 연장이 여전히 없으면 그냥 거절 — 제작을 되풀이하지 않는다
                    if (r.Key == "noTool" && !fromQueue && GatherJob.QueueToolMakeFor(npc, ways.Contains(ToolKind.Axe) ? ToolKind.Axe : ways.Contains(ToolKind.Pickaxe) ? ToolKind.Pickaxe : ToolKind.None, cmd.Item))
                        npc.Ai.Queue.Add(WorkSaves.ToQueued(npc, WorkSaves.GatherItem(who, cmd, args["item"] + " " + cmd.Amount, ReadyWhen.Always)));
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
                case DepartAction.GearRequest:
                    // 아는 적이 셋이 와도 안 되는 곳: 출발하지 않고 장비를 부탁한다(사용자 11-13 00:15 — 위험 판단은 Core)
                    npc.Say(r.Key, args);
                    return;
                case DepartAction.Ask:
                    if (r.Interrupts) { npc.Ai.Interrupt(); npc.Ai.GoHome(false); npc.Say("dropJob", args); }
                    s_pending[npc] = new Pending { Result = r, Cmd = cmd, Who = who, AskedAt = Now };
                    args["extra"] = r.ExtraKey != null ? npc.Line(r.ExtraKey, args) : "";
                    npc.Say(r.Key, args);
                    if (TestHook.Running && TestAutoGo)
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

        /// <summary>
        /// 질문을 기다리는 사이 다른 명령으로 일을 시작했으면(E112) 그 일을 덮어쓰지 않는다: 대답("그래도 가" 등)은 하던 일을 대기열 맨 앞에 넣고 가고,
        /// 저절로 채워진 조건(answered = false)은 질문의 일을 대기열 뒤에 넣는다. 대기열에 넣었으면 true(Start 안 함).
        /// </summary>
        static bool KeepBusy(WorkerNpc npc, Pending p, bool answered)
        {
            if (npc.Ai.Current != WorkerAI.Mode.Work || npc.Ai.Job == null || npc.Ai.Yields) return false;
            if (answered)
            {
                npc.Ai.Interrupt("질문 대답");
                Plugin.Log.LogInfo($"{npc.NpcName} 질문 대답: 하던 일 {npc.Ai.Queue[0].Label}은 대기열 맨 앞(E112)");
                return false;
            }
            npc.Ai.Queue.Add(WorkSaves.ToQueued(npc, WorkSaves.GatherItem(p.Who, p.Cmd, GatherJob.ItemLabel(p.Cmd.Item) + " " + p.Cmd.Amount, ReadyWhen.Always)));
            Plugin.Log.LogInfo($"{npc.NpcName} 질문 조건 채워짐: 하던 일 {npc.Ai.Job.Label}이 있어 {p.Cmd.Item} {p.Cmd.Amount}은 대기열 뒤(E112)");
            return true;
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
            && (g.Ai.Current == WorkerAI.Mode.Idle || g.Ai.Current == WorkerAI.Mode.Home || g.Ai.Current == WorkerAI.Mode.Work && (g.Ai.Job is GuardReadyJob || GuardChore(g.Ai.Job)));

        /// <summary>호위병이 쉴 때 하는 거점 안 집안일(11-14) — 동행 요청이 오면 놓는다(GuardFree가 쉬는 것으로 봄).</summary>
        internal static bool GuardChore(IWorkerJob j) => j is HoneyJob || j is LivestockJob || j is FarmJob;

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

        /// <summary>
        /// ⑦ 셋이면 될 때(TeamPlan.Needed 3): 호위병을 붙이고(GoWithGuard) 쉬는 동료 일꾼도 따라오게 한다(기다림은 호위병만, 동료는 Follow).
        /// 호위병을 못 붙이면 null(동료도 안 부름).
        /// </summary>
        internal static WorkerNpc GoWithTeam(WorkerNpc npc, WorkerNpc mate)
        {
            WorkerNpc g = GoWithGuard(npc);
            if (g == null || mate == null || mate.IsDead() || !GuardFree(mate)) return g;
            mate.Ai.Follow(npc.GetZDOID(), npc.NpcName);
            npc.Ai.EscortAlso(mate.NpcName);
            Plugin.Log.LogInfo($"{npc.NpcName}: 동료 {mate.NpcName}도 같이 감(셋, {Vector3.Distance(mate.transform.position, npc.transform.position):0}m)");
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
                    KeepBusy(npc, p, true);
                    Start(npc, p.Cmd, ways, r.Place != null ? new List<PlaceOption> { r.Place } : null);
                    npc.Say("answer.alternative", args);
                    break;
                case ReplyAction.GoAlone:
                case ReplyAction.GoGrumble:
                case ReplyAction.GoEscort:
                {
                    s_pending.Remove(npc);
                    KeepBusy(npc, p, true);
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
                    KeepBusy(npc, p, true);
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
                            if (KeepBusy(npc, p, false)) continue;
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
