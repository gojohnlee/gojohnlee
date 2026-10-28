using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public sealed partial class GatherJob
    {
        // ---- 찾기 ----

        bool UpdateSearch(float dt)
        {
            if (Carried() >= Remaining() || Full()) { Go(Step.Return); return true; }
            if (m_npc.InInterior()) return UpdateInside();
            if (m_leftDrops is Vector3 left)
            {
                // 짐이 차서 남기고 온 것부터 줍는다. 두 번 가 봐도 남거나 2분 걸어도 못 가면 잊는다(못 닿는 곳).
                if (m_leftDropsTries < 2 && m_stepTime < 120f && NearestDrop(left) != null)
                {
                    if (!Walk(dt, left, 6f)) return true;
                    m_leftDropsTries++;
                    m_targetPos = left;
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 남기고 간 것 주우러 옴({m_leftDropsTries}번째)");
                    Go(Step.Collect);
                    return true;
                }
                m_leftDrops = null;
            }
            if (m_hunt)
            {
                if (FindPrey()) { Go(Step.Hunt); return true; }
                // ⑫ 풀링 마을 무리·록스처럼 혼자면 못 잡는 사냥감: 호위병과 둘이면 되면 불러서 같이, 바쁘면 남은 양을 호위병이 쉴 때로 미룸(사용자 결정 2026-10-26)
                if ((m_packFoe != null || m_strongFoe != null) && CallGuardForPrey()) return true;
                if (m_guardQueued) return GiveUp("preyWaitGuard");
                // 둘이도 안 되거나 호위병이 없으면 헤매지 말고 그만두고 더 좋은 장비를 달라고 한다
                if (m_packFoe != null) return GiveUp("preyPack");
                if (m_strongFoe != null) return GiveUp("preyStrong");
                // 사슴 가죽처럼 활로만 잡는 것뿐인데 쏠 활·화살이 없다(장비 상자에 넣어 주면 Prep이 챙긴다)
                if (!Prey.Any(Item, false) && (m_bowFailed || m_npc.ShootingBow() == null)) return GiveUp("needBow");
            }
            else if (FindInSight(out ZDO zdo))
            {
                Found(zdo);
                return true;
            }
            if (m_sawTooHard && !m_saidTooWeak)
            {
                // 해 보고 배운다(8-2장): 보이는 건 있는데 지금 연장으론 안 된다. 한 번만 말한다.
                m_saidTooWeak = true;
                m_npc.Say("toolTooWeak", Args());
            }
            // E049: 바닥 위에서 못 고르면 왜인지(바닥 길 시험이 바위 대신 먼 구역으로 감)
            if (FloorReach) Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 바닥 길 켬, 못 고름(바닥 위 {WorkerAI.LavaFloored(m_npc.transform.position)}, 자리 {m_npc.transform.position:F1}) / {SightDiag(m_npc.transform.position)}");
            if (m_sawLava)
            {
                // 용암 한가운데라 못 닿는 것만 보였다: 말하고(Fail·GiveUp이 lavaOut을 말함) 그만둔다 — 불꽃금속 바위는 다 용암 속이라 다른 구역을 찾아 헤매지 않는다(decisions 회차 225)
                LavaOuts++;
                return GiveUp("lavaOut");
            }
            if (Dungeons.InsideOnly(Item) && TryStartCrypt()) return true;
            Knowledge k = Brain.Get();
            Vector3 pos = m_npc.transform.position;
            ZoneXY here = Senses.ZoneOf(pos);
            if (Utils.DistanceXZ(Senses.ZoneCenter(here), pos) < 20f)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 구역 {here.X},{here.Y}에 와서 봤는데 쓸 게 없음 / {SightDiag(pos)}");
                if (!m_sawSkipped) k.Forget(here, Item); // 여기 와서 봤는데 없다(건너뛴 것만 보이면 지식은 그대로)
                m_tried.Add(here);
            }
            foreach (var m in k.ZonesWith(Item, here))
            {
                if (m.Zone.Equals(here) || ZoneAvoided(m.Zone) || m_tried.Contains(m.Zone) || ZoneExcluded(m.Zone)) continue; // 위험한 곳·이미 가 본 곳은 안 간다
                if (!Scout.Reachable(pos, m.Zone))
                {
                    // 바다 건너(이어진 포탈도 없음)는 안 가고 한 번 말한다(사용자 결정 2026-10-23 "바다 건너 일터는 포탈로만"). 좁은 강·늪 물은 예전처럼 말없이 거름.
                    if (!m_saidSea && !Scout.WalkReachable(pos, m.Zone) && Scout.OceanBetween(pos, Senses.ZoneCenter(m.Zone)))
                    {
                        m_saidSea = true;
                        SeaSkips++;
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 구역 {m.Zone.X},{m.Zone.Y}는 바다 건너, 이어진 포탈 없음 → 안 감");
                        m_npc.Say("depart.sea", Args());
                    }
                    continue;
                }
                m_goalZone = m.Zone;
                PortalDepart();
                GoZone();
                return true;
            }
            // 1) 아는 곳(밝혀진 곳) 중 안 둘러본 구역: 거점 가까운 곳부터 15분까지
            if (!m_exploring && m_searchTime <= SearchLimit
                && SearchPlan.Next(Senses.ZoneOf(m_npc.Home), here, KnownRing, k, Scout.Explored, z => !ZoneAvoided(z) && !m_tried.Contains(z) && !ZoneExcluded(z) && Scout.Reachable(pos, z), out m_goalZone))
            {
                if (!m_saidSearching && Delivered == 0 && Carried() == 0) { m_saidSearching = true; m_npc.Say("searching", Args()); }
                GoZone();
                return true;
            }
            // 2) 다 둘러봤거나 15분이 지났다: 새 땅(지도 경계)으로 30분까지. 밤에는 모르는 땅에 가지 않는다.
            if (m_exploreTime > ExploreLimit) return GiveUp("notFound");
            if (EnvMan.IsNight()) return GiveUp(m_exploring ? "nightBack" : "nightNoExplore");
            int biomes = k.BiomesOf(Item); // 그 물건이 나던 바이옴 쪽 경계부터
            if (Scout.NextFrontier(m_npc, 0, 0, biomes, m_avoid, out m_goalZone))
            {
                if (!m_exploring)
                {
                    m_exploring = true;
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 아는 곳에 없음 → 새 땅 탐험");
                    if (!m_saidExplore) { m_saidExplore = true; m_npc.Say("exploreStart", Args()); }
                }
                GoZone();
                return true;
            }
            return GiveUp("notFound");
        }

        // ---- 지하묘지 (⑦ 철: 고철은 늪 지하묘지의 진흙 더미에서만 나온다) ----

        /// <summary>갈 지하묘지가 있으면 거기로 간다(바깥 걷기 → 닫혔으면 열쇠 문 열기 → 입구 Teleport). 열린 곳 먼저, 다 캤으면 열 수 있는 닫힌 곳.</summary>
        bool TryStartCrypt()
        {
            if (EnvMan.IsNight()) return false; // 밤에 늪 바깥을 걷지 않는다(들어가는 건 낮에만)
            var skip = new List<Vector3>(m_cryptsDone);
            if (m_departAvoid.Count > 0) // 출발 판단이 피하라고 한 던전(⑬ 바-5: 시커에게서 도망친 광산)
                foreach (var d in Dungeons.All(Item)) if (m_departAvoid.Contains(Senses.ZoneOf(d.Pos))) skip.Add(d.Pos);
            if (!Dungeons.TryChoose(m_npc.transform.position, out Dungeons.Crypt c, skip, Item)) return false;
            m_crypt = c;
            m_doorTries = 0;
            m_doorPhase = false;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지 {c.Name}로({Utils.DistanceXZ(c.Pos, m_npc.transform.position):0}m, {(c.Open ? "열림" : "닫힘 → 연다")}, 더미 {c.Left})");
            Go(Step.GoCrypt);
            return true;
        }

        bool UpdateGoCrypt(float dt)
        {
            if (m_npc.InInterior())
            {
                m_insideTime = 0f;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지 들어감(y {m_npc.transform.position.y:0})");
                Go(Step.Search);
                return true;
            }
            if (m_stepTime > CryptGoLimit) return AbandonCrypt("시간 초과");
            Vector3 pos = m_npc.transform.position;
            if (!m_crypt.Keyless && !Dungeons.IsOpen(m_crypt.Door)) return UpdateOpenDoor(dt, pos);
            if (m_doorPhase)
            {
                // 문이 열렸다: 문까지의 막힘 기록을 지우고 바로 입구로
                m_doorPhase = false;
                m_bestDist = float.MaxValue;
                m_progressTimer = 0f;
                m_gateUsedAt = -10f;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지 문 열림({m_stepTime:0}초)");
            }
            Teleport gate = Dungeons.Entrance(m_crypt.Pos);
            if (gate == null)
            {
                // 입구 구역이 아직 안 불러와졌다: 가까이 가서 기다린다
                if (!WalkVia(dt, m_crypt.Pos, 15f) && Stuck(dt, PortalOr(m_crypt.Pos))) return AbandonCrypt("입구 가기 막힘");
                return true;
            }
            Vector3 gp = gate.transform.position;
            if (Walk(dt, gp, 1.5f) || Utils.DistanceXZ(gp, pos) < 2.5f)
            {
                if (m_stepTime - m_gateUsedAt > 3f)
                {
                    m_gateUsedAt = m_stepTime;
                    m_ai.StopMoving();
                    if (!Dungeons.Use(m_npc, gate)) return AbandonCrypt("입구 Teleport 실패");
                }
            }
            else if (Stuck(dt, gp))
            {
                // 입구 앞 막힘(⑬ 바-3, 막힘 규칙과 같게): 일꾼·입구 둘레에 플레이어가 없으면 그 입구를 바로 쓴다(들어가 서는 곳은 걸어 들어간 것과 같음)
                if (TestNoInteriorHop || !NoPlayerAround(pos) || !NoPlayerAround(gp)) return AbandonCrypt("입구 앞 막힘");
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 입구 앞 막힘({Vector3.Distance(gp, pos):0}m) → 플레이어 없어 입구 바로 씀");
                m_gateUsedAt = m_stepTime;
                m_ai.StopMoving();
                GateHops++;
                if (!Dungeons.Use(m_npc, gate)) return AbandonCrypt("입구 Teleport 실패");
            }
            return true;
        }

        /// <summary>닫힌 지하묘지(플레이어가 그 열쇠를 얻음): 열쇠 문 앞까지 걸어가 연다. 세 번 해도 안 열리면 이 묘지는 포기.</summary>
        bool UpdateOpenDoor(float dt, Vector3 pos)
        {
            m_doorPhase = true;
            Door door = Dungeons.DoorOf(m_crypt.Door);
            if (door == null)
            {
                // 문 구역이 아직 안 불러와졌다: 가까이 간다
                if (!WalkVia(dt, m_crypt.Pos, 15f) && Stuck(dt, PortalOr(m_crypt.Pos))) return AbandonCrypt("문 가기 막힘");
                return true;
            }
            Vector3 dp = door.transform.position;
            if (Walk(dt, dp, 2f) || Utils.DistanceXZ(dp, pos) < 3f)
            {
                if (m_stepTime - m_gateUsedAt > 3f)
                {
                    if (m_doorTries >= 3) return AbandonCrypt("문 안 열림");
                    m_doorTries++;
                    m_gateUsedAt = m_stepTime;
                    m_ai.StopMoving();
                    if (!Dungeons.Open(m_npc, door)) return AbandonCrypt("문 못 엶(열쇠·와드)");
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지 열쇠 문 엶 {m_doorTries}번째");
                    if (m_doorTries == 1) m_npc.Say("cryptOpen", Args());
                }
            }
            else if (Stuck(dt, dp)) return AbandonCrypt("문 앞 막힘");
            return true;
        }

        bool AbandonCrypt(string why)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지 포기({why})");
            LogStuck("지하묘지", m_crypt.Pos);
            m_cryptsDone.Add(m_crypt.Pos);
            Go(Step.Search);
            RescueIfStuckAgain();
            return true;
        }

        /// <summary>안에서 찾기: 90m 안의 진흙 더미 중 가까운 것. 없으면 이 지하묘지는 다 캔 것 → 나간다.</summary>
        bool UpdateInside()
        {
            if (FindInSight(out ZDO zdo, CryptSight)) { Found(zdo); return true; }
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지에 더 없음 / {SightDiag(m_npc.transform.position)}");
            m_cryptsDone.Add(m_crypt.Pos);
            Go(Step.Return);
            return true;
        }

        /// <summary>안에서 나가는 Teleport까지 걸어가 쓴다. 나가는 길이 없거나 2분 넘으면 깃발로.</summary>
        void LeaveCrypt(float dt)
        {
            Teleport exit = Dungeons.Exit(m_npc.transform.position);
            if (exit == null || m_stepTime > 120f)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지 나가는 길 없음/시간 초과 → 깃발로");
                if (!m_ai.TeleportHome()) m_ai.RestHere(m_npc); // 귀환 통일: 도착 확인, 안 되면 그 자리에서 쉼
                return;
            }
            Vector3 ep = exit.transform.position;
            if (ExitHop(dt, exit)) return;
            if (Walk(dt, ep, 1.5f) || Vector3.Distance(ep, m_npc.transform.position) < 2.5f)
            {
                if (m_stepTime - m_gateUsedAt > 3f)
                {
                    m_gateUsedAt = m_stepTime;
                    m_ai.StopMoving();
                    Dungeons.Use(m_npc, exit);
                }
            }
        }

        float m_exitHopWait = -1f;

        /// <summary>시험용: 나가는 길 이동 생략·입구 앞 막힘에 입구 바로 쓴 횟수.</summary>
        public static int ExitHops, GateHops;

        /// <summary>
        /// 나가는 길 이동 생략(⑬ 바-2f, InteriorHop과 같은 규칙): 던전 안 칸 길이 나가는 Teleport까지 끊겼고 일꾼 둘레 64m에 플레이어가 없으면
        /// 직선 거리 ÷ 달리기 속도(15초까지) 서서 기다렸다가 그 Teleport를 바로 쓴다(나가서 서는 곳은 걸어 나간 것과 같음). 기다리는 사이 길이 생기면 그만둔다.
        /// </summary>
        bool ExitHop(float dt, Teleport exit)
        {
            Vector3 ep = exit.transform.position, me = m_npc.transform.position;
            if (TestNoInteriorHop || !m_ai.InteriorNoPath(ep, 1.5f) || !NoPlayerAround(me)) { m_exitHopWait = -1f; return false; }
            if (m_exitHopWait < 0f)
            {
                m_exitHopWait = Mathf.Min(15f, Vector3.Distance(me, ep) / Mathf.Max(1f, m_npc.m_runSpeed));
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 나가는 문까지 던전 안 길 없음({Vector3.Distance(me, ep):0}m) → 이동 생략 {m_exitHopWait:0}초 기다림");
            }
            m_ai.Hold("던전 나가기 이동 생략");
            m_ai.StopMoving();
            m_exitHopWait -= dt;
            if (m_exitHopWait > 0f) return true;
            m_exitHopWait = -1f;
            m_gateUsedAt = m_stepTime;
            ExitHops++;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 던전 나가기 이동 생략 → 나가는 문 씀");
            Dungeons.Use(m_npc, exit);
            return true;
        }

        void GoZone()
        {
            m_goalPos = Scout.GroundPos(m_goalZone);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 구역 {m_goalZone.X},{m_goalZone.Y}로({Utils.DistanceXZ(m_goalPos, m_npc.transform.position):0}m, 탐험 {m_exploring})");
            Go(Step.GoZone);
        }

        /// <summary>보이는 대상으로 간다. 새 땅에서 처음 찾았으면 어디인지 알린다.</summary>
        void Found(ZDO zdo)
        {
            if (m_exploring && !m_saidFound)
            {
                m_saidFound = true;
                Scout.FromHome(m_npc.Home, zdo.GetPosition(), out string dir, out string dist);
                var args = Args();
                args["dir"] = dir;
                args["dist"] = dist;
                m_npc.Say("exploreFound", args);
            }
            m_exploring = false; // 찾았으니 다음엔 다시 아는 곳부터
            ArrivedAt(Senses.ZoneOf(zdo.GetPosition()));
            Aim(zdo);
            Go(Step.Approach);
        }

        bool UpdateGoZone(float dt)
        {
            if (m_exploring)
            {
                m_exploreTime += dt;
                if (m_exploreTime > ExploreLimit) return GiveUp("notFound");
                if (EnvMan.IsNight()) return GiveUp("nightBack");
            }
            else
            {
                m_searchTime += dt;
                if (m_searchTime > SearchLimit) { Go(Step.Search); return true; } // 새 땅으로
            }
            // 가는 길에 보이면 바로 그쪽으로
            m_scanTimer -= dt;
            if (m_scanTimer <= 0f && m_hunt && FindPrey()) { Go(Step.Hunt); return true; }
            if (m_scanTimer <= 0f && !m_hunt && FindInSight(out ZDO zdo))
            {
                Found(zdo);
                return true;
            }
            if (m_scanTimer <= 0f)
            {
                m_scanTimer = 1f;
                // 새 땅으로 가는 길에 목표 구역이 밝혀졌다: 경계가 밀려났으니 다음 경계로(둘러봄 표시는 안 한다)
                // 멀리선 이동 생략 중엔 안 봄: 지도 위치가 밝혔어도 몸은 아직 출발점(내린 뒤에 봄)
                if (m_exploring && m_far == null && Scout.Explored(m_goalZone)) { Go(Step.Search); return true; }
            }
            bool arrived = WalkVia(dt, m_goalPos, 6f);
            if (arrived || Stuck(dt, PortalOr(m_goalPos)))
            {
                if (arrived) ArrivedAt(m_goalZone);
                if (!arrived)
                {
                    LogStuck("구역 가기", m_goalPos);
                    Vector3 c = Senses.ZoneCenter(m_goalZone); // ZoneExcluded와 같은 자리로 센다
                    // 탐험(새 땅 경계)은 순간이동 안 함(사용자 결정 2026-10-23). 옮겼으면 그 구역으로 마저 걷는다.
                    bool done = WalkFailed(c, !m_exploring, out bool keep, out bool moved);
                    if (moved) return true;
                    m_tried.Add(m_goalZone); // 못 간 곳을 또 고르면 같은 자리에서 되풀이한다
                    if (done) { Brain.Get().MarkLooked(m_goalZone); return keep; }
                }
                Brain.Get().MarkLooked(m_goalZone); // 갔거나 못 가는 곳: 다시 뒤지지 않는다
                Go(Step.Search);
                if (!arrived) RescueIfStuckAgain();
            }
            return true;
        }

        /// <summary>
        /// 시야 안에서 그 물건이 나오는 것 중 지금 다룰 수 있는 것.
        /// 가까운 순이되 가산점: 쓰러진 통나무 30m > 큰 나무·바위 10m > 줍는 것 5m > 어린 나무·그루터기·작은 돌 0.
        /// </summary>
        bool FindInSight(out ZDO best, float range = Senses.SightRange)
        {
            best = null;
            m_sawSkipped = false;
            bool inside = m_npc.InInterior();
            float bestScore = float.MaxValue;
            Vector3 pos = m_npc.transform.position;
            var nearby = Senses.Nearby(pos, range + 4f);
            var pieces = new List<Vector3>();
            foreach (var zdo in nearby)
                if (Buildings.IsPiece(zdo.GetPrefab())) pieces.Add(zdo.GetPosition());
            foreach (var zdo in nearby)
            {
                if (Character.InInterior(zdo.GetPosition()) != inside) continue; // 바깥에서 지하묘지 안 물체를, 안에서 바깥 물체를 고르지 않는다
                // 막힘 기록(StuckBook)은 바깥 걷기 자리(xz)라 지하묘지 안(같은 xz, y+5000) 대상에는 안 쓴다 — 회차 136: 밖에서 막힌 자리가 묘지 안 더미를 가렸다
                bool skipped = m_skip.Contains(zdo.m_uid) || Avoided(zdo.GetPosition()) || (!inside && m_book.Excluded(zdo.GetPosition().x, zdo.GetPosition().z));
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null || !m_ways.Contains(info.Tool) || !info.Produces.Contains(Item)) continue;
                // 건너뛴 대상이 보이면 "와서 봤는데 없음"이 아니다 — 지식에서 지우지 않고 이 일 동안만 그 구역을 뺀다(m_tried)
                if (skipped) { m_sawSkipped = true; continue; }
                if (info.Pickable && zdo.GetBool(ZDOVars.s_picked)) continue;
                // 타르에 잠긴 덩어리는 바닐라도 못 줍는다(Pickable.m_tarPreventsPicking — "타르에 붙음") — 떠오를 수 있으니 지식은 그대로
                if (info.Pickable && StuckInTar(zdo)) { m_sawSkipped = true; continue; }
                // 묻힌 은 광맥(숨는 광맥)은 위시본을 얻었을 때만 알고, 드러난 것이 먼저(HiddenOres, ⑪).
                bool exposed = info.Hidden && HiddenOres.Exposed(zdo, info);
                if (info.Hidden && !exposed && !HiddenOres.HaveFinder()) continue;
                // 갈린 땅(밭)의 기른 작물은 줍지 않는다: 사람이 기르는 것 — 밭일(FarmJob)만 거둔다. 기른 작물만 본다: 타르 웅덩이 땅도
                // 갈린 땅 칠(IsCultivated)이라 웅덩이 위 줍는 타르를 밭 작물로 건너뛰었다(회차 174 vh_tartest).
                if (info.Pickable && Crops.IsGrown(info.Prefab) && Farmland(zdo.GetPosition())) { m_skip.Add(zdo.m_uid); continue; }
                // 건축물 4m 안의 나무·바위는 건드리지 않는다: 쓰러지는 나무·캐낸 바위 위의 상자가 부서진다(0.3.1 사용자 관찰).
                // 용암 위 바위는 뺌: 둘레 건물은 거기 가려고 깐 바닥(회차 250, 불꽃금속 나)).
                if (info.Tool != ToolKind.None && NearAny(zdo.GetPosition(), pieces, 4f) && !(FloorReach && OnLava(zdo.GetPosition()))) { m_skip.Add(zdo.m_uid); continue; }
                // 다른 나무·바위에 높이 걸린 쪼개지는 통나무는 고르지 않는다: 밑에서 쪼개면 반토막이 머리 위에 생겨 떨어진다(HungLog, E020)
                if (HungLog(zdo, info)) continue;
                if (info.Tool != ToolKind.None)
                {
                    ItemDrop.ItemData tool = WorkerNpc.BestTool(m_npc.GetInventory(), info.Tool);
                    if (tool == null) continue;
                    if (info.MinToolTier > 0 && info.MinToolTier > tool.m_shared.m_toolTier) { m_sawTooHard = true; continue; }
                }
                float dist = Utils.DistanceXZ(zdo.GetPosition(), pos);
                if (dist > range) continue;
                // ⑭ 용암 한가운데 바위(LeviathanLava 불꽃금속)·줍기 물건은 용암을 안 밟고 닿을 마른 땅이 없으면 못 캔다(decisions 회차 225) — 지식은 그대로
                if (info.Tool != ToolKind.Hunt && LavaLocked(zdo, info.Tool == ToolKind.None)) { m_sawSkipped = true; m_sawLava = true; continue; }
                float score = HiddenOre.Score(dist, info.Priority, info.Hidden, exposed);
                if (score < bestScore) { bestScore = score; best = zdo; }
            }
            return best != null;
        }

        /// <summary>진단: 시야(44m, 지하묘지 안은 CryptSight) 안에서 그 물건이 나온다고 목록에 있는 것들과 FindInSight가 고르지 않은 까닭(앞 6개, 같은 안/밖만).</summary>
        string SightDiag(Vector3 pos)
        {
            bool inside = Character.InInterior(pos);
            float range = inside ? CryptSight : Senses.SightRange;
            var nearby = Senses.Nearby(pos, range + 4f);
            var pieces = new List<Vector3>();
            foreach (var zdo in nearby)
                if (Buildings.IsPiece(zdo.GetPrefab())) pieces.Add(zdo.GetPosition());
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var zdo in nearby)
            {
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null || !info.Produces.Contains(Item) || Character.InInterior(zdo.GetPosition()) != inside) continue;
                if (++n > 6) break;
                string why = m_skip.Contains(zdo.m_uid) ? "건너뜀 목록" : Avoided(zdo.GetPosition()) ? "위험 구역"
                    : !inside && m_book.Excluded(zdo.GetPosition().x, zdo.GetPosition().z) ? "막힘 기록으로 뺀 곳"
                    : !m_ways.Contains(info.Tool) ? "방법 아님" : info.Pickable && zdo.GetBool(ZDOVars.s_picked) ? "이미 땀" : info.Pickable && StuckInTar(zdo) ? "타르에 잠김" : info.Pickable && Crops.IsGrown(info.Prefab) && Farmland(zdo.GetPosition()) ? "밭 작물"
                    : info.Tool != ToolKind.None && NearAny(zdo.GetPosition(), pieces, 4f) && !(FloorReach && OnLava(zdo.GetPosition())) ? "건축물 4m 안"
                    : HungLog(zdo, info) ? "높이 걸린 통나무"
                    : info.Tool != ToolKind.None && WorkerNpc.BestTool(m_npc.GetInventory(), info.Tool) == null ? "도구 없음"
                    : info.MinToolTier > 0 && info.Tool != ToolKind.None && WorkerNpc.BestTool(m_npc.GetInventory(), info.Tool) is ItemDrop.ItemData t && info.MinToolTier > t.m_shared.m_toolTier ? "도구 단계 낮음"
                    : Utils.DistanceXZ(zdo.GetPosition(), pos) > range ? "시야 밖"
                    : info.Tool != ToolKind.Hunt && LavaLocked(zdo, info.Tool == ToolKind.None) ? "용암 한가운데" : "고를 수 있음";
                sb.Append($"[{info.Prefab} {Utils.DistanceXZ(zdo.GetPosition(), pos):0}m {why}]");
            }
            return n == 0 ? "목록에 있는 대상 없음" : sb.ToString();
        }

        /// <summary>
        /// 줍는 것이 타르에 잠겨 지금 못 줍는지(바닐라 Pickable.Interact: m_tarPreventsPicking이고 Floating.IsInTar — 무게중심이 타르 표면보다 0.2m 넘게 아래).
        /// 웅덩이 위 타르 덩어리(Pickable_Tar)는 떠 있다 잠겼다 한다(회차 174 vh_tartest: 잠긴 것을 Interact만 되풀이). 불러오지 않은 것은 모름(false).
        /// </summary>
        static bool StuckInTar(ZDO zdo)
        {
            ZNetView view = ZNetScene.instance.FindInstance(zdo);
            if (view == null) return false;
            Pickable pick = view.GetComponent<Pickable>();
            return pick != null && pick.m_tarPreventsPicking && view.GetComponent<Floating>() is Floating f && f.IsInTar();
        }

        /// <summary>땅 칠로 용암 위인지(애쉬랜드만, 건물 바닥은 안 봄).</summary>
        static bool OnLava(Vector3 p) =>
            WorldGenerator.instance != null && WorldGenerator.instance.GetBiome(p) == Heightmap.Biome.AshLands && (Heightmap.FindHeightmap(p)?.GetLava(p) ?? 0f) > WorkerAI.LavaEdge;

        /// <summary>
        /// 대상이 용암 위(WorkerAI.Lava)이고 닿을 거리(ReachOf) 안 둘레(16방향 × 닿을 거리의 0.5·0.8·1배)에 용암 아닌 땅이 없는지.
        /// 지형이 안 불러진 곳은 모름(false — 가 보면 다시 봄). 둘레는 건물 바닥(WorkerAI.Floored)이면 갈 수 있음, 큰 바위는 바위 겉 바로 밖도 봄.
        /// </summary>
        /// <summary>용암 위 건물 바닥을 설 자리로 쓰는지(불꽃금속 나), 회차 250 — 바닥 위에서 둘레 길을 못 찾고 바닥 밖 용암에 빠져 죽음, E049 풀 때까지 끔 — 시험 FloorWalk만 잠깐 켬).</summary>
        internal static bool FloorReach;

        static bool LavaLocked(ZDO zdo, bool pickable) => LavaStand(zdo, pickable, out bool lava) == null && lava;

        /// <summary>
        /// 용암 위 대상(onLava) 둘레에서 가장 가까운 설 자리(용암 아닌 땅, 건물 바닥이면 그 바닥 높이), 없으면 null. 용암 위가 아니면 null·false.
        /// 회차 250: 대상 자신은 건너뛰고 땅 3m 위에서 봄(바위 몸은 꿰뚫어 봄, 띄운 바닥도 보게). 다가갈 땐 이 자리로 걸어감(바위 가운데로 가면 바닥 밑 땅으로 내려감).
        /// </summary>
        static Vector3? LavaStand(ZDO zdo, bool pickable, out bool onLava)
        {
            Vector3 p = zdo.GetPosition();
            ZNetView view = ZNetScene.instance.FindInstance(zdo);
            onLava = WorkerAI.Lava(p, 3f, view);
            if (!onLava) return null;
            float reach = ReachOf(view, pickable);
            float ext = 0f; // 큰 바위(LeviathanLava)는 닿을 거리 안이 다 바위 몸 → 바위 겉 바로 밖도 봄(TouchingRock으로 닿음)
            if (view != null)
                foreach (var c in view.GetComponentsInChildren<Collider>())
                    if (!c.isTrigger)
                        ext = Mathf.Max(ext, Mathf.Abs(c.bounds.max.x - p.x), Mathf.Abs(c.bounds.min.x - p.x), Mathf.Abs(c.bounds.max.z - p.z), Mathf.Abs(c.bounds.min.z - p.z));
            var dists = new List<float> { reach * 0.5f, reach * 0.8f, reach };
            if (ext + 0.5f > reach) { dists.Add(ext + 0.5f); dists.Add(ext + 1.5f); }
            foreach (float d in dists)
                for (int k = 0; k < 16; k++)
                {
                    Vector3 q = p + new Vector3(Mathf.Cos(k * Mathf.PI / 8f), 0f, Mathf.Sin(k * Mathf.PI / 8f)) * d;
                    if (Heightmap.FindHeightmap(q) == null || WorkerAI.Lava(q, 3f, view)) continue;
                    if (!FloorReach && WorkerAI.Floored(q, 3f, view)) continue;
                    if (WorkerAI.FloorHeight(q, 3f, view, out float y) || ZoneSystem.instance.GetGroundHeight(q, out y)) q.y = y;
                    return q;
                }
            return null;
        }

        /// <summary>갈린 땅이거나 적어 둔 밭(!밭) 안인지.</summary>
        static bool Farmland(Vector3 p)
        {
            if (Brain.Farms.At(p.x, p.z) != null) return true;
            Heightmap hm = Heightmap.FindHeightmap(p);
            return hm != null && hm.IsCultivated(p);
        }

        static bool NearAny(Vector3 p, List<Vector3> points, float range)
        {
            float r2 = range * range;
            foreach (var q in points)
            {
                float dx = p.x - q.x, dz = p.z - q.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        /// <summary>info = 자원 목록 밖 대상(길을 막은 얼음 벽 등, ResourceCatalog.Obstacle)일 때만 준다.</summary>
        void Aim(ZDO zdo, ResourceInfo info = null)
        {
            m_targetId = zdo.m_uid;
            m_targetPos = zdo.GetPosition();
            m_targetInfo = info ?? ResourceCatalog.Get(zdo.GetPrefab());
            m_reach = ReachOf(ZNetScene.instance.FindInstance(zdo), m_targetInfo != null && m_targetInfo.Pickable);
        }

        /// <summary>닿을 거리: 줍기 1.3·그 밖 1.7 + 대상 충돌체 반폭×0.6(1.5까지). 던전 안 줍기는 대상 자리에 걸친 받침(반폭 2.5m 안 충돌체)까지 셈 —
        /// 검은 핵 받침 stand는 Pickable 밖이라 닿을 거리 1.4로는 받침 둘레 칸이 목표가 못 됐다(⑬ 바-2d).</summary>
        public static float ReachOf(ZNetView view, bool pickable)
        {
            float r = pickable ? 1.3f : 1.7f;
            if (view == null) return r;
            var col = view.GetComponentInChildren<Collider>();
            float ext = col != null ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) : 0f;
            Vector3 at = view.transform.position;
            if (pickable && Character.InInterior(at))
                foreach (var c in Physics.OverlapCapsule(at + Vector3.up * 0.3f, at + Vector3.down * 1.5f, 0.4f, InteriorNav.Mask, QueryTriggerInteraction.Ignore))
                {
                    float e = Mathf.Max(c.bounds.extents.x, c.bounds.extents.z);
                    if (e < 2.5f && e > ext) ext = e;
                }
            return r + Mathf.Min(1.5f, ext * 0.6f);
        }

    }
}
