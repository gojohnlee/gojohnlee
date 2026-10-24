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

        static void ClearBag()
        {
            Inventory bag = Player.m_localPlayer.GetInventory();
            bag.RemoveItem("$item_wood", bag.CountItems("$item_wood"));
            bag.RemoveItem("$item_stone", bag.CountItems("$item_stone"));
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
            yield return Settle();
            checks.Add($"꺼내기 답 뒤: 호스트 {host.CountItems(W)}·가방 {bag.CountItems(W)}·화면 {view.CountItems(W)}", host.CountItems(W) == 0 && bag.CountItems(W) == 20 && view.CountItems(W) == 0);

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
        }
    }
}
