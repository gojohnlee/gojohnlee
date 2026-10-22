using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 일꾼 포탈 놓기·짝·재료·철거(R2 가, 사용자 결정 2026-10-23 건축 예외로 포탈만). 호스트에서만.
    /// 재료 = portal_wood의 Piece.m_resources(실제 제작법), 거점 재료 상자(불러온 것)에서 한 프레임에 뺌. 모자라면 아무것도 안 뺌.
    /// 짝 = 두 포탈 ZDO "tag"를 같은 "일꾼-&lt;일터&gt;"로 → Game.ConnectPortals(5초마다, 여기서도 바로 부름)가 이어 줌.
    /// 일꾼 것 표시 = ZDO MarkKey(플레이어 포탈은 표시가 없어 철거·이름 바꾸기 안 함). 철거 = 재료(m_recover인 것) 전부 상자로 → 다 들어가야 지움.
    /// 제작대: 바닐라는 놓는 사람 둘레 작업대를 요구하지만(Player.PlacePiece) 일꾼은 작업대를 짓지 않으므로 따르지 않음([기본안], decisions.txt).
    /// </summary>
    public static class PortalBuilder
    {
        public const string MarkKey = "vh_wportal";
        public const string UsedDayKey = "vh_wportal_day";
        static int s_mask;

        public static Piece Prefab()
        {
            GameObject go = ZNetScene.instance?.GetPrefab(Portals.PrefabName);
            return go != null ? go.GetComponent<Piece>() : null;
        }

        /// <summary>포탈 하나에 드는 재료(프리팹 이름).</summary>
        public static List<PortalCost> Cost()
        {
            var list = new List<PortalCost>();
            Piece p = Prefab();
            if (p == null) return list;
            foreach (var r in p.m_resources)
                if (r.m_resItem != null && r.m_amount > 0) list.Add(new PortalCost(Utils.GetPrefabName(r.m_resItem.gameObject), r.m_amount));
            return list;
        }

        /// <summary>일꾼이 놓은 포탈 ZDO 전부(구역이 안 불러와져도).</summary>
        public static List<ZDO> Mine()
        {
            var result = new List<ZDO>();
            if (ZDOMan.instance == null) return result;
            var list = ZdoScan.All(Portals.PrefabName);
            foreach (var z in list) if (z.GetBool(MarkKey)) result.Add(z);
            return result;
        }

        public static int Pairs()
        {
            var tags = new List<string>();
            foreach (var z in Mine()) tags.Add(z.GetString(ZDOVars.s_tag));
            return WorkerPortal.Pairs(tags);
        }

        static int Have(List<Container> chests, string prefab)
        {
            string name = CraftStep.SharedName(prefab);
            if (name == null) return 0;
            int n = 0;
            foreach (var c in chests) n += c.GetInventory().CountItems(name);
            return n;
        }

        /// <summary>
        /// 자리 찾기: near 둘레 2~12m(가까운 것부터)에서 포탈 하나 놓을 곳. 조건: 구역 불러와짐, 지형(바위·건물 위 아님), 평평(발판 네 귀 높이차 0.6m 안),
        /// 물·용암 아님, 건축 금지 장소 아님, 남의 와드 안 아님(PrivateArea.CheckAccess wardCheck — 호스트가 쓸 수 있는 와드는 됨), 놓을 자리에 바위·건물·나무 없음.
        /// </summary>
        public static bool FindSpot(Vector3 near, out Vector3 spot, out Quaternion rot, out string why)
        {
            spot = near;
            rot = Quaternion.identity;
            if (s_mask == 0) s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle");
            string last = "후보 없음";
            int tried = 0;
            for (float r = 2f; r <= 12f; r += 2f)
            {
                int n = Mathf.Max(6, Mathf.RoundToInt(r * 3f));
                for (int i = 0; i < n; i++)
                {
                    tried++;
                    float a = i * Mathf.PI * 2f / n;
                    var p = near + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    // 포탈 앞(출구 쪽)이 near를 보게: 거점·일터 쪽으로 나옴
                    Vector3 face = near - p; face.y = 0f;
                    Quaternion q = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face.normalized) : Quaternion.identity;
                    if (!SpotOk(p, q, out float h, out last)) continue;
                    spot = new Vector3(p.x, h, p.z);
                    rot = q;
                    why = $"{Utils.DistanceXZ(near, spot):0.0}m (후보 {tried}번째)";
                    return true;
                }
            }
            why = $"자리 없음(후보 {tried}개, 마지막: {last})";
            return false;
        }

        static bool SpotOk(Vector3 p, Quaternion q, out float h, out string why)
        {
            h = 0f;
            if (!ZoneSystem.instance.IsZoneLoaded(p)) { why = "구역 안 불러와짐"; return false; }
            if (Location.IsInsideNoBuildLocation(p)) { why = "건축 금지 장소"; return false; }
            if (!PrivateArea.CheckAccess(p, 3f, false, true)) { why = "남의 와드"; return false; }
            if (!ZoneSystem.instance.GetSolidHeight(p, out h, out Vector3 normal, out GameObject go) || go == null) { why = "땅 없음"; return false; }
            Heightmap hm = go.GetComponentInParent<Heightmap>();
            if (hm == null) { why = "지형 아님(" + go.name + ")"; return false; }
            float lo = h, hi = h;
            foreach (var off in new[] { new Vector3(-1.5f, 0f, -0.8f), new Vector3(1.5f, 0f, -0.8f), new Vector3(-1.5f, 0f, 0.8f), new Vector3(1.5f, 0f, 0.8f) })
            {
                if (!ZoneSystem.instance.GetGroundHeight(p + q * off, out float g)) { why = "땅 없음(귀)"; return false; }
                lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
            }
            if (hi - lo > 0.6f) { why = $"안 평평({hi - lo:0.0}m)"; return false; }
            var at = new Vector3(p.x, h, p.z);
            if (h < ZoneSystem.instance.m_waterLevel + 0.3f || Floating.GetLiquidLevel(at) > h + 0.1f) { why = "물"; return false; }
            if (hm.IsLava(at)) { why = "용암"; return false; }
            if (Physics.CheckBox(at + Vector3.up * (hi - h + 2.2f), new Vector3(1.7f, 1.9f, 0.7f), q, s_mask, QueryTriggerInteraction.Ignore)) { why = "자리 막힘"; return false; }
            why = "";
            return true;
        }

        /// <summary>
        /// 한 쌍 놓기: home(거점 깃발) 둘레와 site(일터) 둘레에 자리를 찾고, 재료 둘 몫을 거점 재료 상자에서 빼고, 두 포탈을 같은 이름표로 놓는다.
        /// 실패하면 아무것도 안 바뀜(why에 까닭, missing에 모자란 재료). 두 자리 모두 구역이 불러와져 있어야 한다.
        /// </summary>
        public static bool PlacePair(string siteName, Vector3 home, Vector3 baseNear, Vector3 siteNear, out List<ZDO> placed, out Dictionary<string, int> missing, out string why)
        {
            placed = new List<ZDO>();
            missing = new Dictionary<string, int>();
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { why = "호스트 아님"; return false; }
            Piece prefab = Prefab();
            if (prefab == null) { why = "포탈 프리팹 없음"; return false; }
            string tag = WorkerPortal.Tag(siteName);
            int pairs = Pairs();
            if (!WorkerPortal.CanAddPair(pairs)) { why = $"한도 {WorkerPortal.MaxPairs}쌍"; return false; }
            foreach (var z in Mine()) if (z.GetString(ZDOVars.s_tag) == tag) { why = "이미 있는 이름표 " + tag; return false; }
            if (!FindSpot(baseNear, out Vector3 a, out Quaternion ra, out string wa)) { why = "거점 쪽 " + wa; return false; }
            if (!FindSpot(siteNear, out Vector3 b, out Quaternion rb, out string wb)) { why = "일터 쪽 " + wb; return false; }
            if (Utils.DistanceXZ(a, b) < 5f) { why = "두 자리가 너무 가까움"; return false; }

            var cost = Cost();
            var chests = Chests.NearBase(home, ChestRole.Mats);
            foreach (var kv in WorkerPortal.Missing(cost, k => Have(chests, k), 2)) missing[kv.Key] = kv.Value;
            if (missing.Count > 0) { why = "재료 모자람"; return false; }
            foreach (var c in chests) if (Chests.InUse(c)) { why = "상자 사용 중"; return false; }
            foreach (var kv in WorkerPortal.Need(cost, 2)) CraftStep.Take(chests, kv.Key, kv.Value);

            long today = Today();
            var spots = new[] { a, b };
            var rots = new[] { ra, rb };
            for (int i = 0; i < 2; i++)
            {
                ZDO zdo = Spawn(prefab, spots[i], rots[i], tag, today);
                if (zdo != null) placed.Add(zdo);
            }
            Game.instance?.ConnectPortals();
            why = $"{tag}: 거점 쪽 {wa}, 일터 쪽 {wb}";
            Plugin.Log.LogInfo($"일꾼 포탈 놓음 {why}");
            return placed.Count == 2;
        }

        static ZDO Spawn(Piece prefab, Vector3 at, Quaternion rot, string tag, long today)
        {
            GameObject go = Object.Instantiate(prefab.gameObject, at, rot);
            ZDO zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return null;
            zdo.Set(ZDOVars.s_tag, tag);
            zdo.Set(ZDOVars.s_tagauthor, "");
            zdo.Set(MarkKey, true);
            zdo.Set(UsedDayKey, today);
            return zdo;
        }

        static long Today() => EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;

        public static int MineWithTag(string tag) => Mine().FindAll(z => z.GetString(ZDOVars.s_tag) == tag).Count;

        /// <summary>포탈 한 몫(재료 전부)을 들고 있나.</summary>
        public static bool Carrying(Inventory inv)
        {
            var cost = Cost();
            if (cost.Count == 0) return false;
            foreach (var kv in WorkerPortal.Need(cost, 1))
            {
                string name = CraftStep.SharedName(kv.Key);
                if (name == null || inv.CountItems(name) < kv.Value) return false;
            }
            return true;
        }

        /// <summary>
        /// 먼 일터로 떠날 때(R2 나): placeBase면 거점 쪽 포탈을 깃발 둘레에 놓고(재료 한 몫), 한 몫은 일꾼 짐에 넣는다(일터에 가서 PlaceCarried).
        /// 재료는 거점 재료 상자에서 — 필요한 몫이 다 있을 때만 뺀다(missing). 짐에 못 들어가면 아무것도 안 바뀜.
        /// </summary>
        public static bool StartPair(string tag, Vector3 home, Inventory carry, bool placeBase, out Dictionary<string, int> missing, out string why)
        {
            missing = new Dictionary<string, int>();
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { why = "호스트 아님"; return false; }
            Piece prefab = Prefab();
            if (prefab == null) { why = "포탈 프리팹 없음"; return false; }
            int shares = placeBase ? 2 : 1;
            Vector3 a = home; Quaternion ra = Quaternion.identity; string wa = "";
            if (placeBase)
            {
                if (!WorkerPortal.CanAddPair(Pairs())) { why = $"한도 {WorkerPortal.MaxPairs}쌍"; return false; }
                if (MineWithTag(tag) > 0) { why = "이미 있는 이름표 " + tag; return false; }
                if (!FindSpot(home, out a, out ra, out wa)) { why = "거점 쪽 " + wa; return false; }
            }
            var cost = Cost();
            var chests = Chests.NearBase(home, ChestRole.Mats);
            foreach (var kv in WorkerPortal.Missing(cost, k => Have(chests, k), shares)) missing[kv.Key] = kv.Value;
            if (missing.Count > 0) { why = "재료 모자람"; return false; }
            foreach (var c in chests) if (Chests.InUse(c)) { why = "상자 사용 중"; return false; }
            var one = WorkerPortal.Need(cost, 1);
            if (!FitsIn(carry, one)) { why = "짐 자리 모자람"; return false; }
            if (placeBase && Spawn(prefab, a, ra, tag, Today()) == null) { why = "놓기 실패"; return false; } // 재료 빼기 전에 — 실패해도 재료 그대로
            foreach (var kv in WorkerPortal.Need(cost, shares)) CraftStep.Take(chests, kv.Key, kv.Value);
            foreach (var kv in one) AddTo(carry, kv.Key, kv.Value);
            why = placeBase ? $"{tag}: 거점 쪽 {wa}, 한 몫 들고 감" : $"{tag}: 한 몫 들고 감(거점 쪽은 이미 있음)";
            Plugin.Log.LogInfo($"일꾼 포탈 {why}");
            return true;
        }

        /// <summary>들고 간 한 몫으로 일터 쪽 포탈을 near 둘레에 놓는다(거점 쪽 하나만 있을 때). 놓으면 짝이 이어진다.</summary>
        public static bool PlaceCarried(string tag, Vector3 near, Inventory carry, out string why)
        {
            Piece prefab = Prefab();
            if (prefab == null) { why = "포탈 프리팹 없음"; return false; }
            var mine = Mine().FindAll(z => z.GetString(ZDOVars.s_tag) == tag);
            if (mine.Count != 1) { why = $"그 이름표 포탈 {mine.Count}개"; return false; }
            if (!Carrying(carry)) { why = "재료 안 들고 있음"; return false; }
            if (!FindSpot(near, out Vector3 b, out Quaternion rb, out string wb)) { why = "일터 쪽 " + wb; return false; }
            if (Utils.DistanceXZ(mine[0].GetPosition(), b) < 5f) { why = "두 자리가 너무 가까움"; return false; }
            foreach (var kv in WorkerPortal.Need(Cost(), 1)) carry.RemoveItem(CraftStep.SharedName(kv.Key), kv.Value);
            if (Spawn(prefab, b, rb, tag, Today()) == null) { why = "놓기 실패"; return false; }
            Game.instance?.ConnectPortals();
            why = $"{tag}: 일터 쪽 {wb}";
            Plugin.Log.LogInfo($"일꾼 포탈 놓음 {why}");
            return true;
        }

        /// <summary>거점에 와서 아직 들고 있는 한 몫을 재료 상자에 돌려놓는다(다 들어갈 때만). 돌려놨으면 true.</summary>
        public static bool ReturnCarried(Vector3 home, Inventory carry)
        {
            if (!Carrying(carry)) return false;
            var one = WorkerPortal.Need(Cost(), 1);
            var chests = Chests.NearBase(home, ChestRole.Mats);
            foreach (var c in chests) if (Chests.InUse(c)) return false;
            if (!Fits(chests, one)) return false;
            foreach (var kv in one)
            {
                carry.RemoveItem(CraftStep.SharedName(kv.Key), kv.Value);
                int left = CraftStep.Put(home, kv.Key, kv.Value);
                if (left > 0) { AddTo(carry, kv.Key, left); Plugin.Log.LogWarning($"일꾼 포탈 한 몫 돌려놓기: {kv.Key} {left}개 못 넣어 다시 짐에"); }
            }
            Plugin.Log.LogInfo("일꾼 포탈: 못 놓은 한 몫을 상자에 돌려놓음");
            return true;
        }

        /// <summary>gate(포탈 입구) 3m 안의 일꾼 포탈을 오늘 썼다고 적는다.</summary>
        public static void MarkUsedNear(Vector3 gate)
        {
            foreach (var z in Mine()) if (Utils.DistanceXZ(z.GetPosition(), gate) < 3f) MarkUsed(z);
        }

        static bool FitsIn(Inventory inv, Dictionary<string, int> items)
        {
            int empty = inv.GetEmptySlots();
            foreach (var kv in items)
            {
                GameObject go = ObjectDB.instance.GetItemPrefab(kv.Key);
                if (go == null) return false;
                var shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;
                int max = Mathf.Max(1, shared.m_maxStackSize);
                int left = kv.Value;
                foreach (var it in inv.GetAllItems()) if (it.m_shared.m_name == shared.m_name) left -= Mathf.Max(0, max - it.m_stack);
                if (left > 0) empty -= (left + max - 1) / max;
            }
            return empty >= 0;
        }

        static void AddTo(Inventory inv, string prefab, int amount)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) return;
            ItemDrop.ItemData proto = go.GetComponent<ItemDrop>().m_itemData;
            int max = Mathf.Max(1, proto.m_shared.m_maxStackSize);
            while (amount > 0)
            {
                ItemDrop.ItemData d = proto.Clone();
                d.m_stack = Mathf.Min(amount, max);
                d.m_dropPrefab = go;
                int stack = d.m_stack;
                if (!inv.AddItem(d)) { amount -= stack - d.m_stack; Plugin.Log.LogWarning($"일꾼 포탈 짐 넣기: {prefab} {amount}개 못 넣음"); return; }
                amount -= stack;
            }
        }

        /// <summary>그 일꾼 포탈을 오늘 썼다고 적는다(오래 안 씀 판단용).</summary>
        public static void MarkUsed(ZDO zdo)
        {
            if (zdo == null || !zdo.GetBool(MarkKey) || EnvMan.instance == null) return;
            zdo.Set(UsedDayKey, (long)EnvMan.instance.GetDay());
        }

        /// <summary>
        /// 한 쌍(그 이름표의 일꾼 포탈 전부) 철거: 재료(m_recover)를 거점 재료 상자에 다 넣을 수 있을 때만 넣고 지운다. 플레이어 포탈(표시 없음)은 안 건드림.
        /// </summary>
        public static bool RemovePair(string tag, Vector3 home, out string why)
        {
            var mine = Mine().FindAll(z => z.GetString(ZDOVars.s_tag) == tag);
            if (mine.Count == 0) { why = "없음"; return false; }
            Piece prefab = Prefab();
            if (prefab == null) { why = "포탈 프리팹 없음"; return false; }
            var refund = new Dictionary<string, int>();
            foreach (var r in prefab.m_resources)
            {
                if (r.m_resItem == null || !r.m_recover || r.m_amount <= 0) continue;
                string k = Utils.GetPrefabName(r.m_resItem.gameObject);
                refund.TryGetValue(k, out int n);
                refund[k] = n + r.m_amount * mine.Count;
            }
            var chests = Chests.NearBase(home, ChestRole.Mats);
            foreach (var c in chests) if (Chests.InUse(c)) { why = "상자 사용 중"; return false; }
            if (!Fits(chests, refund)) { why = "상자 자리 모자람"; return false; }
            foreach (var kv in refund)
            {
                int left = CraftStep.Put(home, kv.Key, kv.Value);
                if (left > 0) Plugin.Log.LogWarning($"일꾼 포탈 철거: {kv.Key} {left}개 못 넣음(자리 계산 어긋남)");
            }
            foreach (var z in mine)
            {
                ZNetView view = ZNetScene.instance.FindInstance(z);
                if (view != null) { view.ClaimOwnership(); ZNetScene.instance.Destroy(view.gameObject); }
                else { z.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(z); }
            }
            why = $"{tag} {mine.Count}개 철거, 재료 상자로";
            Plugin.Log.LogInfo($"일꾼 포탈 {why}");
            return true;
        }

        /// <summary>오래 안 쓴 일꾼 포탈 쌍 철거(마지막으로 쓴 날 = 쌍 중 늦은 날). 철거한 이름표들.</summary>
        public static List<string> RemoveIdle(Vector3 home)
        {
            var done = new List<string>();
            if (EnvMan.instance == null) return done;
            long today = EnvMan.instance.GetDay();
            var last = new Dictionary<string, long>();
            foreach (var z in Mine())
            {
                string t = z.GetString(ZDOVars.s_tag);
                long d = z.GetLong(UsedDayKey);
                if (!last.TryGetValue(t, out long old) || d > old) last[t] = d;
            }
            foreach (var kv in last)
                if (WorkerPortal.IsIdle(kv.Value, today) && RemovePair(kv.Key, home, out _)) done.Add(kv.Key);
            return done;
        }

        /// <summary>재료를 다 넣을 자리가 있나(같은 물건 더미 남은 칸 + 빈 칸).</summary>
        static bool Fits(List<Container> chests, Dictionary<string, int> items)
        {
            int empty = 0;
            foreach (var c in chests) empty += c.GetInventory().GetEmptySlots();
            foreach (var kv in items)
            {
                GameObject go = ObjectDB.instance.GetItemPrefab(kv.Key);
                if (go == null) return false;
                var shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;
                int max = Mathf.Max(1, shared.m_maxStackSize);
                int left = kv.Value;
                foreach (var c in chests)
                    foreach (var it in c.GetInventory().GetAllItems())
                        if (it.m_shared.m_name == shared.m_name) left -= Mathf.Max(0, max - it.m_stack);
                if (left > 0) empty -= (left + max - 1) / max;
            }
            return empty >= 0;
        }
    }
}
