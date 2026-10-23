using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 작업 상태 저장 (risks.txt "작업 상태 저장 설계", 묶음 1 작업 2): 하던 일 + 대기열 → NPC ZDO "vh_queue", 질문 대기 → "vh_pending".
    /// 재접속·서버 재시작·구역 내려감 뒤 NPC가 다시 생기면 불러온다. 하던 일은 대기열 맨 앞(Resume)으로 돌아와 한가해지면 바로 이어 간다.
    /// 소유자일 때만 읽고 쓴다. 불러오기 전에는 저장하지 않는다(빈 대기열로 덮어쓰지 않게). 스위치 Features.SaveWork. [게임 시험 안 됨]
    /// </summary>
    public static class WorkSaves
    {
        public const string KeyQueue = "vh_queue";
        public const string KeyPending = "vh_pending";

        const float Every = 1f;
        static float s_timer;
        static readonly Dictionary<WorkerNpc, string[]> s_saved = new Dictionary<WorkerNpc, string[]>();

        /// <summary>대기열 칸을 데이터에서 만든다(Start/Ready 람다).</summary>
        public static QueuedWork ToQueued(WorkerNpc npc, WorkItem w)
        {
            var q = new QueuedWork { Label = w.Label, Work = w };
            if (w.Ready == ReadyWhen.Morning) q.Ready = () => WorkState.IsReady(w, ZNet.instance.GetTimeSeconds(), EnvMan.IsNight());
            if (w.Kind == WorkKind.Gather)
            {
                if (w.Resume)
                    q.Start = () =>
                    {
                        HashSet<ToolKind> ways = ResourceCatalog.WaysFor(w.Item);
                        if (ways.Count == 0) return;
                        var job = new GatherJob(npc, w.Item, w.Amount, ways);
                        job.LoadStuck(w.Stuck); // 막힌 목적지는 불러와도 다시 안 감(사용자 결정 2026-10-23)
                        job.LoadPortal(w.PortalTag);
                        npc.Ai.StartJob(job);
                    };
                else
                    q.Start = () => Departures.Gather(new Speaker(w.Speaker ?? "", ZDOID.None, ZDOID.None), CommandOf(w), npc, true);
            }
            else if (w.Kind == WorkKind.Make)
                q.Start = () => Departures.ResumeMake(npc, w);
            else if (w.Kind == WorkKind.Farm)
                q.Start = () =>
                {
                    // 그 사이 밭을 지웠으면 안 한다. 계획은 지금 재고로 다시(FarmJob.ForField).
                    FarmField f = Brain.Farms.At(w.FarmX, w.FarmZ);
                    if (f != null) Commands.StartFarm(npc, f, false);
                    else Plugin.Log.LogInfo($"{npc.NpcName} 밭일 칸 건너뜀: ({w.FarmX:0},{w.FarmZ:0})에 밭이 없음");
                };
            else
            {
                if (w.Resume) q.Start = () => npc.Ai.StartJob(new ExploreJob(npc, w.DirX, w.DirY, w.BiomeId, w.Where ?? ""));
                else q.Start = () => Commands.StartExplore(npc, new ChatCommand { Kind = CommandKind.Explore, Arg = w.Arg, Biome = w.Biome, Target = w.Target });
            }
            return q;
        }

        public static ChatCommand CommandOf(WorkItem w) =>
            new ChatCommand { Kind = CommandKind.Item, Item = w.Item, Amount = w.Amount, Urgent = w.Urgent, Target = w.Target };

        public static WorkItem GatherItem(Speaker who, ChatCommand cmd, string label, ReadyWhen ready) => new WorkItem
        {
            Kind = WorkKind.Gather, Label = label, Item = cmd.Item, Amount = cmd.Amount, Speaker = who.Name,
            Target = cmd.Target, Urgent = cmd.Urgent, Ready = ready,
            NotBefore = ready == ReadyWhen.Morning && EnvMan.instance != null
                ? WorkState.NextMorning(ZNet.instance.GetTimeSeconds(), EnvMan.instance.m_dayLengthSec) : 0,
        };

        static readonly AccessTools.FieldRef<GatherJob, ZoneXY> GoalZone = AccessTools.FieldRefAccess<GatherJob, ZoneXY>("m_goalZone");
        static readonly AccessTools.FieldRef<GatherJob, Vector3> GoalPos = AccessTools.FieldRefAccess<GatherJob, Vector3>("m_goalPos");

        /// <summary>하던 일 → 이어 하기 칸(큰 단계만). 저장할 수 없는 일(묘비 회수 등)이면 null.</summary>
        public static WorkItem FromJob(IWorkerJob job)
        {
            if (job is GatherJob g)
            {
                int left = Mathf.Max(1, g.Target - g.Delivered);
                var w = new WorkItem { Kind = WorkKind.Gather, Resume = true, Item = g.Item, Amount = left, Label = GatherJob.ItemLabel(g.Item) + " " + left, Stuck = g.StuckSave, PortalTag = g.PortalCarryTag };
                if (GoalPos(g) != Vector3.zero) { w.HasZone = true; w.Zone = GoalZone(g); }
                return w;
            }
            if (job is PlanJob p && p.Goal != null) return p.Goal;
            if (job is FarmJob fj) return fj.ToWork();
            if (job is ExploreJob e) return e.ToWork();
            return null;
        }

        /// <summary>저장할 칸들: 하던 일(맨 앞) + 대기열(데이터가 있는 칸만).</summary>
        public static List<WorkItem> Snapshot(WorkerNpc npc)
        {
            var list = new List<WorkItem>();
            WorkerAI ai = npc.Ai;
            if (ai.Current == WorkerAI.Mode.Work && ai.Job != null)
            {
                WorkItem cur = FromJob(ai.Job);
                if (cur != null) list.Add(cur);
            }
            foreach (var q in ai.Queue)
            {
                if (q.Work == null) continue;
                // 멈춘 일(Resume 객체)은 지금 진행으로 수량을 새로
                list.Add(q.Resume != null ? FromJob(q.Resume) ?? q.Work : q.Work);
            }
            return list;
        }

        /// <summary>호스트만, 1초마다: 아직 안 불러온 NPC는 불러오고, 불러온 NPC는 바뀐 것만 저장.</summary>
        public static void Tick(float dt)
        {
            if (!Features.SaveWork || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            s_timer -= dt;
            if (s_timer > 0f) return;
            s_timer = Every;
            foreach (var npc in new List<WorkerNpc>(s_saved.Keys))
                if (npc == null) s_saved.Remove(npc);
            foreach (var npc in WorkerNpc.All)
            {
                if (npc == null || npc.Ai == null) continue;
                ZNetView view = npc.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;
                ZDO zdo = view.GetZDO();
                if (!s_saved.TryGetValue(npc, out string[] last))
                {
                    Load(npc, zdo);
                    s_saved[npc] = new[] { zdo.GetString(KeyQueue), zdo.GetString(KeyPending) };
                    continue;
                }
                string queue = WorkState.SaveQueue(Snapshot(npc));
                string pending = WorkState.SavePending(Departures.Snapshot(npc));
                if (queue != last[0]) zdo.Set(KeyQueue, queue);
                if (pending != last[1]) zdo.Set(KeyPending, pending);
                last[0] = queue;
                last[1] = pending;
            }
        }

        /// <summary>시험용: 그 NPC를 지금 바로 저장(다음 Tick을 기다리지 않음).</summary>
        public static void SaveNow(WorkerNpc npc)
        {
            ZDO zdo = npc.GetComponent<ZNetView>().GetZDO();
            string queue = WorkState.SaveQueue(Snapshot(npc)), pending = WorkState.SavePending(Departures.Snapshot(npc));
            zdo.Set(KeyQueue, queue);
            zdo.Set(KeyPending, pending);
            s_saved[npc] = new[] { queue, pending };
        }

        static void Load(WorkerNpc npc, ZDO zdo)
        {
            var items = WorkState.LoadQueue(zdo.GetString(KeyQueue));
            PendingData p = WorkState.LoadPending(zdo.GetString(KeyPending));
            if (items.Count == 0 && p == null) return;
            // 이미 이 NPC에 대기열이 있으면(같은 세션에서 막 받은 명령) 저장된 걸 뒤에 붙인다
            foreach (var w in items) npc.Ai.Queue.Add(ToQueued(npc, w));
            if (p != null) Departures.Restore(npc, p); // 3분 넘었으면 Departures.Tick이 바로 보류 처리(answer.timeout)
            Plugin.Log.LogInfo($"{npc.NpcName} 작업 상태 불러옴: 대기열 {items.Count}칸, 질문 대기 {(p != null ? p.Question.ToString() : "없음")}");
        }
    }
}
