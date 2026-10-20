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

        static string State(WorkerNpc npc)
        {
            switch (npc.Ai.Current)
            {
                case WorkerAI.Mode.Follow: return npc.Line("state.follow", new Dictionary<string, string> { ["player"] = npc.Ai.FollowName });
                case WorkerAI.Mode.Home: return npc.Line("state.home");
                default: return npc.Line("state.idle");
            }
        }
    }
}
