using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 쉴 때 걸어서 상자 정리(상자 기능 ② 가-2): Tidy.Plan의 옮길 것마다 옮길 상자로 걸어가 그 물건을 가방 자리만큼 꺼내고 → 모을 상자로 걸어가 넣는다.
    /// 다 옮기거나 MaxTrips번·Limit초면 끝, 남은 짐(모을 상자가 꽉 참 등)은 Unload로 상자에. 가방에 이미 그 물건이 있으면 그 옮기기는 건너뜀(일꾼 것과 안 섞음).
    /// 누가 상자를 열고 있으면 EditWait초 기다리다 건너뜀. 도중에 다른 일·명령·싸움 위험이 오면(WorkerAI.Job이 바뀜) PutBack으로 든 것을 바로 상자에 되돌린다.
    /// 쉬는 일이라 대기열에 안 들어가고 새 명령에 바로 자리를 내준다(WorkerAI.Yields).
    /// 개인 상자 준비(상자 기능 ③)도 같은 걸음으로: 옮길 것 목록(plan)·상태 말(label)만 바꾸고 Move.Count만큼만 꺼낸다.
    /// </summary>
    public sealed class TidyJob : IWorkerJob
    {
        const float Limit = 180f, EditWait = 15f, WalkLimit = 40f;
        const int MaxTrips = 8;

        enum Step { Pick, ToFrom, ToTarget, Store }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        Step m_step = Step.Pick;
        Tidy.Move m_move;
        /// <summary>지금 들고 가는 물건(프리팹 이름), 없으면 null.</summary>
        string m_carry;
        int m_trips;
        float m_time, m_stepTime;
        bool m_abort;
        Unload m_unload;
        readonly HashSet<string> m_skip = new HashSet<string>();
        readonly Func<Vector3, List<Tidy.Move>> m_plan;
        readonly string m_label;

        /// <summary>옮긴 물건 수(시험용).</summary>
        public int Moved { get; private set; }
        public string StepName => "Tidy" + m_step;
        public string Label => m_npc.Line(m_label);
        public string StateLine() => m_npc.Line(m_label);
        public void AvoidHere(Vector3 danger) => m_abort = true;

        public TidyJob(WorkerNpc npc, Func<Vector3, List<Tidy.Move>> plan = null, string label = "state.tidy")
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_plan = plan ?? Tidy.Plan;
            m_label = label;
        }

        void Go(Step s)
        {
            m_step = s;
            m_stepTime = 0f;
            if (s == Step.Store) m_unload = new Unload(m_npc);
            m_ai.StopMoving();
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_stepTime += dt;
            if (m_abort) { PutBack(); return false; }
            if (m_time > Limit && m_step != Step.Store) { Plugin.Log.LogInfo($"{m_npc.NpcName} 상자 정리 시간 끝({m_time:0}초, 옮김 {Moved})"); Go(Step.Store); }
            switch (m_step)
            {
                case Step.Pick: return Pick();
                case Step.ToFrom: return UpdateFrom(dt);
                case Step.ToTarget: return UpdateTarget(dt);
                case Step.Store:
                    if (m_unload.Update(dt)) return true;
                    if (Moved > 0) BaseStock.Changed();
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 상자 정리 끝: {Moved}개, {m_trips}번, {m_time:0}초");
                    return false;
            }
            return false;
        }

        bool Pick()
        {
            if (m_trips >= MaxTrips) { Go(Step.Store); return true; }
            m_move = null;
            Inventory mine = m_npc.GetInventory();
            foreach (var m in m_plan(m_npc.Home))
                if (!m_skip.Contains(m.Name) && Tidy.Count(mine, m.Name) == 0) { m_move = m; break; }
            if (m_move == null) { Go(Step.Store); return true; }
            Go(Step.ToFrom);
            return true;
        }

        DoorWay m_way; Container m_wayFor;

        /// <summary>상자 앞까지 걷는다. 닿았으면 true, 못 닿으면(WalkLimit초) 그 물건 건너뜀.</summary>
        bool Reach(float dt, Container c, out bool failed)
        {
            failed = false;
            if (c == null) { failed = true; return false; }
            Vector3 at = c.transform.position;
            float d = Utils.DistanceXZ(at, m_npc.transform.position);
            if (d <= 2.5f || m_wayFor == c && m_way.Busy)
            {
                if (m_wayFor != c) { m_way = new DoorWay(); m_wayFor = c; }
                if (m_way.Steer(dt, m_npc, c)) return false; // 집 안 상자: 벽이 가리면 문으로(회차 314)
                m_ai.StopMoving(); m_ai.Face(at); return true;
            }
            m_ai.MoveToPoint(dt, at, 1.8f, false);
            if (m_stepTime > WalkLimit) failed = true;
            return false;
        }

        bool UpdateFrom(float dt)
        {
            if (!Reach(dt, m_move.From, out bool failed)) { if (failed) Skip("못 닿음"); return true; }
            Inventory mine = m_npc.GetInventory();
            string name = m_move.Name;
            int limit = m_move.Count;
            bool done = Chests.TryEdit(m_move.From, finv =>
            {
                if (limit > 0) { Tidy.TakeSome(finv, mine, name, limit); return; }
                foreach (var it in new List<ItemDrop.ItemData>(finv.GetAllItems()))
                    if (it.m_dropPrefab != null && it.m_dropPrefab.name == name && mine.CanAddItem(it, 1)) mine.MoveItemToThis(finv, it);
            });
            if (!done) { if (m_stepTime > WalkLimit + EditWait) Skip("상자 열림"); return true; }
            int got = Tidy.Count(mine, name);
            if (got == 0) { Skip("가방 자리 없음·이미 없음"); return true; }
            m_carry = name;
            m_trips++;
            Go(Step.ToTarget);
            return true;
        }

        bool UpdateTarget(float dt)
        {
            if (!Reach(dt, m_move.To, out bool failed)) { if (failed) { m_skip.Add(m_carry); m_carry = null; Go(Step.Store); } return true; }
            Inventory mine = m_npc.GetInventory();
            string name = m_carry;
            int before = Tidy.Count(mine, name);
            bool done = Chests.TryEdit(m_move.To, tinv =>
            {
                foreach (var it in new List<ItemDrop.ItemData>(mine.GetAllItems()))
                    if (it.m_dropPrefab != null && it.m_dropPrefab.name == name) tinv.MoveItemToThis(mine, it); // 자리만큼만
            });
            if (!done && m_stepTime < WalkLimit + EditWait) return true; // 누가 열고 있다
            int left = Tidy.Count(mine, name);
            Moved += before - left;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 상자 정리: {name} {before - left}개 옮김{(left > 0 ? $", {left}개 남음(자리 없음 → 짐 정리로)" : "")}");
            m_carry = null;
            if (left > 0) { m_skip.Add(name); Go(Step.Store); return true; } // 남은 건 Unload가 같은 물건 상자·빈자리로
            Go(Step.Pick);
            return true;
        }

        void Skip(string why)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 상자 정리: {m_move.Name} 건너뜀({why})");
            m_skip.Add(m_move.Name);
            Go(Step.Pick);
        }

        /// <summary>
        /// 도중에 그만둠(다른 일·명령·위험 — WorkerAI.Job이 바뀔 때 부름): 들고 있던 물건을 바로 꺼낸 상자로(안 되면 같은 물건·빈자리 상자로).
        /// 그래도 못 넣은 건 가방에 남고 다음 짐 정리(Unload)가 넣는다. 두 번 불려도 됨.
        /// </summary>
        public void PutBack()
        {
            if (m_carry == null) return;
            string name = m_carry;
            m_carry = null;
            Inventory mine = m_npc.GetInventory();
            int had = Tidy.Count(mine, name);
            if (had == 0) return;
            foreach (Container c in new[] { m_move?.From, Chests.ForItem(name, m_npc.Home) })
            {
                if (c == null) continue;
                Chests.TryEdit(c, inv =>
                {
                    foreach (var it in new List<ItemDrop.ItemData>(mine.GetAllItems()))
                        if (it.m_dropPrefab != null && it.m_dropPrefab.name == name) inv.MoveItemToThis(mine, it);
                });
                if (Tidy.Count(mine, name) == 0) break;
            }
            int left = Tidy.Count(mine, name);
            BaseStock.Changed();
            Plugin.Log.LogInfo($"{m_npc.NpcName} 상자 정리 그만둠: 든 {name} {had - left}개 상자에 되돌림{(left > 0 ? $", {left}개 가방에(다음 짐 정리)" : "")}");
        }
    }
}
