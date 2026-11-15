using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_guesttest (상자 기능 ④ 다-3): 손님 쪽 화면 옮기기. 호스트 안의 가짜 손님(SharedGuest.Force, 상자 사본, 부탁 0.2~0.5초 늦춤)이
    /// 실제 InventoryGui 옮기기(OnSelectedItem Move·OnTakeAll)를 하면 화면·가방은 바로 바뀌고(예측) 호스트 상자는 답 뒤에 맞음,
    /// 열림 답 전 옮기기 막힘, 꺼내는 사이 남이 가져가면 가방을 줄임(복제 없음), 꽉 찬 사이 넣으면 돌려받음(손실 없음),
    /// 상자 물건 땅에 버리기 막힘, 닫으면 호스트의 보는 사람에서 빠짐.
    /// </summary>
    static class GuestTest
    {
        public static void Register() => TestRunner.Register("vh_guesttest", "사람끼리 동시 열기 손님 쪽 시험", () => TestKit.Flat(Run()));

        static readonly MethodInfo Selected = AccessTools.Method(typeof(InventoryGui), "OnSelectedItem");
        static readonly MethodInfo TakeAll = AccessTools.Method(typeof(InventoryGui), "OnTakeAll");

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
            Container chest = null;
            try { yield return Body(w, checks, c => chest = c); }
            finally
            {
                SharedGuest.Force = false;
                SharedGuest.DelayMin = SharedGuest.DelayMax = 0f;
                if (InventoryGui.instance != null && InventoryGui.IsVisible()) InventoryGui.instance.Hide();
                SharedGuest.Close();
                SharedChests.ClearFakes();
                if (chest != null) ZNetScene.instance.Destroy(chest.gameObject);
                ClearBag();
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static string ClubName => ObjectDB.instance.GetItemPrefab("Club").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

        static void ClearBag()
        {
            Inventory bag = Player.m_localPlayer.GetInventory();
            bag.RemoveItem("$item_wood", bag.CountItems("$item_wood"));
            bag.RemoveItem("$item_stone", bag.CountItems("$item_stone"));
            ItemDrop.ItemData club = bag.GetItem(ClubName);
            if (club != null) { Player.m_localPlayer.UnequipItem(club); bag.RemoveItem(club); }
        }

        static void Move(InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod = InventoryGrid.Modifier.Move) =>
            Selected.Invoke(InventoryGui.instance, new object[] { grid, item, item.m_gridPos, mod });

        static ItemDrop.ItemData First(Inventory inv, string name)
        {
            foreach (var it in inv.GetAllItems()) if (it.m_shared.m_name == name) return it;
            return null;
        }

        static IEnumerator Settle()
        {
            float until = Time.time + 3f;
            while (SharedGuest.Pending > 0 && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.2f); // 시험 사본이 호스트 ZDO를 다시 읽음
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Action<Container> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null) yield break;
            Player p = w.Player;
            Vector3 at = GatherTest.Ground(p.transform.position + p.transform.forward * 2f);
            p.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), at, p.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container c = GatherTest.ChestAt(at);
            made(c);
            checks.Add("시험 상자 만듦", c != null);
            if (c == null) yield break;
            ZDOID id = c.GetComponent<ZNetView>().GetZDO().m_uid;
            GameObject wood = ObjectDB.instance.GetItemPrefab("Wood"), stone = ObjectDB.instance.GetItemPrefab("Stone");
            const string W = "$item_wood", S = "$item_stone";
            Inventory bag = p.GetInventory();
            bag.RemoveItem(W, bag.CountItems(W));
            bag.RemoveItem(S, bag.CountItems(S));
            Inventory host = c.GetInventory();
            Chests.TryEdit(c, inv => { inv.RemoveAll(); inv.AddItem(wood, 20); });

            SharedChests.Fake.Add(ZNet.GetUID());
            SharedGuest.Force = true;
            SharedGuest.DelayMin = 0.2f;
            SharedGuest.DelayMax = 0.5f;
            float t0 = Time.time;
            SharedGuest.Open(c);
            yield return null;
            InventoryGrid cg = InventoryGui.instance.ContainerGrid, pg = InventoryGui.instance.m_playerGrid;
            Inventory view = SharedGuest.View;
            ItemDrop.ItemData vw = First(view, W);
            if (vw != null) Move(cg, vw);
            checks.Add($"열림 답 전 옮기기 막힘 (화면 나무 {view.CountItems(W)}, 가방 {bag.CountItems(W)})", vw != null && view.CountItems(W) == 20 && bag.CountItems(W) == 0);
            while (!SharedGuest.Confirmed && Time.time - t0 < 3f) yield return null;
            float took = Time.time - t0;
            yield return new WaitForSeconds(0.2f);
            checks.Add($"열림 답 {took:0.00}초 뒤(늦춤 0.2~0.5), 화면 상자 칸 = 사본", SharedGuest.Confirmed && took >= 0.15f && cg.GetInventory() == view && view != host);

            // 1) 꺼내기: 화면·가방은 바로, 호스트 상자는 답 뒤
            Move(cg, First(view, W));
            bool predicted = bag.CountItems(W) == 20 && view.CountItems(W) == 0 && host.CountItems(W) == 20;
            checks.Add($"꺼내기 예측: 가방 {bag.CountItems(W)}·화면 {view.CountItems(W)} 바로, 호스트 상자 {host.CountItems(W)}는 아직", predicted);
            // 1-1) 답 전 꺼내 온 나무: 버리기·상자에 다시 넣기·건축 재료 막힘(가방 수는 그대로)
            Piece bench = ZNetScene.instance.GetPrefab("piece_workbench").GetComponent<Piece>();
            bool dropped = p.DropItem(bag, First(bag, W), 1);
            Move(pg, First(bag, W));
            bool canBuild = p.HaveRequirements(bench, Player.RequirementMode.CanBuild);
            checks.Add($"답 전 나무: 버리기 {dropped}·다시 넣기 화면 {view.CountItems(W)}·작업대 재료 {canBuild} 막힘, 가방 {bag.CountItems(W)}",
                SharedGuest.Pending > 0 && !dropped && view.CountItems(W) == 0 && !canBuild && bag.CountItems(W) == 20);
            yield return Settle();
            checks.Add($"꺼내기 답 뒤: 호스트 {host.CountItems(W)}·가방 {bag.CountItems(W)}·화면 {view.CountItems(W)}", host.CountItems(W) == 0 && bag.CountItems(W) == 20 && view.CountItems(W) == 0);
            checks.Add($"답 뒤 나무는 작업대 재료로 씀 ({p.HaveRequirements(bench, Player.RequirementMode.CanBuild)})", p.HaveRequirements(bench, Player.RequirementMode.CanBuild));

            // 1-2) 답 전 꺼내 온 몽둥이: 쓰기·입기 막힘, 답 뒤 입힘
            host.AddItem(ObjectDB.instance.GetItemPrefab("Club"), 1);
            yield return new WaitForSeconds(0.3f);
            ItemDrop.ItemData vc = First(view, ClubName);
            if (vc != null) Move(cg, vc);
            ItemDrop.ItemData club = First(bag, ClubName);
            bool eq1 = false;
            if (club != null) { p.UseItem(bag, club, true); eq1 = p.IsItemEquiped(club) || p.EquipItem(club); }
            checks.Add($"답 전 몽둥이: 화면 {vc != null}·가방 {club != null}, 쓰기·입기 막힘 ({eq1})", club != null && SharedGuest.Pending > 0 && !eq1);
            yield return Settle();
            bool eq2 = club != null && p.EquipItem(club) && p.IsItemEquiped(club);
            if (club != null) { p.UnequipItem(club); bag.RemoveItem(club); }
            checks.Add($"답 뒤 몽둥이 입힘 ({eq2})", eq2);

            // 2) 넣기
            bag.AddItem(stone, 10);
            Move(pg, First(bag, S));
            bool putPred = bag.CountItems(S) == 0 && view.CountItems(S) == 10 && host.CountItems(S) == 0;
            yield return Settle();
            checks.Add($"넣기: 바로 가방 0·화면 10({putPred}), 답 뒤 호스트 {host.CountItems(S)}·화면 {view.CountItems(S)}", putPred && host.CountItems(S) == 10 && view.CountItems(S) == 10 && bag.CountItems(S) == 0);

            // 3) 꺼내는 사이 남(호스트)이 6개 가져감 → 가방 4로 고침
            Move(cg, First(view, S));
            host.RemoveItem(First(host, S), 6);
            int mid = bag.CountItems(S);
            yield return Settle();
            checks.Add($"꺼내는 사이 6개 가져감: 바로 가방 {mid} → 답 뒤 {bag.CountItems(S)}, 호스트 {host.CountItems(S)} (복제 없음)", mid == 10 && bag.CountItems(S) == 4 && host.CountItems(S) == 0);

            // 4) 넣는 사이 상자가 꽉 참 → 돌려받음
            Move(pg, First(bag, W));
            int guard = 0;
            while (host.GetEmptySlots() > 0 && guard++ < 64) host.AddItem(stone, 50);
            yield return Settle();
            checks.Add($"넣는 사이 꽉 참: 가방 나무 {bag.CountItems(W)}로 돌려받음(손실 없음), 호스트 나무 {host.CountItems(W)}", bag.CountItems(W) == 20 && host.CountItems(W) == 0);

            // 5) 모두 꺼내기
            host.RemoveAll();
            host.AddItem(wood, 7);
            host.AddItem(stone, 5);
            yield return new WaitForSeconds(0.3f);
            int bw = bag.CountItems(W), bs = bag.CountItems(S);
            TakeAll.Invoke(InventoryGui.instance, null);
            bool allPred = bag.CountItems(W) == bw + 7 && bag.CountItems(S) == bs + 5 && host.NrOfItems() == 2;
            yield return Settle();
            checks.Add($"모두 꺼내기: 바로 가방 +7·+5({allPred}), 답 뒤 호스트 물건 {host.NrOfItems()}", allPred && host.NrOfItems() == 0 && bag.CountItems(W) == bw + 7);

            // 6) 상자 물건 땅에 버리기 막힘
            host.AddItem(wood, 3);
            yield return new WaitForSeconds(0.3f);
            ItemDrop.ItemData v3 = First(view, W);
            if (v3 != null) Move(cg, v3, InventoryGrid.Modifier.Drop);
            checks.Add($"상자 물건 땅에 버리기 막힘 (화면 {view.CountItems(W)}, 부탁 {SharedGuest.Pending})", v3 != null && view.CountItems(W) == 3 && SharedGuest.Pending == 0);

            // 7) 닫기
            InventoryGui.instance.Hide();
            yield return new WaitForSeconds(0.8f);
            checks.Add($"닫으면 손님 길 끝·호스트 보는 사람에서 빠짐 ({SharedGuest.Active}, {SharedChests.GuestViewing(id)})", !SharedGuest.Active && !SharedChests.GuestViewing(id));

            yield return CraftPart(w, checks);
            yield return BuildPart(w, checks);
        }

        static readonly MethodInfo DoCrafting = AccessTools.Method(typeof(InventoryGui), "DoCrafting");

        /// <summary>막대 시작 흉내(바닐라 OnCraftPressed가 고치는 칸 그대로) → SharedCraft.Started.</summary>
        static void StartBar(Recipe r)
        {
            var t = Traverse.Create(InventoryGui.instance);
            t.Field("m_craftRecipe").SetValue(r);
            t.Field("m_craftUpgradeItem").SetValue(null);
            t.Field("m_multiCrafting").SetValue(false);
            t.Field("m_craftVariant").SetValue(0);
            t.Field("m_craftTimer").SetValue(0f);
            SharedCraft.Started(InventoryGui.instance);
        }

        static void EndBar()
        {
            try { DoCrafting.Invoke(InventoryGui.instance, new object[] { Player.m_localPlayer }); }
            catch (System.Exception e) { TestRunner.Log($"DoCrafting 예외(제작 뒤 화면 갱신일 수 있음): {(e.InnerException ?? e).GetType().Name} {(e.InnerException ?? e).Message}"); }
        }

        static void StopBar()
        {
            var t = Traverse.Create(InventoryGui.instance);
            t.Field("m_craftTimer").SetValue(-1f);
            t.Field("m_craftRecipe").SetValue(null);
            SharedCraft.Held = false;
        }

        static IEnumerator WaitCraft()
        {
            float until = Time.time + 3f;
            while (SharedCraft.Pending > 0 && Time.time < until) yield return null;
        }

        /// <summary>8) 다-4a 손님 제작 재료 부탁: 거점 상자 재료로 제작 가능, 막대 시작 때 부탁 → 답 전 막대 끝은 잡아 둠 → 답 뒤 만듦(상자에서 한 번만 빠짐), 그사이 상자에서 빠지면 안 만들어짐.</summary>
        static IEnumerator CraftPart(TestKit.World w, GatherTest.Checks checks)
        {
            Player p = w.Player;
            Inventory bag = p.GetInventory();
            Recipe recipe = StockTest.PickRecipe();
            checks.Add($"손님 제작: 제작대 없는 제작법 ({(recipe != null ? recipe.m_item.name : "없음")})", recipe != null && w.Chest != null);
            if (recipe == null || w.Chest == null) yield break;
            Piece.Requirement ra = recipe.m_resources[0], rb = recipe.m_resources[1];
            string a = ra.m_resItem.m_itemData.m_shared.m_name, b = rb.m_resItem.m_itemData.m_shared.m_name, item = recipe.m_item.m_itemData.m_shared.m_name;
            string pa = ra.m_resItem.name, pb = rb.m_resItem.name;
            int na = ra.GetAmount(1), nb = rb.GetAmount(1);
            bag.RemoveItem(a, bag.CountItems(a));
            bag.RemoveItem(b, bag.CountItems(b));
            TestKit.ClearBaseStock(w, pa, pb);
            TestKit.PutInChest(w.Chest, pa, na);
            TestKit.PutInChest(w.Chest, pb, nb + 2);
            yield return new WaitForSeconds(0.6f);
            int items0 = bag.CountItems(item);
            try
            {
                checks.Add($"손님도 거점 상자 재료로 제작 가능 ({a} {na}·{b} {nb}, 가방 0)", p.HaveRequirements(recipe, false, 1) && BaseStock.Active(p) == false);

                StartBar(recipe);
                bool asked = SharedCraft.Pending == 1;
                EndBar();
                checks.Add($"막대 시작 때 부탁({asked}), 답 전 막대 끝은 잡아 둠(만든 것 {bag.CountItems(item)}/{items0}, 잡음 {SharedCraft.Held})",
                    asked && SharedCraft.Held && bag.CountItems(item) == items0 && GatherTest.Count(w.Chest, pa) == na);
                yield return WaitCraft();
                checks.Add($"답 뒤 가방에 재료 ({a} {bag.CountItems(a)}/{na}·{b} {bag.CountItems(b)}/{nb}), 상자 {GatherTest.Count(w.Chest, pa)}·{GatherTest.Count(w.Chest, pb)}",
                    SharedCraft.Pending == 0 && bag.CountItems(a) == na && bag.CountItems(b) == nb && GatherTest.Count(w.Chest, pa) == 0 && GatherTest.Count(w.Chest, pb) == 2);
                EndBar();
                checks.Add($"답 뒤 막대 끝에 만들어짐 ({item} {bag.CountItems(item)}/{items0 + recipe.m_amount}), 상자에서 더 안 빠짐 ({b} {GatherTest.Count(w.Chest, pb)}/2)",
                    bag.CountItems(item) == items0 + recipe.m_amount && bag.CountItems(a) == 0 && bag.CountItems(b) == 0 && GatherTest.Count(w.Chest, pb) == 2);
                StopBar();

                // 그사이 호스트가 상자에서 가져감 → 받은 만큼만, 안 만들어지고 복제 없음
                TestKit.PutInChest(w.Chest, pa, na);
                TestKit.TakeFromChest(w.Chest, pb);
                TestKit.PutInChest(w.Chest, pb, nb);
                yield return new WaitForSeconds(0.6f);
                int items1 = bag.CountItems(item);
                StartBar(recipe);
                TestKit.TakeFromChest(w.Chest, pa);
                yield return WaitCraft();
                EndBar();
                int ga = bag.CountItems(a), gb = bag.CountItems(b);
                checks.Add($"그사이 상자에서 빠짐: 받은 것 {a} {ga}·{b} {gb}, 안 만들어짐 ({bag.CountItems(item)}/{items1}), 상자 {b} {GatherTest.Count(w.Chest, pb)}",
                    ga == 0 && gb == nb && bag.CountItems(item) == items1 && GatherTest.Count(w.Chest, pb) == 0);
                StopBar();
            }
            finally
            {
                StopBar();
                bag.RemoveItem(a, bag.CountItems(a));
                bag.RemoveItem(b, bag.CountItems(b));
                int made = bag.CountItems(item) - items0;
                if (made > 0) bag.RemoveItem(item, made);
                TestKit.ClearBaseStock(w, pa, pb);
            }
        }

        /// <summary>9) 다-4b 손님 건축 재료: 가방 + 거점 상자로 놓기 가능·망치 개수 표시, 놓자마자 조각·가방 몫만 빠짐 → 답 뒤 상자 몫 빠짐, 그사이 상자에서 빠지면 조각 걷고 가방 몫 돌려받음.</summary>
        static IEnumerator BuildPart(TestKit.World w, GatherTest.Checks checks)
        {
            Player p = w.Player;
            Inventory bag = p.GetInventory();
            Piece piece = ZNetScene.instance.GetPrefab("fire_pit")?.GetComponent<Piece>();
            bool usable = piece != null && piece.m_craftingStation == null && piece.m_resources.Length >= 2 && w.Chest != null;
            checks.Add($"손님 건축: 제작대 없는 조각 ({(piece != null ? piece.name : "없음")})", usable);
            if (!usable) yield break;
            Piece.Requirement ra = piece.m_resources[0], rb = piece.m_resources[1];
            string a = ra.m_resItem.m_itemData.m_shared.m_name, b = rb.m_resItem.m_itemData.m_shared.m_name;
            string pa = ra.m_resItem.name, pb = rb.m_resItem.name;
            int na = ra.m_amount, nb = rb.m_amount;
            bag.RemoveItem(a, bag.CountItems(a));
            bag.RemoveItem(b, bag.CountItems(b));
            TestKit.ClearBaseStock(w, pa, pb);
            bag.AddItem(ObjectDB.instance.GetItemPrefab(pa), na);
            TestKit.PutInChest(w.Chest, pb, nb + 1);
            yield return new WaitForSeconds(0.6f);
            Piece one = null, two = null;
            try
            {
                checks.Add($"손님도 가방 {a} {na} + 상자 {b} {nb}로 놓기 가능, 망치 개수 표시도 상자 셈",
                    p.HaveRequirements(piece, Player.RequirementMode.CanBuild) && SharedCraft.CountsShown(p) && !BaseStock.Active(p));

                one = SharedCraft.TestPlace(p, piece, GatherTest.Ground(p.transform.position + p.transform.forward * 4f + p.transform.right * 3f), p.transform.rotation);
                checks.Add($"놓자마자 조각 생김({one != null}), 가방 몫만 빠짐 ({a} {bag.CountItems(a)}), 상자 몫은 부탁({SharedCraft.BuildPending}) — 답 전 상자 {b} {GatherTest.Count(w.Chest, pb)}/{nb + 1}",
                    one != null && bag.CountItems(a) == 0 && SharedCraft.BuildPending == 1 && GatherTest.Count(w.Chest, pb) == nb + 1);
                yield return WaitBuild();
                checks.Add($"답 뒤 상자에서 {b} {nb}개 빠짐 (남음 {GatherTest.Count(w.Chest, pb)}/1), 조각 그대로({one != null})",
                    SharedCraft.BuildPending == 0 && one != null && GatherTest.Count(w.Chest, pb) == 1 && bag.CountItems(b) == 0);

                // 그사이 호스트 쪽에서 상자 물건이 빠짐 → 조각 걷고 가방 몫 돌려받음(복제·손실 없음)
                bag.AddItem(ObjectDB.instance.GetItemPrefab(pa), na);
                if (nb > 1) TestKit.PutInChest(w.Chest, pb, nb - 1);
                yield return new WaitForSeconds(0.6f);
                two = SharedCraft.TestPlace(p, piece, GatherTest.Ground(p.transform.position + p.transform.forward * 4f - p.transform.right * 3f), p.transform.rotation);
                bool placed = two != null && bag.CountItems(a) == 0;
                TestKit.TakeFromChest(w.Chest, pb);
                yield return WaitBuild();
                checks.Add($"그사이 상자에서 빠짐: 놓였다가({placed}) 조각 걷음({two == null}), 가방 {a} {bag.CountItems(a)}/{na} 돌려받음, 상자 {b} {GatherTest.Count(w.Chest, pb)}",
                    placed && two == null && bag.CountItems(a) == na && bag.CountItems(b) == 0 && GatherTest.Count(w.Chest, pb) == 0);
            }
            finally
            {
                if (one != null) ZNetScene.instance.Destroy(one.gameObject);
                if (two != null) ZNetScene.instance.Destroy(two.gameObject);
                bag.RemoveItem(a, bag.CountItems(a));
                bag.RemoveItem(b, bag.CountItems(b));
                TestKit.ClearBaseStock(w, pa, pb);
            }
        }

        static IEnumerator WaitBuild()
        {
            float until = Time.time + 3f;
            while (SharedCraft.BuildPending > 0 && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.3f); // 걷은 조각이 프레임 끝에 사라짐
        }
    }
}
