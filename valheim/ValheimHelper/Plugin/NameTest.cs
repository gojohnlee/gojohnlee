using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_nametest (I-2): 상자 이름(ZDO "ChestName", CustomChestName 저장 위치)이 역할 선택에 실제로 쓰이는지.
    /// 이름 없는 기본 상자 + "my_gear" + "음식상자" + "개인_백산수"(손대지 않음)를 두고 Chests.NearBase·ForItem을 확인한다.
    /// 상자는 시험이 만들고 끝나면 지운다. 이름을 ZDO에 직접 써서 CustomChestName 모드 없이도 돈다.
    /// </summary>
    static class NameTest
    {
        public static void Register() => TestRunner.Register("vh_nametest", "상자 이름 → 역할 선택 시험", () => TestKit.Flat(Run()));

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
            var made = new System.Collections.Generic.List<Container>();
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var c in made) if (c != null) ZNetScene.instance.Destroy(c.gameObject);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static Container Chest(TestKit.World w, System.Collections.Generic.List<Container> made, Vector3 at, string name)
        {
            at = GatherTest.Ground(at);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), at, w.Player.transform.rotation, doAttack: false);
            Container c = GatherTest.ChestAt(at);
            if (c == null) return null;
            made.Add(c);
            if (name != null) c.GetComponent<ZNetView>().GetZDO().Set("ChestName", name);
            return c;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Collections.Generic.List<Container> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            TestKit.ClearBaseStock(w, "Bow", "Wood", "Honey");
            Container plain = w.Chest;
            Container gear = Chest(w, made, w.Home - right * 3f, "my_gear");
            yield return new WaitForSeconds(0.5f);
            Container food = Chest(w, made, w.Home - right * 5f, "음식상자");
            yield return new WaitForSeconds(0.5f);
            Container priv = Chest(w, made, w.Home - right * 7f, "개인_백산수");
            yield return new WaitForSeconds(1f);
            checks.Add("이름 붙은 상자 3개 만듦", gear != null && food != null && priv != null);
            if (gear == null || food == null || priv == null) yield break;

            var g = Chests.NearBase(w.Home, ChestRole.Gear);
            checks.Add($"장비 역할 = my_gear 하나 ({g.Count})", g.Count == 1 && g[0] == gear);
            var f = Chests.NearBase(w.Home, ChestRole.Food);
            checks.Add($"음식 역할 = 음식상자 하나 ({f.Count})", f.Count == 1 && f[0] == food);
            var m = Chests.NearBase(w.Home, ChestRole.Mats);
            checks.Add($"재료 역할 = 이름 없는 일반 상자들(이름 붙은 3개 제외, {m.Count})", m.Contains(plain) && !m.Contains(gear) && !m.Contains(food) && !m.Contains(priv));
            var all = Chests.NearBase(w.Home);
            checks.Add($"개인_백산수는 어디에도 안 들어감 ({all.Count})", !all.Contains(priv) && !g.Contains(priv) && !f.Contains(priv) && !m.Contains(priv));

            // 넣을 상자 고르기·재고 세기도 같은 규칙
            checks.Add("활 넣을 상자 = my_gear", Chests.ForItem("Bow", w.Home, Chests.RoleFor(ObjectDB.instance.GetItemPrefab("Bow").GetComponent<ItemDrop>().m_itemData)) == gear);
            TestKit.PutInChest(priv, "Wood", 50);
            TestKit.PutInChest(plain, "Wood", 7);
            int wood = Chests.CountAll(w.Home, inv => inv.CountItems(ObjectDB.instance.GetItemPrefab("Wood").GetComponent<ItemDrop>().m_itemData.m_shared.m_name));
            checks.Add($"재고 세기에서 개인 상자 제외 (나무 {wood}, 7이어야)", wood == 7);
            TestKit.TakeFromChest(plain, "Wood");
            TestKit.TakeFromChest(priv, "Wood");
        }
    }
}
