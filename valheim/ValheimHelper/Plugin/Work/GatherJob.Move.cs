using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public sealed partial class GatherJob
    {
        // ---- 일꾼 포탈(R2 나, [기본안]: 거점에서 300m 넘는 일터에 두 번째 갈 때) ----

        /// <summary>시험용(vh_portalbuildtest 8): 떠날 때·도착·귀환의 일꾼 포탈 처리만 따로 부른다.</summary>
        internal void TestPortalDepart(ZoneXY z) { m_goalZone = z; PortalDepart(); }
        internal void TestPortalArrive(ZoneXY z) => ArrivedAt(z);
        internal void TestPortalHome() => PortalHome();
        internal string PortalCarryTag => m_portalTag;

        /// <summary>
        /// 저장에서 불러온 이름표(WorkItem.PortalTag): 한 몫을 실제로 들고 있을 때만 이어 간다. 그 이름표 포탈이 1개면 도착해 짝을 놓고,
        /// 0개·2개면 PlaceCarried가 거절하니 귀환 때 한 몫을 상자로(PortalHome). 안 들고 있으면 잊는다.
        /// </summary>
        public void LoadPortal(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (!PortalBuilder.Carrying(m_npc.GetInventory())) { Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 포탈 {tag} 한 몫이 짐에 없어 잊음"); return; }
            m_portalTag = tag;
            int n = PortalBuilder.MineWithTag(tag);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 포탈 {tag} 이어 감(그 이름표 {n}개{(n == 1 ? ", 도착하면 짝" : ", 귀환 때 한 몫 상자로")})");
        }
        /// <summary>시험용(vh_woodtest 높이 걸린 통나무): 지금 고른 대상.</summary>
        internal ZDOID TargetId => m_targetId;

        /// <summary>먼 아는 구역으로 떠날 때(거점 근처에서만 — 상자·깃발 구역이 불러와져 있음): 거점 쪽 포탈을 놓고 한 몫을 들고 간다.</summary>
        void PortalDepart()
        {
            Vector3 home = m_npc.Home;
            if (m_portalTag != null || m_exploring || Utils.DistanceXZ(home, m_npc.transform.position) > 30f) return;
            Vector3 site = Senses.ZoneCenter(m_goalZone);
            string tag = WorkerPortal.Tag(m_goalZone.ToString());
            bool linked = Portals.TryChoose(home, site, out _);
            Inventory inv = m_npc.GetInventory();
            var act = WorkerPortal.OnDepart(Utils.DistanceXZ(home, site), Brain.Get().VisitsOf(m_goalZone), linked,
                PortalBuilder.MineWithTag(tag), PortalBuilder.Carrying(inv), PortalBuilder.Pairs());
            if (act == Departing.None) return;
            if (PortalBuilder.StartPair(tag, home, inv, act == Departing.PlaceBaseAndCarry, out var missing, out string why)) { m_portalTag = tag; return; }
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 포탈 안 놓음 — {why}");
            if (missing.Count > 0 && !m_saidPortalNeed)
            {
                m_saidPortalNeed = true;
                var parts = new List<string>();
                foreach (var kv in missing) parts.Add($"{ItemLabel(kv.Key)} {kv.Value}개");
                var args = Args();
                args["need"] = string.Join(", ", parts);
                m_npc.Say("portal.need", args); // 수르틀링 핵 등은 구하러 가지 않고 말만(사용자 결정 2026-10-23)
            }
        }

        /// <summary>일하러 구역에 왔다: 먼 곳이면 방문을 센다(한 일에 한 번). 그 구역 몫을 들고 왔으면 일터 쪽 포탈을 놓는다.</summary>
        void ArrivedAt(ZoneXY z)
        {
            if (Utils.DistanceXZ(m_npc.Home, Senses.ZoneCenter(z)) > WorkerPortal.FarFromBase && m_visited.Add(z)) Brain.Get().Visit(z);
            if (m_portalTag == null || m_portalTag != WorkerPortal.Tag(z.ToString())) return;
            if (PortalBuilder.PlaceCarried(m_portalTag, m_npc.transform.position, m_npc.GetInventory(), out string why)) m_portalTag = null;
            else Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 일터 쪽 포탈 못 놓음 — {why}");
        }

        /// <summary>거점에 왔다: 못 놓고 들고 온 한 몫은 상자에 돌려놓고, 오래 안 쓴 일꾼 포탈은 철거한다.</summary>
        void PortalHome()
        {
            if (m_portalTag != null && PortalBuilder.ReturnCarried(m_npc.Home, m_npc.GetInventory())) m_portalTag = null;
            if (!PortalBuilder.Carrying(m_npc.GetInventory())) m_portalTag = null; // 짐 정리 등으로 이미 내려놨다
            foreach (var t in PortalBuilder.RemoveIdle(m_npc.Home)) Plugin.Log.LogInfo($"{m_npc.NpcName}: 오래 안 쓴 포탈 {t} 철거");
        }

        // ---- 공통 ----

        int Carried() => m_npc.CountItem(Item);
        int Remaining() => Target - Delivered;
        bool Full()
        {
            if (m_itemPrefab == null) m_itemPrefab = ObjectDB.instance.GetItemPrefab(Item);
            Inventory inv = m_npc.GetInventory();
            return inv.GetTotalWeight() >= m_npc.MaxCarry || m_itemPrefab == null || !inv.CanAddItem(m_itemPrefab, 1);
        }

        /// <summary>걷는다(WorkerAI.MoveToPoint: 길찾기 + 길 없으면 곧장 + 막히면 비켜 가기). 도착하면 true.</summary>
        /// <summary>먼 목적지는 포탈이 더 빠르면 포탈 앞까지 걸어가 타고(순간이동), 아니면 그냥 걷는다(단계마다 한 번만 고름). 도착하면 true.</summary>
        bool WalkVia(float dt, Vector3 dest, float dist)
        {
            if (!m_portalChecked)
            {
                m_portalChecked = true;
                if (Portals.TryChoose(m_npc.transform.position, dest, out PortalLink link))
                {
                    m_portal = link;
                    m_bestDist = float.MaxValue;
                    m_progressTimer = 0f;
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 포탈로 간다(입구 {link.X:0},{link.Z:0} → 출구 {link.ExitX:0},{link.ExitZ:0})");
                }
            }
            if (m_portal.HasValue)
            {
                var p = m_portal.Value;
                var gate = new Vector3(p.X, p.Y, p.Z);
                if (Walk(dt, gate, 2f) || Utils.DistanceXZ(gate, m_npc.transform.position) < 2.5f)
                {
                    m_npc.TeleportTo(new Vector3(p.ExitX, p.ExitY, p.ExitZ), m_npc.transform.rotation, false);
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 포탈 탐");
                    PortalBuilder.MarkUsedNear(gate);
                    m_portal = null;
                    m_ai.StopMoving();
                    m_bestDist = float.MaxValue;
                    m_progressTimer = 0f;
                }
                else if (Stuck(dt, gate)) { m_portal = null; m_bestDist = float.MaxValue; m_progressTimer = 0f; } // 못 가면 걸어서
                return false;
            }
            if (FarWalk(dt, dest)) return false;
            return Walk(dt, dest, dist);
        }

        /// <summary>시험용: 멀리선 이동 생략을 시작한 횟수·다 옮겨 끝낸 횟수·도중에 그만둔 횟수.</summary>
        public static int FarSkips, FarLanded, FarCancelled;
        /// <summary>시험용: 마지막 생략의 길이(m)·기다린 시간(초)·지도 위치를 밝힌 횟수.</summary>
        public static float FarLastLength, FarLastSeconds;
        public static int FarReveals;
        /// <summary>시험용: 마지막 FarRoute가 고른 길 종류(직선·거친 길·못 찾아 직선)와 찾는 데 쓴 시간(ms).</summary>
        public static string FarLastKind = "";
        public static float FarLastMs;

        /// <summary>게임에서 한 번 찾을 때 펼치는 칸 한도(한 프레임 멈춤을 줄임, 넘으면 직선).</summary>
        const int FarRouteExpand = 6000;

        /// <summary>
        /// 생략 중 지도상 위치가 따라갈 길(가-3): 목적지가 지도에 밝혀졌으면 게임 생성 지형(WorldGenerator.GetHeight — 불러오지 않은 곳도,
        /// 실제 땅과 0.8m쯤 다름)으로 RoughPath(깊은 물·가파름 피함, 밝혀진 칸 우선), 안 밝혀졌거나 못 찾으면 직선.
        /// 포탈은 WalkVia가 앞(입구까지 걷기)·뒤(출구부터 이 길)로 따로 나눈다.
        /// </summary>
        public static List<RoughPath.P> FarRoute(Vector3 from, Vector3 dest)
        {
            var a = new RoughPath.P(from.x, from.z);
            var b = new RoughPath.P(dest.x, dest.z);
            WorldGenerator wg = WorldGenerator.instance;
            if (wg == null || !Scout.ExploredAt(dest)) { FarLastKind = "직선(안 밝혀짐)"; FarLastMs = 0f; return new List<RoughPath.P> { a, b }; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<RoughPath.P> path = RoughPath.Find(a, b, (x, z) => wg.GetHeight((float)x, (float)z), ZoneSystem.instance.m_waterLevel,
                (x, z) => Scout.ExploredAt(new Vector3((float)x, 0f, (float)z)), FarRouteExpand);
            FarLastMs = (float)sw.Elapsed.TotalMilliseconds;
            FarLastKind = path != null ? $"거친 길 {path.Count}점" : "직선(길 못 찾음)";
            return path ?? new List<RoughPath.P> { a, b };
        }

        /// <summary>
        /// 멀리선 이동 생략(사용자 결정 2026-10-23, Core FarSkip): 일꾼·목적지 모두 플레이어 64m 밖이고 FarSkip.MinDistance m 넘으면
        /// 걷지 않고 길 길이 ÷ m_runSpeed초 서서 기다리며 2초마다 지도상 위치를 밝히고(Scout.Reveal), 다 기다리면 불러와진 구역 안에서
        /// HopStep m씩 목적지 쪽으로 옮긴다(LandNear: 단단한 땅·물 아님·위험 판단 통과). 목적지 ArriveNear m 안이면 마저 걷는다.
        /// 길은 FarRoute(밝혀진 목적지는 물 피한 거친 길). 플레이어가 몸 64m 안에 오면 그만두고 그 자리부터, 지도 위치 64m 안에 오면 그 근처에 내려(FarDrop) 걷는다.
        /// true = 이번 프레임은 생략이 맡음(걷지 않음).
        /// </summary>
        bool FarWalk(float dt, Vector3 dest)
        {
            Vector3 me = m_npc.transform.position;
            m_npc.FarJob = this;
            m_farTickAt = Time.time;
            if (m_far == null)
            {
                m_farCheck -= dt;
                if (m_farCheck > 0f) return false;
                m_farCheck = 2f;
                float d = Utils.DistanceXZ(me, dest);
                if (!FarSkip.Worth(d) || HopBlocked() != null || !FarFromPlayers(dest)) return false;
                ZNetView nv = m_npc.GetComponent<ZNetView>();
                if (nv == null || !nv.IsOwner()) return false;
                m_far = new FarSkip(FarRoute(me, dest), m_npc.m_runSpeed);
                m_farDest = dest;
                m_ai.StopMoving();
                FarSkips++;
                FarLastLength = (float)m_far.Length;
                FarLastSeconds = (float)m_far.Seconds;
                FarReveals = 0;
                m_farCheck = 0f; // 다 기다린 뒤 옮기기 재시도 간격으로 씀
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 멀리선 이동 생략 {m_far.Length:0}m({FarLastKind}, 직선 {d:0}m) → {m_far.Seconds:0}초 기다림(달리기 {m_npc.m_runSpeed:0.0})");
            }
            if (Utils.DistanceXZ(m_farDest, dest) > 1f) { m_far = null; return false; } // 목적지가 바뀜: 다음 검사에서 새로
            if (m_far.Waiting)
            {
                m_ai.Hold("멀리선 이동 생략");
                if (m_far.Tick(dt))
                {
                    RoughPath.P mp = m_far.MapPos;
                    var map = new Vector3((float)mp.X, me.y, (float)mp.Z);
                    if (!FarFromPlayers(me)) return FarCancel("플레이어가 몸에 다가옴");
                    if (!FarFromPlayers(map)) return FarDrop(dest);
                    Scout.Reveal(map);
                    FarLastMap = map;
                    FarReveals++;
                }
                return true;
            }
            m_far.Tick(dt);
            if (m_far.LandTimedOut) return FarCancel($"옮기기 {FarSkip.LandLimit:0}초 넘음");
            if (!FarSkip.HopAim(new RoughPath.P(me.x, me.z), new RoughPath.P(dest.x, dest.z), FarSkip.HopStep, out RoughPath.P aim))
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 멀리선 이동 생략 끝({m_far.Seconds:0}초 + 옮기기 {m_far.Landing:0.0}초, 목적지 {Utils.DistanceXZ(me, dest):0}m) → 마저 걷기");
                FarLanded++;
                m_far = null;
                m_farCheck = 30f; // 마저 걷는 짧은 길은 다시 생략하지 않음
                m_bestDist = float.MaxValue;
                m_progressTimer = 0f;
                return false;
            }
            var to = new Vector3((float)aim.X, dest.y, (float)aim.Z);
            m_farCheck -= dt;
            if (m_farCheck > 0f) return true;
            m_farCheck = 1f; // 못 옮기면 1초 뒤 다시(시간 넘으면 그만둠)
            if (!ZoneSystem.instance.IsZoneLoaded(to)) return true; // 일꾼 둘레 구역이 만들어지길 기다림(FarZones)
            if (LandNear(to, out string why)) { m_farCheck = 0f; return true; }
            // 그 둘레에 내릴 자리가 없음: 반만 가 본다
            to = Vector3.Lerp(me, to, 0.5f);
            if (Utils.DistanceXZ(me, to) >= 5f && LandNear(to, out why)) m_farCheck = 0f;
            else Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 멀리선 이동 생략 옮길 자리 없음({why})");
            return true;
        }

        /// <summary>멀리선 이동 생략으로 서서 기다리는 중이면 지도상 위치(지도 아이콘용, MapIcons), 아니면 null.</summary>
        public Vector3? FarMapPos
        {
            get
            {
                if (m_far == null || !m_far.Waiting || Time.time - m_farTickAt > 3f) return null;
                RoughPath.P mp = m_far.MapPos;
                return new Vector3((float)mp.X, m_npc.transform.position.y, (float)mp.Z);
            }
        }

        /// <summary>시험용: 생략 중 플레이어가 지도상 위치에 다가와 그 근처(또는 길 앞쪽)에 내린 횟수와 마지막 지도상 위치.</summary>
        public static int FarDropped;
        public static Vector3 FarLastMap;

        /// <summary>
        /// 생략 중 플레이어가 지도상 위치 64m 안에 옴(사용자 결정 2026-10-23 [고침]): 몸을 그 근처 안전한 땅(LandNear, 플레이어 곁 허용)에
        /// 내려 거기서부터 걷는다. [기본안] 없으면 길 앞쪽으로 10m씩 6곳. 다 안 되면 그 자리부터 걷기(FarCancel).
        /// </summary>
        bool FarDrop(Vector3 dest)
        {
            string why = "자리 없음";
            int i = 0;
            foreach (RoughPath.P a in m_far.DropAims(10, 6))
            {
                var aim = new Vector3((float)a.X, dest.y, (float)a.Z);
                if (i++ == 0) FarLastMap = aim;
                if (!ZoneSystem.instance.IsZoneLoaded(aim)) { why = "구역 안 불러와짐"; continue; }
                if (!LandNear(aim, out why, true)) continue;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 멀리선 이동 생략 중 플레이어가 지도 위치에 다가옴({m_far.Elapsed:0}/{m_far.Seconds:0}초) → 길 {(i - 1) * 10}m 앞 자리에 내림({why}) → 걷기");
                FarDropped++;
                m_far = null;
                m_farCheck = 30f;
                m_bestDist = float.MaxValue;
                m_progressTimer = 0f;
                return false;
            }
            return FarCancel($"플레이어가 지도 위치에 다가옴, 내릴 자리 없음({why})");
        }

        bool FarCancel(string why)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 멀리선 이동 생략 그만둠({why}, {m_far.Elapsed:0}/{m_far.Seconds:0}초) → 그 자리부터 걷기");
            FarCancelled++;
            m_far = null;
            m_farCheck = 30f;
            m_bestDist = float.MaxValue;
            m_progressTimer = 0f;
            return false;
        }

        /// <summary>막힘 판단 기준점: 포탈로 가는 중이면 포탈 입구, 아니면 목적지.</summary>
        Vector3 PortalOr(Vector3 dest) => m_portal.HasValue ? new Vector3(m_portal.Value.X, m_portal.Value.Y, m_portal.Value.Z) : dest;

        bool Walk(float dt, Vector3 point, float dist) =>
            m_ai.MoveToPoint(dt, point, dist, Utils.DistanceXZ(point, m_npc.transform.position) > 10f);

        /// <summary>MoveStuckLimit초 동안 목표에 1m도 가까워지지 않거나 깊은 물 앞에서 섰으면 막힌 것.</summary>
        bool Stuck(float dt, Vector3 point)
        {
            float d = Utils.DistanceXZ(point, m_npc.transform.position);
            if (d < m_bestDist - 1f) { m_bestDist = d; m_progressTimer = 0f; return false; }
            if (m_far != null) { m_progressTimer = 0f; return false; } // 멀리선 이동 생략 중: 서서 기다리는 것이지 막힌 게 아니다
            if (m_ai.Carving) return false; // 구덩이에서 경사로를 깎는 중: 막힌 게 아니다
            if (m_ai.OnInteriorPath) { m_progressTimer = 0f; return false; } // 지하묘지 안 길을 따라 도는 중(목표에서 잠시 멀어질 수 있음)
            m_progressTimer += dt;
            if (m_ai.WaterBlockedTime > WorkerAI.WaterGiveUp) return true;
            if (m_progressTimer <= (float)StuckHop.NearStuckSeconds) return false;
            // 근처 막힘 건너뛰기를 할 걷기는 5초에 막힘으로 본다(사용자 10-31), 그 밖은 MoveStuckLimit까지 해 본다
            return m_progressTimer > MoveStuckLimit || HopBlocked() == NearWhy;
        }
    }
}
