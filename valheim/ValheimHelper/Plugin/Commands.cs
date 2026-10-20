using System.Collections.Generic;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;

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
            ChatResult r = Parser().Parse(text);
            switch (r.Route)
            {
                case ChatRoute.Command:
                    Run(who, r.Command, npcs);
                    break;
                case ChatRoute.Haiku:
                    // Claude 연동(5-1장)은 아직 없다.
                    Pick(CommandKind.Item, r.Target, npcs)?.Say("onlyBang");
                    break;
                // Sonnet(그냥 채팅)·짧은 대답은 아직 처리하지 않는다. 수다일 수 있으니 조용히 넘긴다.
            }
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
                infos.Add(new NpcInfo { Name = n.NpcName, Personality = n.Personality, Busy = n.Ai.Current != WorkerAI.Mode.Idle });
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
                        npc.Ai.Follow(who.CharacterId, who.Name);
                        npc.Say("escort");
                        break;
                    case CommandKind.Stop:
                    case CommandKind.ClearQueue:
                        npc.Ai.GoHome();
                        npc.Say("stop");
                        break;
                    case CommandKind.Item:
                        StartGather(npc, cmd);
                        break;
                    case CommandKind.SetChest:
                        SetChest(who, cmd, npc);
                        return; // 한 번만 처리(장비 상자는 안에서 NPC별로)
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

        static void StartGather(WorkerNpc npc, ChatCommand cmd)
        {
            // 캐거나 줍는 물건만(맨손·도끼·곡괭이, 게임 데이터에서 읽음). 가공·제작은 ⑥.
            HashSet<ToolKind> ways = ResourceCatalog.WaysFor(cmd.Item);
            if (ways.Count == 0) { npc.Say("notYet"); return; }
            var args = new Dictionary<string, string> { ["item"] = GatherJob.ItemLabel(cmd.Item), ["n"] = cmd.Amount.ToString() };
            if (!GatherJob.CanStart(npc, ways)) { npc.Say("noTool", args); return; }
            if (npc.Ai.Current == WorkerAI.Mode.Work) npc.Say("dropJob", args);
            npc.Ai.StartJob(new GatherJob(npc, cmd.Item, cmd.Amount, ways));
            bool known = Brain.Get().ZonesWith(cmd.Item, Senses.ZoneOf(npc.transform.position)).Count > 0;
            npc.Say(known ? "gatherKnown" : "gatherStart", args);
        }

        /// <summary>!상자 &lt;종류&gt;: 말한 사람이 바라보는 상자를 지정한다. 거점 깃발 20m 안의 상자만.</summary>
        static void SetChest(Speaker who, ChatCommand cmd, WorkerNpc speaker)
        {
            Container chest = Chests.FromId(who.Hover);
            if (chest == null) { speaker.Say("chestNone"); return; }
            if (Utils.DistanceXZ(chest.transform.position, speaker.Home) > Chests.BaseRange + 3f) { speaker.Say("chestFar"); return; }
            ZDOID id = chest.GetComponent<ZNetView>().GetZDO().m_uid;
            string label;
            switch (cmd.Arg)
            {
                case "equipment":
                    // 장비 상자는 각자: 이름을 말하면 그 NPC 것, 아니면 둘 다.
                    foreach (var npc in WorkerNpc.All)
                        if (cmd.Target == null || npc.NpcName == cmd.Target) Chests.Assign(Chests.KeyEquip((int)npc.Personality), id);
                    label = cmd.Target != null ? cmd.Target + " 장비" : "장비";
                    break;
                case "food": Chests.Assign(Chests.KeyFood, id); label = "음식"; break;
                case "seeds": Chests.Assign(Chests.KeySeeds, id); label = "씨앗"; break;
                default: Chests.Assign(Chests.KeyItem(cmd.Item), id); label = GatherJob.ItemLabel(cmd.Item); break;
            }
            speaker.Say("chestSet", new Dictionary<string, string> { ["kind"] = label });
        }

        static string State(WorkerNpc npc)
        {
            switch (npc.Ai.Current)
            {
                case WorkerAI.Mode.Work:
                    var job = npc.Ai.Job;
                    return npc.Line("state.work", new Dictionary<string, string>
                    {
                        ["item"] = GatherJob.ItemLabel(job.Item), ["n"] = job.Target.ToString(), ["done"] = job.Delivered.ToString(),
                    });
                case WorkerAI.Mode.Follow: return npc.Line("state.follow", new Dictionary<string, string> { ["player"] = npc.Ai.FollowName });
                case WorkerAI.Mode.Home: return npc.Line("state.home");
                default: return npc.Line("state.idle");
            }
        }
    }
}
