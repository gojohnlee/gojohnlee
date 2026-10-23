using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 채집 작업 (구상서 6장, 8-2장): 짐 정리(Unload) → 출발 준비(Prep) → 도구 챙기기 → 찾기(보이는 것 → 기억하는 곳 → 안 둘러본 밝혀진 곳) →
    /// 다가가기 → 손으로 줍기 또는 바닐라 공격으로 베기·캐기 → 떨어진 것 줍기 → 짐이 차거나 다 모으면 거점 상자에 넣기 → 남았으면 다시.
    /// 단계마다 시간 제한이 있고, 막히면 다른 대상·다른 곳으로 넘어간다. 얻는 방법(맨손·도끼·곡괭이)은 게임 데이터에서 읽는다.
    /// </summary>
    public sealed class GatherJob : IWorkerJob
    {
        enum Step { Unload, Prep, Tool, GoChest, Search, GoZone, GoCrypt, Approach, Work, Hunt, Collect, Return, Deposit }

        public readonly string Item;
        public readonly int Target;
        public int Delivered { get; private set; }
        public string StepName => m_step.ToString();
        readonly HashSet<ToolKind> m_ways;
        ResourceInfo m_targetInfo;
        /// <summary>지난 휘두름 끝의 대상 체력 기록(HealthMark). null이면 다음 프레임에 잰다.</summary>
        string m_lastHealth;
        bool m_sawTooHard;
        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;

        Step m_step = Step.Unload;
        Prep m_prep;
        Unload m_unload;
        float m_stepTime;
        float m_searchTime;
        Container m_chest;
        ZDOID m_targetId = ZDOID.None;
        Vector3 m_targetPos;
        float m_reach;
        ZoneXY m_goalZone;
        Vector3 m_goalPos;
        PortalLink? m_portal;
        bool m_portalChecked;
        readonly HashSet<ZDOID> m_skip = new HashSet<ZDOID>();
        /// <summary>막힘 복구(사용자 결정 2026-10-23, Core StuckBook): 이번 일 동안 막힌 목적지·귀환 막힘. 작업 저장(WorkItem.Stuck)에 들어가 불러와도 이어진다.</summary>
        StuckBook m_book = new StuckBook();
        public string StuckSave => m_book.Save();
        public void LoadStuck(string text) => m_book = StuckBook.Load(text);
        /// <summary>시험용: 이번 일에서 막혀 뺀 목적지 수.</summary>
        public int StuckExcluded => m_book.ExcludedCount;
        int m_swings, m_swingsNoProgress;
        bool m_wasAttacking;
        float m_bestDist;
        float m_progressTimer;
        bool m_saidTooWeak, m_saidSearching, m_saidExplore, m_saidFound;
        /// <summary>일꾼 포탈(R2 나): 한 몫을 들고 가는 이름표, 이번 일에서 와 본 먼 구역, 재료 모자람을 말했나.</summary>
        string m_portalTag;
        readonly HashSet<ZoneXY> m_visited = new HashSet<ZoneXY>();
        bool m_saidPortalNeed;
        float m_scanTimer;
        GameObject m_itemPrefab;
        /// <summary>아는 곳(밝혀진 곳)을 다 둘러봐서 새 땅으로 나갔다.</summary>
        bool m_exploring;
        float m_exploreTime;
        /// <summary>그만둘 이유. 들고 있는 게 있으면 상자에 넣고 나서 말하고 끝낸다.</summary>
        string m_giveUp;

        // 시간 제한 (구상서 6장 막힘 처리, 8-2장 헛수고 상한)
        const float SearchLimit = 15f * 60f;
        const float ExploreLimit = 30f * 60f;
        /// <summary>안 둘러본 밝혀진 구역을 거점에서 몇 구역까지 찾는지.</summary>
        const int KnownRing = 10;
        const float MoveStuckLimit = 20f;
        /// <summary>지하묘지(⑦ 철): 입구까지 가는 최대 시간, 안에서 머무는 최대 시간, 안에서 보는 거리.</summary>
        const float CryptGoLimit = 6f * 60f;
        const float CryptStayLimit = 15f * 60f;
        const float CryptSight = 90f;
        /// <summary>한 대상에 쓰는 최대 시간(큰 광맥 포함). 진척이 30초 없으면 그보다 먼저 그만둔다(WorkIdleLimit).</summary>
        const float ChopLimit = 10f * 60f;
        /// <summary>나무를 쓰러뜨린 뒤 통나무가 멈출 때까지 기다리는 시간.</summary>
        const float FallWait = 4f;
        /// <summary>쓰러뜨리거나 쪼갠 뒤 둘레 통나무가 멈추기를 기다리는 최대 시간(비탈을 오래 구르는 것 등), 그 둘레(m).</summary>
        const float SettleLimit = 12f, LogWatch = 15f;
        /// <summary>오는 통나무가 이만큼 안이면(가장 가까운 점까지 수평 m) 비킨다.</summary>
        const float DodgeRange = 6f;
        /// <summary>
        /// 이보다 빠르면(m/s, rad/s) 통나무가 아직 구른다, 일꾼 쪽으로 이보다 빨리 다가오면(m/s) 오는 것.
        /// 바닐라 통나무 충돌 피해는 그 점 속도 1m/s부터(반토막 beech_log_half 최소 1·최대 5, 피해 합 80, 일꾼이 받는 건 약 50 → 체력 25면 약 3m/s에 죽음).
        /// </summary>
        const float RollSpeed = 0.5f, RollSpin = 1f, ComeSpeed = 0.5f;

        public GatherJob(WorkerNpc npc, string item, int target, HashSet<ToolKind> ways)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            Item = item;
            Target = target;
            m_ways = ways;
            m_hunt = ways.Contains(ToolKind.Hunt);
        }

        /// <summary>사냥(⑧): 캐거나 줍는 게 아니라 사냥감을 찾아 잡고 떨어진 것을 줍는다. 나머지(짐 정리·준비·탐색·귀환·넣기)는 채집과 같다.</summary>
        readonly bool m_hunt;
        Character m_prey;
        Vector3 m_preyPos;
        bool m_huntArmed;
        /// <summary>이 일 동안 안 잡을 사냥감과 그 시각(Time.time까지): 위험해 보이거나 못 닿은 것.</summary>
        readonly Dictionary<Character, float> m_huntSkip = new Dictionary<Character, float>();
        const float PreySight = 60f, HuntLimit = 120f, HuntHurt = 0.4f, LootWait = 15f;
        /// <summary>활로 이만큼 쏘았는데 체력이 그대로면(안 맞거나 피해가 안 드는 것) 그 짐승은 그만둔다.</summary>
        const int MissLimit = 10;

        /// <summary>지금 그 방법을 쓸 수 있는지(맨손·사냥은 항상, 도구는 들고 있을 때).</summary>
        bool Usable(ToolKind kind) => kind == ToolKind.None || kind == ToolKind.Hunt || WorkerNpc.BestTool(m_npc.GetInventory(), kind) != null;

        /// <summary>시작 전 확인용: NPC나 거점 상자에 쓸 수 있는 방법이 하나라도 있는지.</summary>
        public static bool CanStart(WorkerNpc npc, HashSet<ToolKind> ways)
        {
            foreach (var w in ways)
            {
                if (w == ToolKind.None || w == ToolKind.Hunt || WorkerNpc.BestTool(npc.GetInventory(), w) != null) return true;
                if (FindToolChest(npc, w) != null) return true;
            }
            return false;
        }

        public static string ItemLabel(string prefab)
        {
            GameObject go = ObjectDB.instance?.GetItemPrefab(prefab);
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
        }

        Dictionary<string, string> Args(int n = -1) => new Dictionary<string, string>
        {
            ["item"] = ItemLabel(Item),
            ["n"] = (n < 0 ? Target : n).ToString(),
        };

        void Go(Step s)
        {
            if (m_step != s) Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: {m_step} → {s}");
            if (m_step == Step.Hunt && s != Step.Hunt) m_npc.StopDraw();
            m_step = s;
            m_stepTime = 0f;
            // 이전 단계의 걷기 방향이 남으면 계속 걸어간다(0.3.2: 돌을 줍고 한참 갔다가 돌아옴).
            m_ai.StopMoving();
            m_bestDist = float.MaxValue;
            m_progressTimer = 0f;
            m_portal = null;
            m_portalChecked = false;
            m_gateUsedAt = -10f; // 앞 단계(나가는 Teleport)의 시각이 남으면 다음 묘지 입구 앞에서 한참 선다
            if (s == Step.Deposit) m_chest = null; // 넣을 상자는 그때그때 고른다
            if (s == Step.Prep) m_prep = null;
            if (s == Step.Unload) m_unload = null;
        }

        bool Fail(string key)
        {
            m_npc.Say(key, Args());
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item} 실패: {key} ({Delivered}/{Target})");
            return false;
        }

        /// <summary>그만둔다. 모은 게 있으면 먼저 상자에 넣는다.</summary>
        bool GiveUp(string key)
        {
            if (Carried() == 0) return Fail(key);
            m_giveUp = key;
            Go(Step.Return);
            return true;
        }

        public bool Exploring => m_exploring;

        /// <summary>위험해서 버린 구역(Guard: 도망쳤거나 두 번 물러남). 이 일 동안은 그 구역의 대상·구역을 고르지 않는다.</summary>
        readonly HashSet<ZoneXY> m_avoid = new HashSet<ZoneXY>();

        bool Avoided(Vector3 p) => m_avoid.Count > 0 && m_avoid.Contains(Senses.ZoneOf(p));

        /// <summary>
        /// 이 일 동안 이미 가 봤거나(와서 봤는데 쓸 게 없었다) 못 간(길이 막힘) 구역. 지식(Brain)은 시야 안의 목록 대상을 다 세므로(FindInSight가 안 고르는
        /// 건축물 옆 통·건너뛴 대상 포함) 지웠다가도 다시 채워진다 → 구역을 또 고르면 두 구역을 오가거나(0.3.8 주석 시험 2차) 막힌 자리에서
        /// 같은 구역을 되풀이한다(1차: 나무 옆 38m에서 5분). 위험 구역(m_avoid)과 달리 그 구역 안의 대상은 보이면 여전히 고른다.
        /// </summary>
        readonly HashSet<ZoneXY> m_tried = new HashSet<ZoneXY>();

        /// <summary>이 일 동안 이미 다녀왔거나 못 간 지하묘지 자리.</summary>
        readonly List<Vector3> m_cryptsDone = new List<Vector3>();
        Dungeons.Crypt m_crypt;
        float m_insideTime;
        float m_gateUsedAt = -10f;
        int m_doorTries;
        bool m_doorPhase;

        /// <summary>Guard: 거기는 위험하다. 그 구역(과 지금 목표)을 버리고 다른 데를 찾는다. 들고 있는 건 그대로.</summary>
        public void AvoidHere(Vector3 danger)
        {
            if (m_npc.InInterior())
            {
                // 지하묘지 안 위험(Guard 물러남·도망): 이 묘지는 그만, 나가는 Teleport로 나간다. 안과 입구는 xz가 같아 장소 기억은 입구 구역에 쌓인다.
                m_cryptsDone.Add(m_crypt.Pos);
                if (m_step != Step.Return) Go(Step.Return);
                return;
            }
            m_avoid.Add(Senses.ZoneOf(danger));
            if (m_step == Step.Return || m_step == Step.Deposit || m_step == Step.Unload || m_step == Step.Prep || m_step == Step.Tool || m_step == Step.GoChest) return;
            if (!m_targetId.IsNone()) m_skip.Add(m_targetId);
            if (m_prey != null) m_huntSkip[m_prey] = float.MaxValue;
            m_prey = null;
            Go(Step.Search);
        }

        public string Label => ItemLabel(Item) + " " + Target;

        public string StateLine() => m_npc.Line(m_exploring ? "state.workExplore" : "state.work", new Dictionary<string, string>
        {
            ["item"] = ItemLabel(Item), ["n"] = Target.ToString(), ["done"] = Delivered.ToString(),
        });

        float m_autoPickTimer;

        public bool Update(float dt)
        {
            m_stepTime += dt;
            if (m_npc.InInterior())
            {
                m_insideTime += dt;
                if (m_insideTime > CryptStayLimit && m_step != Step.Return)
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 지하묘지 안 {CryptStayLimit / 60f:0}분 넘음 → 나감");
                    m_cryptsDone.Add(m_crypt.Pos);
                    Go(Step.Return);
                }
            }
            // 플레이어의 자동 줍기처럼 발밑(2m) 물건은 걸어가지 않고 바로 줍는다(0.3.3: 줍는 게 느림).
            m_autoPickTimer -= dt;
            if (m_autoPickTimer <= 0f)
            {
                m_autoPickTimer = 0.25f;
                AutoPickup();
            }
            switch (m_step)
            {
                case Step.Prep: return UpdatePrep(dt);
                case Step.Tool: return UpdateTool();
                case Step.GoChest: return UpdateGoChest(dt);
                case Step.Search: return UpdateSearch(dt);
                case Step.GoZone: return UpdateGoZone(dt);
                case Step.GoCrypt: return UpdateGoCrypt(dt);
                case Step.Approach: return UpdateApproach(dt);
                case Step.Work: return UpdateWork();
                case Step.Hunt: return UpdateHunt(dt);
                case Step.Collect: return UpdateCollect(dt);
                case Step.Return: return UpdateReturn(dt);
                case Step.Deposit: return UpdateDeposit(dt);
                case Step.Unload: return UpdateUnload(dt);
            }
            return false;
        }

        // ---- 준비·도구 ----

        /// <summary>출발 준비(장비 상자·음식 상자, Prep). 처음과 짐을 넣고 다시 나갈 때마다.</summary>
        bool UpdatePrep(float dt)
        {
            if (m_prep == null) m_prep = new Prep(m_npc);
            if (m_prep.Update(dt)) return true;
            Go(Step.Tool);
            return true;
        }

        bool UpdateTool()
        {
            // 필요한데 없는 도구가 상자에 있으면 가지러 간다(한 번에 다 챙긴다).
            bool room = m_npc.GetInventory().GetEmptySlots() > 0, noRoom = false;
            foreach (var w in m_ways)
            {
                if (Usable(w)) continue;
                m_chest = FindToolChest(m_npc, w);
                if (m_chest == null) continue;
                if (room) { Go(Step.GoChest); return true; }
                noRoom = true;
            }
            foreach (var w in m_ways) if (Usable(w)) { Go(Step.Search); return true; }
            if (noRoom)
            {
                // 가방이 차서 도구를 못 받는다(그대로 두면 상자↔도구를 끝없이 오감): 모은 게 있으면 넣고 다시 나온다
                if (Carried() > 0) { Go(Step.Return); return true; }
                return Fail("bagFull");
            }
            return GiveUp("noTool"); // 연장이 부러졌으면 모은 것부터 상자에 넣는다
        }

        static Container FindToolChest(WorkerNpc npc, ToolKind kind)
        {
            if (kind == ToolKind.None || kind == ToolKind.Hunt) return null;
            return Chests.First(npc.Home, inv => WorkerNpc.BestTool(inv, kind) != null, ChestRole.Gear);
        }

        bool UpdateGoChest(float dt)
        {
            if (m_chest == null) { Go(Step.Tool); return true; }
            // Prep처럼 2.5m 안이면 닿은 것(옆 상자·깃발에 막혀 1.8m까지 못 가던 것, coppertest). 못 닿으면 모은 것부터 넣는다.
            if (!Walk(dt, m_chest.transform.position, 1.8f) && Utils.DistanceXZ(m_chest.transform.position, m_npc.transform.position) > 2.5f)
                return m_stepTime < 60f || GiveUp("noTool");
            bool took = Chests.TryEdit(m_chest, inv =>
            {
                foreach (var w in m_ways)
                {
                    if (Usable(w)) continue;
                    ItemDrop.ItemData best = WorkerNpc.BestTool(inv, w);
                    if (best != null && BaseStock.Allow(m_chest, best.m_shared.m_name, 1) > 0) { m_npc.GetInventory().MoveItemToThis(inv, best); BaseStock.Changed(); }
                }
            });
            if (!took) return m_stepTime < 90f || GiveUp("noTool"); // 누가 열고 있다: 기다린다
            Go(Step.Tool); // 다른 상자에 다른 도구가 있으면 또 간다
            return true;
        }

        // ---- 찾기 ----

        bool UpdateSearch(float dt)
        {
            if (Carried() >= Remaining() || Full()) { Go(Step.Return); return true; }
            if (m_npc.InInterior()) return UpdateInside();
            if (m_hunt)
            {
                if (FindPrey()) { Go(Step.Hunt); return true; }
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
            if (Item == Dungeons.ScrapItem && TryStartCrypt()) return true;
            Knowledge k = Brain.Get();
            Vector3 pos = m_npc.transform.position;
            ZoneXY here = Senses.ZoneOf(pos);
            if (Utils.DistanceXZ(Senses.ZoneCenter(here), pos) < 20f)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 구역 {here.X},{here.Y}에 와서 봤는데 쓸 게 없음 / {SightDiag(pos)}");
                k.Forget(here, Item); // 여기 와서 봤는데 없다
                m_tried.Add(here);
            }
            foreach (var m in k.ZonesWith(Item, here))
            {
                if (m.Zone.Equals(here) || m_avoid.Contains(m.Zone) || m_tried.Contains(m.Zone) || ZoneExcluded(m.Zone) || !Scout.Reachable(pos, m.Zone)) continue; // 물 건너·위험한 곳·이미 가 본 곳은 안 간다
                m_goalZone = m.Zone;
                PortalDepart();
                GoZone();
                return true;
            }
            // 1) 아는 곳(밝혀진 곳) 중 안 둘러본 구역: 거점 가까운 곳부터 15분까지
            if (!m_exploring && m_searchTime <= SearchLimit
                && SearchPlan.Next(Senses.ZoneOf(m_npc.Home), here, KnownRing, k, Scout.Explored, z => !m_avoid.Contains(z) && !m_tried.Contains(z) && !ZoneExcluded(z) && Scout.Reachable(pos, z), out m_goalZone))
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
            if (!Dungeons.TryChoose(m_npc.transform.position, out Dungeons.Crypt c, m_cryptsDone)) return false;
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
            if (!Dungeons.IsOpen(m_crypt.Door)) return UpdateOpenDoor(dt, pos);
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
            else if (Stuck(dt, gp)) return AbandonCrypt("입구 앞 막힘");
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
                if (m_exploring && Scout.Explored(m_goalZone)) { Go(Step.Search); return true; }
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
                if (m_skip.Contains(zdo.m_uid) || Avoided(zdo.GetPosition()) || m_book.Excluded(zdo.GetPosition().x, zdo.GetPosition().z)) continue;
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null || !m_ways.Contains(info.Tool) || !info.Produces.Contains(Item)) continue;
                if (info.Pickable && zdo.GetBool(ZDOVars.s_picked)) continue;
                // 묻힌 은 광맥(숨는 광맥)은 위시본을 얻었을 때만 알고, 드러난 것이 먼저(HiddenOres, ⑪).
                bool exposed = info.Hidden && HiddenOres.Exposed(zdo, info);
                if (info.Hidden && !exposed && !HiddenOres.HaveFinder()) continue;
                // 갈린 땅(밭)의 작물은 줍지 않는다: 사람이 기르는 것 — 밭일(FarmJob)만 거둔다.
                if (info.Pickable && Farmland(zdo.GetPosition())) { m_skip.Add(zdo.m_uid); continue; }
                // 건축물 4m 안의 나무·바위는 건드리지 않는다: 쓰러지는 나무·캐낸 바위 위의 상자가 부서진다(0.3.1 사용자 관찰).
                if (info.Tool != ToolKind.None && NearAny(zdo.GetPosition(), pieces, 4f)) { m_skip.Add(zdo.m_uid); continue; }
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
                float score = HiddenOre.Score(dist, info.Priority, info.Hidden, exposed);
                if (score < bestScore) { bestScore = score; best = zdo; }
            }
            return best != null;
        }

        /// <summary>진단: 시야(44m) 안에서 그 물건이 나온다고 목록에 있는 것들과 FindInSight가 고르지 않은 까닭(앞 6개).</summary>
        string SightDiag(Vector3 pos)
        {
            var nearby = Senses.Nearby(pos, Senses.SightRange + 4f);
            var pieces = new List<Vector3>();
            foreach (var zdo in nearby)
                if (Buildings.IsPiece(zdo.GetPrefab())) pieces.Add(zdo.GetPosition());
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var zdo in nearby)
            {
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null || !info.Produces.Contains(Item)) continue;
                if (++n > 6) break;
                string why = m_skip.Contains(zdo.m_uid) ? "건너뜀 목록" : Avoided(zdo.GetPosition()) ? "위험 구역"
                    : !m_ways.Contains(info.Tool) ? "방법 아님" : info.Pickable && zdo.GetBool(ZDOVars.s_picked) ? "이미 땀" : info.Pickable && Farmland(zdo.GetPosition()) ? "밭 작물"
                    : info.Tool != ToolKind.None && NearAny(zdo.GetPosition(), pieces, 4f) ? "건축물 4m 안"
                    : HungLog(zdo, info) ? "높이 걸린 통나무"
                    : info.Tool != ToolKind.None && WorkerNpc.BestTool(m_npc.GetInventory(), info.Tool) == null ? "도구 없음"
                    : info.MinToolTier > 0 && info.Tool != ToolKind.None && WorkerNpc.BestTool(m_npc.GetInventory(), info.Tool) is ItemDrop.ItemData t && info.MinToolTier > t.m_shared.m_toolTier ? "도구 단계 낮음"
                    : Utils.DistanceXZ(zdo.GetPosition(), pos) > Senses.SightRange ? "시야 밖" : "고를 수 있음";
                sb.Append($"[{info.Prefab} {Utils.DistanceXZ(zdo.GetPosition(), pos):0}m {why}]");
            }
            return n == 0 ? "목록에 있는 대상 없음" : sb.ToString();
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

        void Aim(ZDO zdo)
        {
            m_targetId = zdo.m_uid;
            m_targetPos = zdo.GetPosition();
            m_targetInfo = ResourceCatalog.Get(zdo.GetPrefab());
            m_reach = m_targetInfo != null && m_targetInfo.Pickable ? 1.3f : 1.7f;
            ZNetView view = ZNetScene.instance.FindInstance(zdo);
            if (view != null)
            {
                var col = view.GetComponentInChildren<Collider>();
                if (col != null) m_reach += Mathf.Min(1.5f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) * 0.6f);
            }
        }

        // ---- 다가가서 베기 ----

        bool UpdateApproach(float dt)
        {
            ZDO zdo = ZDOMan.instance.GetZDO(m_targetId);
            if (zdo == null) { Go(Step.Collect); return true; }
            m_targetPos = zdo.GetPosition();
            if (m_targetInfo != null && m_targetInfo.Log && m_stepTime < SettleLimit && Rolling(zdo, out Rigidbody body, out Vector3 vel))
            {
                // 고른 통나무가 아직 구른다(쪼갠 반토막·막 쓰러진 통나무): 다가가지 않고 멈출 때까지 기다린다, 이쪽으로 오면 비킨다
                // (E020 진단: 구르는 반토막 쪽으로 걸어가다 부딪힘)
                if (Coming(m_targetPos, body, vel, out Vector3 at)) DodgeLog(dt, at);
                else m_ai.StopMoving();
                return true;
            }
            if (HungLog(zdo, m_targetInfo))
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo.Prefab} 높이 걸린 통나무(뿌리 높이 {m_targetPos.y:0.0}) → 안 쪼갬");
                Skip();
                return true;
            }
            if (Walk(dt, m_targetPos, m_reach) || Utils.DistanceXZ(m_targetPos, m_npc.transform.position) <= m_reach + 0.3f || TouchingRock(zdo))
            {
                m_swings = m_swingsNoProgress = 0;
                m_workIdle = m_closerTime = 0f;
                m_dropCheck = 5f;
                m_lastDigs = m_ai.Digs;
                m_lastBlocked = m_ai.BlockedDigs;
                m_saidDig = false;
                m_farDig = null;
                m_lastHealth = null; // 캐기 첫 프레임에 잰다(HealthMark는 인스턴스가 필요)
                Vector3 me = m_npc.transform.position;
                Plugin.Log.LogInfo($"{m_npc.NpcName}: {m_targetInfo?.Prefab} 캐기 시작(뿌리 ({m_targetPos.x:0.0},{m_targetPos.y:0.0},{m_targetPos.z:0.0})까지 {Utils.DistanceXZ(m_targetPos, me):0.0}m, 높이차 {me.y - m_targetPos.y:0.0})");
                Go(Step.Work);
                return true;
            }
            if (Stuck(dt, m_targetPos) || m_stepTime > 40f)
            {
                LogStuck("다가가기", m_targetPos);
                if (WalkFailed(m_targetPos, true, out bool keep, out _)) return keep;
                Skip();
                RescueIfStuckAgain();
            }
            return true;
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
            StuckNext next = m_book.WalkFailed(dest.x, dest.z, why == null);
            if (next == StuckNext.Hop)
            {
                if (HopToward(dest, out why))
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 막힘 기록 ({dest.x:0},{dest.z:0}) 1번 → 목표 쪽 순간이동 {why}");
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

        /// <summary>목표 쪽 순간이동을 못 하는 까닭(되면 null): 사냥·지하묘지·동행·실내는 안 함(사용자 결정 2026-10-23), 떠날 자리가 플레이어 64m 안(근처)이면 안 함.</summary>
        string HopBlocked()
        {
            if (m_hunt) return "사냥";
            if (m_npc.InInterior()) return "지하묘지·실내"; // 묘지 가기(GoCrypt)는 자기 막힘 처리라 여기 안 옴
            if (m_ai.EscortName != null) return "동행";
            if (!FarFromPlayers(m_npc.transform.position)) return "플레이어 근처";
            return null;
        }

        /// <summary>가장 가까운 플레이어(호스트 + 접속한 사람들의 기준 위치)에서 StuckHop.NearPlayer m 밖인가.</summary>
        static bool FarFromPlayers(Vector3 p)
        {
            if (TestIgnorePlayers) return true;
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
            ZNetView nv = m_npc.GetComponent<ZNetView>();
            if (nv == null || !nv.IsOwner()) { why = "소유자 아님"; return false; }
            if (s_bodyMask == 0) s_bodyMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
            Vector3 from = m_npc.transform.position;
            int tried = 0;
            string last = "후보 없음";
            foreach (var c in StuckHop.Candidates(from.x, from.z, dest.x, dest.z))
            {
                tried++;
                var p = new Vector3((float)c.X, dest.y, (float)c.Z);
                if (!ZoneSystem.instance.IsZoneLoaded(p)) { last = "구역 안 불러와짐"; continue; }
                if (!FarFromPlayers(p)) { last = "플레이어 근처"; continue; }
                if (!ZoneSystem.instance.GetSolidHeight(p, out float h, out Vector3 normal, out GameObject go) || go == null) { last = "땅 없음"; continue; }
                if (go.GetComponentInParent<Heightmap>() == null) { last = "지형 아님(" + go.name + ")"; continue; }
                if (normal.y < StuckHop.MinNormalY) { last = $"가파름({normal.y:0.00})"; continue; }
                p.y = h;
                if (h < ZoneSystem.instance.m_waterLevel + 0.3f || Floating.GetLiquidLevel(p) > h + 0.1f) { last = "물"; continue; }
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.45f, s_bodyMask, QueryTriggerInteraction.Ignore)) { last = "몸 자리 막힘"; continue; }
                if (m_ai.Guard != null && !m_ai.Guard.SafeAt(p)) { last = "위험"; continue; }
                m_ai.StopMoving();
                if (!m_npc.TeleportTo(p + Vector3.up * 0.2f, m_npc.transform.rotation, false)) { last = "옮기기 실패"; continue; }
                Hops++;
                WorkerAI.StuckTeleports++;
                why = $"{Utils.DistanceXZ(from, p):0.0}m → 목표에서 {Utils.DistanceXZ(dest, p):0.0}m (후보 {tried}번째)";
                return true;
            }
            why = $"자리 없음(후보 {tried}개, 마지막: {last})";
            return false;
        }

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
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 같은 자리에서 {m_stuckCount}번 막힘 → 깃발로 강제 복귀");
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
            ZDO zdo = ZDOMan.instance.GetZDO(m_targetId);
            ZNetView view = zdo != null ? ZNetScene.instance.FindInstance(zdo) : null;
            if (zdo == null || view == null || !view.IsValid())
            {
                // 쓰러졌다(또는 부서졌다): 떨어진 것을 줍는다.
                Go(Step.Collect);
                return true;
            }
            if (m_targetInfo == null) { Skip(); return true; }
            if (m_targetInfo.Log) m_targetPos = zdo.GetPosition(); // 통나무는 맞으면 밀려 움직인다: 쪼개진 뒤 물러날 때 지금 자리에서
            if (m_targetInfo.Pickable)
            {
                // 손으로 줍기: 바닐라 Interact(스킬만 플레이어 전용). 떨어진 것은 Collect가 줍는다.
                Pickable pick = view.GetComponent<Pickable>();
                if (pick == null || !pick.CanBePicked()) { Skip(); return true; }
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
            if (!attacking && m_dropCheck <= 0f && m_targetInfo.Tool == ToolKind.Pickaxe)
            {
                m_dropCheck = 5f;
                ItemDrop near = NearestDrop();
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
                if (!progress && m_farAim)
                {
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
                else m_swingsNoProgress++;
                m_lastDigs = m_ai.Digs;
                m_lastBlocked = m_ai.BlockedDigs;
                if (m_swingsNoProgress >= 4)
                {
                    // 바닐라 공격 판정이 닿지 않는다(구상서 12장: 4번째부터 같은 피해 값을 직접 준다. 예: 바위 밑에 낀 통나무 — 휘두름이 바위에 먼저 닿음).
                    // 직접 피해가 들면 진척이니 건너뛰지 않고 다음 휘두름에도 직접 준다. 못 들면(너무 단단함) 8번째에 건너뛴다.
                    DirectHit(view);
                    if (HealthMark(zdo, view) != mark) { m_swingsNoProgress = 4; m_workIdle = 0f; }
                }
                // 직접 피해 뒤에 잰다: 그 피해를 다음 휘두름이 바닐라 진척으로 잘못 보지 않게
                m_lastHealth = HealthMark(zdo, view);
                if (m_swingsNoProgress >= 8) { Plugin.Log.LogInfo($"{m_npc.NpcName}: 휘두름 8번 헛침 → 건너뜀 / {m_npc.MoveDebug}"); Skip(); return true; }
            }
            m_wasAttacking = attacking;
            if (!attacking && (m_farAim || m_ai.IsLookingAt(aim, 25f))) m_npc.StartAttack(null, false);
            return true;
        }

        float m_workIdle, m_closerTime, m_dropCheck;
        int m_lastDigs, m_lastBlocked, m_digOnly;
        Collider m_aimCol, m_digAim;
        const int DigOnlyLimit = 10;
        bool m_saidDig;
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
            foreach (var col in view.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger) continue;
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
            m_npc.RaiseSkill(tool.m_shared.m_skillType);
            Plugin.Log.LogInfo($"{m_npc.NpcName}: 공격 판정이 안 닿아 직접 피해 {hit.m_damage.m_chop + hit.m_damage.m_pickaxe:0} " +
                               $"({m_targetInfo?.Prefab} {hitCol?.name}, 맞은 점까지 {Vector3.Distance(point, me):0.0}m, 방향 ({hit.m_dir.x:0.0},{hit.m_dir.y:0.0},{hit.m_dir.z:0.0}))");
        }

        void Skip()
        {
            if (!m_targetId.IsNone()) m_skip.Add(m_targetId);
            m_targetId = ZDOID.None;
            Go(Step.Search);
        }

        // ---- 사냥 (⑧) ----

        /// <summary>60m 안에서 그 물건을 떨어뜨리는 사냥감 중 가장 가까운 것(혼자 상대해도 되는 것만). 찾으면 m_prey에 둔다.</summary>
        bool FindPrey()
        {
            Vector3 me = m_npc.transform.position;
            Character best = null;
            float bestD = PreySight;
            bool canShoot = !m_bowFailed && m_npc.ShootingBow() != null;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || c.IsTamed() || c is WorkerNpc || !Prey.Is(Item, c, canShoot)) continue;
                if (m_huntSkip.TryGetValue(c, out float until) && Time.time < until) continue;
                Vector3 p = c.transform.position;
                float d = Utils.DistanceXZ(p, me);
                if (d >= bestD || Mathf.Abs(p.y - me.y) > 15f || Avoided(p)) continue;
                if (!m_ai.Guard.SafePrey(c)) { m_huntSkip[c] = Time.time + 120f; continue; }
                best = c;
                bestD = d;
            }
            m_prey = best;
            m_huntArmed = false;
            m_preyShots = 0;
            if (best != null) Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: {Utils.GetPrefabName(best.gameObject)} {bestD:0}m 앞");
            return best != null;
        }

        /// <summary>쫓아가 친다(Guard.UpdateFight와 같은 방식). 죽으면 떨어진 것을 줍고, 2분 안에 못 잡거나 체력이 40% 아래면 그만둔다.</summary>
        bool UpdateHunt(float dt)
        {
            if (m_prey != null && !m_prey.IsDead()) m_preyPos = m_prey.transform.position;
            if (m_prey == null || m_prey.IsDead())
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 잡음({m_stepTime:0}초)");
                m_prey = null;
                m_targetPos = m_preyPos;
                m_targetInfo = null;
                Go(Step.Collect);
                return true;
            }
            if (m_npc.GetHealth() < m_npc.GetMaxHealth() * HuntHurt)
            {
                m_huntSkip[m_prey] = float.MaxValue;
                m_prey = null;
                return GiveUp("huntHurt");
            }
            if (m_stepTime > HuntLimit)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: {HuntLimit:0}초 안에 못 잡아 포기");
                m_huntSkip[m_prey] = float.MaxValue;
                m_prey = null;
                Go(Step.Search);
                return true;
            }
            if (Prey.NeedsBow(m_prey)) return UpdateShoot(dt);
            if (!m_huntArmed || m_npc.HoldingBow()) { m_huntArmed = true; m_npc.HoldWeapon(); } // 없으면 맨손으로
            ItemDrop.ItemData weapon = m_npc.GetCurrentWeapon();
            float range = weapon != null ? Mathf.Clamp(weapon.m_shared.m_attack.m_attackRange, 1.5f, 3f) : 1.5f;
            float d = Utils.DistanceXZ(m_preyPos, m_npc.transform.position) - m_prey.GetRadius();
            if (d > range * 0.9f) m_ai.MoveToPoint(dt, m_preyPos, range * 0.8f + m_prey.GetRadius(), true);
            else
            {
                m_ai.StopMoving();
                m_ai.Face(m_prey.GetCenterPoint());
                if (!m_npc.InAttack() && m_ai.IsLookingAt(m_preyPos, 25f)) m_npc.StartAttack(m_prey, false);
            }
            return true;
        }

        int m_shots, m_preyShots;
        static int s_shotMask;

        /// <summary>
        /// 활 사냥(사슴·토끼, 도망만 함): 30m 안에서 가리는 것이 없으면 서서 활을 끝까지 당겨, 갈 곳을 앞질러(Archery.Aim) 쏜다.
        /// 멀거나 가리면 다가간다(당기는 동안은 못 뛴다 → 당김을 풀고). 화살이 떨어지면 그 짐승은 그만두고 근접 무기를 든다.
        /// </summary>
        bool UpdateShoot(float dt)
        {
            if (!m_npc.HoldingBow() && !m_npc.HoldBow())
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 화살이 없어 그만둠(쏜 화살 {m_shots})");
                m_npc.StopDraw();
                m_huntSkip[m_prey] = float.MaxValue;
                m_prey = null;
                m_npc.HoldWeapon();
                Go(Step.Search);
                return true;
            }
            Vector3 eye = m_npc.GetEyePoint(), center = m_prey.GetCenterPoint();
            float d = Utils.DistanceXZ(m_preyPos, m_npc.transform.position);
            if (s_shotMask == 0) s_shotMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            bool clear = d <= Archery.ShootRange && !Physics.Linecast(eye, center, s_shotMask);
            if (!clear)
            {
                m_npc.StopDraw();
                m_ai.MoveToPoint(dt, m_preyPos, d <= Archery.ShootRange ? 4f : Archery.Approach, true); // 가리면 더 다가간다
                return true;
            }
            m_ai.StopMoving();
            Vector3 aim = center;
            ItemDrop.ItemData bow = m_npc.GetCurrentWeapon();
            if (ArrowFlight(bow, out float speed, out float gravity))
            {
                Vector3 rel = center - eye, v = m_prey.GetVelocity();
                if (Archery.Aim(rel.x, rel.y, rel.z, v.x, v.y, v.z, speed, gravity, out float ax, out float ay, out float az))
                    aim = eye + new Vector3(ax, ay, az);
            }
            m_ai.Face(aim);
            if (m_diagShot && bow.m_lastProjectile != null && bow.m_lastProjectile != m_diagPrev)
            {
                // 화살은 공격 애니메이션의 발사 순간에 생긴다
                m_diagShot = false;
                m_diagArrow = bow.m_lastProjectile;
                m_diagTracking = true;
                Plugin.Log.LogInfo($"DIAG 활: 눈 {eye} 겨눔 {aim} 보는 쪽 {m_npc.GetLookDir()} 몸 앞 {m_npc.transform.forward} 화살 {m_diagArrow.transform.position} 속력 {speed:0} 중력 {gravity:0.0} 사슴 속도 {m_prey.GetVelocity()}");
            }
            if (m_diagArrow != null) m_diagLast = m_diagArrow.transform.position;
            else if (m_diagTracking)
            {
                m_diagTracking = false;
                Plugin.Log.LogInfo($"DIAG 화살 사라짐: 마지막 {m_diagLast} 사슴 {center} 체력 {m_prey.GetHealth():0.0}/{m_prey.GetMaxHealth():0}");
            }
            if (m_releaseCheck && Time.time - m_releaseAt > 2f)
            {
                // 놓았는데 화살이 안 나갔다(공격 애니메이션의 발사 순간이 안 옴): 세 번 잇달면 이 일 동안 활을 안 쓴다
                m_releaseCheck = false;
                int now = Gear.CountArrows(m_npc.GetInventory(), bow);
                if (now >= m_releaseArrows)
                {
                    m_dryShots++;
                    Plugin.Log.LogInfo($"DIAG 활 안 나감 {m_dryShots}번째: 비틀 {m_npc.IsStaggering()} 공격 중 {m_npc.InAttack()} 손 {m_npc.HandsDiag()} 땅 {m_npc.IsOnGround()} 화살 {now}");
                    if (m_dryShots >= 3)
                    {
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 활이 안 나가 이 일 동안 활은 안 씀");
                        m_bowFailed = true;
                        m_npc.StopDraw();
                        m_npc.HoldWeapon();
                        m_prey = null;
                        Go(Step.Search);
                        return true;
                    }
                }
                else m_dryShots = 0;
            }
            if (m_npc.InAttack()) return true;
            float draw = m_npc.DrawBow(dt);
            if (draw >= 1f && m_ai.IsLookingAt(aim, 10f) && m_npc.ReleaseBow())
            {
                m_shots++;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 활 쏨 {m_shots}번째, {d:0}m, 화살 {Gear.CountArrows(m_npc.GetInventory(), bow)}개 남음");
                if (m_shots <= 3) { m_diagShot = true; m_diagPrev = bow.m_lastProjectile; }
                m_releaseCheck = true;
                m_releaseAt = Time.time;
                m_releaseArrows = Gear.CountArrows(m_npc.GetInventory(), bow);
                if (++m_preyShots >= MissLimit && m_prey.GetHealth() >= m_prey.GetMaxHealth())
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: {m_preyShots}발 쏘았는데 체력 그대로 → 그 짐승은 그만둠");
                    m_huntSkip[m_prey] = float.MaxValue;
                    m_prey = null;
                    Go(Step.Search);
                }
            }
            return true;
        }

        bool m_diagShot, m_diagTracking, m_releaseCheck, m_bowFailed;
        float m_releaseAt;
        int m_releaseArrows, m_dryShots;
        Vector3 m_diagLast;
        GameObject m_diagArrow, m_diagPrev;

        /// <summary>끝까지 당겼을 때 화살 속력과 중력(바닐라 Attack.ProjectileAttackTriggered: 활 값 + 화살 값, 화살의 Projectile.m_gravity).</summary>
        bool ArrowFlight(ItemDrop.ItemData bow, out float speed, out float gravity)
        {
            speed = 0f;
            gravity = 0f;
            if (bow == null) return false;
            ItemDrop.ItemData ammo = m_npc.GetAmmoItem();
            if (!Gear.IsArrowFor(ammo, bow)) ammo = m_npc.GetInventory().GetAmmoItem(bow.m_shared.m_ammoType);
            Attack a = bow.m_shared.m_attack;
            speed = a.m_projectileVel;
            GameObject proj = a.m_attackProjectile;
            if (ammo != null)
            {
                speed += ammo.m_shared.m_attack.m_projectileVel;
                if (ammo.m_shared.m_attack.m_attackProjectile != null) proj = ammo.m_shared.m_attack.m_attackProjectile;
            }
            Projectile p = proj != null ? proj.GetComponent<Projectile>() : null;
            if (p != null) gravity = p.m_gravity;
            return speed > 0f;
        }

        // ---- 줍기 ----

        bool UpdateCollect(float dt)
        {
            // 떨어지기를 기다린다: 줍는 것(Pickable)은 바로 생기고, 베고 캔 것은 통나무·바위가 부서지며 흩어진다.
            float wait = m_targetInfo != null && m_targetInfo.Pickable ? 0.3f : 1.2f;
            if (m_targetInfo != null && (m_targetInfo.Falls || m_targetInfo.Splits))
            {
                // 서 있는 나무를 쓰러뜨렸거나 통나무를 쪼갰다: 통나무(반토막)가 넘어지며 구르는 동안 뒤로 물러나, 다 멈출 때까지 기다린다
                // (0.3.7: 바로 주우러 가다 구르는 통나무에 깔려 죽음, 체력 25. E020: 쪼갠 자리에 생긴 반토막이 구르는데 1.2초 만에 주우러 가다 죽음)
                Vector3 me = m_npc.transform.position;
                if (m_stepTime < 1.5f)
                {
                    // 쪼갠 자리는 반토막이 통나무 양 끝에 생긴다: 가운데(뿌리)에서 멀어지면 가까운 반토막 쪽으로 갈 수 있어(회차 96 시험: 그 반토막이 2.8m/s로 굴러와 부딪힘)
                    // 가장 가까운 통나무 몸에서 멀어진다. 쓰러뜨린 나무는 예전대로 뿌리 반대쪽(통나무는 맞은 쪽 = 일꾼 반대로 넘어감).
                    Vector3 from = m_targetInfo.Splits ? NearestLog(8f, m_targetPos) : m_targetPos;
                    Vector3 back = me - from;
                    back.y = 0f;
                    if (back.sqrMagnitude < 0.01f) back = -m_npc.transform.forward;
                    m_ai.MoveToPoint(dt, me + back.normalized * 3f, 0.5f, true);
                    return true;
                }
                bool rolling = RollingLogs(LogWatch, out bool coming, out Vector3 log);
                if (m_stepTime < SettleLimit && (rolling || m_stepTime < (m_targetInfo.Falls ? FallWait : wait)))
                {
                    if (coming) DodgeLog(dt, log);
                    else m_ai.StopMoving();
                    return true;
                }
            }
            if (m_stepTime < wait) { m_ai.StopMoving(); return true; }
            if (Full()) { Go(Step.Return); return true; }
            ItemDrop drop = NearestDrop();
            if (drop == null && m_hunt && m_stepTime < LootWait)
            {
                // 사냥감의 떨굼은 쓰러진 몸(Ragdoll)이 사라질 때 나온다 → 그 자리로 가서 기다린다(활로 잡으면 멀다)
                Walk(dt, m_targetPos, 1.5f);
                return true;
            }
            if (drop == null || m_stepTime > 30f)
            {
                Go(Step.Search); // 쓰러진 통나무가 있으면 Search가 먼저 고른다
                return true;
            }
            if (Walk(dt, drop.transform.position, 1.2f) || Utils.DistanceXZ(drop.transform.position, m_npc.transform.position) < 1.6f)
                Pick(drop);
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

        ItemDrop NearestDrop()
        {
            ItemDrop best = null;
            float bestD = float.MaxValue; // 대상 14m 안(Nearby)에서 일꾼에게 가장 가까운 것 — 일꾼 기준 14m면 활로 잡은 먼 사냥감 것을 못 찾음
            foreach (var zdo in Senses.Nearby(m_targetPos, 14f))
            {
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view == null) continue;
                ItemDrop drop = view.GetComponent<ItemDrop>();
                if (drop == null || Utils.GetPrefabName(drop.gameObject) != Item) continue;
                float d = Utils.DistanceXZ(drop.transform.position, m_npc.transform.position);
                if (d < bestD) { bestD = d; best = drop; }
            }
            return best;
        }

        /// <summary>
        /// 일꾼 둘레(range)에 아직 구르는 통나무(TreeLog)가 있는지. coming = 그중 일꾼 쪽으로 오는(머리 위에서 떨어지는 것 포함) 것이 있음, at = 그 가장 가까운 점.
        /// 바닐라 통나무는 ImpactEffect로 부딪힌 사람을 친다: 통나무 그 점 속도가 일꾼 쪽으로 m_minVelocity(반토막 1m/s) 넘을 때만 — 일꾼이 멈춘 통나무에 부딪히는 건 안 다친다.
        /// 그래서 오는 것만 뛰어 비키고 나머지는 서서 기다린다(회차 96 시험: 구르는 것마다 비키다 둘레 반토막을 계속 건드려 12초 내내 안 멈춤).
        /// </summary>
        bool RollingLogs(float range, out bool coming, out Vector3 at)
        {
            coming = false;
            at = Vector3.zero;
            bool any = false;
            float best = float.MaxValue;
            Vector3 me = m_npc.transform.position;
            foreach (var zdo in Senses.Nearby(me, range))
            {
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null || !info.Log || !Rolling(zdo, out Rigidbody body, out Vector3 vel)) continue;
                any = true;
                if (Coming(zdo.GetPosition(), body, vel, out Vector3 p) && Utils.DistanceXZ(p, me) < best) { best = Utils.DistanceXZ(p, me); at = p; coming = true; }
            }
            return any;
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

        /// <summary>
        /// 통나무가 움직이는지. 주인이면 Rigidbody(잠들지 않았고 속도·회전이 있음, body로 돌려줌), 아니면 주인이 쓴 ZDO 속도(ZSyncTransform s_velHash, body null).
        /// </summary>
        static bool Rolling(ZDO zdo, out Rigidbody body, out Vector3 vel)
        {
            ZNetView view = ZNetScene.instance.FindInstance(zdo);
            body = view != null && view.IsValid() && view.IsOwner() ? view.GetComponent<Rigidbody>() : null;
            if (body != null)
            {
                vel = body.linearVelocity;
                return !body.IsSleeping() && (vel.sqrMagnitude > RollSpeed * RollSpeed || body.angularVelocity.sqrMagnitude > RollSpin * RollSpin);
            }
            vel = zdo.GetVec3(ZDOVars.s_velHash, Vector3.zero);
            return vel.sqrMagnitude > RollSpeed * RollSpeed;
        }

        /// <summary>view의 충돌체 중 p에 가장 가까운 점(오목한 메시는 경계 상자로, Closest). 충돌체가 없으면 fallback.</summary>
        static Vector3 NearestPoint(ZNetView view, Vector3 p, Vector3 fallback)
        {
            Vector3 at = fallback;
            if (view == null) return at;
            float best = float.MaxValue;
            foreach (var col in view.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger || !col.enabled) continue;
                Vector3 q = Closest(col, p);
                float d = (q - p).sqrMagnitude;
                if (d < best) { best = d; at = q; }
            }
            return at;
        }

        /// <summary>일꾼 range 안 통나무(구르든 멈췄든) 몸에서 일꾼 가슴에 가장 가까운 점. 없으면 fallback.</summary>
        Vector3 NearestLog(float range, Vector3 fallback)
        {
            Vector3 chest = m_npc.transform.position + Vector3.up, at = fallback;
            float best = float.MaxValue;
            foreach (var zdo in Senses.Nearby(chest, range))
            {
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null || !info.Log) continue;
                Vector3 q = NearestPoint(ZNetScene.instance.FindInstance(zdo), chest, zdo.GetPosition());
                float d = Utils.DistanceXZ(q, chest);
                if (d < best) { best = d; at = q; }
            }
            return at;
        }

        /// <summary>
        /// 이 통나무가 일꾼에게 오는지: 통나무 몸에서 일꾼 가슴에 가장 가까운 점(at)이 DodgeRange 안이고, 그 점이 가슴 쪽으로 ComeSpeed 넘게 다가옴
        /// (주인이면 회전까지 든 그 점 속도 — 넘어지는 긴 통나무는 끝이 빠르다).
        /// </summary>
        bool Coming(Vector3 logPos, Rigidbody body, Vector3 vel, out Vector3 at)
        {
            Vector3 chest = m_npc.transform.position + Vector3.up;
            at = body != null ? NearestPoint(body.GetComponent<ZNetView>(), chest, logPos) : logPos;
            if (body != null) vel = body.GetPointVelocity(at);
            if (Utils.DistanceXZ(at, chest) > DodgeRange) return false;
            Vector3 to = chest - at;
            if (to.sqrMagnitude < 0.01f) return vel.sqrMagnitude > ComeSpeed * ComeSpeed;
            return Vector3.Dot(vel, to.normalized) > ComeSpeed;
        }

        /// <summary>오는 통나무(그 가장 가까운 점 log)의 반대쪽으로 뛰어 비킨다.</summary>
        void DodgeLog(float dt, Vector3 log)
        {
            Vector3 me = m_npc.transform.position, away = me - log;
            away.y = 0f;
            if (Time.time - m_dodgeSaid > 2f)
            {
                m_dodgeSaid = Time.time;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 오는 통나무 비킴({away.magnitude:0.0}m, {m_step} {m_stepTime:0.0}초)");
            }
            if (away.sqrMagnitude < 0.01f) away = -m_npc.transform.forward;
            m_ai.MoveToPoint(dt, me + away.normalized * 3f, 0.5f, true);
        }

        float m_dodgeSaid = -10f;

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

        // ---- 돌아가서 넣기 ----

        bool UpdateReturn(float dt)
        {
            if (m_npc.InInterior())
            {
                LeaveCrypt(dt);
                return true;
            }
            if (Carried() == 0 && m_giveUp == null && m_cryptsDone.Count > 0 && Item == Dungeons.ScrapItem)
            {
                Go(Step.Search); // 빈손으로 지하묘지에서 나옴: 집까지 안 가고 다음 묘지·찾기
                return true;
            }
            Vector3 home = m_npc.Home;
            if (WalkVia(dt, home, 2.5f) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) { m_book.HomeReached(); PortalHome(); Go(Step.Deposit); return true; }
            if (Stuck(dt, PortalOr(home)))
            {
                // 막힘 복구(사용자 결정 2026-10-23): 귀환 걷기는 StuckBook.HomeWalkTries번까지(비켜 가기·점프는 걷기가 계속 함), 넘으면 깃발로 순간이동.
                // 순간이동마저 안 되면 그 자리에서 쉰다("집에 못 가요") — 다시 걷기 되풀이 금지.
                bool limit = m_book.HomeWalkFailed();
                Plugin.Log.LogInfo($"{m_npc.NpcName}: 귀환 막힘 {m_book.HomeStuck}번째(집에서 {Utils.DistanceXZ(home, m_npc.transform.position):0.0}m) {(limit ? "→ 깃발로 순간이동" : "→ 더 걸어 봄")} {m_ai.BlockerAhead(home)} / {m_ai.MoveState}");
                if (!limit) { m_bestDist = float.MaxValue; m_progressTimer = 0f; return true; }
                if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return true; }
                m_book.HomeReached();
                PortalHome();
                Go(Step.Deposit);
            }
            return true;
        }

        bool UpdateDeposit(float dt)
        {
            if (Carried() == 0)
            {
                if (m_giveUp != null) return Fail(m_giveUp);
                // 빈손으로 왔다 = 나가자마자 가방이 찼다(다른 짐). 나갈 때 짐 정리를 이미 했으니 또 나가 봐야 같다
                // (ChatGPT-02: 준비·귀환만 되풀이).
                if (Full()) return Fail("bagFull");
                Go(Step.Unload);
                return true;
            }
            if (m_chest == null) m_chest = DepositChest();
            if (m_chest == null) return Fail("noChest");
            if (!Walk(dt, m_chest.transform.position, 1.8f) && Utils.DistanceXZ(m_chest.transform.position, m_npc.transform.position) > 2.5f)
            {
                if (m_stepTime > 30f)
                {
                    LogStuck("넣기", m_chest.transform.position);
                    m_npc.TeleportTo(m_chest.transform.position + m_chest.transform.forward * 1.5f, m_npc.transform.rotation, false);
                    WorkerAI.StuckTeleports++;
                }
                return true;
            }
            int moved = 0;
            bool done = Chests.TryEdit(m_chest, inv =>
            {
                foreach (var it in new List<ItemDrop.ItemData>(m_npc.GetInventory().GetAllItems()))
                {
                    if (it.m_dropPrefab == null || it.m_dropPrefab.name != Item) continue;
                    int n = it.m_stack;
                    if (!inv.CanAddItem(it)) continue;
                    inv.MoveItemToThis(m_npc.GetInventory(), it);
                    moved += n;
                }
            });
            if (!done)
            {
                if (m_stepTime > 60f) return Fail("chestBusy");
                return true; // 누가 열고 있다
            }
            Delivered += moved;
            Plugin.Log.LogInfo($"{m_npc.NpcName}: {Item} {moved}개 넣음 ({Delivered}/{Target})");
            if (Delivered >= Target)
            {
                m_npc.Say("gatherDone", Args(Delivered));
                return false;
            }
            if (Carried() > 0) return Fail("chestFull");
            if (m_giveUp != null) return Fail(m_giveUp);
            Go(Step.Unload); // 다시 나가기 전에 짐 정리, 먹고 장비 확인
            return true;
        }

        /// <summary>나가기 전 짐 정리: 들고 다닐 것·모으는 물건 말고 다른 짐은 상자에 넣는다(다른 짐으로 가방이 차서 못 모으는 일 방지).</summary>
        bool UpdateUnload(float dt)
        {
            if (m_unload == null) m_unload = new Unload(m_npc, Item) { Hold = m_portalTag != null ? PortalBuilder.ShareItems() : null };
            if (m_unload.Update(dt)) return true;
            Go(Step.Prep);
            return true;
        }

        Container DepositChest() => Chests.ForItem(Item, m_npc.Home, ChestRole.Mats);

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
            return Walk(dt, dest, dist);
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
            if (m_ai.Carving) return false; // 구덩이에서 경사로를 깎는 중: 막힌 게 아니다
            if (m_ai.OnInteriorPath) { m_progressTimer = 0f; return false; } // 지하묘지 안 길을 따라 도는 중(목표에서 잠시 멀어질 수 있음)
            m_progressTimer += dt;
            return m_progressTimer > MoveStuckLimit || m_ai.WaterBlockedTime > WorkerAI.WaterGiveUp;
        }
    }
}
