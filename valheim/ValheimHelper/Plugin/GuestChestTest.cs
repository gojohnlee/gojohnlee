using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_guestchesttest (멀티 시험 라): 전용 서버에 손님으로 들어와 깃발·상자를 세우고 실제 상자 누르기(Container.Interact → 우리 열기)로
    /// 넣기·꺼내기(서버 상자와 맞음, 복제·소실 없음), 상자 Alt+우클릭 등록(→ "!계속채워" → 일꾼 대답), 소유권(상자·일꾼 주인 = 서버)을 본다.
    /// </summary>
    static class GuestChestTest
    {
        public static void Register() => TestRunner.Register("vh_guestchesttest", "멀티: 손님 상자 열기·Alt+우클릭 등록·소유권 시험", Run, 180f);

        static readonly List<string> s_heard = new List<string>();
        static void OnHeard(string name, string text) => s_heard.Add($"{name}: {text}");
        static readonly MethodInfo Selected = AccessTools.Method(typeof(InventoryGui), "OnSelectedItem");
        static readonly MethodInfo SplitOk = AccessTools.Method(typeof(InventoryGui), "OnSplitOk");
        const string W = "$item_wood";

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            Player p = Player.m_localPlayer;
            bool guest = ZNet.instance != null && !ZNet.instance.IsServer() && p != null;
            checks.Add("손님으로 들어옴", guest);
            if (!guest) { End(checks); yield break; }
            s_heard.Clear();
            ChatNet.Heard += OnHeard;
            p.SetGodMode(true);
            long me = ZDOMan.GetSessionID();

            // 1) 깃발(일꾼 생김) + 상자 — 바닐라 PlacePiece라 둘 다 손님 것으로 생김
            Vector3 at = GatherTest.Ground(p.transform.position + p.transform.forward * 6f);
            GameObject flag = Object.Instantiate(BaseFlag.Prefab, at, Quaternion.Euler(0f, p.transform.eulerAngles.y, 0f));
            flag.GetComponent<BaseFlagPiece>().OnPlaced();
            Vector3 cAt = GatherTest.Ground(p.transform.position + p.transform.forward * 2f);
            p.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), cAt, p.transform.rotation, doAttack: false);
            float until = Time.time + 30f;
            while (Time.time < until && WorkerNpc.All.Count < 2) yield return new WaitForSeconds(1f);
            Container c = GatherTest.ChestAt(cAt);
            checks.Add($"깃발 → 일꾼 {WorkerNpc.All.Count}명, 상자 만듦", WorkerNpc.All.Count >= 2 && c != null);
            if (c == null || WorkerNpc.All.Count < 2) { End(checks); yield break; }
            ZDO cz = c.GetComponent<ZNetView>().GetZDO();
            TestRunner.Log($"상자 주인 {cz.GetOwner()} (나 {me}) 만든 이 {cz.GetLong(ZDOVars.s_creator, 0L)}");

            Inventory bag = p.GetInventory();
            bag.RemoveItem(W, bag.CountItems(W));
            bag.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 30);

            // 2) 실제 누르기 → 서버 열림 답
            c.Interact(p, false, false);
            until = Time.time + 5f;
            while (Time.time < until && !SharedGuest.Confirmed) yield return null;
            checks.Add($"상자 누름 → 서버 열림 답 (열림 {SharedGuest.IsOpen(c)}, 답 {SharedGuest.Confirmed})", SharedGuest.IsOpen(c) && SharedGuest.Confirmed);
            if (!SharedGuest.Confirmed) { End(checks); yield break; }

            // 3) 넣기: 가방 나무 30 → 서버 상자(ZDO로 손님 상자에 돌아옴)
            Move(InventoryGui.instance.m_playerGrid, First(bag, W));
            yield return Settle(() => c.GetInventory().CountItems(W) == 30);
            checks.Add($"넣기: 가방 {bag.CountItems(W)}·서버 상자 {c.GetInventory().CountItems(W)}·화면 {SharedGuest.View?.CountItems(W)}",
                bag.CountItems(W) == 0 && c.GetInventory().CountItems(W) == 30);
            checks.Add($"상자 주인 = 서버 쪽 ({cz.GetOwner()}, 나 {me})", cz.GetOwner() != me && cz.GetOwner() != 0);

            // 4) Alt+우클릭 등록: 나누기 창(제목 계속 채우기) → 확인 → "!계속채워 Wood 30" → 일꾼 대답
            ItemDrop.ItemData vw = SharedGuest.View != null ? First(SharedGuest.View, W) : null;
            bool opened = vw != null && RefillPick.Open(InventoryGui.instance, vw, c);
            checks.Add($"Alt+우클릭 창 열림 ({RefillPick.Active}, 값 {InventoryGui.instance.m_splitDialog.m_splitSlider.value})", opened && RefillPick.Active == "Wood");
            int heard0 = s_heard.Count;
            if (opened) SplitOk.Invoke(InventoryGui.instance, null);
            until = Time.time + 6f;
            while (Time.time < until && s_heard.Count == heard0) yield return new WaitForSeconds(0.25f);
            for (int i = heard0; i < s_heard.Count; i++) TestRunner.Log("  들음 " + s_heard[i]);
            checks.Add($"등록 보냄 \"{RefillPick.LastSent}\" → 일꾼 대답 {s_heard.Count - heard0}개", RefillPick.LastSent == "!계속채워 Wood 30" && s_heard.Count > heard0);

            // 5) 꺼내기: 화면 상자 → 가방 30, 서버 상자 0 (복제·소실 없음)
            vw = SharedGuest.View != null ? First(SharedGuest.View, W) : null;
            if (vw != null) Move(InventoryGui.instance.ContainerGrid, vw);
            yield return Settle(() => c.GetInventory().CountItems(W) == 0);
            checks.Add($"꺼내기: 가방 {bag.CountItems(W)}·서버 상자 {c.GetInventory().CountItems(W)}", bag.CountItems(W) == 30 && c.GetInventory().CountItems(W) == 0);

            InventoryGui.instance.Hide();
            yield return new WaitForSeconds(1f);
            checks.Add($"닫힘 (우리 열기 {SharedGuest.Active})", !SharedGuest.Active);
            checks.Add("일꾼 주인 모두 손님 아님", WorkerNpc.All.TrueForAll(n => n.Zdo.GetOwner() != me));

            Say("!계속채워 끔 Wood");
            yield return new WaitForSeconds(2f);
            End(checks);
        }

        static void Move(InventoryGrid grid, ItemDrop.ItemData item)
        {
            if (item != null) Selected.Invoke(InventoryGui.instance, new object[] { grid, item, item.m_gridPos, InventoryGrid.Modifier.Move });
        }

        static ItemDrop.ItemData First(Inventory inv, string name)
        {
            foreach (var it in inv.GetAllItems()) if (it.m_shared.m_name == name) return it;
            return null;
        }

        static IEnumerator Settle(System.Func<bool> done)
        {
            float until = Time.time + 5f;
            while (Time.time < until && (SharedGuest.Pending > 0 || !done())) yield return new WaitForSeconds(0.1f);
            yield return new WaitForSeconds(0.3f);
        }

        static void Say(string text)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text);
        }

        static void End(GatherTest.Checks checks)
        {
            ChatNet.Heard -= OnHeard;
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
