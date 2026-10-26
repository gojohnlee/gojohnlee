using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 밭에 심기(⑨, 구상서 6-3, 평탄화 없음): 상자에서 경작기와 계획(FarmPlan)만큼 씨앗을 짐에 챙겨 밭으로 가서
    /// 심을 자리(Farm.Spots 뱀 꼴)마다 걸어가 갈기 조각을 놓고(TerrainOp.Awake가 칠함) 새싹을 세운다(씨앗은 그때 짐에서 뺌 — 바닐라 PlacePiece처럼).
    /// 자리 거르기는 바닐라 Plant 자라기 판정과 같게: 물 아래·바이옴·지붕(위 광선)·m_growRadius 안 충돌체, 그리고 건축 금지 구역.
    /// 거두기(harvest): 밭에 도착하면 심기 전에 다 자란 작물(Pickable, 작물 Produce)마다 걸어가 Interact(바닐라 RPC_Pick이 떨굼) → 떨군 것을 줍는다.
    /// 심을 것이 없으면(거두기만) 경작기·씨앗을 챙기지 않는다.
    /// 다 심으면(또는 자리·시간이 다 되면) 거점에 돌아와 경작기·남은 씨앗·거둔 것을 상자에 넣는다(Unload).
    /// "!밭"은 ForField(거두기 + Farm.Fill 심기·씨앗 유지), 대기열·재시작 저장은 ToWork(WorkKind.Farm).
    /// </summary>
    public sealed class FarmJob : IWorkerJob
    {
        const float TotalLimit = 15f * 60f, BusyLimit = 30f, GoStuckLimit = 15f /* 막힘 30 → 15초, 사용자 결정 2026-10-26 */, SpotLimit = 15f, HomeLimit = 180f, PickWait = 4f;

        enum Step { Fetch, Go, Harvest, Plant, Home, Store }

        public readonly FarmField Field;
        readonly WorkerNpc m_npc;
        readonly WorkerAI m_ai;
        /// <summary>새싹 프리팹 → 아직 심을 수(챙긴 씨앗만큼으로 줄어듦).</summary>
        readonly List<Ingredient> m_todo = new List<Ingredient>();
        Step m_step = Step.Fetch;
        float m_time, m_stepTime, m_bestDist = float.MaxValue, m_progress;
        List<SpotXZ> m_spots;
        int m_spot, m_total;
        Unload m_unload;
        bool m_abort, m_toldDone, m_toldHarvest;
        readonly bool m_harvest;
        List<Pickable> m_ripe;
        Vector3 m_pickAt;
        float m_pickTime = -1f;
        readonly Dictionary<string, int> m_got = new Dictionary<string, int>();

        public int Planted { get; private set; }
        /// <summary>거둔 작물 수(Interact 한 수).</summary>
        public int Harvested { get; private set; }
        public string StepName => "Farm" + m_step;

        public FarmJob(WorkerNpc npc, FarmField field, FarmPlan plan, bool harvest = false)
        {
            m_npc = npc;
            m_ai = npc.Ai;
            Field = field;
            m_harvest = harvest;
            if (plan != null)
                foreach (var p in plan.Plant)
                    if (p.Amount > 0 && Crops.BySapling(p.Item) != null) m_todo.Add(new Ingredient(p.Item, p.Amount));
        }

        /// <summary>
        /// "!밭"·대기열·이어 하기: 그 밭의 다 자란 것을 거두고 상자(+짐)의 씨앗을 Farm.Fill만큼 심는 일.
        /// 자리 수는 그 바이옴에서 자라는 작물 중 가장 넓은 간격으로 센다(실제 간격은 그보다 좁거나 같아 자리가 모자라지 않음).
        /// </summary>
        public static FarmJob ForField(WorkerNpc npc, FarmField field) => new FarmJob(npc, field, PlanFor(npc, field, null), true);

        /// <summary>"!당근 10"(출발 판단 농사 연결): 거두기 + 그 물건이 나오는 작물을 Farm.Plan으로(씨앗 유지 포함).</summary>
        public static FarmJob ForWant(WorkerNpc npc, FarmField field, string item, int amount, out FarmPlan plan)
        {
            plan = PlanFor(npc, field, new List<Ingredient> { new Ingredient(item, amount) });
            return new FarmJob(npc, field, plan, true);
        }

        static FarmPlan PlanFor(WorkerNpc npc, FarmField field, List<Ingredient> want)
        {
            int biome = Scout.BiomeAt(new Vector3(field.X, 0f, field.Z));
            float spacing = 0f;
            foreach (var c in Crops.All) if (Farm.CanGrow(c, biome)) spacing = Mathf.Max(spacing, Farm.Spacing(c.GrowRadius));
            int slots = spacing > 0f ? Farm.Spots(field.X, field.Z, field.Radius, spacing).Count : 0;
            var chests = Chests.NearBase(npc.Home, ChestRole.Mats);
            Inventory mine = npc.GetInventory();
            System.Func<string, int> stock = item =>
            {
                int n = Count(mine, item);
                foreach (var c in chests) n += Count(c.GetInventory(), item);
                return n;
            };
            return want == null ? Farm.Fill(Crops.All, biome, slots, stock) : Farm.Plan(Crops.All, biome, slots, stock, want);
        }

        /// <summary>저장(WorkSaves)·대기열 칸: 밭 자리만 적고 이어 할 때 ForField로 계획을 다시 세운다.</summary>
        public WorkItem ToWork() => new WorkItem
        {
            Kind = WorkKind.Farm, Resume = true, Label = m_npc.Line("job.farm"), Amount = Field.Radius, FarmX = Field.X, FarmZ = Field.Z,
        };

        public string Label => m_npc.Line("job.farm");
        public string StateLine() => m_npc.Line("state.farm", Args());
        Dictionary<string, string> Args() => new Dictionary<string, string> { ["n"] = Planted.ToString(), ["total"] = m_total.ToString() };

        /// <summary>Guard: 밭 쪽이 위험하다 → 심기를 접고 돌아간다.</summary>
        public void AvoidHere(Vector3 danger) => m_abort = true;

        Vector3 Center => new Vector3(Field.X, ZoneSystem.instance.GetGroundHeight(new Vector3(Field.X, 0f, Field.Z)), Field.Z);

        void Go(Step s)
        {
            Plugin.Log.LogInfo($"{m_npc.NpcName} 밭: {m_step} → {s} (심음 {Planted}/{m_total})");
            m_step = s;
            m_stepTime = 0f;
            m_bestDist = float.MaxValue;
            m_progress = 0f;
            if (s == Step.Store) m_unload = new Unload(m_npc);
            m_ai.StopMoving();
        }

        public bool Update(float dt)
        {
            m_time += dt;
            m_stepTime += dt;
            if ((m_time > TotalLimit || m_abort) && m_step < Step.Home) { TellDone(); Go(Step.Home); }
            switch (m_step)
            {
                case Step.Fetch: return UpdateFetch();
                case Step.Go: return UpdateGo(dt);
                case Step.Harvest: return UpdateHarvest(dt);
                case Step.Plant: return UpdatePlant(dt);
                case Step.Home: return UpdateHome(dt);
                case Step.Store:
                    if (m_unload.Update(dt)) return true;
                    TellDone();
                    return false;
            }
            return false;
        }

        // ── 챙기기: 경작기 1·씨앗(계획만큼, 있는 만큼). 상자는 한 프레임에 고친다(Chests.TryEdit). ──

        bool UpdateFetch()
        {
            if (m_todo.Count == 0 && m_harvest) { Go(Step.Go); return true; } // 거두기만: 챙길 것 없음
            Inventory mine = m_npc.GetInventory();
            if (FindTool(mine) == null)
            {
                Container c = Chests.First(m_npc.Home, inv => FindTool(inv) != null, ChestRole.Gear);
                if (c == null) { m_npc.Say("farmNoTool"); return false; }
                if (Chests.InUse(c)) return m_stepTime < BusyLimit || Busy();
                Chests.TryEdit(c, inv =>
                {
                    var tool = FindTool(inv);
                    if (tool != null && mine.CanAddItem(tool) && BaseStock.Allow(c, tool.m_shared.m_name, 1) > 0) { mine.MoveItemToThis(inv, tool); BaseStock.Changed(); }
                });
                if (FindTool(mine) == null) { m_npc.Say("chestFull"); return false; } // 가방이 꽉 참
            }
            foreach (var c in Chests.NearBase(m_npc.Home, ChestRole.Mats))
                if (Chests.InUse(c)) return m_stepTime < BusyLimit || Busy();
            var chests = Chests.NearBase(m_npc.Home, ChestRole.Mats);
            var taken = new List<string>();
            m_total = 0;
            foreach (var t in m_todo)
            {
                Crop crop = Crops.BySapling(t.Item);
                int cost = Mathf.Max(1, crop.SeedCost);
                int need = t.Amount * cost - Count(mine, crop.Seed);
                if (need > 0) TakeSeeds(chests, mine, crop.Seed, need);
                t.Amount = Mathf.Min(t.Amount, Count(mine, crop.Seed) / cost);
                m_total += t.Amount;
                if (t.Amount > 0) taken.Add(GatherJob.ItemLabel(crop.Produce) + " " + t.Amount);
            }
            if (m_total == 0)
            {
                m_npc.Say("farmNoSeed");
                Go(m_harvest ? Step.Go : Step.Store); // 경작기는 도로 넣는다(거두기는 그대로 간다)
                return true;
            }
            m_npc.Say("farmGo", new Dictionary<string, string> { ["list"] = string.Join(", ", taken.ToArray()) });
            Go(Step.Go);
            return true;
        }

        bool Busy()
        {
            m_npc.Say("chestBusy");
            Go(Step.Store);
            return true;
        }

        static ItemDrop.ItemData FindTool(Inventory inv)
        {
            foreach (var it in inv.GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == Crops.Cultivator) return it;
            return null;
        }

        static int Count(Inventory inv, string prefab)
        {
            string name = CraftStep.SharedName(prefab);
            return name == null ? 0 : inv.CountItems(name);
        }

        /// <summary>상자들에서 씨앗을 짐으로 옮긴다(복사 → 넣기 성공한 만큼만 상자에서 뺌).</summary>
        static void TakeSeeds(List<Container> chests, Inventory mine, string prefab, int need)
        {
            string name = CraftStep.SharedName(prefab);
            if (name == null) return;
            foreach (var c in chests)
            {
                if (need <= 0) return;
                Chests.TryEdit(c, inv =>
                {
                    foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                    {
                        if (need <= 0 || it.m_shared.m_name != name) continue;
                        int n = BaseStock.Allow(c, name, Mathf.Min(need, it.m_stack)); // 사람 제작 막대가 예약한 몫은 안 가져감(보리·아마 씨앗은 재료이기도)
                        if (n <= 0) return;
                        ItemDrop.ItemData copy = it.Clone();
                        copy.m_stack = n;
                        if (!mine.AddItem(copy)) return;
                        inv.RemoveItem(it, n);
                        BaseStock.Changed();
                        need -= n;
                    }
                });
            }
        }

        // ── 밭까지 ──

        bool UpdateGo(float dt)
        {
            Vector3 c = Center;
            float d = Utils.DistanceXZ(c, m_npc.transform.position);
            if (m_ai.MoveToPoint(dt, c, 2f, d > 10f) || d < Field.Radius)
            {
                float spacing = 0f;
                foreach (var t in m_todo) spacing = Mathf.Max(spacing, Farm.Spacing(Crops.BySapling(t.Item).GrowRadius));
                m_spots = Farm.Spots(Field.X, Field.Z, Field.Radius, spacing);
                m_spot = 0;
                if (m_harvest)
                {
                    m_ripe = Ripe(Field);
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 밭 도착: 다 자란 것 {m_ripe.Count}");
                    Go(Step.Harvest);
                    return true;
                }
                Plugin.Log.LogInfo($"{m_npc.NpcName} 밭 도착: 자리 {m_spots.Count}곳, 간격 {spacing:0.00}m");
                Go(Step.Plant);
                return true;
            }
            if (d < m_bestDist - 1f) { m_bestDist = d; m_progress = 0f; }
            else m_progress += dt;
            if (m_progress > GoStuckLimit || m_ai.WaterBlockedTime > WorkerAI.WaterGiveUp)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 밭까지 못 감({d:0}m)");
                TellDone();
                Go(Step.Home);
            }
            return true;
        }

        // ── 거두기 ──

        /// <summary>밭 안의 다 자란 작물(아직 안 거둔 Pickable, 작물 Produce).</summary>
        public static List<Pickable> Ripe(FarmField f)
        {
            var list = new List<Pickable>();
            foreach (var p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
            {
                if (p == null || p.m_itemPrefab == null || p.GetPicked() || !Crops.IsProduce(p.m_itemPrefab.name)) continue;
                Vector3 at = p.transform.position;
                if (f.Contains(at.x, at.z)) list.Add(p);
            }
            return list;
        }

        /// <summary>밭에서 그 물건이 곧 나올 수: 자라는 새싹(Plant) × 수확량 + 다 자란 것(자동 보충이 같은 걸 거듭 심지 않게, 재시작 뒤에도).</summary>
        public static int Coming(FarmField f, string produce)
        {
            int n = 0;
            foreach (var p in Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
            {
                if (p == null) continue;
                Crop c = Crops.BySapling(Utils.GetPrefabName(p.gameObject));
                Vector3 at = p.transform.position;
                if (c != null && c.Produce == produce && f.Contains(at.x, at.z)) n += Mathf.Max(1, c.Yield);
            }
            foreach (var p in Ripe(f))
                if (p.m_itemPrefab.name == produce) n += Mathf.Max(1, p.m_amount);
            return n;
        }

        bool UpdateHarvest(float dt)
        {
            if (m_pickTime >= 0f) return Collect(dt);
            m_ripe.RemoveAll(x => x == null || x.GetPicked());
            if (m_ripe.Count == 0)
            {
                TellHarvest();
                if (m_todo.Exists(x => x.Amount > 0)) Go(Step.Plant);
                else Go(Step.Home);
                return true;
            }
            Vector3 me = m_npc.transform.position;
            m_ripe.Sort((a, b) => Utils.DistanceXZ(a.transform.position, me).CompareTo(Utils.DistanceXZ(b.transform.position, me)));
            Pickable p = m_ripe[0];
            Vector3 at = p.transform.position;
            if (!m_ai.MoveToPoint(dt, at, 1.2f, false) && Utils.DistanceXZ(at, me) > 1.5f)
            {
                if (m_stepTime > SpotLimit)
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 거두기 건너뜀: 못 닿음 {at}");
                    m_ripe.Remove(p);
                    m_stepTime = 0f;
                }
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(at);
            p.Interact(m_npc, false, false); // 주인이면 RPC_Pick이 바로 떨군다
            m_ripe.Remove(p);
            Harvested++;
            m_pickAt = at;
            m_pickTime = 0f;
            return true;
        }

        /// <summary>떨군 것(위로 튀었다 내려옴)을 줍는다. 다 주웠거나 PickWait가 지나면 다음 작물로(못 주운 건 땅에 남음 — 로그).</summary>
        bool Collect(float dt)
        {
            m_pickTime += dt;
            if (m_pickTime < 0.6f) return true;
            int left = 0;
            foreach (Collider col in Physics.OverlapSphere(m_pickAt, 3f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d == null || !d.TryGetComponent(out ZNetView v) || !v.IsValid()) continue;
                string name = Utils.GetPrefabName(d.gameObject);
                if (!Crops.IsProduce(name) && !Crops.All.Exists(c => c.Seed == name)) continue;
                int n = d.m_itemData.m_stack;
                if (m_npc.PickDrop(d))
                {
                    m_got.TryGetValue(name, out int had);
                    m_got[name] = had + n;
                }
                else left++;
            }
            if (left == 0 && m_pickTime > 1.2f || m_pickTime > PickWait)
            {
                if (left > 0) Plugin.Log.LogInfo($"{m_npc.NpcName} 거둔 것 {left}개 못 주움(짐 빈칸 {m_npc.GetInventory().GetEmptySlots()})");
                m_pickTime = -1f;
                m_stepTime = 0f;
                if (left > 0 && m_npc.GetInventory().GetEmptySlots() == 0) { m_ripe.Clear(); } // 짐이 꽉 참 → 거두기 그만
            }
            return true;
        }

        void TellHarvest()
        {
            if (m_toldHarvest || m_got.Count == 0) return;
            m_toldHarvest = true;
            var parts = new List<string>();
            foreach (var kv in m_got) parts.Add(GatherJob.ItemLabel(kv.Key) + " " + kv.Value);
            Plugin.Log.LogInfo($"{m_npc.NpcName} 거둠 {Harvested}곳: {string.Join(", ", parts.ToArray())}");
            m_npc.Say("farmHarvest", new Dictionary<string, string> { ["list"] = string.Join(", ", parts.ToArray()) });
        }

        // ── 심기 ──

        bool UpdatePlant(float dt)
        {
            Ingredient t = m_todo.Find(x => x.Amount > 0);
            if (t == null || m_spot >= m_spots.Count) { TellDone(); Go(Step.Home); return true; }
            Crop crop = Crops.BySapling(t.Item);
            SpotXZ s = m_spots[m_spot];
            Vector3 p = new Vector3(s.X, 0f, s.Z);
            p.y = ZoneSystem.instance.GetGroundHeight(p);
            string why = Unusable(p, crop);
            if (why != null) { Skip(why); return true; }
            float d = Utils.DistanceXZ(p, m_npc.transform.position);
            if (!m_ai.MoveToPoint(dt, p, 1.2f, false) && d > 1.5f)
            {
                if (m_stepTime > SpotLimit) Skip("못 닿음");
                return true;
            }
            m_ai.StopMoving();
            m_ai.Face(p);
            Inventory mine = m_npc.GetInventory();
            int cost = Mathf.Max(1, crop.SeedCost);
            string seed = CraftStep.SharedName(crop.Seed);
            GameObject sapling = ZNetScene.instance.GetPrefab(crop.Sapling);
            GameObject till = Crops.CultivatePrefab;
            if (sapling == null || till == null || seed == null || mine.CountItems(seed) < cost)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} {crop.Sapling} 못 심음(새싹 {sapling != null}, 갈기 {till != null}, 씨앗 {(seed != null ? mine.CountItems(seed) : -1)})");
                t.Amount = 0;
                return true;
            }
            Object.Instantiate(till, p, Quaternion.identity); // 갈기: Awake에서 칠하고 스스로 지워짐
            mine.RemoveItem(seed, cost);
            Object.Instantiate(sapling, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            t.Amount--;
            Planted++;
            m_spot++;
            m_stepTime = 0f;
            return true;
        }

        void Skip(string why)
        {
            if (why != "자리 참") Plugin.Log.LogInfo($"{m_npc.NpcName} 밭 자리 {m_spot} 건너뜀: {why}");
            m_spot++;
            m_stepTime = 0f;
        }

        static readonly int RoofMask = LayerMask.GetMask("Default", "static_solid", "piece");
        static readonly int SpaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
        static readonly Collider[] s_hits = new Collider[8];

        /// <summary>바닐라 Plant 자라기 판정(codemap "농사")과 같은 것을 미리 본다. 쓸 수 있으면 null, 아니면 이유.</summary>
        internal static string Unusable(Vector3 p, Crop crop, bool space = true)
        {
            if (p.y < ZoneSystem.instance.m_waterLevel + 0.1f) return "물";
            if (!Farm.CanGrow(crop, Scout.BiomeAt(p))) return "바이옴";
            if (Location.IsInsideNoBuildLocation(p)) return "건축 금지";
            if (Physics.Raycast(p, Vector3.up, 100f, RoofMask)) return "지붕";
            if (space && Physics.OverlapSphereNonAlloc(p, crop.GrowRadius, s_hits, SpaceMask) > 0) return "자리 참";
            return null;
        }

        // ── 돌아와 넣기 ──

        bool UpdateHome(float dt)
        {
            Vector3 home = m_npc.Home;
            if (m_ai.MoveToPoint(dt, home, 2.5f, true) || Utils.DistanceXZ(home, m_npc.transform.position) < 3f) { Go(Step.Store); return true; }
            if (m_stepTime > HomeLimit)
            {
                // 귀환 통일(요청 코드 정리 나): 깃발 순간이동은 WorkerAI.TeleportHome(도착 확인), 그것도 안 되면 그 자리에서 쉼.
                if (!m_ai.TeleportHome()) { m_ai.RestHere(m_npc); return true; }
                Go(Step.Store);
            }
            return true;
        }

        void TellDone()
        {
            TellHarvest();
            if (m_toldDone || m_total == 0) return;
            m_toldDone = true;
            int left = m_total - Planted;
            string more = left > 0 ? m_npc.Line("farmNoRoom", new Dictionary<string, string> { ["left"] = left.ToString() }) : "";
            m_npc.Say("farmDone", new Dictionary<string, string> { ["n"] = Planted.ToString(), ["more"] = more });
        }
    }
}
