using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_sharetest (상자 기능 ④ 다-2): 호스트 쪽 손님 부탁 처리. 가짜 손님(피어 없는 uid)이 열면 주인은 호스트 그대로·일꾼 TryEdit 막힘·
    /// 모드 없는 사람의 바닐라 열기는 거절, 꺼내기(같은 물건만·있는 만큼)·넣기(놓은 칸 → 같은 물건 → 빈칸, 꽉 차면 나머지 돌려줌, 내구도 그대로),
    /// 호스트가 연 상자도 손님 부탁이 됨, 안 연 손님 부탁은 0, 닫으면 일꾼이 다시 씀, 떠난 손님은 Tick이 뺌.
    /// </summary>
    static class ShareTest
    {
        public static void Register() => TestRunner.Register("vh_sharetest", "사람끼리 동시 열기 호스트 쪽 시험", () => TestKit.Flat(Run()));

        const long Guest = 7700001, Guest2 = 7700002, Gone = 7700003;

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
                SharedChests.ClearFakes();
                SharedChests.Close(Gone, chest != null ? chest.GetComponent<ZNetView>().GetZDO().m_uid : ZDOID.None);
                if (InventoryGui.instance != null && InventoryGui.IsVisible()) InventoryGui.instance.Hide();
                if (chest != null) ZNetScene.instance.Destroy(chest.gameObject);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static int Count(Container c, string name) => c.GetInventory().CountItems(name);

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Action<Container> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null) yield break;
            Player p = w.Player;
            Vector3 at = GatherTest.Ground(w.Home - p.transform.right * 3f);
            p.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), at, p.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container c = GatherTest.ChestAt(at);
            made(c);
            checks.Add("시험 상자 만듦", c != null);
            if (c == null) yield break;
            ZNetView view = c.GetComponent<ZNetView>();
            ZDOID id = view.GetZDO().m_uid;
            long me = Game.instance.GetPlayerProfile().GetPlayerID();
            ItemDrop wood = ObjectDB.instance.GetItemPrefab("Wood").GetComponent<ItemDrop>();
            ItemDrop stone = ObjectDB.instance.GetItemPrefab("Stone").GetComponent<ItemDrop>();
            string woodName = wood.m_itemData.m_shared.m_name, stoneName = stone.m_itemData.m_shared.m_name;
            Chests.TryEdit(c, inv => { inv.RemoveAll(); inv.AddItem(wood.gameObject, 10); });
            ItemDrop.ItemData woodItem = c.GetInventory().GetAllItems()[0];
            int woodSlot = woodItem.m_gridPos.y * c.GetInventory().GetWidth() + woodItem.m_gridPos.x;

            SharedChests.Fake.Add(Guest);
            SharedChests.Fake.Add(Guest2);
            bool opened = SharedChests.Open(Guest, id, me);
            checks.Add("가짜 손님 열기 됨, 주인은 호스트 그대로", opened && view.GetZDO().IsOwner());
            checks.Add("손님이 보는 동안 일꾼 TryEdit 막힘(Chests.InUse)", Chests.InUse(c) && !Chests.TryEdit(c, inv => inv.AddItem(stone.gameObject, 1)) && Count(c, stoneName) == 0);

            AccessTools.Method(typeof(Container), "RPC_RequestOpen").Invoke(c, new object[] { Guest2, me });
            checks.Add("손님이 보는 동안 모드 없는 사람의 바닐라 열기 거절(주인 안 넘어감)", view.GetZDO().IsOwner() && view.GetZDO().GetOwner() == ZDOMan.GetSessionID());

            string woodKey = SharedChests.Key(woodItem);
            int t1 = SharedChests.Take(Guest, id, woodSlot, woodKey, 4);
            checks.Add($"꺼내기 4 → 4 (상자 나무 {Count(c, woodName)})", t1 == 4 && Count(c, woodName) == 6);
            int t2 = SharedChests.Take(Guest, id, woodSlot, SharedChests.Key(stone.m_itemData), 2);
            checks.Add($"다른 물건 키로 꺼내기 → 0 ({t2})", t2 == 0 && Count(c, woodName) == 6);
            int t3 = SharedChests.Take(Guest, id, woodSlot, woodKey, 9);
            checks.Add($"있는 것보다 많이 꺼내기 → 있는 만큼 6 ({t3})", t3 == 6 && Count(c, woodName) == 0);
            int t4 = SharedChests.Take(Guest2, id, woodSlot, woodKey, 1);
            checks.Add("안 연 손님 부탁 → 0", t4 == 0 && SharedChests.Put(Guest2, id, 0, StackOf(stone, 3)) == 0 && Count(c, stoneName) == 0);

            // 넣기: 놓은 칸(5번)에, 다음 같은 물건은 그 더미로, 내구도 그대로 감(Pack·Unpack)
            int p1 = SharedChests.Put(Guest, id, 5, SharedChests.Unpack(SharedChests.Pack(StackOf(stone, 5))));
            ItemDrop.ItemData at5 = c.GetInventory().GetItemAt(5 % c.GetInventory().GetWidth(), 5 / c.GetInventory().GetWidth());
            checks.Add($"넣기 돌 5 → 놓은 칸 5에 5 ({p1})", p1 == 5 && at5 != null && at5.m_shared.m_name == stoneName && at5.m_stack == 5);
            int p2 = SharedChests.Put(Guest, id, 0, StackOf(stone, 3));
            checks.Add($"다른 칸에 놓아도 같은 물건 더미 먼저 → 칸 5가 8 ({p2})", p2 == 3 && at5.m_stack == 8 && c.GetInventory().GetItemAt(0, 0) == null);
            ItemDrop hammer = ObjectDB.instance.GetItemPrefab("Hammer").GetComponent<ItemDrop>();
            ItemDrop.ItemData h = hammer.m_itemData.Clone();
            h.m_dropPrefab = hammer.gameObject; h.m_stack = 1; h.m_durability = 7.5f;
            int p3 = SharedChests.Put(Guest, id, 0, SharedChests.Unpack(SharedChests.Pack(h)));
            ItemDrop.ItemData at0 = c.GetInventory().GetItemAt(0, 0);
            checks.Add($"망치 넣기 → 칸 0, 내구도 7.5 그대로 ({(at0 != null ? at0.m_durability : -1f)})", p3 == 1 && at0 != null && Mathf.Abs(at0.m_durability - 7.5f) < 0.01f);

            // 꽉 찬 상자: 남은 칸을 돌 50씩 채우고 돌 60 넣기 → 돌 더미 남은 자리(42)만, 나머지 18 돌려줌
            Inventory ci = c.GetInventory();
            int maxStone = stone.m_itemData.m_shared.m_maxStackSize;
            int cw = ci.GetWidth(), ch = ci.GetHeight();
            for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++)
                    if (ci.GetItemAt(x, y) == null) { var s = StackOf(wood, wood.m_itemData.m_shared.m_maxStackSize); s.m_gridPos = new Vector2i(x, y); ci.AddItem(s, s.m_gridPos); }
            int room = maxStone - at5.m_stack;
            int p4 = SharedChests.Put(Guest, id, 3, StackOf(stone, room + 18));
            checks.Add($"꽉 찬 상자에 돌 {room + 18} → {room}만 넣고 18은 돌려줌 ({p4})", p4 == room && at5.m_stack == maxStone);

            // 다-4c: 일꾼이 막 넣은 송진 때문에 자리가 없으면 송진을 근처 일꾼 가방으로 되돌리고 손님 부싯돌을 넣음
            SharedChests.Close(Guest, id);
            ItemDrop.ItemData oneWood = ci.GetAllItems().Find(it => it.m_shared.m_name == woodName);
            ci.RemoveItem(oneWood); // 칸 하나 비움(TryEdit 아님 = 일꾼이 넣은 것으로 안 셈)
            ItemDrop resin = ObjectDB.instance.GetItemPrefab("Resin").GetComponent<ItemDrop>();
            ItemDrop flint = ObjectDB.instance.GetItemPrefab("Flint").GetComponent<ItemDrop>();
            string resinName = resin.m_itemData.m_shared.m_name, flintName = flint.m_itemData.m_shared.m_name;
            Chests.TryEdit(c, inv => inv.AddItem(resin.gameObject, 20));
            int woodBefore = Count(c, woodName), bagResin = w.Ragnar.GetInventory().CountItems(resinName);
            w.Ragnar.transform.position = c.transform.position + c.transform.forward * 1.5f;
            SharedChests.Open(Guest, id, me);
            int p5 = SharedChests.Put(Guest, id, 3, StackOf(flint, 5));
            int gotResin = w.Ragnar.GetInventory().CountItems(resinName) - bagResin;
            checks.Add($"일꾼이 막 넣은 송진 20 → 라그나 가방으로 되돌리고 부싯돌 5 넣음 (넣음 {p5}, 상자 송진 {Count(c, resinName)}, 가방 +{gotResin}, 나무 그대로 {Count(c, woodName) == woodBefore})",
                p5 == 5 && Count(c, flintName) == 5 && Count(c, resinName) == 0 && gotResin == 20 && Count(c, woodName) == woodBefore);
            int p6 = SharedChests.Put(Guest, id, 3, StackOf(resin, 1));
            checks.Add($"되돌릴 것이 더 없으면 송진 1 → 0 돌려줌 ({p6})", p6 == 0 && Count(c, resinName) == 0);
            w.Ragnar.GetInventory().RemoveItem(resinName, gotResin);

            // 호스트가 연 상자에도 손님 부탁이 됨
            c.SetInUse(true); // 호스트가 연 상태(InventoryGui.UpdateContainer가 하는 것 — 최소화 창에선 GUI가 안 돌 수 있음)
            yield return new WaitForSeconds(0.5f);
            int t5 = SharedChests.Take(Guest, id, 5, SharedChests.Key(at5), 10);
            checks.Add($"호스트가 연 상자에서도 손님 꺼내기 10 ({t5}, 사용 중 {c.IsInUse()})", t5 == 10 && c.IsInUse());
            c.SetInUse(false);
            yield return new WaitForSeconds(0.5f);

            // 닫으면 일꾼이 다시 씀, 떠난 손님(피어 없음·Fake 아님)은 Tick이 뺌
            SharedChests.Close(Guest, id);
            SharedChests.Close(Guest2, id);
            checks.Add("손님 닫으면 일꾼 TryEdit 다시 됨", !Chests.InUse(c) && Chests.TryEdit(c, inv => { }));
            bool goneOpen = SharedChests.Open(Gone, id, me);
            yield return new WaitForSeconds(1.5f);
            checks.Add("떠난 손님은 1초 Tick이 보는 사람에서 뺌", goneOpen && !SharedChests.GuestViewing(id));
        }

        static ItemDrop.ItemData StackOf(ItemDrop prefab, int n)
        {
            ItemDrop.ItemData d = prefab.m_itemData.Clone();
            d.m_dropPrefab = prefab.gameObject;
            d.m_stack = n;
            return d;
        }
    }
}
