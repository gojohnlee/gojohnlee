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
        public static void Register()
        {
            TestRunner.Register("vh_cooktest", "조리대 굽기 시험", () => TestKit.Flat(Run(false)));
            // E014 원래 조건: 뇌우로 고정하면 화로가 젖어 조리대가 안 굽는다 → 일꾼이 까닭(cookWet)을 말하고 계획을 그만두는지, 날고기는 그대로인지
            TestRunner.Register("vh_cookwettest", "조리대 굽기 비바람 시험(E014)", () => TestKit.Flat(Run(true)));
        }

        static IEnumerator Run(bool wet)
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
            try { yield return Body(w, checks, made, wet); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made, bool wet)
        {
            yield return TestKit.Setup(w);
            if (wet)
            {
                // Setup이 고정한 맑음·약한 바람을 뇌우·센 바람으로(끝의 Restore가 원래대로). 날씨는 10여 초에 걸쳐 섞이며 바뀌고(EnvMan, 회차 127 확인),
                // 마른 불이 한 번 켜지면 젖어도 불 영역이 남아 계속 굽는다 → 다 젖은 뒤에 화로를 세운다
                w.Env.m_debugEnv = "ThunderStorm";
                w.Env.SetDebugWind(0f, 1f);
                yield return TestKit.WaitFor(() => EnvMan.IsWet() && EnvMan.instance.GetWindIntensity() >= 0.8f, 40f);
                checks.Add($"뇌우·센 바람으로 바뀜 ({TestKit.Waited:0}초, 바람 {EnvMan.instance.GetWindIntensity():0.00})", TestKit.Ok);
                yield return new WaitForSeconds(1f);
            }
            else if (EnvMan.IsWet() || EnvMan.instance.GetWindIntensity() >= 0.8f)
                yield return TestKit.WaitFor(() => !EnvMan.IsWet() && EnvMan.instance.GetWindIntensity() < 0.8f, 40f);   // 앞 시험의 비바람이 맑음으로 다 바뀔 때까지
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
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
            checks.Add($"굽기 계획 시작 ({w.Ragnar.Ai.Current}, {w.Ragnar.Ai.Job?.GetType().Name})", w.Ragnar.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => fz.GetFloat(ZDOVars.s_fuel) > 0f, 60f);
            checks.Add($"화로에 나무를 넣어 지핌 ({TestKit.Waited:0}초, 연료 {fz.GetFloat(ZDOVars.s_fuel):0.0})", TestKit.Ok);
            if (wet)
            {
                Fireplace ffp = fire.GetComponent<Fireplace>();
                TestRunner.Log($"DIAG 비바람: 젖음 {HarmonyLib.Traverse.Create(ffp).Field("m_wet").GetValue<bool>()} EnvMan.IsWet {EnvMan.IsWet()} 바람 {EnvMan.instance.GetWindIntensity():0.00}"
                    + $" 센불 {ffp.m_enabledObjectHigh?.activeSelf} 약한불 {ffp.m_enabledObjectLow?.activeSelf}");
                foreach (EffectArea ea in fire.GetComponentsInChildren<EffectArea>(true))
                    TestRunner.Log($"DIAG 불 영역 {ea.name} 종류 {ea.m_type} 켜짐 {ea.gameObject.activeInHierarchy} 부모 {ea.transform.parent?.name}");
                yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null, 90f);
                checks.Add($"젖은 불 → 계획 그만둠 ({TestKit.Waited:0}초, {w.Ragnar.Ai.Current}, 날씨 {EnvMan.instance.GetCurrentEnvironment()?.m_name})", TestKit.Ok);
                var lines = new List<string>(Core.Npc.Speech.Options(Core.Npc.Personality.Ragnar, "cookWet"));
                lines.AddRange(Core.Npc.Speech.Options(Core.Npc.Personality.Floki, "cookWet"));
                checks.Add("비바람 까닭을 말함(cookWet)", w.Heard.Exists(h => lines.Exists(l => h.EndsWith(": " + l))));
                ZDO wz = cs.GetComponent<ZNetView>().GetZDO();
                int onStand = (wz.GetString("slot0") == "RawMeat" ? 1 : 0) + (wz.GetString("slot1") == "RawMeat" ? 1 : 0);
                int left = Stock(w, "RawMeat") + Bag(w, "RawMeat") + onStand, done = Stock(w, "CookedMeat") + Bag(w, "CookedMeat");
                checks.Add($"날고기 그대로 (상자·짐·조리대 {left} = 2, 구운 {done})", left == 2 && done == 0);
                TestKit.ClearBaseStock(w, "RawMeat", "CookedMeat", "Wood", burnt);
                yield break;
            }
            yield return TestKit.WaitFor(() => Stock(w, "CookedMeat") >= 2 && w.Ragnar.Ai.Job == null, 180f);
            int cooked = Stock(w, "CookedMeat") + Bag(w, "CookedMeat"), raw = Stock(w, "RawMeat") + Bag(w, "RawMeat"), black = Stock(w, burnt) + Bag(w, burnt);
            checks.Add($"구운 고기 2개가 상자에 ({TestKit.Waited:0}초, 상자 {Stock(w, "CookedMeat")}, {w.Ragnar.Ai.Current})", TestKit.Ok);
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
            w.Ragnar.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
    }
}
