using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 잡담 게임 연결 (사용자 결정 2026-10-23 "잡담", progress 잡담 나)). 고르기는 Core Banter, 여기는 언제·누가·어디서.
    /// 1초마다: 둘 다 쉬는 중(Idle·Home, 싸움 아님)·서로 15m 안·플레이어 30m 안이면 주고받기, 한 명만 그러면 혼잣말.
    /// 일이 생기면(WorkerNpc.Say에서 OnSaid) 상대·플레이어가 30m 안일 때 바로 반응, 아니면 소식으로 들고 있다가 다음 잡담에.
    /// 말은 머리 위 말풍선만(ChatNet.NpcBubble, 채팅창엔 안 씀). 주고받는 동안 쉬는 두 NPC는 멈춰 서로 바라본다(WorkerAI가 Facing을 봄).
    /// 호스트만 돈다.
    /// </summary>
    public static class WorkerBanter
    {
        public const float PairRange = 15f, NearRange = 30f, HoldAfter = 2f;
        /// <summary>밖에 이만큼(초) 넘게 있다 거점 범위로 돌아오면 Back.</summary>
        public const float BackAfter = 300f;

        static Banter s_banter = new Banter();
        static readonly System.Random s_rng = new System.Random();
        static float s_nextTick;

        sealed class Pending { public float At; public int Npc; public string Key; public Dictionary<string, string> Args; }
        static readonly List<Pending> s_pending = new List<Pending>();
        /// <summary>서로 바라보며 서 있을 때까지(Time.time). 0이면 안 함.</summary>
        static float s_holdUntil;

        static readonly Dictionary<ZDOID, float> s_outSince = new Dictionary<ZDOID, float>();
        static bool s_raidWas;

        /// <summary>시험용: 말풍선 띄운 횟수.</summary>
        public static int Said;
        /// <summary>시험용: 말한 줄 "이름:키" 차례대로.</summary>
        internal static readonly List<string> SaidKeys = new List<string>();

        /// <summary>시험용: 새 Banter(쿨다운·소식 비움)로 바꾸고 다음 잡담 때를 지금으로.</summary>
        internal static void ResetForTest(bool dueNow)
        {
            s_banter = new Banter();
            s_pending.Clear();
            s_holdUntil = 0f;
            if (!dueNow) return;
            s_banter.Tick(Now(), false, -1, null, Names()); // 첫 때를 잡게 하고
            s_banter.SetNext(Now());                          // 바로 됨
        }

        /// <summary>시험용: 다음 잡담 때를 지금으로.</summary>
        internal static void DueNow() => s_banter.SetNext(Now());

        static double Now() => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

        static WorkerNpc Slot(int i) => WorkerNpc.All.Find(n => n != null && (int)n.Personality == i);

        static string[] Names()
        {
            var a = Slot(0);
            var b = Slot(1);
            return new[] { a != null ? a.NpcName : Workers.Name1, b != null ? b.NpcName : Workers.Name2 };
        }

        static bool PlayerNear(Vector3 p, float r)
        {
            foreach (var pl in Player.GetAllPlayers())
                if (pl != null && !pl.IsDead() && Vector3.Distance(pl.transform.position, p) <= r) return true;
            return false;
        }

        static string NearPlayerName(Vector3 p, float r)
        {
            foreach (var pl in Player.GetAllPlayers())
                if (pl != null && !pl.IsDead() && Vector3.Distance(pl.transform.position, p) <= r) return pl.GetPlayerName();
            return null;
        }

        static bool Resting(WorkerNpc n) =>
            n != null && !n.IsDead() && n.Ai != null && !n.Ai.GuardBusy && (n.Ai.Current == WorkerAI.Mode.Idle || n.Ai.Current == WorkerAI.Mode.Home);

        /// <summary>쉬는 두 NPC가 말하는 동안 바라볼 곳. 아니면 false.</summary>
        public static bool Facing(WorkerNpc npc, out Vector3 at)
        {
            at = default;
            if (s_holdUntil <= 0f || Time.time > s_holdUntil || npc == null) return false;
            WorkerNpc other = Slot(1 - (int)npc.Personality);
            if (other == null || !Resting(npc) || !Resting(other)) return false;
            if (Vector3.Distance(npc.transform.position, other.transform.position) > PairRange) return false;
            at = other.transform.position;
            return true;
        }

        public static void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            SpeakDue();
            if (Time.time < s_nextTick) return;
            s_nextTick = Time.time + 1f;
            WorkerNpc a = Slot(0), b = Slot(1);
            if (a == null && b == null) return;
            WatchBack(a);
            WatchBack(b);
            WatchRaid(a, b);
            double now = Now();
            if (!s_banter.Due(now) || s_pending.Count > 0) return;
            bool ra = Resting(a) && PlayerNear(a.transform.position, NearRange);
            bool rb = Resting(b) && PlayerNear(b.transform.position, NearRange);
            bool together = ra && rb && Vector3.Distance(a.transform.position, b.transform.position) <= PairRange;
            int alone = together ? -1 : ra ? 0 : rb ? 1 : -1;
            if (!together && alone < 0) { s_banter.Tick(now, false, -1, null, Names()); return; }
            var lines = s_banter.Tick(now, together, alone, Facts(a, b), Names());
            Schedule(lines, 0f, together);
        }

        /// <summary>WorkerNpc.Say가 말한 뒤 부른다: 그 말이 잡담 거리면 이벤트로.</summary>
        public static void OnSaid(WorkerNpc npc, string key, IDictionary<string, string> args)
        {
            if (npc == null || npc.IsGuard || ZNet.instance == null || !ZNet.instance.IsServer()) return; // 호위병은 잡담에 안 낌(대사·성격 나중에)
            BanterEvent e;
            Dictionary<string, string> ea = null;
            switch (key)
            {
                case "died": case "diedNoGrave": e = BanterEvent.Died; break;
                case "cantReach": case "homeFail": case "graveFail": case "craft.tooLong": e = BanterEvent.Failed; break;
                case "gatherDone":
                    if (args == null || !args.TryGetValue("n", out string ns) || !int.TryParse(ns, out int n) || n < 20) return;
                    e = BanterEvent.Haul; ea = Pick(args, "item", "n"); break;
                case "rescue": e = BanterEvent.Rescued; break;
                case "skillUp": e = BanterEvent.SkillUp; ea = Pick(args, "skill", "level"); break;
                case "flee": e = BanterEvent.Fled; ea = Pick(args, "foe"); break;
                case "exploreBiomeFound": e = BanterEvent.Biome; ea = Pick(args, "biome"); break;
                default: return;
            }
            Event((int)npc.Personality, e, ea);
        }

        static void Event(int subject, BanterEvent e, Dictionary<string, string> args)
        {
            WorkerNpc self = Slot(subject), other = Slot(1 - subject);
            bool near = self != null && other != null && !other.IsDead()
                && Vector3.Distance(self.transform.position, other.transform.position) <= NearRange
                && PlayerNear(self.transform.position, NearRange);
            var lines = s_banter.OnEvent(Now(), subject, e, args, near, Names());
            // 겪은 쪽 말(죽음 알림 등) 뒤에 반응이 오게 2초 띄운다. 둘 다 쉬는 중이면 서로 바라본다.
            Schedule(lines, 2f, Resting(self) && Resting(other));
        }

        static Dictionary<string, string> Pick(IDictionary<string, string> args, params string[] keys)
        {
            var d = new Dictionary<string, string>();
            if (args != null) foreach (var k in keys) if (args.TryGetValue(k, out string v) && v != null) d[k] = v;
            return d;
        }

        static void Schedule(List<BanterLine> lines, float first, bool hold)
        {
            if (lines == null || lines.Count == 0) return;
            float at = Time.time + first;
            foreach (var l in lines)
            {
                at += l.Delay;
                s_pending.Add(new Pending { At = at, Npc = l.Npc, Key = l.Key, Args = l.Args });
            }
            if (hold) s_holdUntil = at + HoldAfter;
        }

        static void SpeakDue()
        {
            for (int i = 0; i < s_pending.Count; i++)
            {
                var p = s_pending[i];
                if (Time.time < p.At) continue;
                s_pending.RemoveAt(i--);
                WorkerNpc n = Slot(p.Npc);
                if (n == null || n.IsDead()) continue; // 말할 NPC가 없으면 그 줄은 건너뜀
                ChatNet.NpcBubble(n, n.Line(p.Key, p.Args));
                Said++;
                SaidKeys.Add(n.NpcName + ":" + p.Key);
            }
        }

        // ---- 이벤트 감시: 습격 끝·멀리 갔다 돌아옴 ----

        static void WatchBack(WorkerNpc n)
        {
            if (n == null || n.IsDead()) return;
            ZDOID id = n.GetZDOID();
            bool outside = Utils.DistanceXZ(n.transform.position, n.Home) > BaseArea.Radius;
            if (outside) { if (!s_outSince.ContainsKey(id)) s_outSince[id] = Time.time; return; }
            if (!s_outSince.TryGetValue(id, out float since)) return;
            s_outSince.Remove(id);
            if (Time.time - since >= BackAfter) Event((int)n.Personality, BanterEvent.Back, null);
        }

        static void WatchRaid(WorkerNpc a, WorkerNpc b)
        {
            var ev = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
            bool raid = false;
            foreach (var n in new[] { a, b })
                if (ev != null && n != null && Utils.DistanceXZ(n.Home, ev.m_pos) < ev.m_eventRange) raid = true;
            if (s_raidWas && !raid)
            {
                int subject = a != null && !a.IsDead() ? (b != null && !b.IsDead() ? s_rng.Next(2) : 0) : 1;
                Event(subject, BanterEvent.Raid, null);
            }
            s_raidWas = raid;
        }

        // ---- 기억 모으기 (잡담 때만) ----

        static BanterFacts Facts(WorkerNpc a, WorkerNpc b)
        {
            var f = new BanterFacts();
            try
            {
                WorkerNpc any = a ?? b;
                Vector3 home = any.Home;
                ZoneXY hz = Senses.ZoneOf(home);
                f.FromPlaces(Brain.Places.All, hz, FoeName);
                int homeBiome = WorldGenerator.instance != null ? (int)WorldGenerator.instance.GetBiome(home) : 0;
                f.FromKnowledge(Brain.Get(), hz, homeBiome, GatherJob.ItemLabel, s_rng.Next);
                f.Crop = FieldCrop();
                for (int i = 0; i < 2; i++)
                {
                    WorkerNpc n = i == 0 ? a : b;
                    if (n == null) continue;
                    var raised = n.SkillLevels.Raised();
                    if (raised.Count == 0) continue;
                    f.Skill[i] = WorkerNpc.SkillName((Skills.SkillType)raised[0].Key);
                    f.Level[i] = (int)raised[0].Value;
                }
                f.Night = EnvMan.IsNight();
                f.Player = NearPlayerName(any.transform.position, NearRange);
            }
            catch (Exception e) { Plugin.Log.LogWarning("잡담 기억 모으기 오류: " + e.Message); }
            return f;
        }

        static string FoeName(string kind)
        {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(kind) : null;
            Character c = go != null ? go.GetComponent<Character>() : null;
            return c != null ? Localization.instance.Localize(c.m_name) : null; // 떨어짐 같은 피해 종류는 이름 없음 → 그 주제 안 고름
        }

        /// <summary>"!밭"으로 정한 밭에 심긴 새싹 하나의 작물 이름. 없으면 null.</summary>
        static string FieldCrop()
        {
            var fields = Brain.Farms.Fields;
            if (fields.Count == 0) return null;
            foreach (var p in UnityEngine.Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
            {
                Vector3 pos = p.transform.position;
                bool inField = false;
                foreach (var fl in fields) if (fl.Contains(pos.x, pos.z)) { inField = true; break; }
                if (!inField) continue;
                Crop c = Crops.BySapling(Utils.GetPrefabName(p.gameObject));
                if (c != null) return GatherJob.ItemLabel(c.Produce);
            }
            return null;
        }
    }
}
