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
            if (Workers.Name1 == "Claude") names["클로드"] = "Claude";
            if (Workers.Name2 == "Alarak") names["알라라크"] = "Alarak";
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
                    // 질문을 기다리는 중이면 "!그냥 가"·"!같이 가자"는 대답(설계 B-7)
                    if (Departures.Awaiting && (r.Command.Kind == CommandKind.GoAnyway || r.Command.Kind == CommandKind.Escort)
                        && Departures.Answer(who, r.Command.Kind == CommandKind.GoAnyway ? Reply.GoAnyway : Reply.Escort, r.Command.Target)) break;
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
                infos.Add(new NpcInfo { Name = n.NpcName, Personality = n.Personality, Busy = n.Ai.Current != WorkerAI.Mode.Idle || n.Ai.Queue.Count > 0 });
            return infos;
        }

        static void Run(Speaker who, ChatCommand cmd, List<WorkerNpc> npcs)
        {
            // 순회 중에 목록이 바뀌지 않게 복사한다.
            var targets = new List<WorkerNpc>();
            foreach (int i in Dispatch.Choose(cmd.Kind, cmd.Target, Infos(npcs))) targets.Add(npcs[i]);
            foreach (var npc in targets)
            {
                switch (cmd.Kind)
                {
                    case CommandKind.Escort:
                        // 일하는 중이면 일을 버리지 않고 그 일에 동행을 붙인다(구상서 8-1), 쉬는 중이면 따라가기
                        if (npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null)
                        {
                            npc.Ai.EscortBy(who.CharacterId, who.Name);
                            npc.Say("answer.escort");
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
                        break;
                    case CommandKind.SetChest:
                        SetChest(who, cmd, npc);
                        return; // 한 번만 처리
                    case CommandKind.Knowledge:
                        TellKnowledge(npc);
                        break;
                    case CommandKind.Memory:
                        Departures.TellMemory(npc);
                        break;
                    case CommandKind.Explore:
                        StartExplore(npc, cmd);
                        break;
                    case CommandKind.Skills:
                        TellSkills(npc);
                        break;
                    case CommandKind.SetList:
                        TellSets(npc);
                        break;
                    case CommandKind.SetMake:
                        Departures.MakeSet(npc, cmd.Number, cmd.Amount);
                        break;
                    case CommandKind.Keep:
                        Brain.Refill.Keep(cmd.Item, cmd.Amount);
                        npc.Say("keepSet", new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(cmd.Item), ["n"] = cmd.Amount.ToString() });
                        break;
                    case CommandKind.KeepSet:
                        Brain.Refill.KeepSet(cmd.Number, cmd.Amount);
                        npc.Say("keepSet", new Dictionary<string, string> { ["item"] = "세트 " + cmd.Number + "번", ["n"] = cmd.Amount.ToString() });
                        break;
                    case CommandKind.KeepOff:
                        Brain.Refill.Off(cmd.Item);
                        npc.Say("keepOff", new Dictionary<string, string> { ["item"] = cmd.Item != null ? GatherJob.ItemLabel(cmd.Item) : "전부" });
                        break;
                    case CommandKind.Farm:
                        SetFarm(who, cmd, npc);
                        break;
                    case CommandKind.Status:
                        npc.Say("status", new Dictionary<string, string>
                        {
                            ["state"] = State(npc),
                            ["hp"] = npc.GetHealth().ToString("0"),
                            ["max"] = npc.GetMaxHealth().ToString("0"),
                        });
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
            if (EnvMan.IsNight()) { npc.Say("nightNoExplore", args); return; }
            // 일하는 중이면: 급하면 하던 일을 멈추고, 아니면 대기열(설계 B-5)
            if (npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null && !(npc.Ai.Job is GraveJob))
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
            if (npc.Ai.Current == WorkerAI.Mode.Work && npc.Ai.Job != null && !(npc.Ai.Job is GraveJob))
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
