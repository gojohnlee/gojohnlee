using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// !탐험 [방향|바이옴] (구상서 8-2장): 지도 경계를 따라 새 땅을 밝힌다. 다니며 본 자원은 Senses가 모두 기억한다.
    /// 30분이 지나거나 밤이 되거나 더 갈 땅이 없으면 끝내고, 새로 둘러본 구역과 처음 본 것을 알린다(거점으로는 WorkerAI가 데려간다).
    /// </summary>
    public sealed class ExploreJob : IWorkerJob
    {
        const float Limit = 30f * 60f;
        const float StuckLimit = 20f;

        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        readonly int m_dirX, m_dirY;
        readonly int m_biome;
        readonly string m_where;
        readonly HashSet<string> m_knewItems = new HashSet<string>();
        /// <summary>지나간 구역들(보고에 쓴다).</summary>
        readonly HashSet<ZoneXY> m_visited = new HashSet<ZoneXY>();
        public int Visited => m_visited.Count;

        ZoneXY m_goal;
        Vector3 m_goalPos;
        bool m_walking;
        float m_time;
        float m_bestDist, m_progressTimer;
        bool m_saidBiome;
        Prep m_prep;

        public string StepName => m_prep != null ? "Prep" : m_walking ? "Walk" : "Pick";

        /// <param name="where">알림에 쓸 말("동쪽", "늪", "가까운 데").</param>
        public ExploreJob(WorkerNpc npc, int dirX, int dirY, int biome, string where)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            m_dirX = dirX;
            m_dirY = dirY;
            m_biome = biome;
            m_where = where;
            m_prep = new Prep(npc);
            Knowledge k = Brain.Get();
            foreach (var ik in k.Summary(Senses.ZoneOf(npc.Home))) m_knewItems.Add(ik.Item);
        }

        public string Label => m_npc.Line("state.explore", new Dictionary<string, string> { ["where"] = m_where });

        public string StateLine() => m_npc.Line("state.explore", new Dictionary<string, string> { ["where"] = m_where });

        /// <summary>Guard: 지금 목표 경계는 위험하다 → 둘러본 셈 치고 다음 경계로.</summary>
        public void AvoidHere(Vector3 danger)
        {
            Brain.Get().MarkLooked(Senses.ZoneOf(danger));
            if (m_walking) Brain.Get().MarkLooked(m_goal);
            m_walking = false;
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_visited.Add(Senses.ZoneOf(m_npc.transform.position));
            if (m_time > Limit) return Done("exploreDone");
            if (EnvMan.IsNight()) return Done("nightBack");
            if (m_prep != null) { if (m_prep.Update(dt)) return true; m_prep = null; } // 출발 준비(장비·음식)
            if (m_biome != 0 && !m_saidBiome && (Scout.BiomeAt(m_npc.transform.position) & m_biome) != 0)
            {
                // 그 바이옴에 들어섰다. 계속 그 바이옴 쪽을 둘러본다(NextFrontier가 그 옆 경계를 먼저 고른다).
                m_saidBiome = true;
                Scout.FromHome(m_npc.Home, m_npc.transform.position, out string dir, out string dist);
                m_npc.Say("exploreBiomeFound", new Dictionary<string, string> { ["biome"] = m_where, ["dir"] = dir, ["dist"] = dist });
            }
            if (!m_walking)
            {
                // 방향을 줬으면 그쪽만, 바이옴을 줬으면 그 바이옴으로 가 본 곳 옆부터(모르면 가까운 경계부터)
                if (!Scout.NextFrontier(m_npc, m_dirX, m_dirY, m_biome, out m_goal)) return Done("exploreNoMore");
                m_goalPos = Scout.GroundPos(m_goal);
                m_walking = true;
                m_bestDist = float.MaxValue;
                m_progressTimer = 0f;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 탐험: 다음 구역 {m_goal} (거점에서 {Utils.DistanceXZ(m_npc.Home, m_goalPos):0}m)");
                return true;
            }
            float d = Utils.DistanceXZ(m_goalPos, m_npc.transform.position);
            bool arrived = m_ai.MoveToPoint(dt, m_goalPos, 6f, true);
            if (d < m_bestDist - 1f) { m_bestDist = d; m_progressTimer = 0f; }
            else m_progressTimer += dt;
            if (arrived || m_progressTimer > StuckLimit || m_ai.WaterBlockedTime > WorkerAI.WaterGiveUp) // 깊은 물에 막히면 바로 다음 경계로
            {
                Brain.Get().MarkLooked(m_goal); // 갔거나 못 가는 곳: 다시 고르지 않는다
                m_ai.StopMoving();
                m_walking = false;
            }
            else if (Scout.Explored(m_goal)) m_walking = false; // 가는 길에 밝혀졌다: 경계가 밀려났으니 다음 경계로
            return true;
        }

        bool Done(string key)
        {
            Knowledge k = Brain.Get();
            var fresh = new List<string>();
            foreach (var ik in k.Summary(Senses.ZoneOf(m_npc.Home)))
                if (!m_knewItems.Contains(ik.Item)) fresh.Add(GatherJob.ItemLabel(ik.Item));
            if (fresh.Count > 5) fresh.RemoveRange(5, fresh.Count - 5);
            var args = new Dictionary<string, string>
            {
                ["where"] = m_where,
                ["zones"] = m_visited.Count.ToString(),
                ["new"] = fresh.Count > 0 ? m_npc.Line("exploreNew", new Dictionary<string, string> { ["items"] = string.Join(", ", fresh) }) : "",
            };
            if (key != "exploreDone") m_npc.Say(key, args);
            m_npc.Say("exploreDone", args);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 탐험 끝({key}): {m_time:0}초, 둘러본 구역 +{args["zones"]}, 처음 본 것 {string.Join(", ", fresh)}");
            return false;
        }
    }
}
