using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 밤사이 정산 가) 아침 정리(요청 R3): 잠자기 넘김이 끝난 순간 호스트에서 한 번, 넘김이 시작될 때 거점(깃발 30m 안)에 있던
    /// 쉬는 일꾼(Idle·Home)만. 보는 사람이 없으니 걷지 않고 바로 한다. 일꾼마다 그 아침 날짜(vh_settled)를 ZDO에 남겨 두 번 안 함.
    /// 지금 조각: 짐 → 상자(Unload를 걷기 없이). 다음 조각: 설비 꺼내기·채우기, 발효기, 밭.
    /// </summary>
    public static class MorningSettle
    {
        public const string KeyDay = "vh_settled";

        static readonly NightSettle s_skip = new NightSettle();
        static readonly HashSet<ZDOID> s_atBase = new HashSet<ZDOID>();

        /// <summary>시험이 넘김 없이 정산을 부를 때 쓴 마지막 결과(일꾼 이름 → 요약). 시험만 읽음.</summary>
        public static readonly Dictionary<string, string> LastResult = new Dictionary<string, string>();

        public static void Tick()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || EnvMan.instance == null) return;
            switch (s_skip.Step(EnvMan.instance.IsTimeSkipping()))
            {
                case NightSettle.Edge.Started: Snapshot(); break;
                case NightSettle.Edge.Ended: SettleAll(EnvMan.instance.GetDay()); break;
            }
        }

        /// <summary>넘김이 시작될 때 거점에 있는 일꾼을 기억한다(밤에 밖에 나간 일꾼은 빠짐).</summary>
        public static void Snapshot()
        {
            s_atBase.Clear();
            foreach (var npc in WorkerNpc.All)
                if (npc != null && npc.Zdo != null && NightSettle.AtBase(Utils.DistanceXZ(npc.transform.position, npc.Home))) s_atBase.Add(npc.Zdo.m_uid);
            Plugin.Log.LogInfo($"밤사이 정산: 넘김 시작, 거점에 있는 일꾼 {s_atBase.Count}명");
        }

        public static void SettleAll(int day)
        {
            LastResult.Clear();
            foreach (var npc in new List<WorkerNpc>(WorkerNpc.All))
            {
                if (npc == null || npc.Zdo == null || !s_atBase.Contains(npc.Zdo.m_uid)) continue;
                try { Settle(npc, day); }
                catch (System.Exception e) { Plugin.Log.LogError($"밤사이 정산 오류 {npc.NpcName}: {e}"); }
            }
            s_atBase.Clear();
        }

        static void Settle(WorkerNpc npc, int day)
        {
            ZDO zdo = npc.Zdo;
            if (!NightSettle.Due(day, zdo.GetInt(KeyDay, -1))) { Plugin.Log.LogInfo($"밤사이 정산: {npc.NpcName} {day}일 아침은 이미 함"); return; }
            if (!NightSettle.AtBase(Utils.DistanceXZ(npc.transform.position, npc.Home))) { Plugin.Log.LogInfo($"밤사이 정산: {npc.NpcName} 거점 밖이라 건너뜀"); return; }
            var mode = npc.Ai.Current;
            if (mode != WorkerAI.Mode.Idle && mode != WorkerAI.Mode.Home) { Plugin.Log.LogInfo($"밤사이 정산: {npc.NpcName} 일하는 중({mode})이라 건너뜀"); return; }
            zdo.Set(KeyDay, day); // 먼저 적는다: 정리 도중 오류가 나도 같은 아침에 되풀이하지 않음
            var parts = new List<string>();
            int stored = StoreBag(npc);
            if (stored > 0) parts.Add(npc.Line("settle.bag", new Dictionary<string, string> { ["n"] = stored.ToString() }));
            string summary = string.Join(", ", parts);
            LastResult[npc.NpcName] = summary;
            Plugin.Log.LogInfo($"밤사이 정산: {npc.NpcName} {day}일 아침 — {(parts.Count == 0 ? "할 것 없음" : summary)}");
            if (parts.Count > 0) npc.Say("settle", new Dictionary<string, string> { ["what"] = summary });
        }

        /// <summary>짐 정리를 걷지 않고 바로 한다. 넣은 물건 수를 돌려준다.</summary>
        static int StoreBag(WorkerNpc npc)
        {
            int before = Count(npc);
            var u = new Unload(npc) { Instant = true };
            for (int i = 0; i < 200 && u.Update(1f); i++) { }
            return Mathf.Max(0, before - Count(npc));
        }

        static int Count(WorkerNpc npc)
        {
            int n = 0;
            foreach (var it in npc.GetInventory().GetAllItems()) n += it.m_stack;
            return n;
        }
    }
}
