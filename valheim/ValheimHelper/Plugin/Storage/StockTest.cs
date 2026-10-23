using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_stocktest (상자 기능 ① 첫 조각): 사람 제작이 거점 상자 재료를 바로 쓰는지 — 가방 먼저 → 가까운 상자, 열린 상자·남의 개인 상자 뺌,
    /// 모자라면 안 만들어지고 아무것도 안 빠짐, 재료 총량 = 처음 − 제작법. 제작대 없이 되는 제작법(망치)을 InventoryGui.DoCrafting(막대 끝)으로 직접 만든다.
    /// </summary>
    static class StockTest
    {
        public static void Register() => TestRunner.Register("vh_stocktest", "상자 재료로 바로 제작 시험", () => TestKit.Flat(Run()));

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            var made = new List<Container>();
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var c in made) if (c != null) ZNetScene.instance.Destroy(c.gameObject);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static Container Chest(TestKit.World w, List<Container> made, Vector3 at)
        {
            at = GatherTest.Ground(at);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), at, w.Player.transform.rotation, doAttack: false);
            Container c = GatherTest.ChestAt(at);
            if (c != null) made.Add(c);
            return c;
        }

        /// <summary>제작대 없이 되고 재료 두 가지 이상인 제작법(망치 먼저).</summary>
        static Recipe PickRecipe()
        {
            Recipe pick = null;
            foreach (var r in ObjectDB.instance.m_recipes)
            {
                if (r == null || !r.m_enabled || r.m_item == null || r.m_craftingStation != null || r.m_repairStation != null || r.m_requireOnlyOneIngredient) continue;
                int n = 0;
                foreach (var q in r.m_resources) if (q.m_resItem && q.m_amount > 0 && !q.m_upgraderResource) n++;
                if (n < 2 || n != r.m_resources.Length) continue;
                if (r.m_item.name == "Hammer") return r;
                if (pick == null) pick = r;
            }
            return pick;
        }

        static string Name(Piece.Requirement r) => r.m_resItem.m_itemData.m_shared.m_name;

        static void Craft(Player p, Recipe r)
        {
            var gui = InventoryGui.instance;
            var t = Traverse.Create(gui);
            t.Field("m_craftRecipe").SetValue(r);
            t.Field("m_craftUpgradeItem").SetValue(null);
            t.Field("m_multiCrafting").SetValue(false);
            t.Field("m_craftVariant").SetValue(0);
            try { AccessTools.Method(typeof(InventoryGui), "DoCrafting").Invoke(gui, new object[] { p }); }
            catch (System.Exception e) { TestRunner.Log($"DoCrafting 예외(제작 뒤 화면 갱신일 수 있음): {(e.InnerException ?? e).GetType().Name} {(e.InnerException ?? e).Message}"); }
            t.Field("m_craftRecipe").SetValue(null);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<Container> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
            Recipe recipe = PickRecipe();
            checks.Add($"제작대 없는 제작법 ({(recipe != null ? recipe.m_item.name : "없음")})", recipe != null);
            if (recipe == null) yield break;
            Piece.Requirement ra = recipe.m_resources[0], rb = recipe.m_resources[1];
            string a = Name(ra), b = Name(rb), item = recipe.m_item.m_itemData.m_shared.m_name;
            int na = ra.GetAmount(1), nb = rb.GetAmount(1), out1 = recipe.m_amount;
            string pa = ra.m_resItem.name, pb = rb.m_resItem.name;
            Player p = w.Player;
            Inventory bag = p.GetInventory();
            int bagA0 = bag.CountItems(a), bagB0 = bag.CountItems(b);
            bag.RemoveItem(a, bagA0);
            bag.RemoveItem(b, bagB0);
            TestKit.ClearBaseStock(w, pa, pb);

            // 가까운 상자 = 시작 상자(거점 오른쪽 3m), 먼 상자 = 거점 앞 10m
            Container near = w.Chest;
            Container far = Chest(w, made, w.Home + p.transform.forward * 10f);
            yield return new WaitForSeconds(1f);
            checks.Add("먼 상자 만듦", far != null);
            if (far == null) yield break;
            if (Utils.DistanceXZ(far.transform.position, p.transform.position) < Utils.DistanceXZ(near.transform.position, p.transform.position)) { var x = near; near = far; far = x; }

            // 가방 a(na−1), 가까운 상자 a 3·b(nb−1), 먼 상자 b 5
            bag.AddItem(ra.m_resItem.gameObject, na - 1);
            TestKit.PutInChest(near, pa, 3);
            if (nb > 1) TestKit.PutInChest(near, pb, nb - 1);
            TestKit.PutInChest(far, pb, 5);
            int totalA = na - 1 + 3, totalB = nb - 1 + 5;
            yield return new WaitForSeconds(0.6f);
            checks.Add($"가방만으론 모자라도 상자 합으로 있음 ({a} {na}·{b} {nb})", p.HaveRequirements(recipe, false, 1));

            // 누가 연 상자는 안 셈
            ZDO nz = near.GetComponent<ZNetView>().GetZDO();
            nz.Set(ZDOVars.s_inUse, 1);
            yield return new WaitForSeconds(0.6f);
            checks.Add("열린 상자는 안 셈 → 모자람", !p.HaveRequirements(recipe, false, 1));
            nz.Set(ZDOVars.s_inUse, 0);
            yield return new WaitForSeconds(0.6f);

            int items0 = bag.CountItems(item);
            Craft(p, recipe);
            yield return null;
            int bagA = bag.CountItems(a), nearA = GatherTest.Count(near, pa), nearB = GatherTest.Count(near, pb), farB = GatherTest.Count(far, pb);
            checks.Add($"만들어짐 ({item} {items0}→{bag.CountItems(item)})", bag.CountItems(item) == items0 + out1);
            checks.Add($"가방 먼저 → 가까운 상자 ({a}: 가방 {bagA}·가까운 {nearA}, 0·{3 - 1}이어야)", bagA == 0 && nearA == 2);
            checks.Add($"가까운 상자 다음 먼 상자 ({b}: 가까운 {nearB}·먼 {farB}, 0·4이어야)", nearB == 0 && farB == 4);
            checks.Add($"재료 총량 = 처음 − 제작법 ({a} {bagA + nearA}/{totalA - na}, {b} {nearB + farB}/{totalB - nb})",
                bagA + nearA == totalA - na && nearB + farB == totalB - nb);

            // 남이 지은 개인 상자는 안 씀, 내가 지은 개인 상자는 씀
            Container priv = Chest(w, made, w.Home - Vector3.Cross(Vector3.up, p.transform.forward).normalized * 3f);
            yield return new WaitForSeconds(1f);
            checks.Add("개인 상자 만듦", priv != null);
            if (priv == null) yield break;
            ZDO pz = priv.GetComponent<ZNetView>().GetZDO();
            pz.Set("ChestName", "개인_시험");
            pz.Set(ZDOVars.s_creator, 123456789L);
            TestKit.TakeFromChest(near, pa); // a는 남의 개인 상자에만 남김 → 모자람
            TestKit.PutInChest(priv, pa, 50);
            TestKit.PutInChest(priv, pb, 50);
            yield return new WaitForSeconds(0.6f);
            // 모자라면 안 만들어지고 아무것도 안 빠짐
            checks.Add("남이 지은 개인 상자는 안 셈", !p.HaveRequirements(recipe, false, 1));
            int items1 = bag.CountItems(item);
            Craft(p, recipe);
            yield return null;
            checks.Add($"모자라면 안 만들어지고 안 빠짐 ({item} {bag.CountItems(item)}/{items1}, 개인 {GatherTest.Count(priv, pa)}/50·{GatherTest.Count(priv, pb)}/50, 먼 {GatherTest.Count(far, pb)}/4)",
                bag.CountItems(item) == items1 && GatherTest.Count(priv, pa) == 50 && GatherTest.Count(priv, pb) == 50 && GatherTest.Count(far, pb) == 4);
            pz.Set(ZDOVars.s_creator, p.GetPlayerID());
            yield return new WaitForSeconds(0.6f);
            checks.Add("내가 지은 개인 상자는 셈", p.HaveRequirements(recipe, false, 1));

            // 정리: 상자 재료·만든 것 치우고 가방 되돌림
            foreach (var c in new[] { near, far, priv }) { TestKit.TakeFromChest(c, pa); TestKit.TakeFromChest(c, pb); }
            bag.RemoveItem(item, bag.CountItems(item) - items0);
            bag.RemoveItem(a, bag.CountItems(a));
            bag.RemoveItem(b, bag.CountItems(b));
            if (bagA0 > 0) bag.AddItem(ra.m_resItem.gameObject, bagA0);
            if (bagB0 > 0) bag.AddItem(rb.m_resItem.gameObject, bagB0);

            yield return Build(w, checks, made, near, far);
        }

        /// <summary>제작대 없이 놓이고 재료 두 가지 이상인 망치 조각(모닥불 먼저).</summary>
        static Piece PickPiece()
        {
            PieceTable table = ObjectDB.instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            if (table == null) return null;
            Piece pick = null;
            foreach (var go in table.m_pieces)
            {
                Piece pc = go != null ? go.GetComponent<Piece>() : null;
                if (pc == null || !pc.m_enabled || pc.m_craftingStation != null || pc.m_dlc.Length > 0 || pc.m_resources.Length < 2) continue;
                bool ok = true;
                foreach (var q in pc.m_resources) if (!q.m_resItem || q.m_amount <= 0) ok = false;
                if (!ok) continue;
                if (go.name == "fire_pit") return pc;
                if (pick == null) pick = pc;
            }
            return pick;
        }

        /// <summary>
        /// 건축(조각 2번째): 망치 놓기 순서(UpdatePlacement 클릭 = Fresh 동안 HaveRequirements(CanBuild) → PlacePiece → ConsumeResources(…, 0))를 그대로 부름.
        /// 가방 a(n−1) + 가까운 상자 a 1, 먼 상자 b 전부 → 목록에 보임·놓임·재료 총량 0. 다 쓴 뒤·열린 상자에만 있으면 안 보이고 안 놓임.
        /// </summary>
        static IEnumerator Build(TestKit.World w, GatherTest.Checks checks, List<Container> made, Container near, Container far)
        {
            Piece piece = PickPiece();
            checks.Add($"제작대 없는 망치 조각 ({(piece != null ? piece.name : "없음")})", piece != null);
            if (piece == null) yield break;
            Piece.Requirement ra = piece.m_resources[0], rb = piece.m_resources[1];
            string a = Name(ra), b = Name(rb), pa = ra.m_resItem.name, pb = rb.m_resItem.name;
            int na = ra.m_amount, nb = rb.m_amount;
            Player p = w.Player;
            Inventory bag = p.GetInventory();
            int bagA0 = bag.CountItems(a), bagB0 = bag.CountItems(b);
            bag.RemoveItem(a, bagA0);
            bag.RemoveItem(b, bagB0);
            foreach (var c in new[] { near, far }) { TestKit.TakeFromChest(c, pa); TestKit.TakeFromChest(c, pb); }
            TestKit.ClearBaseStock(w, pa, pb);

            if (na > 1) bag.AddItem(ra.m_resItem.gameObject, na - 1);
            TestKit.PutInChest(near, pa, 1);
            TestKit.PutInChest(far, pb, nb);
            yield return new WaitForSeconds(0.6f);
            checks.Add($"가방에 {b} 없어도 상자에 있으면 목록에 보임", p.HaveRequirements(piece, Player.RequirementMode.CanAlmostBuild));

            Vector3 at = GatherTest.Ground(w.Home + p.transform.forward * 5f - Vector3.Cross(Vector3.up, p.transform.forward).normalized * 4f);
            BaseStock.Fresh++;
            bool can;
            try
            {
                can = p.HaveRequirements(piece, Player.RequirementMode.CanBuild);
                if (can)
                {
                    p.PlacePiece(piece, at, Quaternion.identity, doAttack: false);
                    p.ConsumeResources(piece.m_resources, 0);
                }
            }
            finally { BaseStock.Fresh--; }
            yield return new WaitForSeconds(0.5f);
            var near1 = new List<Piece>();
            Piece.GetAllPiecesInRadius(at, 2f, near1);
            Piece placed = near1.Find(x => x != null && x.m_name == piece.m_name);
            int left = bag.CountItems(a) + bag.CountItems(b) + GatherTest.Count(near, pa) + GatherTest.Count(near, pb) + GatherTest.Count(far, pa) + GatherTest.Count(far, pb);
            checks.Add($"가방 + 상자 합으로 놓임 ({piece.name} {(placed != null ? "있음" : "없음")})", can && placed != null);
            checks.Add($"재료 총량 = 처음 − 조각 (남은 {a}·{b} {left}, 0이어야)", left == 0);

            yield return new WaitForSeconds(0.6f);
            checks.Add("다 쓰면 목록에 안 보이고 안 놓임",
                !p.HaveRequirements(piece, Player.RequirementMode.CanAlmostBuild) && !p.HaveRequirements(piece, Player.RequirementMode.CanBuild));

            // 누가 연 상자에만 있으면 안 셈
            TestKit.PutInChest(near, pa, na);
            TestKit.PutInChest(near, pb, nb);
            ZDO nz = near.GetComponent<ZNetView>().GetZDO();
            nz.Set(ZDOVars.s_inUse, 1);
            yield return new WaitForSeconds(0.6f);
            checks.Add("열린 상자에만 있으면 안 놓임", !p.HaveRequirements(piece, Player.RequirementMode.CanBuild));
            nz.Set(ZDOVars.s_inUse, 0);
            yield return new WaitForSeconds(0.6f);
            checks.Add("닫으면 다시 놓을 수 있음", p.HaveRequirements(piece, Player.RequirementMode.CanBuild));

            if (placed != null) ZNetScene.instance.Destroy(placed.gameObject);
            TestKit.TakeFromChest(near, pa);
            TestKit.TakeFromChest(near, pb);
            bag.RemoveItem(a, bag.CountItems(a));
            bag.RemoveItem(b, bag.CountItems(b));
            if (bagA0 > 0) bag.AddItem(ra.m_resItem.gameObject, bagA0);
            if (bagB0 > 0) bag.AddItem(rb.m_resItem.gameObject, bagB0);
        }
    }
}
