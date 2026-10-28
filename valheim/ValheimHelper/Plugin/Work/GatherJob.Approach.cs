using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public sealed partial class GatherJob
    {
        // ---- 다가가서 베기 ----

        /// <summary>
        /// 지하묘지 안 대상인데 몸이 밖에 있다(묘지 안 물에서 헤엄만 해 갇힘 구조가 깃발로 옮김 등): 밖에서 안 대상 쪽(같은 xz)으로 걸으면
        /// 지형 위를 헤매다 막힘으로 그 자리를 빼서 묘지 안 남은 더미까지 못 고르게 됐다(회차 136). 대상은 건너뛰고(skip) 다시 찾는다 — 묘지는 다시 들어간다.
        /// </summary>
        bool OutOfInterior(bool skip)
        {
            if (!Character.InInterior(m_targetPos) || m_npc.InInterior()) return false;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지 안 대상인데 밖에 있음({m_step}) → {(skip ? "대상 건너뛰고 " : "")}다시 찾음");
            if (skip) Skip();
            else Go(Step.Search);
            return true;
        }

        bool UpdateApproach(float dt)
        {
            if (OutOfInterior(true)) return true;
            ZDO zdo = ZDOMan.instance.GetZDO(m_targetId);
            if (zdo == null) { Go(Step.Collect); return true; }
            m_targetPos = zdo.GetPosition();
            if (HungLog(zdo, m_targetInfo))
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo.Prefab} 높이 걸린 통나무(뿌리 높이 {m_targetPos.y:0.0}) → 안 쪼갬");
                Skip();
                return true;
            }
            if (Walk(dt, m_targetPos, m_reach) || InteriorNav.Reached(m_npc.transform.position, m_targetPos, m_reach + 0.3f) || TouchingRock(zdo))
            {
                StartWork(false);
                return true;
            }
            if (m_blockChecked != m_targetId && m_ai.InteriorNoPath(m_targetPos, m_reach))
            {
                // 지하묘지 안 길이 없다: 늪 지하묘지는 방 사이 통로를 진흙 더미가, 서리 동굴은 얼음 벽·석순이 막는다 — 그 길에 걸린 것부터 깬다(대상은 그대로 두고 나중에 다시 고름)
                m_blockChecked = m_targetId;
                if (BlockingPile(out ZDO pile, out ResourceInfo pileInfo))
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo?.Prefab}까지 길을 {pileInfo.Prefab}({Utils.DistanceXZ(pile.GetPosition(), m_npc.transform.position):0}m)가 막음 → 그것부터 {(pileInfo.Produces.Count > 0 ? "캠" : "깸")}");
                    m_blockChecked = ZDOID.None; // 깬 뒤 같은 대상을 다시 고르면 그 뒤에 걸린 것도 본다(얼음 벽 뒤 석순)
                    Aim(pile, pileInfo);
                    Go(Step.Approach);
                    return true;
                }
            }
            if (InteriorHop(dt)) return true;
            if (Utils.DistanceXZ(m_targetPos, m_npc.transform.position) <= m_reach + 0.3f && m_ai.InteriorNoPath(m_targetPos, m_reach))
            {
                // 바로 위·아래 다른 층의 대상인데 그 층으로 가는 길이 없다(E032): 헤매지 말고 건너뛴다
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo?.Prefab} 다른 층(높이차 {m_targetPos.y - m_npc.transform.position.y:0.0}m)인데 가는 길 없음 → 건너뜀");
                Skip();
                return true;
            }
            // 다가가기 한도 40 → 20초(막힘 시간 절반, 사용자 결정 2026-10-26). 던전 안은 90m 시야에 길이 굽어 40초 그대로
            if (Stuck(dt, m_targetPos) || m_stepTime > (m_npc.InInterior() ? 40f : 20f))
            {
                LogStuck("다가가기", m_targetPos);
                // 멀리(모든 플레이어 64m 밖) 목표 몇 m 앞에서 막혔다: 그 자리에서 직접 피해로 캔다(E012, 사용자 결정 2026-10-23 "오류 정리 꼼수").
                if (!TestNoFarStuck && m_targetInfo != null && !m_targetInfo.Pickable && m_targetInfo.Tool != ToolKind.None
                    && InteriorNav.Reached(m_npc.transform.position, m_targetPos, FarStuckRange) && FarFromPlayers(m_npc.transform.position)
                    && ZNetScene.instance.FindInstance(zdo) != null)
                {
                    FarStuckHits++;
                    StartWork(true);
                    return true;
                }
                // 던전 안에서 묘지 안 길이 없다고 이미 안 대상: 갇힌 게 아니라 그 대상만 못 가는 것 → 건너뛰기만(E040 — 길 없는 더미 둘에 연달아 막혀 더미 16개 남은 묘지를 버렸다)
                // 막힘 기록(WalkFailed)에도 안 넣는다: 넣으면 던전 안 자리 4곳이 차 일 전체를 포기했다(회차 191 시험)
                if (m_ai.InteriorNoPath(m_targetPos, m_reach)) { Skip(); return true; }
                if (WalkFailed(m_targetPos, true, out bool keep, out _)) return keep;
                Skip();
                RescueIfStuckAgain();
            }
            return true;
        }

        /// <summary>막은 더미를 찾아본 대상(대상마다 한 번 — 길 찾기가 무겁다).</summary>
        ZDOID m_blockChecked;

        /// <summary>
        /// 진흙 더미·광맥을 지나가도 된다고 칠 때만 대상까지 길이 있으면(InteriorNav.PilesOnWay) 그 길에 처음 걸리는 것 중 지금 연장으로 캘 수 있는 것.
        /// 떨구는 것 없는 깨지는 물체(서리 동굴 얼음 벽, ResourceCatalog.Obstacle)도 깬다 — 회차 167 E031: 도착한 방이 얼음 벽으로 막혀 수정 29개 모두 길이 없었다.
        /// </summary>
        bool BlockingPile(out ZDO pile, out ResourceInfo pileInfo)
        {
            pile = null;
            pileInfo = null;
            var piles = InteriorNav.PilesOnWay(m_npc.transform.position, m_targetPos, m_reach, out _);
            if (piles == null) return false;
            foreach (var p in piles)
            {
                ZNetView v = p.GetComponentInParent<ZNetView>();
                if (v == null || !v.IsValid()) continue;
                ZDO z = v.GetZDO();
                if (z.m_uid == m_targetId) return false; // 대상까지 가는 길에 다른 더미가 안 걸림(대상 뒤 더미는 막은 게 아님)
                if (m_skip.Contains(z.m_uid)) continue;
                ResourceInfo info = ResourceCatalog.Get(z.GetPrefab()) ?? ResourceCatalog.Obstacle(z.GetPrefab());
                // 처음 걸린 것을 못 캐면 그 뒤도 못 감. 일의 방법(m_ways)은 안 본다 — 맨손 수정 줍기도 가진 곡괭이로 얼음 벽을 깬다(E041)
                if (info == null || info.Tool == ToolKind.None || info.Tool == ToolKind.Hunt) return false;
                ItemDrop.ItemData tool = WorkerNpc.BestTool(m_npc.GetInventory(), info.Tool);
                if (tool == null || (info.MinToolTier > 0 && info.MinToolTier > tool.m_shared.m_toolTier)) return false;
                pile = z;
                pileInfo = info;
                return true;
            }
            return false;
        }

        /// <summary>멀리서 목표 이만큼(m) 앞에서 막히면 그 자리에서 직접 친다(E012). 목표 쪽 순간이동 자리(StuckHop.Radii 최대 8m)까지 덮는다.</summary>
        const float FarStuckRange = 8f;
        /// <summary>시험용: 멀리서 목표 앞 막혀 그 자리에서 직접 치기 시작한 횟수.</summary>
        public static int FarStuckHits;
        /// <summary>시험용: 목표 쪽 순간이동 칸(vh_hoptest)이 그 자리 치기에 가로채이지 않게 끈다. 평소 false.</summary>
        public static bool TestNoFarStuck;
        /// <summary>이번 대상은 닿지 않는 자리에서 직접 피해로 캔다(멀리서 목표 앞 막힘, E012).</summary>
        bool m_farStuck;

        void StartWork(bool farStuck)
        {
            m_farStuck = farStuck;
            m_swings = m_swingsNoProgress = 0;
            m_workIdle = m_closerTime = 0f;
            m_dropCheck = 5f;
            m_lastDigs = m_ai.Digs;
            m_lastBlocked = m_ai.BlockedDigs;
            m_saidDig = false;
            m_farDig = null;
            m_lastHealth = null; // 캐기 첫 프레임에 잰다(HealthMark는 인스턴스가 필요)
            Vector3 me = m_npc.transform.position;
            m_workPos = me;
            Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo?.Prefab} 캐기 시작(뿌리 ({m_targetPos.x:0.0},{m_targetPos.y:0.0},{m_targetPos.z:0.0})까지 {Utils.DistanceXZ(m_targetPos, me):0.0}m, 높이차 {me.y - m_targetPos.y:0.0})" +
                               (farStuck ? " — 멀리서 목표 앞 막힘, 그 자리에서 직접 침" : ""));
            Go(Step.Work);
        }

        static readonly Collider[] s_touchBuf = new Collider[32];
        static int s_touchMask;
        /// <summary>몸(가운데)에서 이 거리 안에 바위 충돌체가 있으면 닿은 것(몸 반지름 약 0.5m + 곡괭이가 넉넉히 닿는 0.5m).</summary>
        const float TouchRadius = 1f;

        /// <summary>
        /// 곡괭이 대상(바위·광맥)의 남은 충돌체에 몸이 닿았는지. 닿았으면 다가가기 도착이다(E017).
        /// 큰 광맥(MineRock5)은 부위마다 충돌체(조각)이고 깬 조각은 꺼진다(MineRock5.UpdateMesh SetActive(false)) — 조각이 뿌리(ZDO 위치)에서
        /// 9m 넘게 퍼져 있어 뿌리까지 거리(m_reach, 3.5m 안)로만 재면 바깥 조각에 닿은 채 뿌리 쪽으로 밀기만 하다 막힘으로 버린다.
        /// 캐기(UpdateWork·RockAim)는 남은 조각 중 가까운 것을 친다.
        /// </summary>
        bool TouchingRock(ZDO zdo)
        {
            if (m_targetInfo == null || m_targetInfo.Tool != ToolKind.Pickaxe) return false;
            ZNetView view = ZNetScene.instance.FindInstance(zdo);
            if (view == null) return false;
            if (s_touchMask == 0) s_touchMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
            Vector3 p = m_npc.transform.position;
            int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * 0.4f, p + Vector3.up * 1.6f, TouchRadius, s_touchBuf, s_touchMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = s_touchBuf[i];
                if (c == null || !c.transform.IsChildOf(view.transform)) continue;
                if (Utils.DistanceXZ(m_targetPos, p) > m_reach + 0.3f)
                    Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo.Prefab} 조각 {c.name}에 닿음(뿌리에서 {Utils.DistanceXZ(m_targetPos, p):0.0}m, {m_stepTime:0}초) → 캐기");
                return true;
            }
            return false;
        }

        /// <summary>그 구역(가운데 자리)으로 걷다 막혀 뺐다.</summary>
        bool ZoneExcluded(ZoneXY z)
        {
            Vector3 c = Senses.ZoneCenter(z);
            return m_book.Excluded(c.x, c.z);
        }

        /// <summary>
        /// 목적지로 걷다 막혔다(비켜 가기·점프 뒤): 목적지별 실패를 센다(StuckBook). 첫 실패이고 순간이동이 되는 걷기(hopKind)·자리면
        /// 목표 쪽으로 옮겨 마저 걷는다(moved, 같은 단계 그대로). 그 밖엔 부른 쪽이 Skip·구역 표시(false).
        /// 뺀 곳이 많아 대안이 없으면 "갈 수가 없어요" 하고 그만둔다(든 게 있으면 넣고). true = 처리함(옮겼거나 그만둠), keep = 일을 이어 가는지(Update 반환값).
        /// </summary>
        bool WalkFailed(Vector3 dest, bool hopKind, out bool keep, out bool moved)
        {
            keep = true;
            moved = false;
            string why = hopKind ? HopBlocked() : "이 걷기는 안 함";
            bool near = why == NearWhy; // 근처: 목표 둘레 대신 막힌 곳 너머 몇 m로(NearSkip)
            StuckNext next = m_book.WalkFailed(dest.x, dest.z, why == null || near);
            if (next == StuckNext.Hop)
            {
                if (near ? NearSkip(dest, out why) : HopToward(dest, out why))
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 막힘 기록 ({dest.x:0},{dest.z:0}) 1번 → {(near ? "근처 막힘 건너뛰기" : "목표 쪽 순간이동")} {why}");
                    m_bestDist = float.MaxValue;
                    m_progressTimer = 0f;
                    m_stepTime = 0f;
                    m_portal = null; // 옮긴 자리에서 길(포탈)을 다시 본다
                    m_portalChecked = false;
                    moved = true;
                    return true;
                }
                next = m_book.HopFailed(dest.x, dest.z);
            }
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 막힘 기록 ({dest.x:0},{dest.z:0}) {m_book.Fails(dest.x, dest.z)}번 → {next}, 뺀 곳 {m_book.ExcludedCount}{(why != null ? $" (순간이동 안 함: {why})" : "")}");
            if (next != StuckNext.GiveUp) return false;
            Skip();
            Unreachable++;
            keep = GiveUp("cantReach");
            return true;
        }

        /// <summary>시험용: 막힘 복구로 목표 쪽 순간이동한 횟수.</summary>
        public static int Hops;
        /// <summary>시험용: 거점 근처 시험에서 2)를 실제로 돌려 보려고 플레이어 64m 판정을 끈다. 평소 false.</summary>
        public static bool TestIgnorePlayers;
        /// <summary>시험용: TestIgnorePlayers 동안 이 자리에 플레이어가 있는 것으로 본다(멀리선 이동 생략 중 다가옴 흉내). 평소 null.</summary>
        public static Vector3? TestFakePlayer;

        /// <summary>목표 쪽 순간이동을 못 하는 까닭(되면 null): 사냥·지하묘지·동행·실내는 안 함(사용자 결정 2026-10-23), 떠날 자리가 플레이어 64m 안(근처)이면 안 함.</summary>
        string HopBlocked()
        {
            if (m_hunt) return "사냥";
            if (m_npc.InInterior()) return "지하묘지·실내"; // 묘지 가기(GoCrypt)는 자기 막힘 처리라 여기 안 옴
            if (!FarFromPlayers(m_npc.transform.position)) return NearWhy;
            return null;
        }

        /// <summary>가장 가까운 플레이어(호스트 + 접속한 사람들의 기준 위치)에서 StuckHop.NearPlayer m 밖인가.</summary>
        internal static bool FarFromPlayers(Vector3 p)
        {
            if (TestIgnorePlayers) return TestFakePlayer == null || Utils.DistanceXZ(p, TestFakePlayer.Value) > (float)StuckHop.NearPlayer;
            var players = new List<StuckHop.XZ>();
            if (Player.m_localPlayer != null) players.Add(new StuckHop.XZ(Player.m_localPlayer.transform.position.x, Player.m_localPlayer.transform.position.z));
            if (ZNet.instance != null)
                foreach (var peer in ZNet.instance.GetPeers())
                    if (peer != null && peer.m_refPos != Vector3.zero) players.Add(new StuckHop.XZ(peer.m_refPos.x, peer.m_refPos.z));
            return StuckHop.FarFromPlayers(p.x, p.z, players);
        }

        static int s_bodyMask;

        /// <summary>
        /// 목표 둘레(StuckHop.Candidates)에서 내릴 자리를 찾아 옮긴다(짐·체력·장비는 그대로 — TeleportTo는 자리만 바꿈).
        /// 자리 조건: 구역 불러와짐, 플레이어 64m 밖, 단단한 땅(지형, 기울기 StuckHop.MinNormalY 이상, 물·액체 위 아님), 몸 자리에 바위·건물 없음,
        /// 위험 판단 통과(Guard.SafeAt). 못 찾으면 false와 까닭(why). 소유자만 옮긴다.
        /// </summary>
        bool HopToward(Vector3 dest, out string why)
        {
            if (!LandNear(dest, out why)) return false;
            Hops++;
            WorkerAI.StuckTeleports++;
            return true;
        }

        const string NearWhy = "플레이어 근처";
        /// <summary>시험용: 근처 막힘 건너뛰기 횟수.</summary>
        public static int NearSkips;

        /// <summary>
        /// 근처 막힘 건너뛰기(사용자 결정 2026-10-23): 플레이어 64m 안에서 30초 넘게 못 가면(비켜 가기·점프·경사로는 걷기가 이미 해 봄)
        /// 막힌 곳 너머 몇 m(StuckHop.NearCandidates — 목표 쪽, 단단한 땅·물 아님·몸 자리 빔·위험 판단 통과)로 옮기고 "rescue" 말. 짐·체력·일은 그대로.
        /// </summary>
        bool NearSkip(Vector3 dest, out string why)
        {
            Vector3 from = m_npc.transform.position;
            if (!LandNear(dest, out why, true, StuckHop.NearCandidates(from.x, from.z, dest.x, dest.z))) return false;
            NearSkips++;
            WorkerAI.StuckTeleports++;
            m_npc.Say("rescue");
            return true;
        }

        /// <summary>
        /// HopToward·멀리선 이동 생략이 같이 쓰는 내려놓기: around 둘레 후보 중 안전한 첫 자리로 옮긴다.
        /// nearPlayerOk = 플레이어 64m 안이어도 됨(생략 중 플레이어가 지도상 위치에 다가와 그 근처에 내릴 때).
        /// </summary>
        bool LandNear(Vector3 dest, out string why, bool nearPlayerOk = false, IEnumerable<StuckHop.XZ> candidates = null) =>
            Land(m_npc, dest, out why, nearPlayerOk, candidates);

        /// <summary>
        /// LandNear 몸통(설비 접근 막힘 MachineStep도 씀). floorY가 있으면 그 높이 ±0.6m 바닥(지형·건물 바닥)에만 내린다 — 거점 설비 곁은 나무 바닥일 수 있고
        /// 위에서 재면 지붕에 닿으므로 그 높이 1.5m 위에서 아래로 잰다. 없으면 지형만(위에서 잰 가장 높은 단단한 곳).
        /// </summary>
        internal static bool Land(WorkerNpc npc, Vector3 dest, out string why, bool nearPlayerOk = false, IEnumerable<StuckHop.XZ> candidates = null, float? floorY = null)
        {
            WorkerAI ai = npc.Ai;
            ZNetView nv = npc.GetComponent<ZNetView>();
            if (nv == null || !nv.IsOwner()) { why = "소유자 아님"; return false; }
            if (s_bodyMask == 0) s_bodyMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
            if (s_floorMask == 0) s_floorMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            Vector3 from = npc.transform.position;
            int tried = 0;
            string last = "후보 없음";
            foreach (var c in candidates ?? StuckHop.Candidates(from.x, from.z, dest.x, dest.z))
            {
                tried++;
                var p = new Vector3((float)c.X, dest.y, (float)c.Z);
                if (!ZoneSystem.instance.IsZoneLoaded(p)) { last = "구역 안 불러와짐"; continue; }
                if (!nearPlayerOk && !FarFromPlayers(p)) { last = "플레이어 근처"; continue; }
                float h;
                Vector3 normal;
                if (floorY.HasValue)
                {
                    if (!Physics.Raycast(new Vector3(p.x, floorY.Value + 1.5f, p.z), Vector3.down, out RaycastHit hit, 3f, s_floorMask, QueryTriggerInteraction.Ignore)) { last = "바닥 없음"; continue; }
                    h = hit.point.y;
                    normal = hit.normal;
                    if (Mathf.Abs(h - floorY.Value) > 0.6f) { last = $"바닥 높이 다름({h - floorY.Value:0.0}m)"; continue; }
                }
                else
                {
                    if (!ZoneSystem.instance.GetSolidHeight(p, out h, out normal, out GameObject go) || go == null) { last = "땅 없음"; continue; }
                    if (go.GetComponentInParent<Heightmap>() == null) { last = "지형 아님(" + go.name + ")"; continue; }
                }
                if (normal.y < StuckHop.MinNormalY) { last = $"가파름({normal.y:0.00})"; continue; }
                p.y = h;
                if (h < ZoneSystem.instance.m_waterLevel + 0.3f || Floating.GetLiquidLevel(p) > h + 0.1f) { last = "물"; continue; }
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.45f, s_bodyMask, QueryTriggerInteraction.Ignore)) { last = "몸 자리 막힘"; continue; }
                if (ai.Guard != null && !ai.Guard.SafeAt(p)) { last = "위험"; continue; }
                ai.StopMoving();
                if (!npc.TeleportTo(p + Vector3.up * 0.2f, npc.transform.rotation, false)) { last = "옮기기 실패"; continue; }
                why = $"{Utils.DistanceXZ(from, p):0.0}m → 목표에서 {Utils.DistanceXZ(dest, p):0.0}m (후보 {tried}번째)";
                return true;
            }
            why = $"자리 없음(후보 {tried}개, 마지막: {last})";
            return false;
        }

        static int s_floorMask;

        /// <summary>시험용: 대안 없이 막혀 그만둔 횟수.</summary>
        public static int Unreachable;

        /// <summary>
        /// 깃발로 강제 복귀(사용자 결정 2026-10-22): 막혀서 목표·구역을 버렸는데(다른 길·다른 목표 = 재탐색) 다음 막힘도 같은 자리(8m 안)면
        /// 탈출 실패로 보고 거점으로 순간이동한다. 체력·짐·장비·스킬은 그대로, 목표·이동만 비우고(버린 목표·구역은 이 일 동안 다시 안 고름)
        /// 들고 있으면 넣고 아니면 다음 목표로. 습격 도망·전투는 일(GatherJob) 밖이라 여기 안 옴.
        /// </summary>
        void RescueIfStuckAgain()
        {
            Vector3 pos = m_npc.transform.position;
            if (m_stuckAt.HasValue && Utils.DistanceXZ(m_stuckAt.Value, pos) < 8f) m_stuckCount++;
            else { m_stuckAt = pos; m_stuckCount = 1; }
            if (m_stuckCount < 2) return;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 같은 자리에서 {m_stuckCount}번 막힘 → 깃발로 강제 복귀{(m_npc.InInterior() ? " (이 던전은 이 일 동안 다시 안 들어감)" : "")}");
            // 던전 안에서 막혀 돌아오면 그 던전도 버린 곳: 안 빼면 다시 들어가 같은 자리에서 또 막혔다(E031 — 480초 동안 서리 동굴 드나들기 되풀이)
            if (m_npc.InInterior()) m_cryptsDone.Add(m_crypt.Pos);
            m_stuckAt = null;
            m_stuckCount = 0;
            m_targetId = ZDOID.None;
            if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return; } // 깃발 순간이동도 안 됨: 그 자리에서 쉼
            Rescues++;
            m_npc.Say("stuck", Args());
            Go(Carried() > 0 ? Step.Deposit : Step.Search);
        }

        Vector3? m_stuckAt;
        int m_stuckCount;
        /// <summary>시험용: 깃발로 강제 복귀한 횟수.</summary>
        public static int Rescues;

        /// <summary>진단: 걷다 막힌 자리(앞에 걸린 것·발밑·걷기 상태).</summary>
        void LogStuck(string what, Vector3 goal) =>
            Plugin.Log.LogInfo($"{m_npc.NpcName}: {what} 막힘({m_stepTime:0}초, 남은 {Utils.DistanceXZ(goal, m_npc.transform.position):0.0}m, 집에서 {Utils.DistanceXZ(m_npc.Home, m_npc.transform.position):0.0}m) " +
                               $"{m_ai.BlockerAhead(goal)} / {m_ai.MoveState} / {m_npc.MoveDebug} / 목표 {m_targetInfo?.Prefab}({goal.x:0.0},{goal.y:0.0},{goal.z:0.0}) {m_ai.PathDiag(goal)}");

        bool UpdateWork()
        {
            if (OutOfInterior(true)) return true;
            ZDO zdo = ZDOMan.instance.GetZDO(m_targetId);
            ZNetView view = zdo != null ? ZNetScene.instance.FindInstance(zdo) : null;
            if (zdo == null || view == null || !view.IsValid())
            {
                // 쓰러졌다(또는 부서졌다): 떨어진 것을 줍는다.
                Go(Step.Collect);
                return true;
            }
            if (m_targetInfo == null) { Skip(); return true; }
            // 경계(Guard)가 몸을 쓰는 동안(물러나기 40m·싸움)은 이 일이 멈춰 있다: 그 사이 옮겨졌으면 그 자리에서 휘두르지 않고 다시 다가간다
            // (회차 142 검은숲 coppertest: 물러났다 34m 밖에서 일이 이어져 헛휘두름 → 직접 피해가 20.6m 떨어진 조각에 들어감).
            Vector3 here = m_npc.transform.position;
            if (Utils.DistanceXZ(m_workPos, here) > WorkMoved)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: 캐는 사이 {Utils.DistanceXZ(m_workPos, here):0.0}m 옮겨짐(경계 {m_ai.Guard?.StateName}) → 다시 다가감");
                WorkReapproaches++;
                m_wasAttacking = false;
                Go(Step.Approach);
                return true;
            }
            m_workPos = here;
            if (m_targetInfo.Log) m_targetPos = zdo.GetPosition(); // 통나무는 맞으면 밀려 움직인다: 쪼개진 뒤 물러날 때 지금 자리에서
            if (m_targetInfo.Pickable)
            {
                // 손으로 줍기: 바닐라 Interact(스킬만 플레이어 전용). 떨어진 것은 Collect가 줍는다.
                Pickable pick = view.GetComponent<Pickable>();
                if (pick == null || !pick.CanBePicked()) { Skip(); return true; }
                if (StuckInTar(zdo)) { Go(Step.Search); return true; } // 다가가는 사이 잠김
                if (m_stepTime > 0.1f)
                {
                    pick.Interact(m_npc, false, false);
                    Go(Step.Collect);
                }
                return true;
            }
            if (!m_npc.HoldTool(m_targetInfo.Tool)) { Go(Step.Tool); return true; }
            if (m_lastHealth == null) m_lastHealth = HealthMark(zdo, view);
            // 큰 광맥은 다 캐기 전에 목표만큼 모일 수 있다(0.3.8 구리 시험: 10개 시켰는데 18개를 들고 계속 캠)
            if (Carried() >= Remaining() || Full()) { Go(Step.Return); return true; }
            // 진척(대상이 깎이거나 땅을 팜) 없이 30초면 다른 것으로. 큰 광맥은 오래 걸려도 계속 캔다.
            m_workIdle += Time.deltaTime;
            if (m_workIdle > WorkIdleLimit || m_stepTime > ChopLimit)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: 캐기 진척 없음({m_stepTime:0}초, 휘두름 {m_swings}·헛 {m_swingsNoProgress}, 공격 중 {m_npc.InAttack()}, " +
                                   $"대상까지 {Utils.DistanceXZ(m_targetPos, m_npc.transform.position):0.0}m) → 건너뜀 / {m_ai.MoveState} / {m_npc.MoveDebug}");
                Skip();
                return true;
            }

            Vector3 aim = m_targetPos + Vector3.up * 1f;
            m_aimCol = null;
            m_farAim = false;
            if (m_targetInfo.Tool == ToolKind.Pickaxe && !RockAim(view, out aim))
            {
                Skip(); // 땅속 조각만 남았는데 파서 꺼낼 물건이 아니다(돌): 남겨 둔다
                return true;
            }
            bool attacking = m_npc.InAttack();
            // 큰 광맥은 다 캐기 전에 조각마다 떨어진 것이 둘레에 쌓인다(발밑 2m만 저절로 줍는다): 5초마다 보고 주우러 간다
            // (0.3.8 구리 시험: 다 캘 때까지 안 주워 곡괭이가 먼저 부러짐). 주운 뒤 같은 광맥을 다시 고른다.
            m_dropCheck -= Time.deltaTime;
            if (!attacking && !m_farStuck && m_dropCheck <= 0f && m_targetInfo.Tool == ToolKind.Pickaxe) // 멀리서 막힌 자리면 줍기는 다 캔 뒤 Collect가
            {
                m_dropCheck = 5f;
                ItemDrop near = NearestDrop(m_targetPos);
                if (near != null && Utils.DistanceXZ(near.transform.position, m_npc.transform.position) > 2f) { Go(Step.Collect); return true; }
            }
            // 땅속 광석을 파는데 두 번 휘둘러도 안 닿았다: 구덩이가 깊어져 손이 안 닿으니 3초까지 다가간다
            if (!attacking && m_ai.DigAllowed && m_swingsNoProgress >= 2 && m_closerTime < 3f && Utils.DistanceXZ(aim, m_npc.transform.position) > 1.2f)
            {
                m_closerTime += Time.deltaTime;
                m_ai.MoveToPoint(Time.deltaTime, aim, 1f, false);
                m_wasAttacking = false;
                return true;
            }
            m_ai.Face(aim);
            if (m_wasAttacking && !attacking)
            {
                // 한 번 휘두름이 끝났다: 피해가 들어갔는지(대상 ZDO가 바뀌었는지) 보고, 도구를 닳게 한다
                // (바닐라 Attack은 플레이어 도구만 닳게 해서 직접 한다. 구상서 4장: 내구도는 바닐라처럼).
                m_swings++;
                ItemDrop.ItemData tool = m_npc.GetCurrentWeapon();
                if (tool != null && tool.m_shared.m_useDurability && WorkerNpc.WearTool(tool, tool.m_shared.m_useDurabilityDrain))
                {
                    m_npc.UnequipItem(tool); m_npc.Say("toolBroke", Args()); // 최저 1: 일에는 부서진 것, 경사로 깎기에는 씀
                }
                // 대상 체력이 줄었으면(HealthMark) 진척. 땅을 팠으면 그것도 진척.
                string mark = HealthMark(zdo, view);
                bool progress = mark != m_lastHealth || m_ai.Digs != m_lastDigs;
                if (!progress && (m_farAim || m_farStuck))
                {
                    // 멀리서 목표 앞 막혀 닿지 않는 자리에서 캔다(E012)도 같이: 휘두를 때마다 직접 피해.
                    // 멀리(모든 플레이어 64m 밖)서 땅속 조각: 땅은 안 파고 휘두를 때마다 그 조각에 직접 피해(E021 라, 곡괭이 단계·내구도 그대로).
                    DirectHit(view);
                    progress = true;
                }
                if (!progress && m_ai.BlockedDigs != m_lastBlocked && m_aimCol != null)
                {
                    // 휘두름이 바위보다 땅에 먼저 닿았는데 땅은 안 파기로 했다(돌, 3m보다 깊음): 바닐라는 이 휘두름을 버린다.
                    // 겨눈 조각은 닿는 곳에 있으니 거기 맞은 것으로 한다(0.3.8 구리 시험: 휘두름 37번이 이렇게 헛돌아 곡괭이가 먼저 부러짐).
                    DirectHit(view);
                    progress = true;
                }
                // 같은 땅속 조각을 겨눈 채 땅만 10번 파고 조각은 안 깎였다(E021 다 헛파기, 원인은 안 찾음): 그 조각에 직접 피해.
                // 바닐라 휘두름이 조각을 깎으면 다시 센다.
                if (m_aimCol == null || m_aimCol != m_digAim || mark != m_lastHealth) { m_digAim = m_aimCol; m_digOnly = 0; }
                else if (m_ai.Digs != m_lastDigs && ++m_digOnly >= DigOnlyLimit)
                {
                    if (m_digOnly == DigOnlyLimit) Plugin.Log.LogInfo($"{m_npc.NpcName}: 같은 땅속 조각 겨눈 채 땅만 {DigOnlyLimit}번 → 그 조각에 직접 피해");
                    DirectHit(view);
                }
                if (progress) { m_swingsNoProgress = 0; m_workIdle = m_closerTime = 0f; }
                else { m_swingsNoProgress++; MissSwings++; }
                m_lastDigs = m_ai.Digs;
                m_lastBlocked = m_ai.BlockedDigs;
                if (m_swingsNoProgress >= MissDirect)
                {
                    // 바닐라 공격 판정이 닿지 않는다(구상서 12장: 2번째부터 같은 피해 값을 직접 준다 — 사용자 결정 10-25 "2번으로 줄여", 예전 4번째. 예: 바위 밑에 낀 통나무 — 휘두름이 바위에 먼저 닿음).
                    // 직접 피해가 들면 진척이니 건너뛰지 않고 다음 휘두름에도 직접 준다. 못 들면(너무 단단함) MissSkip번째에 건너뛴다.
                    // 땅속 광석의 "2번 헛침 → 3초 다가가기"와 같은 때라, 직접 피해가 들어 그 뒤 다가가기는 3초 안에서만 돈다.
                    DirectHit(view);
                    if (HealthMark(zdo, view) != mark) { m_swingsNoProgress = MissDirect; m_workIdle = 0f; }
                }
                // 직접 피해 뒤에 잰다: 그 피해를 다음 휘두름이 바닐라 진척으로 잘못 보지 않게
                m_lastHealth = HealthMark(zdo, view);
                if (m_swingsNoProgress >= MissSkip) { Plugin.Log.LogInfo($"{m_npc.NpcName}: 휘두름 {MissSkip}번 헛침 → 건너뜀 / {m_npc.MoveDebug}"); Skip(); return true; }
            }
            m_wasAttacking = attacking;
            if (!attacking && (m_farAim || m_farStuck || m_ai.IsLookingAt(aim, 25f))) m_npc.StartAttack(null, false);
            return true;
        }

        float m_workIdle, m_closerTime, m_dropCheck;
        /// <summary>캐기 마지막 프레임의 몸 자리(그 사이 WorkMoved 넘게 옮겨졌으면 다시 다가간다).</summary>
        Vector3 m_workPos;
        /// <summary>캐기 프레임 사이에 이만큼(m) 넘게 옮겨졌으면 경계가 몸을 쓴 것(일은 멈춰 있었음) — 걷기 3초 다가가기는 프레임마다 조금씩이라 안 걸림.</summary>
        const float WorkMoved = 3f;
        /// <summary>시험용: 캐는 사이 옮겨져 다시 다가간 횟수 / 맞은 점이 FarStuckRange보다 먼 직접 피해 횟수(vh_coppertest).</summary>
        public static int WorkReapproaches, LongDirectHits;
        /// <summary>시험용: 진척 없는 휘두름(헛휘두름) 누계(TestKit 멈춤 재기).</summary>
        public static int MissSwings;
        /// <summary>헛휘두름 이만큼(번째)부터 직접 피해, 직접 피해도 안 들어 이만큼이면 건너뜀.</summary>
        const int MissDirect = 2, MissSkip = 5;
        int m_lastDigs, m_lastBlocked, m_digOnly;
        Collider m_aimCol, m_digAim;
        const int DigOnlyLimit = 10;
        bool m_saidDig;
        /// <summary>짐이 차서 떨어진 것을 남기고 돌아간 자리(다음 Search가 먼저 와서 줍는다). null = 없음.</summary>
        Vector3? m_leftDrops;
        int m_leftDropsTries;
        /// <summary>이 대상 캐기 시작 뒤 처음 땅속 조각을 겨눌 때 잰 "모든 플레이어 64m 밖"(그 뒤 플레이어가 와도 그대로 — 땅속이라 안 보임). null = 아직 안 잼.</summary>
        bool? m_farDig;
        /// <summary>이번 프레임 겨눈 것이 멀리서 직접 치는 땅속 조각(땅 안 팜).</summary>
        bool m_farAim;
        const float WorkIdleLimit = 30f;
        /// <summary>땅 낮춤 한계 바닥 밑으로 이만큼(m) 안에 윗면이 있는 묻힌 조각까지 캔다(바닥에 선 일꾼 팔이 닿는 정도).</summary>
        const float BelowFloorReach = 2f;

        /// <summary>
        /// 곡괭이로 칠 곳: 남은 조각 중 땅 위로 드러난 가장 가까운 조각(휘두름이 땅에 먼저 닿으면 바위가 안 깎인다).
        /// 남은 게 다 땅속이면 파서 꺼낼 물건(광석, ResourceCatalog.WorthDigging)일 때만 가장 얕은 조각을 겨누고 땅 파기를 허락한다.
        /// false면 이 대상은 그만둔다(땅속 돌, 너무 깊은 광석).
        /// </summary>
        bool RockAim(ZNetView view, out Vector3 aim)
        {
            aim = m_targetPos + Vector3.up;
            Vector3 chest = m_npc.transform.position + Vector3.up;
            Collider buried = null;
            float buriedTop = float.MinValue, bestD = float.MaxValue;
            bool inside = Character.InInterior(m_targetPos);
            int sunk = 0;
            foreach (var col in view.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger) continue;
                if (inside && !DropLands(view, col, chest)) { sunk++; continue; }
                Bounds b = col.bounds;
                float ground = ZoneSystem.instance.GetGroundHeight(b.center, out float h) ? h : b.min.y;
                if (b.max.y > ground + 0.3f)
                {
                    Vector3 p = b.ClosestPoint(chest);
                    p.y = Mathf.Clamp(p.y, ground + 0.3f, b.max.y);
                    float dd = (p - chest).sqrMagnitude;
                    if (dd < bestD) { bestD = dd; aim = p; m_aimCol = col; }
                }
                else if (b.max.y > buriedTop) { buriedTop = b.max.y; buried = col; }
            }
            // 드러난 조각이 있으면 광석이라도 땅을 파지 않는다: 휘두름이 땅에 먼저 닿으면 겨눈 조각에 맞은 것으로 한다(UpdateWork).
            // 다 땅속이면 광석(파서라도 꺼낼 것)만 바닐라처럼 파 내려간다. 원래 땅에서 8m(바닐라 한계)보다 깊이는 안 판다(NpcPatches.NoDig).
            // (0.3.8 구리 시험: 드러난 조각이 있어도 파게 했더니 52번 파서 3.8m 구덩이)
            if (inside && m_aimCol == null && buried == null && sunk > 0)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo.Prefab} 남은 조각 {sunk}개는 묘지 바닥 속(깨면 떨굼이 바닥 밑으로 빠짐) → 남김");
                return false;
            }
            bool dig = ResourceCatalog.WorthDigging(Item);
            if (m_aimCol != null || buried == null) return true;
            Vector3 c = buried.bounds.center;
            float depth = WorldGenerator.instance.GetHeight(c.x, c.z) - buriedTop; // 원래 땅에서 얼마나 깊은지
            // 한계 바닥(8m)보다 조금 아래 조각(silvervein 바닥 8.3m)도 겨눈다: 바닥을 친 휘두름은 NoDig이 막고 UpdateWork가 그 조각에 직접 피해(E021 나).
            if (!dig || depth > NpcPatches.NoDig.MaxDepth + BelowFloorReach)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo.Prefab} 땅속 조각만 남음(깊이 {depth:0.0}m) → 파지 않고 남김");
                return false;
            }
            m_aimCol = buried;
            // 멀리(모든 플레이어 64m 밖)면 땅은 안 파고 휘두를 때마다 그 조각에 직접 피해(E021 라). 구덩이가 안 생겨 되메우기·탈출이 필요 없다.
            // 생긴 광석은 땅 위로 튀어나온다(ItemDrop.TerrainCheck) → 보통 줍기. 가슴 높이로 휘둘러 땅을 안 친다.
            if (m_farDig == null) m_farDig = FarFromPlayers(m_npc.transform.position);
            if (m_farDig == true)
            {
                if (!m_saidDig) { m_saidDig = true; Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo.Prefab} 땅속 {Item} — 플레이어 멀리라 안 파고 바로 침(깊이 {depth:0.0}m)"); }
                m_farAim = true;
                aim = buried.bounds.ClosestPoint(chest);
                aim.y = chest.y;
                return true;
            }
            if (!m_saidDig) { m_saidDig = true; Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo.Prefab} 땅속 {Item} 파기(깊이 {depth:0.0}m)"); }
            m_ai.DigAllowedUntil = Time.time + 1.5f;
            aim = buried.bounds.ClosestPoint(chest);
            return true;
        }

        static readonly RaycastHit[] s_downHits = new RaycastHit[16];

        /// <summary>
        /// 지하묘지 안 큰 광맥(MineRock5, 진흙 더미 조각) 조각을 깨면 떨굼이 생기는 점(바닐라 DamageArea: m_hitEffectAreaCenter면 조각 가운데, 아니면 맞은 점 ±0.3m)
        /// 아래 3m 안에 그 광맥 아닌 바닥 면이 있는가. 묘지 바닥 속에 묻힌 조각은 가운데가 바닥 밑이라 떨굼이 바닥 아래로 떨어져 잃는다
        /// (회차 136: 직접 피해로 묻힌 조각을 깨 고철이 16~34m 아래로 — 바닐라 휘두름은 바닥에 막혀 못 닿는 조각).
        /// </summary>
        bool DropLands(ZNetView view, Collider col, Vector3 chest)
        {
            MineRock5 rock = view.GetComponent<MineRock5>();
            if (rock == null) return true;
            if (s_sightMask == 0) s_sightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            Vector3 s = rock.m_hitEffectAreaCenter ? col.bounds.center : Closest(col, chest);
            int n = Physics.RaycastNonAlloc(s + Vector3.up * 0.3f, Vector3.down, s_downHits, 3.3f, s_sightMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool floor = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = s_downHits[i];
                if (h.collider.transform.IsChildOf(view.transform) || h.distance >= best) continue;
                best = h.distance;
                floor = h.normal.y >= 0.5f && h.point.y <= s.y + 0.05f;
            }
            return floor;
        }

        /// <summary>충돌체에서 p에 가장 가까운 점. 오목한 메시 충돌체는 바닐라 ClosestPoint가 안 돼서(경고) 상자 경계로.</summary>
        static Vector3 Closest(Collider c, Vector3 p) =>
            c is MeshCollider m && !m.convex ? c.bounds.ClosestPoint(p) : c.ClosestPoint(p);

        /// <summary>
        /// 대상 체력 기록(휘두름 진척 판단용). ZDO DataRevision은 안 된다: 깨어 있는 통나무는 ZSyncTransform이 위치·속도를
        /// 계속 써서 휘두를 때마다 오른다(루프 woodtest: 바위 밑에 끼어 떠는 통나무를 2분 동안 "깎인다"고 봄, 체력 35.6 그대로).
        /// 바닐라 저장 키: TreeBase·TreeLog·Destructible = float s_health, MineRock5 = string s_health(부위별), MineRock = float "Health"+부위.
        /// </summary>
        static string HealthMark(ZDO zdo, ZNetView view)
        {
            string mark = zdo.GetFloat(ZDOVars.s_health, -1f).ToString("R") + "|" + zdo.GetString(ZDOVars.s_health);
            MineRock rock = view.GetComponent<MineRock>();
            if (rock != null)
            {
                int areas = (rock.m_areaRoot != null ? rock.m_areaRoot : rock.gameObject).GetComponentsInChildren<Collider>().Length;
                float sum = 0f;
                for (int i = 0; i < areas; i++) sum += zdo.GetFloat("Health" + i, rock.GetHealth());
                mark += "|" + sum.ToString("R");
            }
            return mark;
        }

        void DirectHit(ZNetView target)
        {
            ItemDrop.ItemData tool = m_npc.GetCurrentWeapon();
            IDestructible d = target.GetComponent<IDestructible>();
            if (tool == null || d == null) return;
            // 겨눈 조각, 없으면 가장 가까운 충돌체를 맞힌 것으로 한다(MineRock5는 충돌체로 부위를 찾는다).
            Vector3 me = m_npc.transform.position + Vector3.up;
            Collider hitCol = m_aimCol;
            float best = float.MaxValue;
            if (hitCol == null)
                foreach (var col in target.GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger) continue;
                    float dd = (Closest(col, me) - me).sqrMagnitude;
                    if (dd < best) { best = dd; hitCol = col; }
                }
            Vector3 point = hitCol != null ? Closest(hitCol, me) : target.transform.position + Vector3.up;
            Vector3 dir = point - me;
            if (target.GetComponent<TreeBase>() != null)
            {
                // 서 있는 나무는 맞은 방향으로 넘어간다(TreeBase.SpawnLog: 꼭대기에 hitDir×0.2×질량). 나무 충돌체(Lod0)는 가지까지 덮는 오목한 메시라
                // 경계 상자로 재면 몸이 그 안이어서 맞은 점 = 내 가슴, 방향 0 → 아무 쪽으로나 넘어져 일꾼 쪽으로 올 수 있다(E020 진단).
                // 바닐라처럼 줄기(뿌리 1m 위)를 수평으로 친 것으로 한다 → 일꾼 반대쪽으로 넘어감(Collect가 뿌리 반대로 물러남).
                point = m_targetPos + Vector3.up;
                dir = point - me;
                dir.y = 0f;
            }
            if (dir.sqrMagnitude < 0.0001f) dir = m_npc.transform.forward;
            var hit = new HitData
            {
                m_damage = tool.GetDamage(),
                m_toolTier = (short)tool.m_shared.m_toolTier,
                m_point = point,
                m_dir = dir.normalized,
                m_skill = tool.m_shared.m_skillType,
                m_hitCollider = hitCol,
            };
            hit.m_damage.Modify(m_npc.GetRandomSkillFactor(tool.m_shared.m_skillType)); // 스킬 반영(바닐라 Attack과 같게)
            hit.SetAttacker(m_npc);
            d.Damage(hit);
            if (Vector3.Distance(point, me) > FarStuckRange) LongDirectHits++;
            m_npc.RaiseSkill(tool.m_shared.m_skillType);
            Plugin.Log.LogInfo($"{m_npc.NpcName}: 공격 판정이 안 닿아 직접 피해 {hit.m_damage.m_chop + hit.m_damage.m_pickaxe:0} " +
                               $"({m_targetInfo?.Prefab} {hitCol?.name}, 맞은 점까지 {Vector3.Distance(point, me):0.0}m, 방향 ({hit.m_dir.x:0.0},{hit.m_dir.y:0.0},{hit.m_dir.z:0.0}))");
        }

        /// <summary>시험용: 던전 안 이동 생략을 끝낸 횟수. TestNoInteriorHop이면 안 함.</summary>
        public static int InteriorHops;
        public static bool TestNoInteriorHop;
        ZDOID m_inHopFor = ZDOID.None;
        float m_inHopWait;
        readonly HashSet<ZDOID> m_inHopped = new HashSet<ZDOID>();

        /// <summary>
        /// 던전 안 이동 생략(⑬ 바-2e, decisions "멀리선 이동 생략"·"⑬ 안개땅 절벽"): 던전 안 칸 길이 끊긴 대상(광산 먼 방 — 계단·낭떠러지 경계)은
        /// 일꾼·대상 둘레 64m(3차원 — 던전은 바깥보다 수천 m 위)에 플레이어가 없으면 직선 거리 ÷ 달리기 속도(15초까지) 서서 기다렸다가
        /// 대상 옆 설 자리(InteriorNav.StandNear)로 옮긴다. 대상마다 한 번(옮긴 뒤에도 못 닿으면 원래 막힘 처리). 기다리는 사이 길이 생기면 그만둔다.
        /// </summary>
        bool InteriorHop(float dt)
        {
            if (TestNoInteriorHop || !m_ai.InteriorNoPath(m_targetPos, m_reach) || m_inHopped.Contains(m_targetId)) { m_inHopFor = ZDOID.None; return false; }
            Vector3 me = m_npc.transform.position;
            if (!NoPlayerAround(me) || !NoPlayerAround(m_targetPos)) { m_inHopFor = ZDOID.None; return false; }
            if (m_inHopFor != m_targetId)
            {
                m_inHopFor = m_targetId;
                m_inHopWait = Mathf.Min(15f, Vector3.Distance(me, m_targetPos) / Mathf.Max(1f, m_npc.m_runSpeed));
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo?.Prefab}까지 던전 안 길 없음({Vector3.Distance(me, m_targetPos):0}m) → 이동 생략 {m_inHopWait:0}초 기다림");
            }
            m_ai.Hold("던전 안 이동 생략");
            m_ai.StopMoving();
            m_inHopWait -= dt;
            if (m_inHopWait > 0f) return true;
            m_inHopped.Add(m_targetId);
            m_inHopFor = ZDOID.None;
            Vector3? spot = InteriorNav.StandNear(m_targetPos, m_reach);
            if (spot == null)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo?.Prefab} 옆에 설 자리 없음 → 건너뜀");
                Skip();
                return true;
            }
            Vector3 face = m_targetPos - spot.Value;
            face.y = 0f;
            m_npc.TeleportTo(spot.Value + Vector3.up * 0.1f, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : m_npc.transform.rotation, false);
            m_ai.StopMoving();
            m_bestDist = float.MaxValue;
            m_progressTimer = 0f;
            m_stepTime = 0f;
            InteriorHops++;
            Plugin.Log.LogInfo($"{m_npc.NpcName}: 던전 안 이동 생략 → {m_targetInfo?.Prefab} 옆 {Utils.DistanceXZ(spot.Value, m_targetPos):0.0}m(높이차 {m_targetPos.y - spot.Value.y:0.0})");
            return true;
        }

        /// <summary>p 둘레 StuckHop.NearPlayer m(3차원) 안에 플레이어(호스트·손님 기준 위치)가 없는가. 시험이 플레이어를 무시하면 FarFromPlayers.</summary>
        internal static bool NoPlayerAround(Vector3 p)
        {
            if (TestIgnorePlayers) return FarFromPlayers(p);
            float near = (float)StuckHop.NearPlayer;
            if (Player.m_localPlayer != null && Vector3.Distance(Player.m_localPlayer.transform.position, p) <= near) return false;
            if (ZNet.instance != null)
                foreach (var peer in ZNet.instance.GetPeers())
                    if (peer != null && peer.m_refPos != Vector3.zero && Vector3.Distance(peer.m_refPos, p) <= near) return false;
            return true;
        }

        void Skip()
        {
            if (!m_targetId.IsNone()) m_skip.Add(m_targetId);
            m_targetId = ZDOID.None;
            Go(Step.Search);
        }

    }
}
