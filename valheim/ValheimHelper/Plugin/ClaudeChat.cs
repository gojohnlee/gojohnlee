using System.Collections.Generic;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 채팅 → Claude 호출 → 검사된 작업 실행 (구상서 5-1장). 한 번에 하나만 부르고, 부르는 동안 온 말은 한 줄만 기다린다.
    /// 호출이 실패하거나 30초가 지나면 "정해진 ! 명령만 돼요"로 대신한다.
    /// </summary>
    public static class ClaudeChat
    {
        static readonly RateLimiter s_sonnet = new RateLimiter(6, 60);
        static ClaudeCli s_call;
        static Speaker s_who;
        static ChatResult s_pending;
        static ChatResult s_waiting;
        static Speaker s_waitingWho;
        static float s_started;
        /// <summary>최근 대화(플레이어 말·NPC 답), 지시문에 붙인다. RecentSeconds 지나거나 RecentMax 넘으면 버린다.</summary>
        static readonly List<KeyValuePair<float, string>> s_recent = new List<KeyValuePair<float, string>>();
        const float RecentSeconds = 300f;
        const int RecentMax = 8;

        static void Remember(string line)
        {
            s_recent.Add(new KeyValuePair<float, string>(Time.realtimeSinceStartup, line));
            if (s_recent.Count > RecentMax) s_recent.RemoveAt(0);
        }

        static List<string> Recent()
        {
            float now = Time.realtimeSinceStartup;
            s_recent.RemoveAll(kv => now - kv.Key > RecentSeconds);
            var l = new List<string>();
            foreach (var kv in s_recent) l.Add(kv.Value);
            return l;
        }

        public static void Ask(Speaker who, ChatResult r)
        {
            if (!Features.ClaudeChat) { Commands.OnlyBang(r.Target); return; }
            if (s_call != null) { s_waiting = r; s_waitingWho = who; return; }
            Start(who, r);
        }

        static void Start(Speaker who, ChatResult r)
        {
            bool bang = r.Route == ChatRoute.Haiku;
            if (!bang && !s_sonnet.TryTake(Time.realtimeSinceStartup)) { Commands.OnlyBang(r.Target); return; }
            var npcs = WorkerNpc.All;
            var lines = new List<string>();
            foreach (var n in npcs) lines.Add(n.NpcName + ": " + Commands.StateOf(n) + " — " + ClaudePrompt.Voice(n.Personality));
            var stock = new Dictionary<string, int>();
            foreach (var c in Chests.NearBase(npcs[0].Home, ChestRole.Mats))
                foreach (var it in c.GetInventory().GetAllItems())
                    if (it.m_dropPrefab != null)
                    {
                        string k = GatherJob.ItemLabel(Utils.GetPrefabName(it.m_dropPrefab));
                        stock[k] = (stock.TryGetValue(k, out int o) ? o : 0) + it.m_stack;
                    }
            var stockLines = new List<string>();
            foreach (var kv in stock) stockLines.Add(kv.Key + " " + kv.Value);
            string prompt = ClaudePrompt.System(lines, stockLines, who.Name, Recent());
            Remember(who.Name + ": " + r.Text);
            s_who = who; s_pending = r; s_started = Time.realtimeSinceStartup;
            s_call = ClaudeCli.Start(ClaudeCall.Model(bang), prompt, r.Text);
            npcs[0].Say("thinking");
        }

        public static void Tick()
        {
            if (s_call == null || !s_call.Done) return;
            var call = s_call; s_call = null;
            var r = s_pending; var who = s_who;
            try { Finish(call, r, who); }
            finally
            {
                if (s_waiting != null) { var w = s_waiting; s_waiting = null; Start(s_waitingWho, w); }
            }
        }

        static void Finish(ClaudeCli call, ChatResult r, Speaker who)
        {
            var npcs = WorkerNpc.All;
            if (npcs.Count == 0) return;
            if (call.Text == null)
            {
                Plugin.Log.LogWarning("Claude 호출 실패: " + call.Error);
                Commands.OnlyBang(r.Target);
                return;
            }
            var names = new List<string>();
            foreach (var n in npcs) names.Add(n.NpcName);
            ClaudeResult res = ClaudeReply.Parse(call.Text, Items.Resolve, names);
            foreach (var why in res.Rejected) Plugin.Log.LogInfo("Claude 작업 버림: " + why);
            if (res.Invalid) { Plugin.Log.LogWarning("Claude 답 형식 오류: " + call.Text); Commands.OnlyBang(r.Target); return; }
            if (res.Chatter) return;
            WorkerNpc speaker = null;
            if (r.Target != null) foreach (var n in npcs) if (n.NpcName == r.Target) speaker = n;
            if (speaker == null) speaker = npcs[0];
            if (res.Reply != null) { speaker.SayText(res.Reply); Remember(speaker.NpcName + ": " + res.Reply); }
            foreach (var t in res.Tasks) Commands.RunClaudeTask(who, t, npcs);
        }
    }
}
