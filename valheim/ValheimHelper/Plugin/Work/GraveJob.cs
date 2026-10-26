using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 묘비 회수 (구상서 9장 기본안): 장비 상자의 예비 장비를 차고(Prep) 묘비로 가서 들어가는 만큼 꺼내 가장 좋은 것을 입고,
    /// 거점에 돌아와 들고 다닐 것 말고는 상자에 넣는다(장비는 장비 상자, 나머지는 자원 상자).
    /// 밤이 되면 돌아왔다가 아침에 다시 간다(WorkerAI.Chores가 다시 시작). 못 닿으면(길·물 막힘) 시도 1번으로 치고,
    /// MaxTries번 실패하거나 거기서 또 죽으면(WorkerAI.OnDied) 위치만 알리고 포기한다. 지도 핀은 죽을 때 찍는다.
    /// </summary>
    public sealed class GraveJob : IWorkerJob
    {
        public const int MaxTries = 2;
        const float GoStuckLimit = 15f, TotalLimit = 20f * 60f; // 막힘 30 → 15초(사용자 결정 2026-10-26)

        enum Step { Prep, Go, Loot, Home, Store }

        public readonly ZDOID Grave;
        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        Step m_step = Step.Prep;
        Prep m_prep;
        float m_time, m_stepTime, m_bestDist = float.MaxValue, m_progress;
        Unload m_unload;

        public string StepName => "Grave" + m_step;

        public GraveJob(WorkerNpc npc, ZDOID grave)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            Grave = grave;
            m_prep = new Prep(npc);
            Vector3 pos = GravePos();
            Scout.FromHome(npc.Home, pos, out string dir, out string dist);
            npc.Say("graveGo", new Dictionary<string, string> { ["dir"] = dir, ["dist"] = dist });
        }

        public string Label => m_npc.Line("state.grave");

        public string StateLine() => m_npc.Line("state.grave");

        /// <summary>Guard: 묘비 쪽이 위험하다 → 이번 시도는 실패로 치고 돌아간다(다음에 다시, 2번이면 포기).</summary>
        public void AvoidHere(Vector3 danger) => m_abort = true;
        bool m_abort;

        Vector3 GravePos()
        {
            ZDO zdo = ZDOMan.instance.GetZDO(Grave);
            return zdo != null ? zdo.GetPosition() : m_npc.Home;
        }

        void Go(Step s)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 묘비: {m_step} → {s}");
            m_step = s;
            m_stepTime = 0f;
            m_bestDist = float.MaxValue;
            m_progress = 0f;
            if (s == Step.Store) m_unload = new Unload(m_npc);
            m_ai.StopMoving();
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_stepTime += dt;
            if (m_time > TotalLimit || m_abort) return Failed();
            switch (m_step)
            {
                case Step.Prep:
                    if (m_prep.Update(dt)) return true;
                    Go(Step.Go);
                    return true;
                case Step.Go: return UpdateGo(dt);
                case Step.Loot: return UpdateLoot();
                case Step.Home: return UpdateHome(dt);
                case Step.Store: return m_unload.Update(dt); // 들고 다닐 것 말고는 상자에 넣는다
            }
            return false;
        }

        bool UpdateGo(float dt)
        {
            if (ZDOMan.instance.GetZDO(Grave) == null) { m_npc.ForgetGrave(Grave); m_npc.Say("graveGone"); return false; }
            if (EnvMan.IsNight()) { m_npc.Say("nightBack"); return false; } // 아침에 Chores가 다시 시작
            Vector3 pos = GravePos();
            float d = Utils.DistanceXZ(pos, m_npc.transform.position);
            if (m_ai.MoveToPoint(dt, pos, 1.5f, d > 10f) || d < 2f) { Go(Step.Loot); return true; }
            if (d < m_bestDist - 1f) { m_bestDist = d; m_progress = 0f; }
            else m_progress += dt;
            if (m_progress > GoStuckLimit || m_ai.WaterBlockedTime > WorkerAI.WaterGiveUp) return Failed();
            return true;
        }

        bool UpdateLoot()
        {
            Container c = Chests.FromId(Grave);
            if (c == null)
            {
                if (ZDOMan.instance.GetZDO(Grave) == null) { m_npc.ForgetGrave(Grave); m_npc.Say("graveGone"); return false; }
                return m_stepTime < 10f || Failed(); // 묘비가 아직 안 불러와졌다
            }
            m_ai.Face(c.transform.position);
            Inventory mine = m_npc.GetInventory();
            int left = -1;
            bool done = Chests.TryEdit(c, inv =>
            {
                foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                    if (mine.CanAddItem(it)) mine.MoveItemToThis(inv, it);
                left = inv.NrOfItems();
            });
            if (!done) return m_stepTime < 20f || Failed(); // 누가 열고 있다
            Prep.WearBest(m_npc);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 묘비 회수: 남은 것 {left}개");
            if (left == 0) m_npc.ForgetGrave(Grave); // 빈 묘비는 바닐라가 지운다
            m_npc.Say(left == 0 ? "graveGot" : "graveGotSome");
            Go(Step.Home);
            return true;
        }

        bool UpdateHome(float dt)
        {
            Vector3 home = m_npc.Home;
            if (m_ai.MoveToPoint(dt, home, 2.5f, true) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) { Go(Step.Store); return true; }
            if (m_stepTime > 180f)
            {
                // 귀환 통일(요청 코드 정리 나): 깃발 순간이동은 WorkerAI.TeleportHome(도착 확인), 그것도 안 되면 그 자리에서 쉼.
                if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return true; }
                Go(Step.Store);
            }
            return true;
        }

        /// <summary>못 닿았다: 시도 1번으로 친다. 남은 시도가 있으면 다음에(한가할 때) 다시 간다.</summary>
        bool Failed()
        {
            m_npc.SetGraves(WorkerNpc.AddTry(m_npc.Graves(), Grave, 1));
            Plugin.Log.LogInfo($"{m_npc.NpcName} 묘비 회수 실패({m_step})");
            m_npc.Say("graveRetry");
            return false;
        }
    }
}
