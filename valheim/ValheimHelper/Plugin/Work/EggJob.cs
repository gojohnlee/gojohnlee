using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 쉴 때 알 품기(사용자 11-10 가축 4 라): 거점 안 가축 상자에 알(EggGrow 있는 물건 — ChickenEgg·AsksvinEgg)이 있으면 한 번에 1개를 꺼내
    /// 거점 불(EffectArea Heat) 곁 자리에 한 개만 놓는다. 부화 조건은 게임 그대로(codemap EggGrow: 한 개짜리 떨굼·Heat 0.5m 안·m_requireUnderRoof면 덮임 0.7 이상).
    /// 같은 알이 4.5m 안에 있으면 그 자리는 안 씀(ItemDrop.AutoStackItems 4m — 겹치면 두 개짜리가 돼 안 깸). 그 종이 우리에 PenMax 넘게 될 것 같으면 안 함.
    /// 부화한 새끼는 길든 채라 데려오기는 TameJob이 맡음.
    /// </summary>
    public sealed class EggJob : IWorkerJob
    {
        const float TotalLimit = 3f * 60f, SpotLimit = 40f, HomeLimit = 180f, BusyLimit = 10f;
        /// <summary>자리를 고를 때 같은 알과 떨어질 거리(AutoStackItems 4m + 여유).</summary>
        internal const float EggGap = 4.5f;

        enum Step { Fetch, Place, Home }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        readonly Container m_chest;
        readonly string m_egg;
        readonly Vector3 m_spot;
        Step m_step = Step.Fetch;
        float m_time, m_stepTime;
        bool m_taken, m_abort;

        /// <summary>놓은 알 수.</summary>
        public int Placed { get; private set; }
        public Vector3 Spot => m_spot;
        public string StepName => "Egg" + m_step;
        public string Label => m_npc.Line("job.egg");
        public bool Yields => false;
        public Core.Work.WorkItem Save() => null;
        public string StateLine() => m_npc.Line("state.egg", new Dictionary<string, string> { ["n"] = Placed.ToString() });
        /// <summary>취소·쉬기면 꺼낸 알을 상자에 되돌림.</summary>
        public void Stop(bool pause) { if (!pause) ReturnEgg(); }
        public void Resume() { }
        public void AvoidHere(Vector3 danger) => m_abort = true;

        EggJob(WorkerNpc npc, Container chest, string egg, Vector3 spot)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_chest = chest;
            m_egg = egg;
            m_spot = spot;
        }

        static EggGrow GrowOf(ItemDrop.ItemData it) => it.m_dropPrefab != null ? it.m_dropPrefab.GetComponent<EggGrow>() : null;

        /// <summary>알에서 나와 자라서 될 종(CullJob.Species 꼴 — Chicken → Hen).</summary>
        internal static string SpeciesOf(EggGrow g)
        {
            if (g == null || g.m_grownPrefab == null) return null;
            Growup up = g.m_grownPrefab.GetComponent<Growup>();
            return Utils.GetPrefabName(up != null && up.m_grownPrefab != null ? up.m_grownPrefab : g.m_grownPrefab);
        }

        /// <summary>그 알을 깔 수 있는 자리(게임 EggGrow.CanGrow와 같은 판정 + 불길 밖 + 같은 알 EggGap 밖). 거점 불마다 둘레 1~3m 둘레를 땅 높이로 훑음. 없으면 null.</summary>
        /// <summary>마지막 FindSpot이 자리를 못 찾은 까닭 집계(로그용).</summary>
        internal static string LastWhy = "";

        internal static Vector3? FindSpot(Vector3 home, EggGrow g)
        {
            string name = Utils.GetPrefabName(g.gameObject);
            int ground = LayerMask.GetMask("Default", "static_solid", "terrain", "piece");
            var why = new int[6];
            foreach (EffectArea area in EffectArea.GetAllAreas())
            {
                if (area == null || (area.m_type & EffectArea.Type.Heat) == 0 || !Core.Work.BaseArea.Contains(Utils.DistanceXZ(area.transform.position, home))) continue;
                why[0]++;
                Vector3 c = area.transform.position;
                foreach (float r in new[] { 1.2f, 1.6f, 2f, 2.5f, 3f })
                    for (int i = 0; i < 12; i++)
                    {
                        float ang = i * 30f * Mathf.Deg2Rad;
                        Vector3 p = c + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                        if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, ground)) { why[1]++; continue; }
                        p = hit.point + Vector3.up * 0.1f;
                        int bad = Bad(p, g, name);
                        if (bad == 0) return p;
                        why[bad]++;
                    }
            }
            LastWhy = $"불 {why[0]}, 땅 없음 {why[1]}, 열 밖 {why[2]}, 불길 {why[3]}, 지붕 {why[4]}, 같은 알 {why[5]}";
            return null;
        }

        /// <summary>0 = 됨, 2 열 밖·3 불길·4 지붕·5 같은 알.</summary>
        static int Bad(Vector3 p, EggGrow g, string name)
        {
            if (g.m_requireNearbyFire && EffectArea.IsPointInsideArea(p, EffectArea.Type.Heat, 0.5f) == null) return 2;
            if (EffectArea.IsPointInsideArea(p, EffectArea.Type.Burning, 0.5f) != null) return 3;   // 모닥불 열 구역은 Heat|Fire 한 몸(8m) — 데는 곳은 Burning(0.5m)뿐(회차 739)
            if (g.m_requireUnderRoof)
            {
                Cover.GetCoverForPoint(p, out float cover, out bool underRoof, 0.1f);
                if (!underRoof || cover < g.m_requireCoverPercentige) return 4;
            }
            foreach (ItemDrop d in JobKit.DropsNear(p, EggGap)) if (Utils.GetPrefabName(d.gameObject) == name) return 5;
            return 0;
        }

        /// <summary>거점 안 그 종(새끼·땅의 알 포함) 수.</summary>
        static int Count(Vector3 home, string species, string egg)
        {
            int n = 0;
            foreach (var a in Character.GetAllCharacters())
                if (a != null && !a.IsDead() && a.IsTamed() && !(a is WorkerNpc) && !a.IsPlayer() && Utils.DistanceXZ(a.transform.position, home) <= Core.Work.BaseArea.Radius && CullJob.Species(a) == species) n++;
            foreach (ItemDrop d in JobKit.DropsNear(home, Core.Work.BaseArea.Radius)) if (Utils.GetPrefabName(d.gameObject) == egg) n += d.m_itemData.m_stack;
            return n;
        }

        /// <summary>가축 상자의 알 하나와 놓을 자리. 이유는 로그로.</summary>
        internal static EggJob For(WorkerNpc npc)
        {
            List<Container> pens = Pens.Near(npc.Home);
            var seen = new HashSet<string>();
            foreach (Container c in pens)
                foreach (var it in c.GetInventory().GetAllItems())
                {
                    EggGrow g = GrowOf(it);
                    string egg = it.m_dropPrefab != null ? it.m_dropPrefab.name : null;
                    if (g == null || !seen.Add(egg)) continue;
                    string species = SpeciesOf(g);
                    int have = Count(npc.Home, species, egg);
                    if (have >= CullJob.PenMax) { Plugin.Log.LogInfo($"알 품기: {egg} 안 함 — {species} {have}마리(상한 {CullJob.PenMax})"); continue; }
                    Vector3? spot = FindSpot(npc.Home, g);
                    if (spot == null) { Plugin.Log.LogInfo($"알 품기: {egg} 놓을 자리 없음(불 곁{(g.m_requireUnderRoof ? "·지붕 아래" : "")} — {LastWhy})"); continue; }
                    Plugin.Log.LogInfo($"알 품기: {egg} → {spot.Value} ({species} {have}마리)");
                    return new EggJob(npc, c, egg, spot.Value);
                }
            return null;
        }

        void Go(Step s)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 알 품기: {m_step} → {s} (놓음 {Placed})");
            m_step = s;
            m_stepTime = 0f;
            m_ai.StopMoving();
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_stepTime += dt;
            if ((m_time > TotalLimit || m_abort) && m_step < Step.Home) Go(Step.Home);
            switch (m_step)
            {
                case Step.Fetch: return UpdateFetch();
                case Step.Place: return UpdatePlace(dt);
                case Step.Home: return JobKit.ArriveHome(m_npc, dt, m_stepTime, HomeLimit) ? Done() : true;
            }
            return false;
        }

        ItemDrop.ItemData EggIn(Inventory inv)
        {
            foreach (var it in inv.GetAllItems()) if (it.m_dropPrefab != null && it.m_dropPrefab.name == m_egg) return it;
            return null;
        }

        bool UpdateFetch()
        {
            if (m_chest == null) { Go(Step.Home); return true; }
            if (Chests.InUse(m_chest)) { if (m_stepTime > BusyLimit) Go(Step.Home); return true; }
            Inventory mine = m_npc.GetInventory();
            Chests.TryEdit(m_chest, inv =>
            {
                ItemDrop.ItemData it = EggIn(inv);
                if (it == null) return;
                ItemDrop.ItemData one = it.Clone();
                one.m_stack = 1;
                if (!mine.AddItem(one)) return;
                inv.RemoveItem(it, 1);
                m_taken = true;
            });
            Go(m_taken ? Step.Place : Step.Home);
            return true;
        }

        bool UpdatePlace(float dt)
        {
            Vector3 me = m_npc.transform.position;
            if (!m_ai.MoveToPoint(dt, m_spot, 1.2f, false) && Utils.DistanceXZ(m_spot, me) > 1.6f)
            {
                if (m_stepTime > SpotLimit) { Plugin.Log.LogInfo($"{m_npc.NpcName} 알 품기: 자리 못 닿음 {m_spot}"); Go(Step.Home); }
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(m_spot);
            Inventory mine = m_npc.GetInventory();
            ItemDrop.ItemData it = EggIn(mine);
            if (it != null)
            {
                ItemDrop.DropItem(it, 1, m_spot, Quaternion.identity);
                mine.RemoveItem(it, 1);
                m_taken = false;
                Placed++;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 알 품기: {m_egg} 놓음 {m_spot}");
            }
            Go(Step.Home);
            return true;
        }

        void ReturnEgg()
        {
            if (!m_taken || m_chest == null || Chests.InUse(m_chest)) return;
            Inventory mine = m_npc.GetInventory();
            Chests.TryEdit(m_chest, inv =>
            {
                ItemDrop.ItemData it = EggIn(mine);
                if (it != null && inv.CanAddItem(it.m_dropPrefab, 1)) { inv.AddItem(it.m_dropPrefab, 1); mine.RemoveItem(it, 1); m_taken = false; }
            });
        }

        /// <summary>못 놓은 알은 상자에 되돌림(가득이면 짐에 남음 — 다음 짐 내리기가 넣음).</summary>
        bool Done()
        {
            ReturnEgg();
            if (Placed > 0) m_npc.Say("eggDone", new Dictionary<string, string>());
            return false;
        }
    }
}
