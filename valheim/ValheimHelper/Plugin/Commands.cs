using System.Collections.Generic;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>호스트에서 채팅 명령을 NPC에게 나눠 준다 (구상서 5장, 3-2장). ① 단계: 따라가기·멈춤·상태만.</summary>
    public static class Commands
    {
        static ChatParser s_parser;
        static string s_namesKey;

        static ChatParser Parser()
        {
            // 설정 이름이 바뀌면 다시 만든다.
            string key = Workers.Name1 + "|" + Workers.Name2;
            if (s_parser != null && s_namesKey == key) return s_parser;
            var names = new Dictionary<string, string>
            {
                [Workers.Name1] = Workers.Name1,
                [Workers.Name2] = Workers.Name2,
            };
            // 기본 이름이면 별칭도(라그나: Ragnar·라그나르, 플로키: Floki). 다른 이름을 골랐으면 그 이름만.
            if (Workers.Name1 == Workers.Default1) foreach (var a in new[] { "Ragnar", "라그나르" }) names[a] = Workers.Name1;
            if (Workers.Name2 == Workers.Default2) names["Floki"] = Workers.Name2;
            s_parser = new ChatParser(names, Items.Resolve);
            s_namesKey = key;
            return s_parser;
        }

        public static void Handle(Speaker who, string text)
        {
            var npcs = WorkerNpc.All;
            if (npcs.Count == 0) return;
            ChatResult r = Parser().Parse(text, Departures.Awaiting);
            switch (r.Route)
            {
                case ChatRoute.Answer:
                    Departures.Answer(who, Departures.ReplyOf(r.Answer), null);
                    break;
                case ChatRoute.Command:
                    // 질문을 기다리는 중이면(옛 저장의 질문만 — 11-14 질문 없앰) "!같이 가자"는 대답(설계 B-7)
                    if (Departures.Awaiting && r.Command.Kind == CommandKind.Escort
                        && Departures.Answer(who, Reply.Escort, r.Command.Target)) break;
                    if (r.Command.Kind == CommandKind.Help) { TellHelp(r.Command.Target, npcs); break; }
                    Run(who, r.Command, npcs);
                    break;
                case ChatRoute.Haiku:
                case ChatRoute.Sonnet:
                    ClaudeChat.Ask(who, r);
                    break;
            }
        }

        /// <summary>Claude를 못 쓰거나 실패했을 때: 정해진 ! 명령만 된다고 말한다(! 문장일 때만, 수다는 조용히).</summary>
        internal static void OnlyBang(string target)
        {
            var npcs = WorkerNpc.All;
            if (npcs.Count > 0) Pick(CommandKind.Item, target, npcs)?.Say("onlyBang");
        }

        internal static string StateOf(WorkerNpc npc) => State(npc);

        /// <summary>!명령어(사용자 11-07): 일꾼 하나가 명령 목록·상자 이름 바꾸는 법을 줄마다 말한다(Claude 없이).</summary>
        static void TellHelp(string target, List<WorkerNpc> npcs)
        {
            WorkerNpc npc = target != null ? npcs.Find(n => n.NpcName == target) : null;
            npc = npc ?? npcs[0];
            foreach (string line in HelpText.Lines) npc.SayText(line);
        }

        /// <summary>검사를 통과한 Claude 작업 하나를 실행. 세트 저장·고치기는 여기서 직접, 나머지는 !명령과 같은 길.</summary>
        internal static void RunClaudeTask(Speaker who, ClaudeTask t, List<WorkerNpc> npcs)
        {
            var cmd = t.Command;
            if (cmd.Kind == CommandKind.SetSave || cmd.Kind == CommandKind.SetEdit)
            {
                var npc = Pick(cmd.Kind, cmd.Target, npcs);
                if (npc == null) return;
                if (cmd.Kind == CommandKind.SetSave)
                {
                    int no = Brain.Sets.Save(cmd.Number, t.Items, t.Name, who.Name);
                    npc.Say(no > 0 ? "setSaved" : "notYet", new Dictionary<string, string> { ["no"] = no.ToString() });
                }
                else
                {
                    bool ok = Brain.Sets.Edit(cmd.Number, t.Remove, t.Items);
                    npc.Say(ok ? "setSaved" : "setNone", new Dictionary<string, string> { ["no"] = cmd.Number.ToString(), ["item"] = "세트 " + cmd.Number + "번", ["n"] = "0" });
                }
                return;
            }
            Run(who, cmd, npcs);
        }

        static WorkerNpc Pick(CommandKind kind, string target, List<WorkerNpc> npcs)
        {
            var chosen = Dispatch.Choose(kind, target, Infos(npcs));
            return chosen.Count > 0 ? npcs[chosen[0]] : null;
        }

        static List<NpcInfo> Infos(List<WorkerNpc> npcs)
        {
            var infos = new List<NpcInfo>(npcs.Count);
            foreach (var n in npcs)
                infos.Add(new NpcInfo { Name = n.NpcName, Personality = n.Personality, Busy = !(n.Ai.Current == WorkerAI.Mode.Idle || n.Ai.Current == WorkerAI.Mode.Work && n.Ai.Yields) || n.Ai.Queue.Count > 0 });
            return infos;
        }

        /// <summary>명령이 일꾼에게 갈 때마다(모드 해석·Claude 해석 모두) — 최종 시험이 "말을 어떻게 알아들었나"를 잼(11-14).</summary>
        internal static event System.Action<WorkerNpc, ChatCommand> Ran;

        static void Run(Speaker who, ChatCommand cmd, List<WorkerNpc> npcs)
        {
            // 순회 중에 목록이 바뀌지 않게 복사한다.
            var targets = new List<WorkerNpc>();
            foreach (int i in Dispatch.Choose(cmd.Kind, cmd.Target, Infos(npcs))) targets.Add(npcs[i]);
            if (Ran != null) foreach (var npc in targets) Ran(npc, cmd);
            foreach (var npc in targets)
            {
                switch (cmd.Kind)
                {
                    case CommandKind.Escort:
                        // 일하는 중이면 그 일에 호위병을 붙인다(사용자 결정 2026-10-26), 쉬는 중이면 말한 사람 따라가기
                        if (npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null && !(npc.Ai.Job is TidyJob))
                        {
                            WorkerNpc mate = npc.Ai.EscortName != null ? null : Departures.GoWithGuard(npc);
                            string partner = mate != null ? mate.NpcName : npc.Ai.EscortName;
                            npc.Say(partner != null ? "answer.escort" : "escort.noMate", new Dictionary<string, string> { ["partner"] = partner ?? "" });
                            break;
                        }
                        npc.Ai.Follow(who.CharacterId, who.Name);
                        npc.Say("escort");
                        break;
                    case CommandKind.Stop:
                    case CommandKind.ClearQueue:
                        npc.Ai.Queue.Clear();
                        Departures.Forget(npc);
                        npc.Ai.GoHome();
                        npc.Ai.HoldChores(); // 쉬라고 했으니 묘비 회수도 스스로 시작하지 않는다
                        npc.Say("stop");
                        break;
                    case CommandKind.Item:
                        Departures.Gather(who, cmd, npc);
                        if (cmd.Target == null) Departures.ShareGather(cmd, npc); // 쉬는 동료도 나눠 맡음(사용자 11-09)
                        break;
                    case CommandKind.SetChest:
                        SetChest(who, cmd, npc);
                        return; // 한 번만 처리
                    case CommandKind.Explore:
                        StartExplore(npc, cmd);
                        break;
                    case CommandKind.SetList:
                        TellSets(npc);
                        break;
                    case CommandKind.SetMake:
                        Departures.MakeSet(npc, cmd.Number, cmd.Amount, null, true);
                        break;
                    case CommandKind.Keep:
                        Brain.Refill.Keep(cmd.Item, cmd.Amount, cmd.Once);
                        Departures.RefillSoon(cmd.Item);
                        npc.Say(cmd.Once ? "fillOnce" : "keepSet", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(cmd.Item), ["n"] = cmd.Amount.ToString() });
                        break;
                    case CommandKind.KeepSet:
                        Brain.Refill.KeepSet(cmd.Number, cmd.Amount, cmd.Once);
                        Departures.RefillSoon(null);
                        npc.Say(cmd.Once ? "fillOnce" : "keepSet", new Dictionary<string, string> { ["item"] = "세트 " + cmd.Number + "번", ["n"] = cmd.Amount.ToString() });
                        break;
                    case CommandKind.KeepOff:
                        Brain.Refill.Off(cmd.Item);
                        npc.Say("keepOff", new Dictionary<string, string> { ["item"] = cmd.Item != null ? GatherJob.ItemLabel(cmd.Item) : "전부" });
                        break;
                    case CommandKind.KeepList:
                        TellRefill(who, npc);
                        break;
                    case CommandKind.Prep:
                    case CommandKind.PrepKeep:
                    case CommandKind.PrepOff:
                        PersonalChests.Command(who, cmd, npc);
                        break;
                    case CommandKind.Farm:
                        SetFarm(who, cmd, npc);
                        break;
                    case CommandKind.Tidy:
                        Chore(npc, false);
                        break;
                    default:
                        npc.Say("notYet");
                        break;
                }
            }
        }

        /// <summary>!세트: 저장된 세트메뉴 목록.</summary>
        static void TellSets(WorkerNpc npc)
        {
            var parts = new List<string>();
            foreach (var s in Brain.Sets.All)
            {
                var names = new List<string>();
                foreach (var it in s.Items) names.Add(GatherJob.ItemLabel(it));
                parts.Add(s.Number + "번" + (s.Name != null ? "(" + s.Name + ")" : "") + " " + string.Join("·", names.ToArray()));
            }
            if (parts.Count == 0) { npc.Say("setEmpty"); return; }
            npc.Say("setList", new Dictionary<string, string> { ["list"] = string.Join(", ", parts.ToArray()) });
        }

        /// <summary>!계속채워 목록(사용자 10-31): 거점 채우기 줄 + "개인:" 말한 사람 것. 없으면 keepNone.</summary>
        static void TellRefill(Speaker who, WorkerNpc npc)
        {
            var parts = RefillWindow.Lines(npc.Home, PersonalChests.OwnerOf(who), false);
            if (parts.Count == 0) { npc.Say("keepNone"); return; }
            npc.Say("keepList", new Dictionary<string, string> { ["list"] = string.Join(", ", parts.ToArray()) });
        }

        /// <summary>!스킬: 오른 스킬 높은 순(없으면 "다 10").</summary>
        static void TellSkills(WorkerNpc npc)
        {
            var raised = npc.SkillLevels.Raised();
            if (raised.Count == 0) { npc.Say("skillsNone"); return; }
            var parts = new List<string>();
            foreach (var kv in raised) parts.Add(WorkerNpc.SkillName((Skills.SkillType)kv.Key) + " " + (int)kv.Value);
            npc.Say("skills", new Dictionary<string, string> { ["list"] = string.Join(", ", parts.ToArray()) });
        }

        internal static void StartExplore(WorkerNpc npc, ChatCommand cmd)
        {
            int dx = 0, dy = 0, biome = 0;
            string where;
            if (cmd.Arg != null && Bearing.Vector(cmd.Arg, out dx, out dy)) where = Bearing.KoreanOf(cmd.Arg);
            else if (cmd.Biome != null && (biome = BiomeNames.FromGame(cmd.Biome)) != 0) where = BiomeNames.Of(biome);
            else where = npc.Line("nearby");
            var args = new Dictionary<string, string> { ["where"] = where, ["biome"] = where };
            // 밤: 아는 바이옴이고 밤 몫을 넣은 장비 위험이 무리가 아니면 간다(사용자 11-13). 방향만 말했거나 모르는 땅은 밤에 안 간다.
            if (EnvMan.IsNight() && (biome == 0 || !Brain.Get().KnowsBiome(biome) || !Departures.NightGearOk(npc, biome))) { npc.Say("nightNoExplore", args); return; }
            // 일하는 중이면: 급하면 하던 일을 멈추고, 아니면 대기열(설계 B-5)
            if (npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null && !npc.Ai.Yields)
            {
                if (!cmd.Urgent)
                {
                    npc.Ai.Queue.Add(WorkSaves.ToQueued(npc, new WorkItem { Kind = WorkKind.Explore, Label = where, Arg = cmd.Arg, Biome = cmd.Biome, Target = cmd.Target }));
                    npc.Say("depart.busy", new Dictionary<string, string> { ["job"] = npc.Ai.Job.Label });
                    return;
                }
                npc.Ai.Interrupt();
                npc.Say("dropJob", args);
            }
            npc.Ai.StartJob(new ExploreJob(npc, dx, dy, biome, where));
            // 그 바이옴에 가 본 적이 없으면 어디인지 모른다고 말하고 가까운 경계부터
            bool knowBiome = biome == 0 || Brain.Get().KnowsBiome(biome);
            npc.Say(knowBiome ? "exploreGo" : "exploreBiomeUnknown", args);
        }

        /// <summary>!지식: 아는 자원(구역 수 많은 순 5개)과 바이옴, 거점에서 가장 가까운 곳.</summary>
        static void TellKnowledge(WorkerNpc npc)
        {
            Knowledge k = Brain.Get();
            var list = k.Summary(Senses.ZoneOf(npc.Home));
            if (list.Count == 0) { npc.Say("knowledgeEmpty"); return; }
            // 짧게(0.3.6 시험: 나무 한 그루가 나무·수지·씨앗을 다 내서 다 같은 말이 반복됐다):
            // 바이옴이 다 같으면 앞에 한 번만, 거점 근처(100m 안)면 방향을 안 붙인다.
            int shown = System.Math.Min(5, list.Count), common = list[0].Biomes;
            for (int i = 1; i < shown; i++) if (list[i].Biomes != common) common = -1;
            var parts = new List<string>();
            for (int i = 0; i < shown; i++)
            {
                ItemKnowledge ik = list[i];
                Vector3 near = Senses.ZoneCenter(ik.Nearest);
                string text = $"{GatherJob.ItemLabel(ik.Item)} {ik.Zones}곳";
                var extra = new List<string>();
                if (common == -1 && ik.Biomes != 0) extra.Add(BiomeNames.Of(ik.Biomes));
                if (Utils.DistanceXZ(npc.Home, near) >= 100f)
                {
                    Scout.FromHome(npc.Home, near, out string dir, out string dist);
                    extra.Add($"가까운 데 {dir} {dist}m");
                }
                parts.Add(extra.Count > 0 ? text + "(" + string.Join(", ", extra) + ")" : text);
            }
            string prefix = common > 0 ? BiomeNames.Of(common) + " — " : "";
            npc.Say("knowledge", new Dictionary<string, string>
            {
                ["list"] = prefix + string.Join(", ", parts),
                ["more"] = list.Count > 5 ? npc.Line("knowledgeMore", new Dictionary<string, string> { ["n"] = (list.Count - 5).ToString() }) : "",
                ["looked"] = k.LookedCount.ToString(),
            });
        }

        /// <summary>!밭 N: 말한 사람이 선 곳을 중심으로 반경 N m 밭을 적어 두고(⑨, 여러 곳) 바로 그 밭일(거두기·심기, FarmJob.ForField)을 시작한다.</summary>
        static void SetFarm(Speaker who, ChatCommand cmd, WorkerNpc npc)
        {
            var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(who.CharacterId) : null;
            if (zdo == null) { npc.Say("farmWhere"); return; }
            Vector3 p = zdo.GetPosition();
            var args = new Dictionary<string, string> { ["r"] = cmd.Number.ToString() };
            if (Crops.All.Count > 0 && !Crops.AnyGrows(Scout.BiomeAt(p))) { npc.Say("farmNoGrow", args); return; }
            var r = Brain.Farms.Set(p.x, p.z, cmd.Number);
            npc.Say(r == FarmSetResult.Full ? "farmFull" : r == FarmSetResult.Moved ? "farmMoved" : "farmSet", args);
            FarmField field = r == FarmSetResult.Full ? null : Brain.Farms.At(p.x, p.z);
            if (field != null) StartFarm(npc, field, cmd.Urgent);
        }

        /// <summary>!가축·!정리(최종 실전 시험 명령, 11-14): 쉬는 중이면 바로 먹이 주기(LivestockJob)·상자 정리(TidyJob), 바쁘면 적어 두고 쉬게 되면 바로(RunLaterChore).
        /// 회차 662: 예전엔 쉴 때 집안일 시계만 당겨서 시험 중(집안일 꺼짐)·밤·30m 밖이면 명령이 사라졌음.</summary>
        static void Chore(WorkerNpc npc, bool feed)
        {
            if (npc.Ai.Current != WorkerAI.Mode.Idle || npc.Ai.Queue.Count > 0)
            {
                s_laterChores.TryGetValue(npc, out int bits);
                s_laterChores[npc] = bits | (feed ? 1 : 2);
                npc.Say("choreLater");
                return;
            }
            StartChore(npc, feed);
        }

        /// <summary>바쁠 때 받은 !가축(1)·!정리(2). 튜플은 게임 런타임에 System.ValueTuple이 없어 못 씀(회차 662 TypeLoadException).</summary>
        static readonly Dictionary<WorkerNpc, int> s_laterChores = new Dictionary<WorkerNpc, int>();

        /// <summary>쉬게 된 일꾼(Departures 쉴 때 차례)이 바쁠 때 받은 !가축·!정리가 있으면 시작(먹이 먼저, 정리는 다음 쉴 때). 시작했으면 true.</summary>
        internal static bool RunLaterChore(WorkerNpc npc)
        {
            if (ContinueTidy(npc)) return true;
            if (!s_laterChores.TryGetValue(npc, out int bits)) return false;
            s_laterChores.Remove(npc);
            if ((bits & 1) != 0 && StartChore(npc, true))
            {
                if ((bits & 2) != 0) s_laterChores[npc] = 2;
                return true;
            }
            return (bits & 2) != 0 && StartChore(npc, false);
        }

        static bool StartChore(WorkerNpc npc, bool feed)
        {
            if (feed)
            {
                LivestockJob job = LivestockJob.For(npc);
                if (job == null) { npc.Say("choreNone"); return false; }
                npc.Ai.StartJob(job);
                npc.Say("idleFeed");
                return true;
            }
            if (Tidy.Plan(npc.Home).Count == 0) { npc.Say("choreNone"); return false; }
            var tidy = new TidyJob(npc);
            npc.Ai.StartJob(tidy);
            s_tidyJob[npc] = tidy;
            npc.Say("choreTidy");
            s_tidyLeft[npc] = Tidy.Plan(npc.Home).Count;
            s_tidyMiss.Remove(npc);
            return true;
        }

        /// <summary>!정리를 시작할 때 옮길 것 수. TidyJob은 한 번에 8번·180초라(회차 662 — 39개 중 일부만 하고 끝) 쉬게 될 때마다 줄어드는 동안 이어서 한다.</summary>
        static readonly Dictionary<WorkerNpc, int> s_tidyLeft = new Dictionary<WorkerNpc, int>();

        /// <summary>가장 적었던 옮길 것보다 못 줄인 연속 횟수. 다른 일꾼이 그사이 물건을 넣어 한 번 늘 수 있어(회차 663 — 19 → 20에서 그만) 3번 내리 못 줄여야 그만.</summary>
        static readonly Dictionary<WorkerNpc, int> s_tidyMiss = new Dictionary<WorkerNpc, int>();

        /// <summary>마지막으로 맡긴 TidyJob — 옮긴 게 있으면 남은 수가 안 줄어도(동료가 그사이 상자에 넣음, 회차 664) 이어 간다.</summary>
        static readonly Dictionary<WorkerNpc, TidyJob> s_tidyJob = new Dictionary<WorkerNpc, TidyJob>();

        static bool ContinueTidy(WorkerNpc npc)
        {
            if (!s_tidyLeft.TryGetValue(npc, out int left)) return false;
            s_tidyLeft.Remove(npc);
            int now = Tidy.Plan(npc.Home).Count;
            s_tidyMiss.TryGetValue(npc, out int miss);
            int moved = s_tidyJob.TryGetValue(npc, out TidyJob last) && last != null ? last.Moved : 0;
            miss = now < left || moved > 0 ? 0 : miss + 1;
            bool stop = now == 0 || miss >= 3; // 다 했거나 더 못 줄임(상자 꽉 참 등)
            Plugin.Log.LogInfo($"{npc.NpcName} !정리 이어서: 옮길 것 {left} → {now}, 지난번 옮김 {moved}{(stop ? " — 그만" : miss > 0 ? $" (못 줄임 {miss})" : "")}");
            if (stop) { s_tidyMiss.Remove(npc); s_tidyJob.Remove(npc); return false; }
            var tidy = new TidyJob(npc);
            npc.Ai.StartJob(tidy);
            s_tidyJob[npc] = tidy;
            s_tidyLeft[npc] = Mathf.Min(left, now);
            s_tidyMiss[npc] = miss;
            return true;
        }

        /// <summary>
        /// 그 밭일(job, 없으면 ForField) 시작. 다른 일꾼이 같은 밭일 중이면 맡지 않는다(같은 자리에 두 번 심지 않게). 일하는 중이면 탐험처럼: 급하면 멈추고,
        /// 아니면 대기열(밭 자리만 — 꺼낼 때 ForField). 지금 시작했으면 true.
        /// </summary>
        internal static bool StartFarm(WorkerNpc npc, FarmField field, bool urgent, FarmJob job = null)
        {
            foreach (var other in WorkerNpc.All)
                if (other != null && other.Ai != null && other.Ai.Job is FarmJob f && f.Field.Contains(field.X, field.Z))
                {
                    if (other != npc) npc.Say("farmAlready", new Dictionary<string, string> { ["npc"] = other.NpcName });
                    return false;
                }
            if (npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null && !npc.Ai.Yields)
            {
                if (!urgent)
                {
                    var w = new WorkItem { Kind = WorkKind.Farm, Label = npc.Line("job.farm"), Amount = field.Radius, FarmX = field.X, FarmZ = field.Z };
                    npc.Ai.Queue.Add(WorkSaves.ToQueued(npc, w));
                    npc.Say("depart.busy", new Dictionary<string, string> { ["job"] = npc.Ai.Job.Label });
                    return false;
                }
                npc.Ai.Interrupt();
                npc.Say("dropJob");
            }
            npc.Ai.StartJob(job ?? FarmJob.ForField(npc, field));
            return true;
        }

        /// <summary>!상자: 상자는 지정하지 않는다(거점 상자 전부를 씀) → 그렇다고 알려 준다.</summary>
        static void SetChest(Speaker who, ChatCommand cmd, WorkerNpc speaker) => speaker.Say("chestAny");

        static string State(WorkerNpc npc)
        {
            switch (npc.Ai.Current)
            {
                case WorkerAI.Mode.Work:
                    string line = npc.Ai.Job.StateLine();
                    return npc.Ai.EscortName != null ? line + " (" + npc.Ai.EscortName + " 동행)" : line;
                case WorkerAI.Mode.Follow: return npc.Line("state.follow", new Dictionary<string, string> { ["player"] = npc.Ai.FollowName });
                case WorkerAI.Mode.Home: return npc.Line("state.home");
                default: return npc.Line("state.idle");
            }
        }
    }
}
