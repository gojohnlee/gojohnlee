using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public sealed partial class GatherJob
    {
        // ---- 줍기 ----

        bool UpdateCollect(float dt)
        {
            if (OutOfInterior(false)) return true;
            // 떨어지기를 기다린다: 줍는 것(Pickable)은 바로 생기고, 베고 캔 것은 통나무·바위가 부서지며 흩어진다.
            // 쓰러뜨린 나무·쪼갠 통나무가 멈추길 물러나 기다리던 것은 뺐다: 일꾼은 통나무에 안 다친다(NpcPatches.NoTreeHurt, 사용자 결정 10-25 "빼").
            float wait = m_targetInfo != null && m_targetInfo.Pickable ? 0.3f : 1.2f;
            if (m_stepTime < wait) { m_ai.Hold("떨어짐 기다림"); m_ai.StopMoving(); return true; }
            if (Full())
            {
                // 짐이 차서 남기고 간다: 내려놓고 와서 마저 줍는다(회차 118: 묻힌 은 72개 중 22개만 들고 가 남은 50개를 잊고 새 광맥 찾으러 감)
                if (NearestDrop(m_targetPos) != null) { m_leftDrops = m_targetPos; m_leftDropsTries = 0; }
                Go(Step.Return);
                return true;
            }
            ItemDrop drop = NearestDrop(m_targetPos);
            if (drop == null && m_hunt && m_stepTime < LootWait)
            {
                // 사냥감의 떨굼은 쓰러진 몸(Ragdoll)이 사라질 때 나온다 → 그 자리로 가서 기다린다(활로 잡으면 멀다)
                Walk(dt, m_targetPos, 1.5f);
                return true;
            }
            if (drop == null || m_stepTime > 30f)
            {
                if (drop == null && m_hunt) LogNoLoot(); // E036: 사냥감을 잡았는데 떨굼을 못 찾음
                Go(Step.Search); // 쓰러진 통나무가 있으면 Search가 먼저 고른다
                return true;
            }
            Vector3 dp = drop.transform.position;
            ZDOID dropId = drop.TryGetComponent(out ZNetView dv) && dv.IsValid() ? dv.GetZDO().m_uid : ZDOID.None;
            bool hand = !dropId.IsNone() && dropId == m_handDrop;
            float reach = hand ? HandWalk : 1.2f;
            if (Walk(dt, dp, reach) || InteriorNav.Reached(m_npc.transform.position, dp, hand ? HandWalk + 0.3f : 1.6f))
            {
                if (!hand) Pick(drop);
                else if (HandReach(drop, out string why)) { Plugin.Log.LogInfo($"{m_npc.NpcName}: 걸어서 못 닿는 {Item} — 손 닿는 데서 주움({why})"); Pick(drop); }
                else { Plugin.Log.LogInfo($"{m_npc.NpcName}: 걸어서 못 닿는 {Item} 안 보임/멂({why}) → 건너뜀"); m_dropSkip.Add(dropId); }
            }
            else if (!dropId.IsNone() && m_ai.InteriorNoPath(dp, reach))
            {
                // 지하묘지 안: 떨굼 1.2m 안까지 가는 길이 없다(더미가 있던 구석·턱 너머로 떨어짐, 회차 136 — 30초씩 헤매다 남김).
                // 플레이어처럼 손 닿는 거리(Player.m_maxInteractDistance, 눈에서 보이는 것)까지만 가 본다. 그 길도 없으면 그 떨굼은 건너뛴다.
                if (!hand) { m_handDrop = dropId; Plugin.Log.LogInfo($"{m_npc.NpcName}: {Item} 떨굼까지 묘지 안 길 없음 → 손 닿는 거리({HandWalk:0.0}m)까지만"); }
                else { Plugin.Log.LogInfo($"{m_npc.NpcName}: {Item} 떨굼 손 닿는 거리까지도 길 없음 → 건너뜀"); m_dropSkip.Add(dropId); }
            }
            return true;
        }

        /// <summary>걸어서 못 닿는 떨굼(지하묘지 안)은 이 수평 거리까지 다가가 손으로 줍는다(눈에서 HandReach 안·보일 때).</summary>
        const float HandWalk = 3f;
        /// <summary>손으로 줍는 거리: 바닐라 Player.m_maxInteractDistance(눈에서 맞은 점까지 5m).</summary>
        const float HandRange = 5f;
        /// <summary>손 닿는 거리로 줍는 중인 떨굼.</summary>
        ZDOID m_handDrop;
        /// <summary>지하묘지 안에서 걸어서도 손으로도 못 닿는 떨굼(이 일 동안 다시 안 고름 — 그것만 보고 줍기·캐기를 오가지 않게).</summary>
        readonly HashSet<ZDOID> m_dropSkip = new HashSet<ZDOID>();
        static int s_sightMask;

        /// <summary>눈에서 떨굼(충돌체 가운데)까지 HandRange 안이고 사이에 단단한 것이 없는가(플레이어가 보고 집을 수 있는 것).</summary>
        bool HandReach(ItemDrop drop, out string why)
        {
            if (s_sightMask == 0) s_sightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            Vector3 eye = m_npc.GetEyePoint();
            var col = drop.GetComponentInChildren<Collider>();
            Vector3 at = col != null ? col.bounds.center : drop.transform.position + Vector3.up * 0.1f;
            float d = Vector3.Distance(eye, at);
            if (d > HandRange) { why = $"눈에서 {d:0.0}m"; return false; }
            if (Physics.Linecast(eye, at, out RaycastHit h, s_sightMask, QueryTriggerInteraction.Ignore)) { why = $"눈에서 {d:0.0}m, 가림 {h.collider.name}"; return false; }
            why = $"눈에서 {d:0.0}m";
            return true;
        }

        static readonly Collider[] s_pickBuf = new Collider[32];
        static int s_itemMask;

        /// <summary>플레이어 자동 줍기와 같은 방식(물건 층 충돌체를 2m 안에서 찾음). 가볍다.</summary>
        void AutoPickup()
        {
            if (Full()) return;
            if (s_itemMask == 0) s_itemMask = LayerMask.GetMask("item");
            int n = Physics.OverlapSphereNonAlloc(m_npc.transform.position + Vector3.up, 2f, s_pickBuf, s_itemMask);
            for (int i = 0; i < n; i++)
            {
                ItemDrop drop = s_pickBuf[i] != null ? s_pickBuf[i].GetComponentInParent<ItemDrop>() : null;
                if (drop != null && Utils.GetPrefabName(drop.gameObject) == Item) Pick(drop);
            }
        }

        /// <summary>내 것이면 줍고 아니면 소유권 요청(WorkerNpc.PickDrop — 지워진 물건 또 줍기·NPC 오류 막음).</summary>
        void Pick(ItemDrop drop) => m_npc.PickDrop(drop);

        ItemDrop NearestDrop(Vector3 at)
        {
            ItemDrop best = null;
            float bestD = float.MaxValue; // 대상 14m 안(Nearby)에서 일꾼에게 가장 가까운 것 — 일꾼 기준 14m면 활로 잡은 먼 사냥감 것을 못 찾음
            foreach (var zdo in Senses.Nearby(at, 14f))
            {
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view == null) continue;
                ItemDrop drop = view.GetComponent<ItemDrop>();
                if (drop == null || Utils.GetPrefabName(drop.gameObject) != Item || m_dropSkip.Contains(zdo.m_uid)) continue;
                float d = Utils.DistanceXZ(drop.transform.position, m_npc.transform.position);
                if (d < bestD) { bestD = d; best = drop; }
            }
            return best;
        }

        /// <summary>E036 진단: 사냥감 자리 14m 안에 떨굼이 없을 때 40m 안의 같은 물건(가장 가까운 거리·높이)과 남은 쓰러진 몸 수.</summary>
        void LogNoLoot()
        {
            int items = 0, bodies = 0;
            string nearest = "없음";
            float bestD = float.MaxValue;
            foreach (var zdo in Senses.Nearby(m_targetPos, 40f))
            {
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view == null) continue;
                if (view.GetComponent<Ragdoll>() != null) { bodies++; continue; }
                ItemDrop drop = view.GetComponent<ItemDrop>();
                if (drop == null || Utils.GetPrefabName(drop.gameObject) != Item) continue;
                items++;
                Vector3 p = drop.transform.position;
                float d = Utils.DistanceXZ(p, m_targetPos);
                if (d < bestD) { bestD = d; nearest = $"{d:0.0}m 높이차 {p.y - m_targetPos.y:0.0}{(m_dropSkip.Contains(zdo.m_uid) ? " 건너뜀" : "")}"; }
            }
            Plugin.Log.LogInfo($"DIAG 떨굼 못 찾음 {Item}: {m_stepTime:0.0}초 기다림, 사냥감 자리 {m_targetPos}, 40m 안 같은 물건 {items}개(가장 가까운 {nearest}), 쓰러진 몸 {bodies}");
        }

        /// <summary>뿌리(ZDO 자리, 통나무 가운데)가 그 밑 땅에서 이만큼(m) 넘게 뜬 통나무는 다른 나무·바위에 걸린 것.</summary>
        const float HungHeight = 2f;

        /// <summary>
        /// 쪼개지는 통나무(Splits)가 다른 나무·바위에 걸려 높이 떠 있는지. 바닐라 휘두름은 거기까지 안 닿고 직접 피해(DirectHit)만 닿는데,
        /// 밑에서 쪼개면 반토막(TreeLog.Destroy)이 머리 위에 생겨 바로 떨어진다(E020 진단: 8.5m 위에 걸린 beech_log를 밑에서 직접 피해 5번 →
        /// 4m 위에 생긴 반토막이 4.2m/s로 떨어져 일꾼 죽음, 같은 자리에서 두 번). 반토막(더 안 쪼개짐)은 쪼개도 나무만 떨어져 괜찮다.
        /// </summary>
        static bool HungLog(ZDO zdo, ResourceInfo info)
        {
            if (info == null || !info.Splits) return false;
            Vector3 p = zdo.GetPosition();
            return ZoneSystem.instance.GetGroundHeight(p, out float ground) && p.y - ground > HungHeight;
        }

    }
}
