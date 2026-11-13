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
            if (m_hunt && m_loot != null)
            {
                // 사냥감의 떨굼은 쓰러진 몸(Ragdoll)이 사라질 때 그 몸 자리에 나온다 → 몸 자리로 가서 기다린다(활로 잡으면 멀다, LootWatch)
                bool waiting = m_loot.Waiting();
                m_targetPos = m_loot.Pos;
                if (drop != null) { if (m_loot.Seen(drop.transform.position)) m_stepTime = wait; } // 줍기 시간(30초·벽 확인)은 떨굼이 보인 때부터
                else if (waiting) { Walk(dt, m_targetPos, 1.5f); return true; }
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
            // 떨굼마다 막힘 판정을 새로 잰다(다가가기와 같은 Stuck — 거의 제자리 5초·1m도 못 가까워짐 MoveStuckLimit, 사용자 11-11 헛기다림 ④)
            if (dropId != m_collectDrop) { m_collectDrop = dropId; m_bestDist = float.MaxValue; m_progressTimer = 0f; }
            bool stuck = Stuck(dt, dp, true);
            if (!hand && !dropId.IsNone() && stuck && m_wallChecked != dropId && !m_npc.InInterior())
            {
                m_bestDist = float.MaxValue; m_progressTimer = 0f; // 캘 것으로 바꾸든 손 닿는 거리 단계로 가든 판정은 새로
                if (WalledIn(drop, dropId)) return true;
                hand = dropId == m_handDrop; reach = hand ? HandWalk : 1.2f; stuck = false;
            }
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
            else if (hand && stuck && !m_npc.InInterior())
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {Item} 떨굼 손 닿는 거리까지도 못 감 → 건너뜀");
                m_dropSkip.Add(dropId);
            }
            return true;
        }

        /// <summary>막힘 판정(Stuck)을 재는 떨굼 — 바뀌면 판정을 새로 시작.</summary>
        ZDOID m_collectDrop;
        /// <summary>막혔는지 본 떨굼(떨굼마다 한 번).</summary>
        ZDOID m_wallChecked;

        /// <summary>
        /// ⑬ 다) 바깥 떨굼까지 걸어서 못 감(회차 195: 거인 해골 안 뇌 giant_brain_frac의 부드러운 조직이 해골 벽 안쪽 땅에 떨어져 27초씩 벽에 막힘).
        /// 몸에서 떨굼까지 가린 것이 지금 연장으로 캘 수 있는 자원(해골 giant_skull_frac 등)이면 그것부터 캔다(대상 바꿈, 떨굼은 다음 Collect가 다시 고름).
        /// 아니면 손 닿는 거리(HandWalk)까지만 가 보고, 그래도 안 되면 그 떨굼은 건너뜀(지하묘지 안과 같은 길). true = 이번 프레임 처리함.
        /// </summary>
        bool WalledIn(ItemDrop drop, ZDOID dropId)
        {
            m_wallChecked = dropId;
            if (s_sightMask == 0) s_sightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            Vector3 from = m_npc.transform.position + Vector3.up, to = drop.transform.position + Vector3.up * 0.2f;
            string what = "없음";
            if (Physics.Linecast(from, to, out RaycastHit h, s_sightMask, QueryTriggerInteraction.Ignore))
            {
                ZNetView v = h.collider.GetComponentInParent<ZNetView>();
                ZDO z = v != null && v.IsValid() ? v.GetZDO() : null;
                ResourceInfo info = z != null && !m_skip.Contains(z.m_uid) ? ResourceCatalog.Get(z.GetPrefab()) : null;
                ItemDrop.ItemData tool = info != null && info.Tool != ToolKind.None && info.Tool != ToolKind.Hunt ? WorkerNpc.BestTool(m_npc.GetInventory(), info.Tool) : null;
                what = $"{h.collider.name}({(z != null ? Utils.GetPrefabName(v.gameObject) : "-")})";
                if (tool != null && (info.MinToolTier <= 0 || info.MinToolTier <= tool.m_shared.m_toolTier))
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName}: {Item} 떨굼({Utils.DistanceXZ(drop.transform.position, m_npc.transform.position):0.0}m, 높이차 {drop.transform.position.y - m_npc.transform.position.y:0.0})을 {info.Prefab}가 가림 → 그것부터 캠");
                    m_wallChecked = ZDOID.None; // 캔 뒤 같은 떨굼을 다시 보면 그 뒤에 걸린 것도 본다
                    Aim(z, info);
                    Go(Step.Approach);
                    return true;
                }
            }
            m_handDrop = dropId;
            Plugin.Log.LogInfo($"{m_npc.NpcName}: {Item} 떨굼까지 {m_stepTime:0}초 못 감(높이차 {drop.transform.position.y - m_npc.transform.position.y:0.0}, 가림 {what}) → 손 닿는 거리({HandWalk:0.0}m)까지만");
            return false;
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
        /// 밑에서 쪼개면 반토막(TreeLog.Destroy)이 머리 위에 생겨 바로 떨어진다(E020 진단: 8.5m 위 beech_log → 반토막에 맞아 일꾼 죽음).
        /// 일꾼은 나무 피해를 안 받으므로(NoTreeHurt, 회차 121) 이제 걸린 것도 밑에서 쪼갠다(사용자 11-01 "통나무 끝까지" 다) — 기록·시험용.
        /// </summary>
        internal static bool HungLog(ZDO zdo, ResourceInfo info)
        {
            if (info == null || !info.Splits) return false;
            Vector3 p = zdo.GetPosition();
            return ZoneSystem.instance.GetGroundHeight(p, out float ground) && p.y - ground > HungHeight;
        }

        /// <summary>통나무 가운데가 지형 윗면(ZoneSystem.GetGroundHeight — 위에서 쏜 지형 층 레이)보다 이만큼(m) 넘게 아래면 지형 밑으로 빠진 것.
        /// 땅에 누운 통나무 가운데는 땅 위, 반쯤 묻혀도 반지름(0.5m 안팎) 아래까지.</summary>
        const float SunkDepth = 2f;

        /// <summary>통나무가 지형 밑으로 빠졌나(지하묘지 안 뺌 — 그 밑엔 지형이 없다). depth = 땅 윗면에서 아래로 몇 m.</summary>
        internal static bool SunkLog(ResourceInfo info, Vector3 p, out float depth)
        {
            depth = 0f;
            if (info == null || !info.Log || Character.InInterior(p) || ZoneSystem.instance == null || !ZoneSystem.instance.GetGroundHeight(p, out float ground)) return false;
            depth = ground - p.y;
            return depth > SunkDepth;
        }

        // ---- 자기가 쓰러뜨린 나무의 통나무(사용자 11-01 "통나무 끝까지") ----

        /// <summary>쓰러뜨린 나무·쪼갠 통나무 자리에서 이만큼(xz) 안의 통나무를 "자기 통나무"로 본다(넘어진 통나무·구른 반토막).</summary>
        const float OwnLogRange = 15f;
        /// <summary>이 일에서 쓰러뜨리거나 쪼갠 자리. 둘레에 쪼갤 통나무가 안 남으면 OwnLog가 지운다.</summary>
        readonly List<Vector3> m_felled = new List<Vector3>();
        /// <summary>시험용: 일꾼들이 쓰러뜨리거나 쪼갠 자리(지우지 않음).</summary>
        public static readonly List<Vector3> FelledLog = new List<Vector3>();
        /// <summary>시험용(E065): 기억 한도로 잊은 자리 수, 건너뛴 통나무(id → 단계·부른 줄).</summary>
        public static int FelledForgot;
        public static readonly Dictionary<ZDOID, string> SkippedLogs = new Dictionary<ZDOID, string>();
        /// <summary>기억하는 자리 수. 8이면 나무 서넛(쓰러뜨림·쪼갬마다 한 자리)에 먼저 쓰러뜨린 나무의 통나무를 잊을 수 있었다(E065) — 빈 자리는 OwnLog가 지우니 넉넉히.</summary>
        const int FelledMax = 32;

        void NoteFelled(Vector3 p)
        {
            m_felled.Add(p);
            if (m_felled.Count > FelledMax) { m_felled.RemoveAt(0); FelledForgot++; }
            FelledLog.Add(p);
            Buildings.NoteWorkerLog(p);
        }

        /// <summary>시험용(vh_houselogtest): 그 자리를 쓰러뜨린 자리로 — 둘레 통나무가 자기 통나무가 됨.</summary>
        internal void TestFelled(Vector3 p) => NoteFelled(p);

        bool NearFelled(Vector3 p, float range = OwnLogRange)
        {
            foreach (var s in m_felled)
                if (Utils.DistanceXZ(s, p) < range) return true;
            return false;
        }

        /// <summary>시험용: 연쇄로 쓰러진 나무·쪼개진 통나무 수.</summary>
        public static int ChainFelled;

        /// <summary>
        /// 연쇄 쓰러짐(사용자 11-12): 일꾼 통나무(from)에 맞아 쓰러진 나무·쪼개진 통나무 자리 p를 from이 가까운 채집 일의 자기 통나무 자리로.
        /// 통나무 몸은 쓰러뜨린 자리에서 길이만큼(최대 약 20m) 떨어져 부딪히니 25m로 본다. 새 자리도 일꾼 통나무가 돼 다음 연쇄로 이어진다.
        /// </summary>
        public static void NoteChain(Vector3 from, Vector3 p)
        {
            foreach (var w in WorkerNpc.All)
                if (w != null && w.Ai?.Job is GatherJob g && g.NearFelled(from, 25f))
                {
                    g.NoteFelled(p);
                    ChainFelled++;
                    Plugin.Log.LogInfo($"연쇄 쓰러짐 ({p.x:0.0},{p.z:0.0}) ← 통나무 ({from.x:0.0},{from.z:0.0}) — {w.NpcName} 자기 통나무로");
                    return;
                }
        }

        /// <summary>같이 캔 팀이 넣은 것·든 것(내 짐 포함)으로 목표를 채웠나 — 채웠으면 자기 통나무도 더 안 쪼개고 둘 다 복귀(사용자 11-09 같이 캐기, 회차 513·514).
        /// Search·Approach·Deposit이 같은 기준이어야 함: Approach만 멈추고 Search·Deposit이 자기 통나무로 다시 보내면 빈손 왕복을 끝없이 되풀이(E089).</summary>
        bool TeamFilled() => Team != null && Remaining() - Carried() <= 0;

        /// <summary>
        /// 자기 통나무 중 지금 쪼갤 수 있는 가장 가까운 것. 다음 나무·바위보다 먼저, 시킨 개수를 채워도 끝까지 쪼갠다.
        /// 거르는 것은 FindInSight와 같다(건너뜀·위험 구역·막힘 기록·높이 걸림·도구), 건축물 4m는 안 거름(사용자 11-12 집 곁 통나무). 쪼갤 것이 안 남은 자리는 지운다.
        /// 같이 캔 팀이 목표를 채웠으면 없음(TeamFilled).
        /// </summary>
        bool OwnLog(out ZDO best)
        {
            best = null;
            if (m_felled.Count == 0 || m_hunt || TeamFilled()) return false;
            Vector3 pos = m_npc.transform.position;
            float bestDist = float.MaxValue;
            for (int i = m_felled.Count - 1; i >= 0; i--)
            {
                bool any = false;
                var nearby = Senses.Nearby(m_felled[i], OwnLogRange + 4f);
                foreach (var zdo in nearby)
                {
                    ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                    Vector3 p = zdo.GetPosition();
                    if (info == null || !info.Log || !m_ways.Contains(info.Tool) || !info.Produces.Contains(Item)) continue;
                    if (Utils.DistanceXZ(p, m_felled[i]) > OwnLogRange || Character.InInterior(p)) continue;
                    if (m_skip.Contains(zdo.m_uid) || Avoided(p) || m_book.Excluded(p.x, p.z)) continue; // 건축물 곁도 침(사용자 11-12) — 미는 방향은 NpcPatches.LogPushAwayFromHouse
                    ItemDrop.ItemData tool = WorkerNpc.BestTool(m_npc.GetInventory(), info.Tool);
                    if (tool == null || info.MinToolTier > tool.m_shared.m_toolTier) continue;
                    any = true;
                    float d = Utils.DistanceXZ(p, pos);
                    if (d < bestDist) { bestDist = d; best = zdo; }
                }
                if (!any) m_felled.RemoveAt(i);
            }
            return best != null;
        }

    }
}
