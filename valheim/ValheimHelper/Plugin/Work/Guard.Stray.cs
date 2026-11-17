using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 지나가며 떨어진 물건 줍기(사용자 11-18): 쉼(Idle)·집 가기(Home)·채집 구역 가기·돌아오기 중 둘레 StrayRange 안 땅 물건을 잠깐 들러 줍는다(가방에 — 상자엔 다음 Unload).
    /// 몬스터·저절로 생긴 것만: 플레이어가 한 번이라도 든 물건은 ItemData.m_pickedUp(ZDO에 저장), 플레이어가 버린 것은 m_autoPickup false(OnPlayerDrop),
    /// 일꾼이 일부러 놓은 것(먹이·알·넘친 결과)은 ZDO KeepKey — 셋 다 안 줍는다(게임 사실: codemap). 알(EggGrow)·헤엄치는 물고기(Fish)·건축물도 뺌.
    /// 한 물건에 StrayLimit초 못 주우면 그 물건은 다시 안 고른다.
    /// </summary>
    public sealed partial class Guard
    {
        public const string KeepKey = "vh_keep";
        const float StrayRange = 6f, StrayLimit = 6f, StrayEvery = 1f, StrayNearWork = 15f;
        /// <summary>플레이어 곁 이 거리 안 물건은 플레이어 몫(시험이 0으로 — 시험 플레이어가 거점에 서 있음).</summary>
        internal static float StrayNearPlayer = 4f;

        /// <summary>시험용: 시험 중엔 꺼짐(시험이 놓은 땅 물건을 가져가지 않게) — 켜는 시험만 true.</summary>
        public static bool TestStray;

        ItemDrop m_stray;
        float m_strayTime, m_strayScan;
        readonly HashSet<ZDOID> m_strayFailed = new HashSet<ZDOID>();

        /// <summary>일꾼이 일부러 땅에 놓는 물건: 지나가며 줍기에서 뺀다.</summary>
        internal static ItemDrop DropKept(ItemDrop.ItemData item, int amount, Vector3 at)
        {
            ItemDrop d = ItemDrop.DropItem(item, amount, at, Quaternion.identity);
            if (d != null && d.TryGetComponent(out ZNetView v) && v.IsValid()) v.GetZDO().Set(KeepKey, true);
            return d;
        }

        /// <summary>지나가며 주워도 되는 물건인가.</summary>
        internal static bool IsStray(ItemDrop d)
        {
            if (d == null || d.IsPiece() || !d.m_autoPickup || d.m_itemData.m_pickedUp) return false;
            if (!d.TryGetComponent(out ZNetView v) || !v.IsValid() || v.GetZDO().GetBool(KeepKey)) return false;
            if (d.GetComponent<Fish>() != null || d.GetComponent<EggGrow>() != null) return false;
            return Player.GetClosestPlayer(d.transform.position, StrayNearPlayer) == null; // 플레이어 곁 물건은 플레이어 몫
        }

        bool UpdateStray(float dt)
        {
            if (TestHook.Running && !TestStray) return false;
            if (m_npc.IsGuard || m_npc.IsDead() || !StrayTime()) { m_stray = null; return false; }
            if (m_stray == null || !m_stray.TryGetComponent(out ZNetView sv) || !sv.IsValid())
            {
                m_stray = null;
                m_strayScan -= dt;
                if (m_strayScan > 0f) return false;
                m_strayScan = StrayEvery;
                m_stray = FindStray();
                if (m_stray == null) return false;
                m_strayTime = 0f;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 떨어진 물건 주우러 {m_stray.m_itemData.m_shared.m_name} x{m_stray.m_itemData.m_stack} ({Vector3.Distance(m_stray.transform.position, m_npc.transform.position):0.0}m)");
            }
            m_strayTime += dt;
            ZDOID id = m_stray.GetComponent<ZNetView>().GetZDO().m_uid;
            if (m_strayTime > StrayLimit || !IsStray(m_stray) || !m_npc.GetInventory().CanAddItem(m_stray.m_itemData))
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 떨어진 물건 그만 {m_stray.m_itemData.m_shared.m_name} ({(m_strayTime > StrayLimit ? "시간 넘음" : "못 주울 것")})");
                m_strayFailed.Add(id);
                m_stray = null;
                return false;
            }
            if (Vector3.Distance(m_stray.transform.position, m_npc.transform.position) <= 1.8f)
            {
                string name = m_stray.m_itemData.m_shared.m_name;
                int n = m_stray.m_itemData.m_stack;
                if (m_npc.PickDrop(m_stray)) { Plugin.Log.LogInfo($"{m_npc.NpcName} 떨어진 물건 주움 {name} x{n}"); m_stray = null; m_strayScan = 0f; }
                m_ai.StopMoving();
                return true;
            }
            m_ai.MoveToPoint(dt, m_stray.transform.position, 1f, false);
            return true;
        }

        /// <summary>쉼·집 가기, 또는 채집의 구역 가기·돌아오기(그 밖 일 중엔 일이 떨굼을 직접 주우니 안 끼어듦).</summary>
        bool StrayTime() => m_ai.Current == WorkerAI.Mode.Idle || m_ai.Current == WorkerAI.Mode.Home
            || (m_ai.Current == WorkerAI.Mode.Work && m_ai.Job is GatherJob g && g.Traveling);

        ItemDrop FindStray()
        {
            Vector3 me = m_npc.transform.position;
            Inventory inv = m_npc.GetInventory();
            ItemDrop best = null;
            float bestD = float.MaxValue;
            foreach (ItemDrop d in JobKit.DropsNear(me, StrayRange))
            {
                if (Mathf.Abs(d.transform.position.y - me.y) > 1.5f || !IsStray(d) || !inv.CanAddItem(d.m_itemData)) continue;
                if (m_strayFailed.Contains(d.GetComponent<ZNetView>().GetZDO().m_uid) || OtherStray(d)) continue;
                float dist = Vector3.Distance(d.transform.position, me);
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }

        /// <summary>다른 일꾼이 이미 주우러 가는 물건, 또는 일하는(길 가는 중 아닌) 동료 곁 물건 — 그 일의 떨굼(회차 783: 쉬던 플로키가 라그나가 벤 나무를 주워 감).</summary>
        bool OtherStray(ItemDrop d)
        {
            foreach (var o in WorkerNpc.All)
            {
                if (o == m_npc || o.Ai == null) continue;
                if (o.Ai.StrayTarget == d) return true;
                if (o.Ai.Current == WorkerAI.Mode.Work && !(o.Ai.Job is GatherJob g && g.Traveling)
                    && Vector3.Distance(o.transform.position, d.transform.position) < StrayNearWork) return true;
            }
            return false;
        }

        internal ItemDrop StrayTarget => m_stray;
    }
}
