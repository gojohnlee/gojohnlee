using System.Collections.Generic;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 쉴 때 야생 가축 길들이기(사용자 11-09 요청 다-3, 첫 조각 = 먹이 주기): 집 TameFar 안 배고픈 야생 길들일 짐승(길들이는 중 > 멧돼지 > 가까운 것) 하나에게
    /// "가축" 상자의 그 종 먹이 1개를 꺼내 시야 밖(m_viewRange + OutOfSight)까지 다가가 곁에 던지고 그 자리에서 먹는지 본다.
    /// 길들이기는 배부르고 경보 아닐 때만 줄어(게임 사실: codemap) 배고파지면 다음 쉴 때 다시 준다. 길들이는 중인 짐승은 사냥·싸움에서 뺀다(Taming).
    /// </summary>
    public sealed class TameJob : IWorkerJob
    {
        const float TotalLimit = 5f * 60f, ReachLimit = 90f, WatchLimit = 40f, HomeLimit = 180f, BusyLimit = 10f;
        /// <summary>집에서 이 거리 안 야생만 길들인다.</summary>
        internal const float TameFar = 100f;
        /// <summary>짐승 시야(m_viewRange, 멧돼지 20m) 밖 이만큼에서 먹이를 곁에 던지고 그 자리에서 지켜봄 — 다가가면 덤벼 길들이기가 깨짐(회차 373 첫 시험).</summary>
        const float OutOfSight = 2f;

        /// <summary>데려오기: 짐승이 이만큼 안에 오면 우리에 다 왔다고 보고 풀어 둠.</summary>
        const float PenNear = 6f, LeadLimit = 120f;

        enum Step { Fetch, Approach, Watch, Reach, Lead, Home }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        readonly Character m_animal;
        readonly Container m_chest;
        readonly HashSet<string> m_foods;
        string m_taken;
        Vector3 m_watch;
        float m_dropFrom;
        Step m_step = Step.Fetch;
        float m_time, m_stepTime;
        bool m_abort, m_told, m_following;
        /// <summary>데려오기 모드(요청 다-3b): 길든 짐승을 이 우리 상자까지 따라오게 해 풀어 둠.</summary>
        readonly Container m_pen;

        /// <summary>떨군 먹이를 먹었으면 1.</summary>
        public int Fed { get; private set; }
        public string StepName => "Tame" + m_step;
        internal Character Animal => m_animal;
        public string Label => m_npc.Line("job.tame");
        public string StateLine() => m_npc.Line("state.feed", new Dictionary<string, string> { ["n"] = Fed.ToString() });
        /// <summary>
        /// 데려오다 그만두면(그만·집으로·따라와·급한 명령·쉬기·죽음) 짐승 따라오기를 풂 — 안 풀면 영영 일꾼을 따라다니고 우리 가축(Cullable)에서도 빠짐.
        /// 취소면 꺼낸 먹이도 상자에. 잠깐 멈춤이면 이어 할 때(Resume) 짐승 곁으로 다시 가서 따라오게 함.
        /// </summary>
        public void Stop(bool pause)
        {
            if (m_following && m_animal != null) m_animal.GetComponent<MonsterAI>().SetFollowTarget(null);
            m_following = false;
            if (!pause) ReturnFood();
        }

        public void Resume()
        {
            if (m_step == Step.Lead && !m_following && m_animal != null) Go(Step.Reach);
        }

        public void AvoidHere(Vector3 danger) => m_abort = true;

        void ReturnFood()
        {
            if (m_taken == null || m_chest == null || Chests.InUse(m_chest)) return;
            Inventory mine = m_npc.GetInventory();
            Chests.TryEdit(m_chest, inv =>
            {
                ItemDrop.ItemData it = LivestockJob.FoodIn(mine, new HashSet<string> { m_taken });
                if (it != null && inv.CanAddItem(it.m_dropPrefab, 1)) { inv.AddItem(it.m_dropPrefab, 1); mine.RemoveItem(it, 1); }
            });
            m_taken = null;
        }

        TameJob(WorkerNpc npc, Character animal, Container chest, HashSet<string> foods)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_animal = animal;
            m_chest = chest;
            m_foods = foods;
            MonsterAI mai = animal.GetComponent<MonsterAI>();
            m_dropFrom = (mai != null ? mai.m_viewRange : 20f) + OutOfSight;
        }

        TameJob(WorkerNpc npc, Character animal, Container pen) : this(npc, animal, null, new HashSet<string>())
        {
            m_pen = pen;
            m_step = Step.Reach;
        }

        /// <summary>우리로 데려올 길든 짐승: 우리 상자 PenRange 밖이고, 플레이어가 따르게 하거나 세워 둔 것(s_follow·s_patrol, 바닐라 Command)이 아닌 것.</summary>
        static bool Stray(Character a, MonsterAI mai, List<Container> pens, Dictionary<Container, string> species, out Container pen)
        {
            pen = null;
            ZDO zdo = a.GetComponent<ZNetView>().GetZDO();
            if (zdo == null || mai.GetFollowTarget() != null || zdo.GetBool(ZDOVars.s_patrol) || zdo.GetString(ZDOVars.s_follow) != "") return false;
            float best = float.MaxValue;
            foreach (var p in pens)
            {
                float d = Utils.DistanceXZ(p.transform.position, a.transform.position);
                if (d <= LivestockJob.PenRange) return false;
                if (d < best && Pens.Accepts(species, p, a)) { best = d; pen = p; } // 그 종을 받는 우리로
            }
            return pen != null;
        }

        /// <summary>길들이기가 시작된(남은 시간이 줄어든) 야생 짐승 — 일꾼이 사냥·싸움에서 뺀다.</summary>
        internal static bool Taming(Character c)
        {
            if (c == null || c.IsTamed()) return false;
            Tameable t = c.GetComponent<Tameable>();
            ZNetView v = c.GetComponent<ZNetView>();
            if (t == null || v == null || v.GetZDO() == null) return false;
            return v.GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, t.m_tamingTime) < t.m_tamingTime - 0.5f;
        }

        static float TameLeft(Character c, Tameable t) => c.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, t.m_tamingTime);

        /// <summary>거점 가축 상자(그 종을 받는 우리 — Pens)에 먹이가 있는 배고픈 야생 짐승 하나(없으면 null). 이유는 로그로.</summary>
        internal static TameJob For(WorkerNpc npc)
        {
            List<Container> pens = Pens.Near(npc.Home);
            if (pens.Count == 0) return null;
            var species = Pens.SpeciesMap(pens);
            Character best = null;
            Container bestChest = null;
            HashSet<string> bestFoods = null;
            float bestScore = float.MaxValue;
            int wild = 0;
            foreach (var a in Character.GetAllCharacters())
            {
                if (a == null || a.IsDead() || a is WorkerNpc || a.IsPlayer()) continue;
                Tameable t = a.GetComponent<Tameable>();
                MonsterAI mai = a.GetComponent<MonsterAI>();
                if (t == null || mai == null || t.m_tamingTime <= 0f) continue;
                float d = Utils.DistanceXZ(a.transform.position, npc.Home);
                if (d > TameFar) continue;
                if (a.IsTamed())
                {
                    if (Stray(a, mai, pens, species, out Container pen))
                    {
                        Plugin.Log.LogInfo($"길들이기(집 {npc.Home}): 길든 {a.m_name}을 우리 {pen.transform.position}로 데려옴");
                        return new TameJob(npc, a, pen);
                    }
                    continue;
                }
                wild++;
                if (!t.IsHungry() || mai.IsAlerted()) continue;
                HashSet<string> foods = LivestockJob.FoodsOf(a);
                if (foods.Count == 0 || LivestockJob.FoodOnGround(a.transform.position, foods)) continue;
                Container chest = pens.Find(p => Pens.Accepts(species, p, a) && LivestockJob.FoodIn(p.GetInventory(), foods) != null); // 종 우리면 그 종만
                if (chest == null) continue;
                float score = d + (Taming(a) ? 0f : 1000f) + (Utils.GetPrefabName(a.gameObject) == "Boar" ? 0f : 500f);
                if (score < bestScore) { bestScore = score; best = a; bestChest = chest; bestFoods = foods; }
            }
            Plugin.Log.LogInfo($"길들이기(집 {npc.Home}): 가축 상자 {pens.Count}, 야생 {wild}, 고름 {(best != null ? best.m_name : "없음")}");
            return best != null ? new TameJob(npc, best, bestChest, bestFoods) : null;
        }

        void Go(Step s)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: {m_step} → {s} (먹음 {Fed})");
            m_step = s;
            m_stepTime = 0f;
            m_ai.StopMoving();
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_stepTime += dt;
            if ((m_time > TotalLimit || m_abort || m_animal == null || m_animal.IsDead()) && m_step < Step.Home) Go(Step.Home);
            switch (m_step)
            {
                case Step.Fetch: return UpdateFetch();
                case Step.Approach: return UpdateApproach(dt);
                case Step.Watch: return UpdateWatch(dt);
                case Step.Reach: return UpdateReach(dt);
                case Step.Lead: return UpdateLead(dt);
                case Step.Home: return UpdateHome(dt);
            }
            return false;
        }

        // ── 챙기기: 그 우리 상자에서 먹이 1개(한 프레임) ──
        bool UpdateFetch()
        {
            if (m_chest == null) { Go(Step.Home); return true; }
            if (Chests.InUse(m_chest)) { if (m_stepTime > BusyLimit) Go(Step.Home); return true; }
            Inventory mine = m_npc.GetInventory();
            Chests.TryEdit(m_chest, inv =>
            {
                ItemDrop.ItemData it = LivestockJob.FoodIn(inv, m_foods);
                if (it == null) return;
                ItemDrop.ItemData one = it.Clone();
                one.m_stack = 1;
                if (!mine.AddItem(one)) return;
                inv.RemoveItem(it, 1);
                m_taken = it.m_dropPrefab.name;
            });
            if (m_taken == null) { Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: 먹이 못 꺼냄(짐 빈칸 {mine.GetEmptySlots()})"); Go(Step.Home); return true; }
            Go(Step.Approach);
            return true;
        }

        // ── 다가가기: 시야 밖까지만 가서 짐승 곁에 던짐 ──
        bool UpdateApproach(float dt)
        {
            Vector3 me = m_npc.transform.position, at = m_animal.transform.position;
            if (Utils.DistanceXZ(at, me) > m_dropFrom && !m_ai.MoveToPoint(dt, at, m_dropFrom - 1f, false))
            {
                if (m_stepTime > ReachLimit) { Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: {m_animal.m_name} 못 닿음 {at}"); Go(Step.Home); }
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(at);
            Inventory mine = m_npc.GetInventory();
            ItemDrop.ItemData it = LivestockJob.FoodIn(mine, new HashSet<string> { m_taken });
            if (it != null)
            {
                Vector3 away = me - at;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                Vector3 drop = at + away * 1.5f + Vector3.up * 0.5f;
                ItemDrop.DropItem(it, 1, drop, Quaternion.identity);
                mine.RemoveItem(it, 1);
                Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: {m_animal.m_name}에게 {m_taken} 떨굼 {drop}");
                m_taken = null;
                m_watch = at + away * (m_dropFrom + 1f);
            }
            Go(it != null ? Step.Watch : Step.Home);
            return true;
        }

        // ── 지켜보기: 시야 밖에서 먹을 때까지(경보면 길들이기가 안 줄어듦) ──
        bool UpdateWatch(float dt)
        {
            Tameable t = m_animal.GetComponent<Tameable>();
            if (t != null && !t.IsHungry())
            {
                Fed = 1;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: {m_animal.m_name} 먹음, 남은 시간 {TameLeft(m_animal, t):0}초, 길듦 {m_animal.IsTamed()}");
                Go(Step.Home);
                return true;
            }
            if (Utils.DistanceXZ(m_watch, m_npc.transform.position) > 2f) m_ai.MoveToPoint(dt, m_watch, 1.5f, true);
            else m_ai.Face(m_animal.transform.position);
            if (m_stepTime > WatchLimit) { Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: {m_animal.m_name} 안 먹음({WatchLimit:0}초)"); Go(Step.Home); }
            return true;
        }

        // ── 데려오기: 길든 짐승 곁(4m)까지 가서 따라오게 함 ──
        bool UpdateReach(float dt)
        {
            Vector3 at = m_animal.transform.position;
            if (Utils.DistanceXZ(at, m_npc.transform.position) > 4f && !m_ai.MoveToPoint(dt, at, 3f, true))
            {
                if (m_stepTime > ReachLimit) { Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: 길든 {m_animal.m_name} 못 닿음 {at}"); Go(Step.Home); }
                return true;
            }
            m_animal.GetComponent<MonsterAI>().SetFollowTarget(m_npc.gameObject);
            m_following = true;
            Go(Step.Lead);
            return true;
        }

        // ── 우리 상자까지 걸어감(짐승이 따라옴, 8m 넘게 처지면 기다림) → 다 오면 그 자리를 순찰 지점으로 풀어 둠(바닐라 "머물러" 꼴) ──
        static System.Action<BaseAI, Vector3> s_setPatrol;
        static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, float> s_moveTimer = HarmonyLib.AccessTools.FieldRefAccess<BaseAI, float>("m_randomMoveUpdateTimer");

        /// <summary>순찰 지점을 그 점으로(바닐라 private BaseAI.SetPatrolPoint(Vector3) — ZDO s_patrol·s_patrolPoint). 떠도는 범위는 m_randomMoveRange(멧돼지 10m) 둘레, 그 두 배 넘으면 돌아옴.</summary>
        internal static void PenAt(BaseAI ai, Vector3 at)
        {
            if (s_setPatrol == null) s_setPatrol = HarmonyLib.AccessTools.MethodDelegate<System.Action<BaseAI, Vector3>>(HarmonyLib.AccessTools.Method(typeof(BaseAI), "SetPatrolPoint", new[] { typeof(Vector3) }));
            s_setPatrol(ai, at);
            // 바닐라는 떠돌기 목표를 타이머(멧돼지 30초)가 끝날 때만 새로 고름 → 풀기 전 목표(회차 378: 순찰점에서 46m)로 계속 걸어감(E071). 바로 다시 고르게
            s_moveTimer(ai) = 0f;
        }

        bool UpdateLead(float dt)
        {
            if (m_pen == null) { Go(Step.Home); return true; }
            Vector3 pen = m_pen.transform.position, me = m_npc.transform.position, at = m_animal.transform.position;
            if (Utils.DistanceXZ(at, pen) < PenNear)
            {
                MonsterAI mai = m_animal.GetComponent<MonsterAI>();
                mai.SetFollowTarget(null);
                PenAt(mai, pen);   // 짐승 자리 말고 상자 자리 둘레를 떠돌게(자리 기준이면 10m 밖으로 떠냄, 회차 375)
                m_following = false;
                Fed = 1;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: 길든 {m_animal.m_name}을 우리에 데려옴 {at}");
                Go(Step.Home);
                return true;
            }
            if (Utils.DistanceXZ(at, me) > 8f) { m_ai.StopMoving(); m_ai.Face(at); }
            else if (Utils.DistanceXZ(pen, me) > 2.5f) m_ai.MoveToPoint(dt, pen, 2f, false);
            else m_ai.Face(at);
            if (m_stepTime > LeadLimit) { Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: 길든 {m_animal.m_name} 우리까지 못 옴 {at}"); Go(Step.Home); }
            return true;
        }

        bool UpdateHome(float dt)
        {
            Vector3 home = m_npc.Home;
            if (m_ai.MoveToPoint(dt, home, 2.5f, true) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) return Done();
            if (m_stepTime > HomeLimit)
            {
                if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return true; }
                return Done();
            }
            return true;
        }

        /// <summary>못 떨군 먹이는 그 상자에 되돌림(가득 차면 짐에 남음 — 다음 짐 내리기가 넣음).</summary>
        bool Done()
        {
            Stop(false);
            if (!m_told && Fed > 0)
            {
                m_told = true;
                m_npc.Say("tameDone", new Dictionary<string, string>());
            }
            return false;
        }
    }
}
