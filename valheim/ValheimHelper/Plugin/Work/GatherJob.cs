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
    public sealed partial class GatherJob : IWorkerJob
    {
        enum Step { Unload, Prep, Tool, GoChest, Search, GoZone, GoCrypt, Approach, Work, Hunt, Collect, Return, Deposit }

        public readonly string Item;
        public readonly int Target;
        public int Delivered { get; private set; }
        public string StepName => m_step.ToString();
        /// <summary>시험용: 가려고 고른 구역.</summary>
        public ZoneXY GoalZone => m_goalZone;
        readonly HashSet<ToolKind> m_ways;
        ResourceInfo m_targetInfo;
        /// <summary>지난 휘두름 끝의 대상 체력 기록(HealthMark). null이면 다음 프레임에 잰다.</summary>
        string m_lastHealth;
        bool m_sawTooHard;
        /// <summary>사냥감 둘레 무리가 세서 못 잡은 적 이름(preyPack 대사). 한 번 보면 이 일 동안 둔다.</summary>
        string m_packFoe;
        /// <summary>사냥감 한 마리가 혼자 상대하기엔 센 것(록스 등, SafePrey가 무리 없이 거절)의 이름(preyStrong 대사). 한 번 보면 이 일 동안 둔다.</summary>
        string m_strongFoe;
        /// <summary>혼자면 안 되던 가장 가까운 사냥감(동료와 합쳐 다시 셈), 동료를 이미 불렀는지(이 일에 한 번).</summary>
        Character m_hardPrey;
        bool m_mateCalled;
        /// <summary>마지막 FindInSight에서 목록 대상이 보였지만 건너뜀(m_skip·위험 구역·막힘 기록)이라 안 고름.</summary>
        bool m_sawSkipped;
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
        FarSkip m_far;          // 멀리선 이동 생략 중인 걷기(null = 걷는 중)
        Vector3 m_farDest;
        float m_farTickAt;     // FarWalk를 마지막으로 부른 때(Time.time) — 버려진 일의 지도 위치를 안 보이려고
        float m_farCheck;       // 생략할지 다시 보기까지(초)
        readonly HashSet<ZDOID> m_skip = new HashSet<ZDOID>();
        /// <summary>막힘 복구(사용자 결정 2026-10-23, Core StuckBook): 이번 일 동안 막힌 목적지·귀환 막힘. 작업 저장(WorkItem.Stuck)에 들어가 불러와도 이어진다.</summary>
        StuckBook m_book = new StuckBook();
        public string StuckSave => m_book.Save();
        /// <summary>시험용: 이 대상을 겨누고 다가가기부터(vh_hoptest 멀리서 목표 앞 막힘 칸).</summary>
        public void TestApproach(ZDO zdo) { Aim(zdo); Go(Step.Approach); }
        internal void TestReturn() { if (m_step != Step.Return) Go(Step.Return); }
        public void LoadStuck(string text) => m_book = StuckBook.Load(text);
        /// <summary>시험용: 이번 일에서 막혀 뺀 목적지 수.</summary>
        public int StuckExcluded => m_book.ExcludedCount;
        int m_swings, m_swingsNoProgress;
        bool m_wasAttacking;
        float m_bestDist;
        float m_progressTimer;
        bool m_saidTooWeak, m_saidSearching, m_saidExplore, m_saidFound, m_saidSea;
        /// <summary>시험용: 바다 건너라 안 간 구역을 말한 횟수.</summary>
        public static int SeaSkips;
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
        const float MoveStuckLimit = 10f; // 막힘 시간 제한 절반(20 → 10초, 사용자 결정 2026-10-26)
        /// <summary>지하묘지(⑦ 철): 입구까지 가는 최대 시간, 안에서 머무는 최대 시간, 안에서 보는 거리.</summary>
        const float CryptGoLimit = 6f * 60f;
        const float CryptStayLimit = 15f * 60f;
        const float CryptSight = 90f;
        /// <summary>한 대상에 쓰는 최대 시간(큰 광맥 포함). 진척이 30초 없으면 그보다 먼저 그만둔다(WorkIdleLimit).</summary>
        const float ChopLimit = 10f * 60f;

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

        /// <summary>
        /// 나서기 전 연장 판단(⑪ 3): 맨손·사냥이거나 짐·장비 상자에 그 물건을 캘 단계(ResourceCatalog.MinTier)의 연장이 있으면 Ok,
        /// 연장은 있는데 다 단계가 모자라면 Weak(청동 곡괭이로 은), 아무것도 없으면 None.
        /// </summary>
        public static ToolState ToolFor(WorkerNpc npc, HashSet<ToolKind> ways, string item)
        {
            bool any = false;
            foreach (var w in ways)
            {
                if (w == ToolKind.None || w == ToolKind.Hunt) return ToolState.Ok;
                int need = ResourceCatalog.MinTier(item, w);
                System.Func<Inventory, bool> strong = inv => WorkerNpc.BestTool(inv, w) is ItemDrop.ItemData t && t.m_shared.m_toolTier >= need;
                if (strong(npc.GetInventory()) || Chests.First(npc.Home, inv => strong(inv), ChestRole.Gear) != null) return ToolState.Ok;
                if (CanStart(npc, new HashSet<ToolKind> { w })) any = true;
            }
            return any ? ToolState.Weak : ToolState.None;
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
            m_far = null;
            m_farCheck = 0f;
            m_gateUsedAt = -10f; // 앞 단계(나가는 Teleport)의 시각이 남으면 다음 묘지 입구 앞에서 한참 선다
            if (s == Step.Deposit) m_chest = null; // 넣을 상자는 그때그때 고른다
            if (s == Step.Prep) m_prep = null;
            if (s == Step.Unload) m_unload = null;
        }

        bool Fail(string key)
        {
            var args = Args();
            if (m_packFoe != null || m_strongFoe != null) args["foe"] = m_packFoe ?? m_strongFoe;
            m_npc.Say(key, args);
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

        /// <summary>도망 뒤 목표(Core FleeBook): 거점 안 목표는 두 번째 도망부터 뺌, 거점 안 위험 구역은 도망이 쌓인 뒤에만 피함.</summary>
        readonly FleeBook<ZDOID> m_fled = new FleeBook<ZDOID>();

        bool InBase(Vector3 p) => BaseArea.Contains(Utils.DistanceXZ(p, m_npc.Home));

        /// <summary>출발 판단이 덜 위험한 곳으로 바꿔 가며 피하라고 한 위험한 곳(거점 밖만, 사용자 결정 2026-10-26). 이 일 동안 안 고른다.</summary>
        readonly HashSet<ZoneXY> m_departAvoid = new HashSet<ZoneXY>();

        public void AvoidZone(ZoneXY z) => m_departAvoid.Add(z);

        bool Avoided(Vector3 p) => (m_avoid.Count > 0 || m_departAvoid.Count > 0) && ZoneAvoided(Senses.ZoneOf(p));

        bool ZoneAvoided(ZoneXY z) => m_departAvoid.Contains(z) || m_avoid.Contains(z) && (m_fled.AvoidInBase || !InBase(Senses.ZoneCenter(z)));

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
            if (m_hunt && m_packFoe == null && PreyNear(danger, out string foe))
            {
                // ⑫ 사냥감이 위험한 무리 속에 있어 물러나거나 도망쳤다 → 다른 데를 헤매지 말고 같이 가 달라고 한다(다음 Search에서 preyPack)
                m_packFoe = foe;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 사냥감이 위험 곁에 있음({foe}) → 도움 요청");
            }
            if (m_step == Step.Return || m_step == Step.Deposit || m_step == Step.Unload || m_step == Step.Prep || m_step == Step.Tool || m_step == Step.GoChest) return;
            bool hasTarget = !m_targetId.IsNone();
            bool drop = m_fled.Fled(m_targetId, hasTarget, InBase(hasTarget ? m_targetPos : m_npc.transform.position));
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item}: 위험 → 구역 {Senses.ZoneOf(danger).X},{Senses.ZoneOf(danger).Y} 피함" +
                               (m_fled.AvoidInBase ? "" : "(거점 안은 빼고)") +
                               (!hasTarget ? "" : $", 목표 {m_targetInfo?.Prefab}(위험에서 {Utils.DistanceXZ(m_targetPos, danger):0}m, 집에서 {Utils.DistanceXZ(m_targetPos, m_npc.Home):0}m) " +
                                                  (drop ? "버림" : "거점 안이라 도망 뒤 다시")));
            if (drop) m_skip.Add(m_targetId);
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


        // ---- 돌아가서 넣기 ----

        bool UpdateReturn(float dt)
        {
            if (m_npc.InInterior())
            {
                LeaveCrypt(dt);
                return true;
            }
            if (Carried() == 0 && m_giveUp == null && m_cryptsDone.Count > 0 && Dungeons.InsideOnly(Item))
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
                // 상자 가기 막힘: 진척 없이 MoveStuckLimit(근처 15초)이면 상자 앞으로(예전 단계 30초 — 막힘 시간 절반, 사용자 결정 2026-10-26). 먼 상자로 그냥 오래 걷는 건 60초까지
                if (Stuck(dt, m_chest.transform.position) || m_stepTime > 60f)
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
    }
}
