using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_cooktest (⑧ 굽기): 거점에 화로(fire_pit, 연료 0으로 꺼 둠)와 그 위 조리대를 세우고 상자에 날고기 2·나무 5를 넣은 뒤
    /// "!CookedMeat 2" → 계획(PlanJob)이 화로에 나무를 넣어 불을 지피고, 조리대에 고기를 올려 곁에서 기다렸다 타기 전에 꺼내
    /// 구운 고기 2개를 상자에 넣는지. 개수 보존(날고기·구운 고기·탄 것 합 2)과 조리대가 비었는지도 본다.
    /// </summary>
    static class CookTest
    {
        public static void Register() => TestRunner.Register("vh_cooktest", "조리대 굽기 시험", () => TestKit.Flat(Run()));

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
            var made = new List<GameObject>();
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
            GameObject pit = ZNetScene.instance.GetPrefab("fire_pit"), stand = ZNetScene.instance.GetPrefab("piece_cookingstation");
            checks.Add("화로·조리대 프리팹", pit != null && stand != null);
            if (pit == null || stand == null) yield break;
            string burnt = stand.GetComponent<CookingStation>().m_overCookedItem.name;
            TestKit.ClearBaseStock(w, "RawMeat", "CookedMeat", "Wood", burnt);
            TestKit.PutInChest(w.Chest, "RawMeat", 2);
            TestKit.PutInChest(w.Chest, "Wood", 5);

            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            Vector3 at = GatherTest.Ground(w.Home + right * 6f);
            // 꺼진 채 생긴 화로 → 일꾼이 지펴야 함. 바닐라 EffectArea는 Awake에서 불 영역을 등록하고 꺼져도 안 지우므로
            // (한 번 탄 불은 꺼져도 조리대가 굽는다) 불 물체가 한 번도 안 켜지게 프리팹의 처음 연료·켜짐을 잠깐 바꿔 세운다.
            Fireplace pitFp = pit.GetComponent<Fireplace>();
            float startFuel = pitFp.m_startFuel;
            bool fireOn = pitFp.m_enabledObject != null && pitFp.m_enabledObject.activeSelf;
            pitFp.m_startFuel = 0f;
            if (pitFp.m_enabledObject != null) pitFp.m_enabledObject.SetActive(false);
            GameObject fire;
            try { fire = Object.Instantiate(pit, at, Quaternion.identity); }
            finally
            {
                pitFp.m_startFuel = startFuel;
                if (pitFp.m_enabledObject != null) pitFp.m_enabledObject.SetActive(fireOn);
            }
            made.Add(fire);
            GameObject cs = Object.Instantiate(stand, at, Quaternion.identity);
            made.Add(cs);
            ZDO fz = fire.GetComponent<ZNetView>().GetZDO();
            yield return new WaitForSeconds(3f);
            var station = cs.GetComponent<CookingStation>();
            foreach (Transform p in station.m_fireCheckPoints ?? new Transform[0])
            {
                EffectArea ea = EffectArea.IsPointInsideArea(p.position, EffectArea.Type.Burning, station.m_fireCheckRadius);
                TestRunner.Log($"DIAG 불 확인점 {p.position}: 불 영역 {(ea != null ? ea.gameObject.name + " (" + ea.transform.root.name + ", 켜짐 " + ea.gameObject.activeInHierarchy + ")" : "없음")}");
            }
            TestRunner.Log($"DIAG 화로 IsBurning {fire.GetComponent<Fireplace>().IsBurning()}, 연료 {fz.GetFloat(ZDOVars.s_fuel)}");
            bool litAtStart = false;
            foreach (Transform p in station.m_fireCheckPoints ?? new Transform[0]) litAtStart |= EffectArea.IsPointPlus025InsideBurningArea(p.position);
            checks.Add("시작 때 조리대 밑 불 없음", !litAtStart);

            GatherTest.Say($"!CookedMeat 2", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"굽기 계획 시작 ({w.Claude.Ai.Current}, {w.Claude.Ai.Job?.GetType().Name})", w.Claude.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => fz.GetFloat(ZDOVars.s_fuel) > 0f, 60f);
            checks.Add($"화로에 나무를 넣어 지핌 ({TestKit.Waited:0}초, 연료 {fz.GetFloat(ZDOVars.s_fuel):0.0})", TestKit.Ok);
            yield return TestKit.WaitFor(() => Stock(w, "CookedMeat") >= 2 && w.Claude.Ai.Job == null, 180f);
            int cooked = Stock(w, "CookedMeat") + Bag(w, "CookedMeat"), raw = Stock(w, "RawMeat") + Bag(w, "RawMeat"), black = Stock(w, burnt) + Bag(w, burnt);
            checks.Add($"구운 고기 2개가 상자에 ({TestKit.Waited:0}초, 상자 {Stock(w, "CookedMeat")}, {w.Claude.Ai.Current})", TestKit.Ok);
            checks.Add($"타지 않음 (탄 것 {black})", black == 0);
            checks.Add($"개수 보존 (날고기 {raw} + 구운 {cooked} + 탄 것 {black} = 2)", raw + cooked + black == 2);
            ZDO sz = cs.GetComponent<ZNetView>().GetZDO();
            checks.Add("조리대 빔", sz.GetString("slot0") == "" && sz.GetString("slot1") == "");
            TestKit.ClearBaseStock(w, "RawMeat", "CookedMeat", "Wood", burnt);
        }

        static int Stock(TestKit.World w, string prefab)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(w.Home)) n += GatherTest.Count(c, prefab);
            return n;
        }

        static int Bag(TestKit.World w, string prefab) =>
            w.Claude.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
    }
}
