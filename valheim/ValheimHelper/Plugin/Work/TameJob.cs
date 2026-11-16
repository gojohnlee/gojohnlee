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
        const float TotalLimit = 5f * 60f, ReachLimit = 90f, HomeLimit = 180f, BusyLimit = 10f;
        /// <summary>집에서 이 거리 안 야생만 길들인다.</summary>
        internal const float TameFar = 100f;
        /// <summary>짐승 시야(m_viewRange, 멧돼지 20m) 밖 이만큼에서 먹이를 곁에 던지고 바로 떠남 — 다가가면 덤벼 길들이기가 깨짐(회차 373 첫 시험), 서서 지켜보면 헛기다림(사용자 11-11 ②) → 먹었는지는 다음 For가 Checked로 확인.</summary>
        /// 2m였을 땐 멈추는 사이 짐승이 떠돌다 20m 안에 들어와 일꾼을 표적으로 잡고(경보 아님) 바닐라 m_fleeIfNotAlerted로 30초쯤 달아나 먹이에서 27m까지 멀어짐 → 90초 안에 못 먹음(회차 736 E127).
        const float OutOfSight = 8f;

        /// <summary>데려오기: 짐승이 이만큼 안에 오면 우리에 다 왔다고 보고 풀어 둠.</summary>
        const float PenNear = 6f, LeadLimit = 120f;

        enum Step { Fetch, Approach, Reach, Lead, Home }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        readonly Character m_animal;
        readonly Container m_chest;
        readonly HashSet<string> m_foods;
        string m_taken;
        float m_dropFrom;
        Step m_step = Step.Fetch;
        float m_time, m_stepTime;
        bool m_abort, m_told, m_following;
        /// <summary>데려오기 모드(요청 다-3b): 길든 짐승을 이 우리 상자까지 따라오게 해 풀어 둠.</summary>
        readonly Container m_pen;
        /// <summary>울타리 우리(가축 ⑥): 우리에 풀어 둔 같은 종 하나 — 상자 곁에서 울타리가 그것을 가리면 문 지나 들어가 그 순찰점에 풀어 둠(m_fenced). 아니면 상자 자리.</summary>
        Character m_mate;
        DoorWay m_way = new DoorWay();
        bool m_fenced, m_wentIn; // m_wentIn: 문을 지나 우리 안 — 그 뒤엔 우리 가축을 다시 겨누지 않음(문 열린 사이 우리 가축이 빠져나가면 안에서 다시 문으로 돌다 포기, 회차 743 시험)

        /// <summary>떨군 먹이를 먹었으면 1.</summary>
        public int Fed { get; private set; }
        /// <summary>먹이를 던지고 떠난 짐승 — 다음 For(1분마다)에서 먹었는지 보고 그 일꾼이 알림.</summary>
        static readonly List<KeyValuePair<Character, WorkerNpc>> s_dropped = new List<KeyValuePair<Character, WorkerNpc>>();

        static void Checked(WorkerNpc npc)
        {
            for (int i = s_dropped.Count - 1; i >= 0; i--)
            {
                Character a = s_dropped[i].Key;
                if (s_dropped[i].Value != npc && s_dropped[i].Value != null) continue;
                Tameable t = a != null && !a.IsDead() ? a.GetComponent<Tameable>() : null;
                if (t != null && t.IsHungry() && LivestockJob.FoodOnGround(a.transform.position, LivestockJob.FoodsOf(a))) continue;   // 아직 안 먹음
                s_dropped.RemoveAt(i);
                if (t == null || t.IsHungry()) continue;
                Plugin.Log.LogInfo($"{npc.NpcName} 길들이기 확인: {a.m_name} 먹음, 남은 시간 {TameLeft(a, t):0}초, 길듦 {a.IsTamed()}");
                npc.Say("tameDone", new Dictionary<string, string>());
            }
        }
        public string StepName => "Tame" + m_step;
        internal Character Animal => m_animal;
        public string Label => m_npc.Line("job.tame");
        public bool Yields => false;
        public Core.Work.WorkItem Save() => null;
        public string StateLine() => m_npc.Line("state.feed", new Dictionary<string, string> { ["n"] = Fed.ToString() });
        /// <summary>
        /// 데려오다 그만두면(그만·집으로·따라와·급한 명령·쉬기·죽음) 짐승 따라오기를 풂 — 안 풀면 영영 일꾼을 따라다니고 우리 가축(Cullable)에서도 빠짐.
        /// 취소면 꺼낸 먹이도 상자에. 잠깐 멈춤이면 이어 할 때(Resume) 짐승 곁으로 다시 가서 따라오게 함.
        /// </summary>
        public void Stop(bool pause)
        {
            Unherd();
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

        /// <summary>길들이기가 시작된(남은 시간이 줄어든) 야생 짐승, 또는 먹이를 던져 두고 먹기를 기다리는 짐승 — 일꾼이 사냥·싸움에서 뺀다(11-12: 던진 직후 7m에서 라그나가 그 멧돼지와 싸움, vh_livestocktest).</summary>
        internal static bool Taming(Character c)
        {
            if (c == null || c.IsTamed()) return false;
            foreach (var kv in s_dropped) if (kv.Key == c) return true;
            Tameable t = c.GetComponent<Tameable>();
            ZNetView v = c.GetComponent<ZNetView>();
            if (t == null || v == null || v.GetZDO() == null) return false;
            return v.GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, t.m_tamingTime) < t.m_tamingTime - 0.5f;
        }

        static float TameLeft(Character c, Tameable t) => c.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, t.m_tamingTime);

        /// <summary>사용자 11-10 가축 ④ 다): 늑대·록스·아스크스빈 야생은 덤비면 일꾼이 못 버팀 → 길들이러 갈 때 호위병(없으면 쉬는 동료)과 같이. 멧돼지(체력 10)는 혼자.</summary>
        internal static bool NeedsGuard(Character a) => a != null && !a.IsTamed() && s_strong.Contains(Utils.GetPrefabName(a.gameObject));
        static readonly HashSet<string> s_strong = new HashSet<string> { "Wolf", "Lox", "Asksvin" };

        /// <summary>거점 가축 상자(그 종을 받는 우리 — Pens)에 먹이가 있는 배고픈 야생 짐승 하나(없으면 null). 이유는 로그로.</summary>
        internal static TameJob For(WorkerNpc npc)
        {
            Checked(npc);
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
                float score = d + (Taming(a) ? 0f : 1000f);   // 센 종은 호위병과 감(Departures.IdleTame — NeedsGuard)
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
                s_dropped.Add(new KeyValuePair<Character, WorkerNpc>(m_animal, m_npc));
            }
            Go(Step.Home);   // 지켜보지 않음 — 집 쪽(짐승 반대)으로 떠나 경보 범위 밖에서 다른 일, 먹었는지는 Checked
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
            m_mate = PenMate(m_animal, m_pen);
            m_way = new DoorWay { PenGate = true }; m_fenced = false; m_wentIn = false; m_settleT = -1f;
            Go(Step.Lead);
            return true;
        }

        // ── 우리 상자까지 걸어감(짐승이 따라옴, 8m 넘게 처지면 기다림) → 다 오면 그 자리를 순찰 지점으로 풀어 둠(바닐라 "머물러" 꼴) ──
        static System.Action<BaseAI, Vector3> s_setPatrol;
        static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, float> s_moveTimer = HarmonyLib.AccessTools.FieldRefAccess<BaseAI, float>("m_randomMoveUpdateTimer");
        static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, bool> s_moveReached = HarmonyLib.AccessTools.FieldRefAccess<BaseAI, bool>("m_reachedRandomMoveTarget");

        /// <summary>순찰 지점을 그 점으로(바닐라 private BaseAI.SetPatrolPoint(Vector3) — ZDO s_patrol·s_patrolPoint). 떠도는 범위는 m_randomMoveRange(멧돼지 10m) 둘레, 그 두 배 넘으면 돌아옴.</summary>
        internal static void PenAt(BaseAI ai, Vector3 at)
        {
            if (s_setPatrol == null) s_setPatrol = HarmonyLib.AccessTools.MethodDelegate<System.Action<BaseAI, Vector3>>(HarmonyLib.AccessTools.Method(typeof(BaseAI), "SetPatrolPoint", new[] { typeof(Vector3) }));
            s_setPatrol(ai, at);
            // 바닐라는 떠돌기 목표를 타이머(멧돼지 30초)가 끝날 때만 새로 고름 → 풀기 전 목표(회차 378: 순찰점에서 46m)로 계속 걸어감(E071). 바로 다시 고르게
            s_moveTimer(ai) = 0f;
            Calm(ai);
        }

        static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, Vector3> s_moveTarget = HarmonyLib.AccessTools.FieldRefAccess<BaseAI, Vector3>("m_randomMoveTarget");
        /// <summary>울타리 우리에 풀 때: 떠돌기 목표를 지금 자리로, 다음 고르기는 한 간격(멧돼지 30초) 뒤로 — 바로 고르면 순찰점 둘레 2.8~4m 목표가 벽 밖일 때
        /// 일꾼이 나가는 사이 열린 문으로 길을 찾아 나감(회차 744 시험 13). 문이 닫힌 뒤엔 벽 밖 목표로 길이 없어 안에 머묾.</summary>
        static void Hold(BaseAI ai) => Hold(ai, ai.transform.position);
        static void Hold(BaseAI ai, Vector3 at)
        {
            s_moveTarget(ai) = at;
            s_moveReached(ai) = false;   // 바닐라는 이 표시가 true면 새 목표를 고를 때까지 안 걸음(BaseAI.RandomMovement) — 문간에 그대로 섬(회차 746)
            s_moveTimer(ai) = ai.m_randomMoveInterval;
        }

        /// <summary>우리 안 얌전: 떠돌기 범위(멧돼지 프리팹 10m → 순찰점 둘레 20m까지 떠돎)를 바닐라 기본 4m로. 프리팹 값이라 저장 안 됨 → 불러온 뒤엔 LivestockJob.Plan이 우리 가축마다 다시.</summary>
        internal const float PenRoam = 4f;
        internal static void Calm(BaseAI ai)
        {
            if (ai != null && ai.m_randomMoveRange > PenRoam) ai.m_randomMoveRange = PenRoam;
        }

        /// <summary>그 우리(상자 PenRange 안·순찰점)에 풀어 둔 같은 종 길든 것 중 순찰점이 상자에 가장 가까운 것.</summary>
        static Character PenMate(Character animal, Container pen)
        {
            if (pen == null) return null;
            string sp = CullJob.Species(animal);
            Vector3 at = pen.transform.position;
            Character best = null; float bd = float.MaxValue;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == animal || c.IsDead() || !c.IsTamed() || CullJob.Species(c) != sp || !LivestockJob.InPen(c, at)) continue;
                ZDO zdo = c.GetComponent<ZNetView>()?.GetZDO();
                if (zdo == null || !zdo.GetBool(ZDOVars.s_patrol)) continue;
                float d = Utils.DistanceXZ(PatrolOf(c), at); // 지금 자리 말고 순찰점(우리 밖 순찰점인 새끼 등은 빼려고, 회차 743 시험)
                if (d > LivestockJob.PenRange) continue;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        static Vector3 PatrolOf(Character c) => c.GetComponent<ZNetView>().GetZDO().GetVec3(ZDOVars.s_patrolPoint, c.transform.position);


        /// <summary>E128 회차 745: 데려오다 시간 넘김 — 따라오기를 안 풀면 짐승이 집 가는 일꾼을 계속 따라감(m_follow는 저장 안 돼 불러오기 전까지). 선 자리 둘레에 풂.</summary>
        void Release()
        {
            Unherd();
            if (m_animal == null) return;
            var mai = m_animal.GetComponent<MonsterAI>();
            if (mai == null) return;
            mai.SetFollowTarget(null);
            PenAt(mai, m_animal.transform.position);
            m_following = false;
        }

        float m_settleT = -1f; Vector3 m_settleIn;
        float m_slowT, m_herdT = -1f; bool m_herdIn; // m_herdIn: 문 바깥 앞에 닿음 — 그 뒤엔 안쪽으로만(문짝이 레이캐스트를 가려 되돌아가지 않게)
        /// <summary>E128 회차 746: 일꾼이 문 지나 안에 있는데 따라오던 짐승이 문 밖에서 2초 넘게 멈춤(열린 문 둘레 길 못 찾음 — MoveTo는 길 없으면 StopMoving) → 짐승 AI를 잠시 끄고 일꾼 문 지나기처럼 수평 MoveTowards로 문 바깥 앞 → 안쪽 2.5m, 10초 상한·못 하면 되돌림.</summary>
        void Herd(float dt, Vector3 at, Vector3 inward, bool past)
        {
            MonsterAI mai = m_animal.GetComponent<MonsterAI>();
            Vector3 mid = DoorWay.Middle(m_way.Gate);
            if (m_herdT < 0f)
            {
                bool near = Utils.DistanceXZ(at, mid) < 6f && !past;
                m_slowT = near && m_animal.GetVelocity().magnitude < 0.5f ? m_slowT + dt : 0f;
                if (m_slowT < 2f) return;
                m_herdT = 0f; m_herdIn = false; mai.enabled = false;
                Diag.Log($"DIAG {m_npc.NpcName} 길든 {m_animal.m_name} 문 밖에서 멈춤 → 문으로 몰기: 자리 {at:F1}·문틀 {mid:F1}·길 짐승 {mai.m_pathAgentType}");
            }
            m_herdT += dt;
            Vector3 rel = at - mid; rel.y = 0f;
            float depth = Vector3.Dot(rel, inward), side = (rel - inward * depth).magnitude;
            if (past || m_herdT > 10f) { Diag.Log($"DIAG {m_npc.NpcName} 문으로 몰기 끝 {m_herdT:0.0}초: 안쪽 {depth:0.0}m·옆 {side:0.0}m"); Unherd(); return; }
            Vector3 inPt = mid + inward * 2.5f, outPt = mid - inward * 1.2f; // 문 옆 벽 곁이면 먼저 문 바깥 앞으로(회차 746)
            if (Utils.DistanceXZ(at, outPt) < 0.6f || side < 0.5f && depth > -1.5f) m_herdIn = true; // 문 앞 통로에 들어선 뒤에만 안으로(벽에 붙으면 Linecast가 벽 충돌체 안에서 시작해 못 잡음)
            Vector3 to = m_herdIn ? inPt : outPt;
            Vector3 dir = to - at; dir.y = 0f;
            mai.MoveTowards(dir, false);
        }

        void Unherd()
        {
            m_slowT = 0f;
            if (m_herdT < 0f) return;
            m_herdT = -1f;
            if (m_animal == null) return;
            var mai = m_animal.GetComponent<MonsterAI>();
            mai.StopMoving(); mai.enabled = true;
        }
        bool UpdateLead(float dt)
        {
            if (m_settleT >= 0f)   // E128 회차 746·747: 풀자마자 나가면 문간·문 축(나가는 길)에 선 짐승을 밀어 문 밖으로 냄 — 문에서 2.5m 넘게 들어가고 문 축에서 1m 넘게 비킬 때까지(8초) 서서 기다림
            {
                m_settleT += dt; m_ai.StopMoving();
                float depth = 99f, aside = 99f;
                if (m_way.Gate != null) { Vector3 r = m_animal.transform.position - DoorWay.Middle(m_way.Gate); r.y = 0f; depth = Vector3.Dot(r, m_settleIn); aside = (r - m_settleIn * depth).magnitude; }
                if (depth > 2.5f && aside > 1f || m_settleT > 8f) { Diag.Log($"DIAG {m_npc.NpcName} 푼 {m_animal.m_name} 안쪽 {depth:0.0}m·옆 {aside:0.0}m까지 기다림 {m_settleT:0.0}초"); m_settleT = -1f; Go(Step.Home); }
                else m_ai.Face(m_animal.transform.position);
                return true;
            }
            if (m_pen == null) { Go(Step.Home); return true; }
            if (m_mate != null && (m_mate.IsDead() || !LivestockJob.InPen(m_mate, m_pen.transform.position))) m_mate = null;
            if (m_fenced && m_way.Passed == 0 && m_way.GaveUp) m_mate = null; // 문 지나기 전 문 길 포기 → 옛대로 상자 곁에 풂(지난 순간 "안에서도 가림" 포기는 무시)
            if (m_mate == null) m_fenced = m_wentIn = false;
            Vector3 pen = m_fenced ? PatrolOf(m_mate) : m_pen.transform.position, me = m_npc.transform.position, at = m_animal.transform.position;
            // 울타리 우리: 일꾼은 우리 가운데 너머(문 반대쪽 2.2m — 따라오는 짐승은 3m 뒤)까지 들어가고, 짐승이 문보다 1.5m 넘게 안쪽이면 풂(문 곁에서 풀면 일꾼이 나가는 사이 열린 문으로 떠나감, 회차 743)(열린 문 너머로 보인다고 문 밖에서 풀면 안 됨, 회차 742 시험)
            Vector3 deep = pen, inward = Vector3.zero; float stop = 2.5f; bool past = true;
            if (m_fenced && m_way.Gate != null)
            {
                Vector3 d = pen - DoorWay.Middle(m_way.Gate); d.y = 0f;
                inward = m_way.Gate.transform.forward; inward.y = 0f; inward.Normalize(); if (Vector3.Dot(inward, d) < 0f) inward = -inward; // 문짝 방향(DoorWay와 같음) — 순찰점 쪽은 비스듬할 수 있음(회차 746)
                Vector3 ain = at - DoorWay.Middle(m_way.Gate); ain.y = 0f; past = Vector3.Dot(ain, inward) > 1.5f && !DoorWay.Fenced(at, pen); deep = DoorWay.Middle(m_way.Gate) + inward * Mathf.Max(Vector3.Dot(d, inward) + 2.2f, 4.5f); stop = 1f; // 순찰점이 문간(0.6m)이면 짐승이 문간에 서서 나가는 일꾼에게 밀려 나감(E128 회차 746) — 문에서 4.5m 넘게
            }
            if (m_fenced && m_way.Passed > 0) m_wentIn = true;
            if (m_wentIn && m_way.Gate != null && !m_way.Busy) Herd(dt, at, inward, past); else Unherd();
            if (Utils.DistanceXZ(at, pen) < PenNear && (!m_fenced || m_wentIn && !m_way.Busy && past && !DoorWay.Blocked(m_animal, m_npc)))
            {
                Unherd();
                MonsterAI mai = m_animal.GetComponent<MonsterAI>();
                mai.SetFollowTarget(null);
                PenAt(mai, pen);   // 짐승 자리 말고 상자 자리(울타리 우리면 우리 가축의 순찰점) 둘레를 떠돌게(자리 기준이면 10m 밖으로 떠냄, 회차 375)
                if (m_fenced)   // 문에서 3m 넘게 안쪽, 문 축에서 1.6m 옆(짐승이 있던 쪽)으로 걸어가 섬 — 문간·문 축에 서면 나가는 일꾼에게 밀려 나감(E128 회차 747)
                {
                    Vector3 mid = DoorWay.Middle(m_way.Gate), side = Vector3.Cross(Vector3.up, inward), r = at - mid; r.y = 0f;
                    if (Vector3.Dot(r, side) < 0f) side = -side;
                    Hold(mai, mid + inward * Mathf.Max(Vector3.Dot(pen - mid, inward), 3f) + side * 1.6f);
                }
                m_following = false;
                Fed = 1;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: 길든 {m_animal.m_name}을 우리에 데려옴 {at}{(m_fenced ? $" — 울타리 안, 우리 가축 {m_mate.transform.position:F1}·순찰점 {pen:F1}·나 {me:F1}" : "")}");
                if (m_fenced) { m_settleT = 0f; m_settleIn = inward; } else Go(Step.Home);
                return true;
            }
            if (Utils.DistanceXZ(at, me) > 8f && !m_way.Busy || m_fenced && !m_wentIn && m_way.AtDoor(me) && Utils.DistanceXZ(at, me) > 3.5f) { m_ai.StopMoving(); m_ai.Face(at); }
            else if (m_mate != null && !m_wentIn && (Utils.DistanceXZ(pen, me) < 20f || m_way.Busy) && m_way.Steer(dt, m_npc, m_mate)) m_fenced = true; // 상자 곁에서 울타리가 우리 가축을 가리면 문 열고 들어가 그 순찰점까지 — 짐승은 따라옴(문은 열린 채, CloseDoors 끔)
            else if (Utils.DistanceXZ(deep, me) > stop) m_ai.MoveToPoint(dt, deep, stop * 0.8f, false);
            else m_ai.Face(at);
            if (m_stepTime > LeadLimit) { Plugin.Log.LogInfo($"{m_npc.NpcName} 길들이기: 길든 {m_animal.m_name} 우리까지 못 옴 {at}"); Release(); Go(Step.Home); }
            return true;
        }

        float m_homeDiag;
        bool UpdateHome(float dt)
        {
            // E128 회차 745: 울타리 안에 푼 짐승이 집 가는 일꾼 곁(1m)까지 따라 나옴 — 무엇이 끄는지 2초마다
            if (m_fenced && m_animal != null && m_stepTime < 30f && m_stepTime >= m_homeDiag)
            {
                m_homeDiag = m_stepTime + 2f;
                var mai = m_animal.GetComponent<MonsterAI>(); var tm = m_animal.GetComponent<Tameable>();
                Diag.Log($"DIAG {m_npc.NpcName} 푼 {m_animal.m_name} {m_stepTime:0}초: 자리 {m_animal.transform.position:F1}·나와 {Utils.DistanceXZ(m_animal.transform.position, m_npc.transform.position):0.0}m·따라감 {(mai != null && mai.GetFollowTarget() != null ? mai.GetFollowTarget().name : "-")}·표적 {(mai != null && mai.GetTargetCreature() != null ? mai.GetTargetCreature().name : "-")}·경보 {(mai != null && mai.IsAlerted())}·배고픔 {(tm != null && tm.IsHungry())}·떠돌기 목표 {s_moveTarget(mai):F1}·속도 {m_animal.GetVelocity().magnitude:0.0}");
            }
            return JobKit.ArriveHome(m_npc, dt, m_stepTime, HomeLimit) ? Done() : true;
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
