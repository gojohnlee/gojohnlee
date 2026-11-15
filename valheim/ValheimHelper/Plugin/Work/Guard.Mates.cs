using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>경계: 동료 부르기·도우러 오기·플레이어 돕기·도움 핑.</summary>
    public sealed partial class Guard
    {
        // ---- 동료 부르기(사용자 결정 2026-10-24 "더 공격적으로" 나) ----

        float m_mateAt = -999f;
        /// <summary>불러 놓고 물러나 기다리는 동료(오면 싸움, 못 오면 MateWait 뒤 원래 판단).</summary>
        WorkerNpc m_waitMate;
        /// <summary>그 기다림이 올린 지원 요청 이름(Brain.Help). 동료가 와서 싸움에 끼면 채우고, 아니면 지운다.</summary>
        string m_helpTask;
        float m_mateBest, m_mateStall;

        /// <summary>동료 기다림이 끝난다. 온 동료로 끝났으면 지원 요청을 채우고, 못 왔으면 지운다.</summary>
        void EndMateWait(bool came)
        {
            if (m_helpTask != null)
            {
                if (came) Brain.Help.Served(m_helpTask, 1);
                else Brain.Help.Remove(m_helpTask);
                m_helpTask = null;
            }
            m_waitMate = null;
        }
        /// <summary>나를 부른 동료(State.Come 동안 그쪽으로 달려감).</summary>
        WorkerNpc m_caller;
        float m_comeTime;
        /// <summary>마지막 동료 부르기(시험용): 부른 쪽·온 쪽 이름과 Wait/Join.</summary>
        public static string LastMateCall { get; internal set; } = "";

        /// <summary>
        /// 20m 안에 동료가 없고, 부를 수 있는 동료(MateCallRange 안·쉬거나 일하는 중 — 싸움·물러남·도망·오는 중·죽음·순간이동 아님)가 있으면
        /// 그 한 방을 합쳐 다시 판단해 Danger.ShouldCallMate대로 부른다. Wait이면 물러나 기다리고 true(이번 판단은 끝), Join이면 부르고 false(그대로 싸움).
        /// </summary>
        bool TryCallMate(Vector3 pos, Verdict alone, float myHit, System.Func<float, float> taken, bool mead, string foeName)
        {
            if (m_state == State.Flee || m_state == State.Come || m_waitMate != null || Time.time - m_mateAt < MateCallEvery) return false;
            if (m_npc.IsGuard) return false; // 호위병은 일꾼을 부르지 않는다(일꾼의 일을 끊지 않게)
            if (alone != Verdict.Fight && alone != Verdict.Back && alone != Verdict.Flee) return false;
            WorkerNpc mate = null;
            float best = float.MaxValue;
            foreach (var n in WorkerNpc.All)
            {
                if (n == null || n == m_npc || n.IsDead() || n.Ai == null) continue;
                float d = Vector3.Distance(n.transform.position, pos);
                if (d <= AllyRange) return false; // 이미 곁에 있다(MyHit에 들어 있음)
                if (d > MateCallRange || n.IsTeleporting() || (n.Ai.Guard != null && n.Ai.Guard.Engaged)) continue;
                if (d < best) { best = d; mate = n; }
            }
            if (mate == null) return false;
            Verdict together = Danger.Judge(m_npc.Personality, m_views, m_npc.GetHealth(), m_npc.GetMaxHealth(), myHit + HitOf(mate) * 0.9f, taken, mead);
            MateCall call = Danger.ShouldCallMate(alone, together, LastRatio, m_views.Count);
            if (call == MateCall.None) return false;
            // 동료 쪽에서 봐도(그 체력·갑옷) 같이면 싸움이어야 부른다(회차 147 vh_fighttest: 맨몸 체력 25 동료를 불러, 와서 혼자 물러나고 부른 쪽이 죽음)
            float scale = Game.instance.GetDifficultyDamageScalePlayer(pos) * Game.m_enemyDamageRate;
            Verdict theirs = Danger.Judge(mate.Personality, m_views, mate.GetHealth(), mate.GetMaxHealth(), HitOf(mate) + myHit * 0.9f,
                Taken(scale, mate.GetBodyArmor(), mate.BlockStats(), m_views.Count), mate.CanDrinkHealthMead());
            if (theirs != Verdict.Fight)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 동료 안 부름: {mate.NpcName} 쪽 판단 {theirs} (체력 {mate.GetHealth():0}/{mate.GetMaxHealth():0}, 갑옷 {mate.GetBodyArmor():0})");
                m_mateAt = Time.time; // 60초 동안 다시 셈하지 않음
                return false;
            }
            if (mate.Ai.Guard == null || !mate.Ai.Guard.Summon(m_npc)) return false;
            m_mateAt = Time.time;
            LastMateCall = $"{m_npc.NpcName}→{mate.NpcName} {call}";
            Plugin.Log.LogInfo($"{m_npc.NpcName} 동료 부름({call}): {mate.NpcName} {best:0}m, 혼자 {alone} 비율 {LastRatio:0.00} → 같이 {together}, {foeName} 등 {m_foes.Count}");
            m_npc.Say("callMate", Args(foeName));
            if (call == MateCall.Join) return false;
            m_waitMate = mate;
            m_helpTask = "호위:" + foeName;
            Brain.Help.Add(m_helpTask, 1);
            m_mateBest = float.MaxValue;
            m_mateStall = 0f;
            m_target = null;
            m_backTo = WaitSpotFor(pos, AwayPoint(pos, BackDistance), myHit, taken);
            m_backTimer = MateWait;
            m_backWalking = true;
            m_backWalkTime = 0f;
            m_chased = 0;
            m_state = State.Back;
            return true;
        }

        /// <summary>동료가 불렀다: 하던 일은 그대로 두고(경계가 일보다 먼저) 그쪽으로 달려가 같은 적과 싸운다. 바쁘면 false.</summary>
        public bool Summon(WorkerNpc caller)
        {
            if (m_state != State.Calm || m_npc.IsDead()) return false;
            m_caller = caller;
            m_comeTime = 0f;
            m_state = State.Come;
            m_judgeTimer = JudgeEvery;
            m_npc.Say("mateComing", new Dictionary<string, string> { ["name"] = caller.NpcName });
            return true;
        }

        /// <summary>
        /// 부름을 받고 가는 중인데 아직 부른 쪽 곁(AllyRange)이 아니고 바로 덤비는 적(CloseRange)도 없다: 혼자 판단으로 싸우거나 물러나지 않고 마저 간다
        /// (회차 147 vh_matetest: 온 동료가 혼자 싸움 판단으로 먼저 덤벼 체력 71→26).
        /// </summary>
        /// <summary>달려가는 쪽: 부른 동료, 없으면 거점 안 도망 뒤 도우러 가는 플레이어.</summary>
        Character ComeTo => m_caller != null ? (Character)m_caller : m_helpPlayer;

        bool ComingAlone()
        {
            Character c = ComeTo;
            if (m_state != State.Come || c == null || c.IsDead()) return false;
            return Vector3.Distance(c.transform.position, m_npc.transform.position) > AllyRange && !Chased(CloseRange);
        }

        /// <summary>거점 안 도망 뒤 그 적과 싸우는 플레이어(가서 돕는다, 적마다 한 번).</summary>
        Player m_helpPlayer;
        Character m_fleeFoe;
        bool m_fleeInBase;
        readonly HashSet<Character> m_helped = new HashSet<Character>();
        /// <summary>시험용: 마지막으로 도우러 간 플레이어 이름.</summary>
        public static string LastPlayerHelp { get; internal set; } = "";

        /// <summary>
        /// 거점 안 도망이 끝났을 때 플레이어가 그 적과 싸우고 있으면(적이 플레이어를 노리거나 플레이어가 적 10m 안) 가서 돕는다 — 사용자 10-28(decisions 도망 뒤 목표).
        /// 아니면 false(목표는 GatherJob.AvoidHere가 이미 버림 → 다른 일).
        /// </summary>
        bool TryHelpPlayer()
        {
            Character f = m_fleeFoe;
            m_fleeFoe = null;
            if (!m_fleeInBase || f == null || f.IsDead() || !m_helped.Add(f)) return false;
            Character ft = f.GetBaseAI()?.GetTargetCreature();
            Vector3 me = m_npc.transform.position;
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null || p.IsDead() || p.InGhostMode() || p.InDebugFlyMode() || Vector3.Distance(p.transform.position, me) > HelpRange) continue;
                if (ft != p && Vector3.Distance(p.transform.position, f.transform.position) > 10f) continue;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 도망 뒤: {p.GetPlayerName()}이(가) {Utils.GetPrefabName(f.gameObject)}와 싸움 → 도우러 감");
                LastPlayerHelp = p.GetPlayerName();
                m_helpPlayer = p;
                m_comeTime = 0f;
                m_state = State.Come;
                return true;
            }
            return false;
        }

        bool UpdateCome(float dt)
        {
            m_comeTime += dt;
            Character c = ComeTo;
            float d = c != null && !c.IsDead() ? Vector3.Distance(c.transform.position, m_npc.transform.position) : -1f;
            // 부른 쪽이 죽었거나 없어졌거나, 다 왔는데 적이 없거나, 너무 오래 걸리면 하던 일로
            if (d < 0f || m_comeTime > ComeLimit || (d < 8f && m_foes.Count == 0))
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} {(m_caller != null ? "동료" : "플레이어")}에게 가기 끝: {(d < 0f ? "부른 쪽 없음" : m_comeTime > ComeLimit ? "시간" : "적 없음")}");
                m_ai.StopMoving();
                m_caller = null;
                m_helpPlayer = null;
                m_state = State.Calm;
                return false;
            }
            if (d < 4f) m_ai.StopMoving();
            else m_ai.MoveToPoint(dt, c.transform.position, 3f, true);
            return true;
        }

        float m_helpAt = -999f;

        /// <summary>위급한데 근처(HelpRange)에 플레이어가 있으면 "도와줘요!!"+지도 핑(구상서 8-1). 60초에 한 번. 불렀으면 true.</summary>
        bool CallHelp(Vector3 pos, string foeName)
        {
            if (Time.time - m_helpAt < 60f) return false;
            bool near = false;
            foreach (var p in Player.GetAllPlayers())
                if (p != null && !p.IsDead() && !p.InGhostMode() && Vector3.Distance(p.transform.position, pos) <= HelpRange) { near = true; break; }
            if (!near) return false;
            m_helpAt = Time.time;
            m_npc.Say("help", Args(foeName));
            try { if (Chat.instance != null) Chat.instance.SendPing(pos); }
            catch (System.Exception e) { Plugin.Log.LogWarning("핑 실패: " + e.Message); }
            return true;
        }
    }
}
