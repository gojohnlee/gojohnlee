using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 채집 작업 (구상서 6장, 8-2장): 도구 챙기기 → 찾기(보이는 것 → 기억하는 곳 → 안 둘러본 밝혀진 곳) →
    /// 다가가기 → 바닐라 공격으로 베기 → 떨어진 것 줍기 → 짐이 차거나 다 모으면 거점 상자에 넣기 → 남았으면 다시.
    /// 단계마다 시간 제한이 있고, 막히면 다른 대상·다른 곳으로 넘어간다. 지금(② 단계)은 도끼로 나오는 것(나무 종류)만.
    /// </summary>
    public sealed class GatherJob
    {
        enum Step { Tool, GoChest, Search, GoZone, Approach, Chop, Collect, Return, Deposit }

        public readonly string Item;
        public readonly int Target;
        public int Delivered { get; private set; }
        public string StepName => m_step.ToString();
        readonly ToolKind m_tool;
        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;

        Step m_step = Step.Tool;
        float m_stepTime;
        float m_searchTime;
        Container m_chest;
        ZDOID m_targetId = ZDOID.None;
        Vector3 m_targetPos;
        float m_reach;
        ZoneXY m_goalZone;
        Vector3 m_goalPos;
        readonly HashSet<ZDOID> m_skip = new HashSet<ZDOID>();
        int m_swings, m_swingsNoProgress;
        float m_lastHealth;
        bool m_wasAttacking;
        float m_bestDist;
        float m_progressTimer;
        bool m_saidTooWeak, m_saidSearching;
        float m_scanTimer;
        GameObject m_itemPrefab;

        // 시간 제한 (구상서 6장 막힘 처리, 8-2장 헛수고 상한)
        const float SearchLimit = 15f * 60f;
        const float MoveStuckLimit = 20f;
        const float ChopLimit = 60f;

        public GatherJob(WorkerNpc npc, string item, int target, ToolKind tool)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            Item = item;
            Target = target;
            m_tool = tool;
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
            m_step = s;
            m_stepTime = 0f;
            m_bestDist = float.MaxValue;
            m_progressTimer = 0f;
            if (s == Step.Deposit) m_chest = null; // 넣을 상자는 그때그때 고른다
        }

        bool Fail(string key)
        {
            m_npc.Say(key, Args());
            Plugin.Log.LogInfo($"{m_npc.NpcName} 작업 {Item} 실패: {key} ({Delivered}/{Target})");
            return false;
        }

        /// <returns>false면 작업 끝(성공이든 실패든). 알림은 여기서 한다.</returns>
        public bool Update(float dt)
        {
            m_stepTime += dt;
            switch (m_step)
            {
                case Step.Tool: return UpdateTool();
                case Step.GoChest: return UpdateGoChest(dt);
                case Step.Search: return UpdateSearch(dt);
                case Step.GoZone: return UpdateGoZone(dt);
                case Step.Approach: return UpdateApproach(dt);
                case Step.Chop: return UpdateChop();
                case Step.Collect: return UpdateCollect(dt);
                case Step.Return: return UpdateReturn(dt);
                case Step.Deposit: return UpdateDeposit(dt);
            }
            return false;
        }

        // ---- 도구 ----

        bool UpdateTool()
        {
            if (m_npc.HoldTool(m_tool)) { Go(Step.Search); return true; }
            m_chest = FindToolChest();
            if (m_chest == null) return Fail("noTool");
            Go(Step.GoChest);
            return true;
        }

        Container FindToolChest()
        {
            Container assigned = Chests.Assigned(Chests.KeyEquip((int)m_npc.Personality));
            if (assigned != null && WorkerNpc.BestTool(assigned.GetInventory(), m_tool) != null) return assigned;
            foreach (var c in Chests.NearBase(m_npc.Home))
                if (WorkerNpc.BestTool(c.GetInventory(), m_tool) != null) return c;
            return null;
        }

        bool UpdateGoChest(float dt)
        {
            if (m_chest == null) { Go(Step.Tool); return true; }
            if (!Walk(dt, m_chest.transform.position, 1.8f)) return m_stepTime < 60f || Fail("noTool");
            if (m_stepTime > 30f) return Fail("noTool");
            bool took = Chests.TryEdit(m_chest, inv =>
            {
                ItemDrop.ItemData best = WorkerNpc.BestTool(inv, m_tool);
                if (best != null) m_npc.GetInventory().MoveItemToThis(inv, best);
            });
            if (!took) return true; // 누가 열고 있다: 기다린다
            if (!m_npc.HoldTool(m_tool)) return Fail("noTool");
            Go(Step.Search);
            return true;
        }

        // ---- 찾기 ----

        bool UpdateSearch(float dt)
        {
            if (Carried() >= Remaining() || Full()) { Go(Step.Return); return true; }
            if (FindInSight(out ZDO zdo))
            {
                Aim(zdo);
                Go(Step.Approach);
                return true;
            }
            Knowledge k = Brain.Get();
            Vector3 pos = m_npc.transform.position;
            ZoneXY here = Senses.ZoneOf(pos);
            if (Utils.DistanceXZ(Senses.ZoneCenter(here), pos) < 20f) k.Forget(here, Item); // 여기 와서 봤는데 없다
            foreach (var m in k.ZonesWith(Item, here))
            {
                if (m.Zone.Equals(here)) continue;
                m_goalZone = m.Zone;
                GoZone();
                return true;
            }
            if (m_searchTime > SearchLimit) return Fail("notFound");
            if (SearchPlan.Next(Senses.ZoneOf(m_npc.Home), here, 5, k, Explored, out m_goalZone))
            {
                if (!m_saidSearching && Delivered == 0 && Carried() == 0) { m_saidSearching = true; m_npc.Say("searching", Args()); }
                GoZone();
                return true;
            }
            return Fail("notFound");
        }

        void GoZone()
        {
            Vector3 c = Senses.ZoneCenter(m_goalZone);
            c.y = ZoneSystem.instance.GetSolidHeight(c, out float h) ? h : WorldGenerator.instance.GetHeight(c.x, c.z);
            m_goalPos = c;
            Go(Step.GoZone);
        }

        static bool Explored(ZoneXY z)
        {
            Minimap map = Minimap.instance;
            if (map == null) return true;
            return MinimapExplored(map, Senses.ZoneCenter(z));
        }

        static readonly System.Func<Minimap, Vector3, bool> MinimapExplored =
            HarmonyLib.AccessTools.MethodDelegate<System.Func<Minimap, Vector3, bool>>(HarmonyLib.AccessTools.Method(typeof(Minimap), "IsExplored"));

        bool UpdateGoZone(float dt)
        {
            m_searchTime += dt;
            if (m_searchTime > SearchLimit) return Fail("notFound");
            // 가는 길에 보이면 바로 그쪽으로
            m_scanTimer -= dt;
            if (m_scanTimer <= 0f && FindInSight(out ZDO zdo))
            {
                Aim(zdo);
                Go(Step.Approach);
                return true;
            }
            if (m_scanTimer <= 0f) m_scanTimer = 1f;
            bool arrived = Walk(dt, m_goalPos, 6f);
            if (arrived || Stuck(dt, m_goalPos))
            {
                Brain.Get().MarkLooked(m_goalZone); // 갔거나 못 가는 곳: 다시 뒤지지 않는다
                Go(Step.Search);
            }
            return true;
        }

        /// <summary>시야 안에서 그 물건이 나오는 것 중 다룰 수 있는 것. 쓰러진 것(통나무) 먼저, 그다음 가까운 것.</summary>
        bool FindInSight(out ZDO best)
        {
            best = null;
            float bestScore = float.MaxValue;
            ItemDrop.ItemData tool = m_npc.GetCurrentWeapon();
            int tier = tool != null ? tool.m_shared.m_toolTier : 0;
            Vector3 pos = m_npc.transform.position;
            foreach (var zdo in Senses.Nearby(pos, Senses.SightRange))
            {
                if (m_skip.Contains(zdo.m_uid)) continue;
                ResourceInfo info = ResourceCatalog.Get(zdo.GetPrefab());
                if (info == null || info.Tool != m_tool || !info.Produces.Contains(Item)) continue;
                if (info.MinToolTier > tier && info.MinToolTier > 0)
                {
                    // 해 보고 배운다(8-2장): 지금은 목록으로 알지만, 한 번만 말한다.
                    if (!m_saidTooWeak) { m_saidTooWeak = true; m_npc.Say("toolTooWeak", Args()); }
                    continue;
                }
                float d = Utils.DistanceXZ(zdo.GetPosition(), pos);
                float score = d - (info.Felled ? 25f : 0f);
                if (score < bestScore) { bestScore = score; best = zdo; }
            }
            return best != null;
        }

        void Aim(ZDO zdo)
        {
            m_targetId = zdo.m_uid;
            m_targetPos = zdo.GetPosition();
            m_reach = 1.7f;
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
            if (Walk(dt, m_targetPos, m_reach) || Utils.DistanceXZ(m_targetPos, m_npc.transform.position) <= m_reach + 0.3f)
            {
                m_swings = m_swingsNoProgress = 0;
                m_lastHealth = Health(zdo);
                Go(Step.Chop);
                return true;
            }
            if (Stuck(dt, m_targetPos) || m_stepTime > 40f) Skip();
            return true;
        }

        bool UpdateChop()
        {
            ZDO zdo = ZDOMan.instance.GetZDO(m_targetId);
            ZNetView view = zdo != null ? ZNetScene.instance.FindInstance(zdo) : null;
            if (zdo == null || view == null || !view.IsValid())
            {
                // 쓰러졌다(또는 부서졌다): 떨어진 것을 줍는다.
                Go(Step.Collect);
                return true;
            }
            if (!m_npc.HoldTool(m_tool)) { Go(Step.Tool); return true; }
            if (m_stepTime > ChopLimit) { Skip(); return true; }

            Vector3 aim = m_targetPos + Vector3.up * 1f;
            m_ai.Face(aim);
            bool attacking = m_npc.InAttack();
            if (m_wasAttacking && !attacking)
            {
                // 한 번 휘두름이 끝났다: 피해가 들어갔는지 보고, 도구를 닳게 한다
                // (바닐라 Attack은 플레이어 도구만 닳게 해서 직접 한다. 구상서 4장: 내구도는 바닐라처럼).
                m_swings++;
                ItemDrop.ItemData tool = m_npc.GetCurrentWeapon();
                if (tool != null && tool.m_shared.m_useDurability)
                {
                    tool.m_durability = Mathf.Max(0f, tool.m_durability - tool.m_shared.m_useDurabilityDrain);
                    if (tool.m_durability <= 0f) { m_npc.UnequipItem(tool); m_npc.Say("toolBroke", Args()); }
                }
                float h = Health(zdo);
                if (h < m_lastHealth - 0.01f) m_swingsNoProgress = 0;
                else m_swingsNoProgress++;
                m_lastHealth = h;
                if (m_swingsNoProgress >= 4)
                {
                    // 바닐라 공격 판정이 닿지 않는다(구상서 12장): 같은 피해 값을 직접 준다.
                    DirectHit(view);
                }
                if (m_swingsNoProgress >= 8) { Skip(); return true; }
            }
            m_wasAttacking = attacking;
            if (!attacking && m_ai.IsLookingAt(aim, 25f)) m_npc.StartAttack(null, false);
            return true;
        }

        void DirectHit(ZNetView target)
        {
            ItemDrop.ItemData tool = m_npc.GetCurrentWeapon();
            IDestructible d = target.GetComponent<IDestructible>();
            if (tool == null || d == null) return;
            var hit = new HitData
            {
                m_damage = tool.GetDamage(),
                m_toolTier = (short)tool.m_shared.m_toolTier,
                m_point = target.transform.position + Vector3.up,
                m_dir = (target.transform.position - m_npc.transform.position).normalized,
                m_skill = tool.m_shared.m_skillType,
            };
            hit.SetAttacker(m_npc);
            d.Damage(hit);
            Plugin.Log.LogInfo($"{m_npc.NpcName}: 공격 판정이 안 닿아 직접 피해 {hit.m_damage.m_chop + hit.m_damage.m_pickaxe:0}");
        }

        static float Health(ZDO zdo) => zdo.GetFloat(ZDOVars.s_health, float.MaxValue);

        void Skip()
        {
            if (!m_targetId.IsNone()) m_skip.Add(m_targetId);
            m_targetId = ZDOID.None;
            Go(Step.Search);
        }

        // ---- 줍기 ----

        bool UpdateCollect(float dt)
        {
            if (m_stepTime < 1.5f) return true; // 떨어지기를 기다린다
            if (Full()) { Go(Step.Return); return true; }
            ItemDrop drop = NearestDrop();
            if (drop == null || m_stepTime > 30f)
            {
                Go(Step.Search); // 쓰러진 통나무가 있으면 Search가 먼저 고른다
                return true;
            }
            if (Walk(dt, drop.transform.position, 1.2f) || Utils.DistanceXZ(drop.transform.position, m_npc.transform.position) < 1.6f)
                drop.Pickup(m_npc);
            return true;
        }

        ItemDrop NearestDrop()
        {
            ItemDrop best = null;
            float bestD = 14f;
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

        // ---- 돌아가서 넣기 ----

        bool UpdateReturn(float dt)
        {
            Vector3 home = m_npc.Home;
            if (Walk(dt, home, 2.5f) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) { Go(Step.Deposit); return true; }
            if (Stuck(dt, home))
            {
                m_npc.TeleportTo(home, m_npc.transform.rotation, false); // 막힌 길 처리는 길 만들기(⑨)에서
                Go(Step.Deposit);
            }
            return true;
        }

        bool UpdateDeposit(float dt)
        {
            if (Carried() == 0) { Go(Step.Search); return true; }
            if (m_chest == null) m_chest = DepositChest();
            if (m_chest == null) return Fail("noChest");
            if (!Walk(dt, m_chest.transform.position, 1.8f) && Utils.DistanceXZ(m_chest.transform.position, m_npc.transform.position) > 2.5f)
            {
                if (m_stepTime > 30f) m_npc.TeleportTo(m_chest.transform.position + m_chest.transform.forward * 1.5f, m_npc.transform.rotation, false);
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
            Go(Step.Search);
            return true;
        }

        Container DepositChest()
        {
            Container assigned = Chests.Assigned(Chests.KeyItem(Item));
            if (assigned != null) return assigned;
            GameObject prefab = ObjectDB.instance.GetItemPrefab(Item);
            ItemDrop.ItemData sample = prefab.GetComponent<ItemDrop>().m_itemData;
            Container fallback = null;
            foreach (var c in Chests.NearBase(m_npc.Home))
            {
                // 장비·음식·씨앗 상자로 지정된 곳에는 넣지 않는다
                if (IsSpecial(c)) continue;
                if (!c.GetInventory().CanAddItem(sample, 1)) continue;
                if (c.GetInventory().HaveItem(sample.m_shared.m_name)) return c; // 이미 같은 게 있는 상자 먼저
                if (fallback == null) fallback = c;
            }
            return fallback;
        }

        static bool IsSpecial(Container c)
        {
            ZDOID id = c.GetComponent<ZNetView>().GetZDO().m_uid;
            foreach (var key in new[] { Chests.KeyEquip(0), Chests.KeyEquip(1), Chests.KeyFood, Chests.KeySeeds })
            {
                Container a = Chests.Assigned(key);
                if (a != null && a.GetComponent<ZNetView>().GetZDO().m_uid == id) return true;
            }
            return false;
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

        /// <summary>BaseAI 길찾기로 걷는다. 도착하면 true.</summary>
        bool Walk(float dt, Vector3 point, float dist)
        {
            float d = Utils.DistanceXZ(point, m_npc.transform.position);
            if (d <= dist) { m_ai.StopMoving(); return true; }
            m_ai.MoveToPoint(dt, point, dist, d > 10f);
            return false;
        }

        /// <summary>MoveStuckLimit초 동안 목표에 1m도 가까워지지 않으면 막힌 것.</summary>
        bool Stuck(float dt, Vector3 point)
        {
            float d = Utils.DistanceXZ(point, m_npc.transform.position);
            if (d < m_bestDist - 1f) { m_bestDist = d; m_progressTimer = 0f; return false; }
            m_progressTimer += dt;
            return m_progressTimer > MoveStuckLimit;
        }
    }
}
