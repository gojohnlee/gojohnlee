using System.Collections.Generic;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 쉴 때 우리 솎기(사용자 11-09 요청 다-4): "가축" 상자 PenRange 둘레 길들인 것이 종별로 PenMax를 넘으면 넘는 수만큼 다 자란 것(Growup 없는 것, 성체 짝은 남김)을 잡아
    /// 떨군 것(고기·가죽)을 주워 돌아와 거점 상자에 넣는다(Unload — 요리에 쓰이게). 이름 붙인 것·따라가는 것은 안 잡는다.
    /// 일꾼 공격은 길든 짐승에 안 맞으므로(Attack: 비플레이어는 IsEnemy만 맞음 — codemap) 곁에서 휘두르며 HitData로 직접 피해를 준다.
    /// 떨굼은 쓰러진 몸(Ragdoll)이 사라질 때 생김(Ragdoll.m_ttl 뒤).
    /// </summary>
    public sealed class CullJob : IWorkerJob
    {
        const float TotalLimit = 4f * 60f, SpotLimit = 20f, HomeLimit = 180f, PickLimit = 20f;
        /// <summary>우리 하나에 한 종을 둘 수(새끼 포함). 넘으면 다 자란 것을 잡되 성체 짝은 남김(Herd).</summary>
        internal const int PenMax = 6;

        enum Step { Kill, Pick, Home, Store }

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        readonly List<Character> m_targets;
        Step m_step = Step.Kill;
        float m_time, m_stepTime, m_swing;
        bool m_abort, m_told;
        Vector3 m_deadAt;
        HashSet<string> m_drops;
        Unload m_unload;
        Dictionary<Character, Vector3> m_pens;

        /// <summary>잡은 수, 주운 것 수.</summary>
        public int Culled { get; private set; }
        public int Got { get; private set; }
        public string StepName => "Cull" + m_step;
        public string Label => m_npc.Line("job.cull");
        public string StateLine() => m_npc.Line("state.cull", new Dictionary<string, string> { ["n"] = Culled.ToString() });
        public void Stop(bool pause) { } // 남기는 것 없음(11-10 확인)
        public void Resume() { }
        public void AvoidHere(Vector3 danger) => m_abort = true;

        CullJob(WorkerNpc npc, List<Character> targets)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_targets = targets;
        }

        internal static CullJob For(WorkerNpc npc)
        {
            var pens = new Dictionary<Character, Vector3>();
            var t = Plan(npc.Home, pens);
            return t.Count > 0 ? new CullJob(npc, t) { m_pens = pens } : null;
        }

        /// <summary>잡아도 되는 것: 다 자람(Growup 없음), 이름 없음, 따라가는 대상 없음.</summary>
        internal static bool Cullable(Character a)
        {
            if (a.GetComponent<Growup>() != null) return false;
            ZNetView v = a.GetComponent<ZNetView>();
            ZDO zdo = v != null && v.IsValid() ? v.GetZDO() : null;
            MonsterAI mai = a.GetComponent<MonsterAI>();
            if (zdo == null || zdo.GetString(ZDOVars.s_tamedName) != "" || zdo.GetString(ZDOVars.s_follow) != "") return false;
            return mai == null || mai.GetFollowTarget() == null;
        }

        /// <summary>종 = 프리팹 이름, 새끼는 자라서 될 것(Growup.m_grownPrefab — Boar_piggy → Boar).</summary>
        internal static string Species(Character a)
        {
            Growup g = a.GetComponent<Growup>();
            return Utils.GetPrefabName(g != null && g.m_grownPrefab != null ? g.m_grownPrefab : a.gameObject);
        }

        /// <summary>그 우리(상자 pen) 둘레 길든 그 종 수(새끼 포함)와 잡아도 되는 성체.</summary>
        static List<Character> InPen(Vector3 pen, string species, out int total)
        {
            var adults = new List<Character>();
            total = 0;
            foreach (var a in Character.GetAllCharacters())
            {
                if (a == null || a.IsDead() || !a.IsTamed() || a is WorkerNpc || a.IsPlayer() || !LivestockJob.InPen(a, pen) || Species(a) != species) continue;
                total++;
                if (Cullable(a)) adults.Add(a);
            }
            return adults;
        }

        /// <summary>
        /// 우리마다 종별로(사용자 11-10 가축 1) 그 종이 PenMax를 넘는 수만큼 잡을 것 — 성체 짝(Herd.MinAdults)은 늘 남김(Core Herd.ToCull).
        /// 잡을 것마다 그 우리 자리를 pens에(잡기 직전 다시 셈). 이유는 로그로.
        /// </summary>
        internal static List<Character> Plan(Vector3 home, Dictionary<Character, Vector3> pens)
        {
            var list = new List<Character>();
            var done = new HashSet<string>();
            List<Container> all = Pens.Near(home);
            var species = Pens.SpeciesMap(all);
            foreach (Container c in all)
            {
                Vector3 pen = c.transform.position;
                foreach (var a in Character.GetAllCharacters())
                {
                    if (a == null || a.IsDead() || !a.IsTamed() || a is WorkerNpc || a.IsPlayer() || !LivestockJob.InPen(a, pen) || !Pens.Accepts(species, c, a)) continue; // 종 우리면 그 종만
                    string sp = Species(a);
                    if (!done.Add(sp + "@" + pen)) continue; // 우리·종마다 한 번
                    List<Character> adults = InPen(pen, sp, out int total);
                    int n = Herd.ToCull(total, adults.Count, PenMax);
                    for (int i = 0; i < n; i++) { if (pens.ContainsKey(adults[i])) continue; list.Add(adults[i]); pens[adults[i]] = pen; }
                    if (total > PenMax) Plugin.Log.LogInfo($"우리 솎기(집 {home}): 상자 {pen} 둘레 {sp} {total}마리, 잡아도 되는 성체 {adults.Count}(짝 {Herd.MinAdults} 남김), 잡을 것 {n}");
                }
            }
            return list;
        }

        /// <summary>잡기 직전 다시 셈: 그 사이 죽거나 데려가거나 새끼가 줄었으면 넘는 수·짝 남김을 다시 봄.</summary>
        bool StillOver(Character t)
        {
            if (m_pens == null || !m_pens.TryGetValue(t, out Vector3 pen)) return true;
            List<Character> adults = InPen(pen, Species(t), out int total);
            return adults.Contains(t) && Herd.ToCull(total, adults.Count, PenMax) > 0;
        }

        void Go(Step s)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 솎기: {m_step} → {s} (잡음 {Culled}, 주움 {Got})");
            m_step = s;
            m_stepTime = 0f;
            if (s == Step.Store) m_unload = new Unload(m_npc);
            m_ai.StopMoving();
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_stepTime += dt;
            if ((m_time > TotalLimit || m_abort) && m_step < Step.Home) Go(Step.Home);
            switch (m_step)
            {
                case Step.Kill: return UpdateKill(dt);
                case Step.Pick: return UpdatePick();
                case Step.Home: return UpdateHome(dt);
                case Step.Store:
                    if (m_unload.Update(dt)) return true;
                    Tell();
                    return false;
            }
            return false;
        }

        bool UpdateKill(float dt)
        {
            m_targets.RemoveAll(a => a == null || a.IsDead() || !Cullable(a));
            if (m_targets.Count == 0) { Go(Step.Home); return true; }
            Vector3 me = m_npc.transform.position;
            m_targets.Sort((a, b) => Utils.DistanceXZ(a.transform.position, me).CompareTo(Utils.DistanceXZ(b.transform.position, me)));
            Character t = m_targets[0];
            Vector3 at = t.transform.position;
            if (Utils.DistanceXZ(at, me) - t.GetRadius() > 1.8f)
            {
                m_ai.MoveToPoint(dt, at, 1.2f + t.GetRadius(), false);
                if (m_stepTime > SpotLimit)
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 솎기 건너뜀: {t.m_name} 못 닿음 {at}");
                    m_targets.Remove(t);
                    m_stepTime = 0f;
                }
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(t.GetCenterPoint());
            if (m_swing <= 0f && !StillOver(t)) { Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 솎기 그만: {t.m_name} 다시 세니 넘지 않음(짝 남김)"); m_targets.Remove(t); return true; }
            if (m_swing <= 0f) { m_npc.HoldWeapon(Guard.Mults(t.GetDamageModifiers())); if (!m_npc.InAttack()) m_npc.StartAttack(t, false); }
            m_swing += dt;
            if (m_swing < 0.6f) return true;
            m_swing = 0f;
            var hit = new HitData();
            hit.m_damage.m_damage = t.GetMaxHealth() * 2f + 100f;
            hit.m_point = t.GetCenterPoint();
            hit.m_dir = (at - me).normalized;
            hit.SetAttacker(m_npc);
            m_deadAt = at;
            m_drops = DropsOf(t);
            t.Damage(hit);
            Culled++;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 솎기: {t.m_name} 잡음 {at} (떨굼 {string.Join(",", m_drops)})");
            m_targets.Remove(t);
            Go(Step.Pick);
            return true;
        }

        static HashSet<string> DropsOf(Character c)
        {
            var set = new HashSet<string>();
            CharacterDrop cd = c.GetComponent<CharacterDrop>();
            if (cd != null) foreach (var d in cd.m_drops) if (d.m_prefab != null) set.Add(d.m_prefab.name);
            return set;
        }

        /// <summary>쓰러진 자리 둘레의 그 짐승 떨굼을 줍는다. 하나라도 주운 뒤 1초 더 없으면, 또는 PickLimit이 지나면 다음.</summary>
        bool UpdatePick()
        {
            bool any = false;
            foreach (Collider col in Physics.OverlapSphere(m_deadAt, 5f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d == null || !d.TryGetComponent(out ZNetView v) || !v.IsValid() || !m_drops.Contains(Utils.GetPrefabName(d.gameObject))) continue;
                any = true;
                int n = d.m_itemData.m_stack;
                if (m_npc.PickDrop(d)) { Got += n; m_pickedAt = m_stepTime; }
            }
            if (m_stepTime > PickLimit || !any && m_pickedAt > 0f && m_stepTime - m_pickedAt > 1f)
            {
                if (m_pickedAt <= 0f) Plugin.Log.LogInfo($"{m_npc.NpcName} 우리 솎기: {PickLimit:0}초 안에 떨굼 못 주움 {m_deadAt}");
                m_pickedAt = 0f;
                Go(Step.Kill);
            }
            return true;
        }

        float m_pickedAt;

        bool UpdateHome(float dt)
        {
            Vector3 home = m_npc.Home;
            if (m_ai.MoveToPoint(dt, home, 2.5f, true) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) { Go(Step.Store); return true; }
            if (m_stepTime > HomeLimit)
            {
                if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return true; }
                Go(Step.Store);
            }
            return true;
        }

        void Tell()
        {
            if (m_told || Culled == 0) return;
            m_told = true;
            m_npc.Say("cullDone", new Dictionary<string, string> { ["n"] = Culled.ToString() });
        }
    }
}
