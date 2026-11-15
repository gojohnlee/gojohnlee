using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_resisttest (D 저항 미드): 준비 때 거점 상자의 독·냉기 저항 미드를 종류마다 둘씩 챙기고,
    /// 중독돼도 안 마시고(늦음), 속성 없는 적과는 안 마시고, 독 공격 적(블롭)과 싸우기 직전에 독 저항만 마시는지(사용자 결정 2026-10-22),
    /// 산(⑪ 5): 빠른 늑대에게서 물러나지 않고 싸우며 냉기 미드는 안 마시고(속성 없음), 드레이크(냉기 침)와 싸우거나 쫓겨 피하기 직전엔 마시는지.
    /// </summary>
    static class ResistTest
    {
        public static void Register() => TestRunner.Register("vh_resisttest", "저항 미드 시험", () => TestKit.Flat(Run()));

        static readonly string[] Meads = { "MeadPoisonResist", "MeadFrostResist" };

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
            var spawned = new System.Collections.Generic.List<GameObject>();
            try { yield return Body(w, checks, spawned); }
            finally
            {
                Cleanup(spawned);
                if (w.Ragnar != null) TestKit.ClearBaseStock(w, Meads);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Collections.Generic.List<GameObject> spawned)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            foreach (var m in Meads)
            {
                GameObject p = ObjectDB.instance.GetItemPrefab(m);
                string kind = p != null ? Gear.ResistOf(p.GetComponent<ItemDrop>().m_itemData) : null;
                checks.Add($"{m} = 저항 미드 {kind}", kind == (m == "MeadPoisonResist" ? "Poison" : "Frost"));
                if (p == null) yield break;
            }
            TestRunner.Log($"지난 시험 미드 {TestKit.ClearBaseStock(w, Meads)}개 비움");
            TestKit.PutInChest(w.Chest, "MeadPoisonResist", 5);
            TestKit.PutInChest(w.Chest, "MeadFrostResist", 3);
            var npc = w.Ragnar;

            // 1) 일 시키기 → 준비 때 챙김
            GatherTest.Say($"!{Workers.Name1} 나무 5", null);
            yield return new WaitForSeconds(0.5f);
            yield return TestKit.WaitFor(() => npc.CountItem("MeadPoisonResist") >= 2 && npc.CountItem("MeadFrostResist") >= 2, 60f);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"종류마다 둘 챙김 (독 {npc.CountItem("MeadPoisonResist")}·냉기 {npc.CountItem("MeadFrostResist")}, {TestKit.Waited:0}초)",
                npc.CountItem("MeadPoisonResist") == 2 && npc.CountItem("MeadFrostResist") == 2);
            checks.Add($"나머지는 상자에 (독 {GatherTest.Count(w.Chest, "MeadPoisonResist")}·냉기 {GatherTest.Count(w.Chest, "MeadFrostResist")})",
                GatherTest.Count(w.Chest, "MeadPoisonResist") == 3 && GatherTest.Count(w.Chest, "MeadFrostResist") == 1);
            checks.Add("아직 독 저항 없음", !npc.Resists("Poison"));

            // 2) 독에 맞아도 안 마심(사용자 결정 2026-10-22: 걸린 뒤엔 늦다)
            var hit = new HitData();
            hit.m_damage.m_poison = 10f;
            hit.m_point = npc.transform.position;
            npc.Damage(hit);
            yield return new WaitForSeconds(3f);
            checks.Add($"중독돼도 안 마심 (독 {npc.CountItem("MeadPoisonResist")}, 중독 {npc.Poisoned})", npc.CountItem("MeadPoisonResist") == 2);

            // 3) 속성 피해 없는 적(목도리도마뱀)과 싸움 → 안 마심
            Vector3 fwd = npc.transform.forward;
            Character neck = Spawn("Neck", npc.transform.position + fwd * 7f, spawned);
            yield return TestKit.WaitFor(() => neck == null || neck.IsDead(), 40f);
            checks.Add($"목도리도마뱀과 싸움 ({TestKit.Waited:0}초) — 저항 미드 안 마심 (독 {npc.CountItem("MeadPoisonResist")}·냉기 {npc.CountItem("MeadFrostResist")})",
                TestKit.Ok && npc.CountItem("MeadPoisonResist") == 2 && npc.CountItem("MeadFrostResist") == 2);

            // 4) 독 공격 적(블롭)과 싸우기 직전 → 독 저항만 마심(초원이어도). 싸울 만하게 쇠칼을 주고 블롭은 배운 적으로(처음 보면 몸집 짐작으로 도망).
            npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("SwordIron"), 1);
            Prep.WearBest(npc);
            Character blob = Spawn("Blob", npc.transform.position + npc.transform.forward * 8f, spawned);
            Guard.Learn(blob);
            yield return TestKit.WaitFor(() => npc.CountItem("MeadPoisonResist") == 1, 30f);
            checks.Add($"블롭과 싸우기 전 독 저항 미드 마심 ({TestKit.Waited:0.0}초)", TestKit.Ok);
            checks.Add("독 저항 생김", npc.Resists("Poison"));
            if (blob != null && !blob.IsDead()) { var kill = new HitData { m_point = blob.GetCenterPoint() }; kill.m_damage.m_damage = 99999f; blob.Damage(kill); } // 마신 것만 본다(블롭 독은 일꾼을 죽일 수 있음)
            yield return new WaitForSeconds(2f);
            checks.Add($"한 병만 마심 (독 {npc.CountItem("MeadPoisonResist")})", npc.CountItem("MeadPoisonResist") == 1);
            checks.Add($"냉기 미드는 안 마심 ({npc.CountItem("MeadFrostResist")})", npc.CountItem("MeadFrostResist") == 2);
            checks.Add("저항 대사", w.Heard.Exists(h => h.Contains("저항 미드")));

            // 5) 산(⑪ 5): 늑대는 속성 피해 없음 → 냉기 미드 안 마심. 드레이크(Hatchling)는 냉기 침 → 싸우기 직전(또는 쫓겨 물러날·도망칠 때) 냉기 저항만 마심.
            // 산 갈 차림(철 갑옷). 늑대는 일꾼보다 빨라 물러나도 못 떨침 → 물러남 대신 싸움. 늑대가 한 번 물거나(체력 70% 밑) 8초 싸우면 늑대를 치움(마실지만 본다).
            // 한 번은 물리게 둔다 — 회차 166: 일꾼이 받는 첫 한 방에 몬스터 뒤치기 4배가 붙어 체력 71·갑옷 42 일꾼이 늑대 한 방에 죽었음(NpcPatches.LikePlayerHit)
            foreach (var a in new[] { "ArmorIronChest", "ArmorIronLegs", "HelmetIron" }) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(a), 1);
            Prep.WearBest(npc);
            npc.Heal(npc.GetMaxHealth(), false);
            int deaths0 = npc.Deaths;
            Character wolf = Spawn("Wolf", npc.transform.position + npc.transform.forward * 12f, spawned);
            Guard.Learn(wolf);
            float wolfT = 0f;
            bool wolfFight = false;
            while (wolfT < 10f && wolf != null && !wolf.IsDead() && !npc.IsDead() && npc.Deaths == deaths0 && npc.GetHealth() > npc.GetMaxHealth() * 0.7f && !(wolfFight && wolfT > 8f))
            {
                if (npc.Ai.Guard?.StateName == "Fight") wolfFight = true;
                wolfT += 0.25f;
                yield return new WaitForSeconds(0.25f);
            }
            checks.Add($"빠른 늑대에게서 물러나지 않고 싸움 ({wolfT:0.0}초, 경계 {npc.Ai.Guard?.StateName}/{npc.Ai.Guard?.LastVerdict})", wolfFight);
            checks.Add($"늑대와 싸움 — 냉기 미드 안 마심 ({npc.CountItem("MeadFrostResist")}, {wolfT:0.0}초, 체력 {npc.GetHealth():0}/{npc.GetMaxHealth():0}·갑옷 {npc.GetBodyArmor():0}, 늑대 {(wolf == null ? "없어짐" : wolf.IsDead() ? "죽음" : "살아 있음")})", npc.CountItem("MeadFrostResist") == 2);
            if (wolf != null && !wolf.IsDead()) { var kill = new HitData { m_point = wolf.GetCenterPoint() }; kill.m_damage.m_damage = 99999f; wolf.Damage(kill); }
            yield return new WaitForSeconds(2f);
            checks.Add($"늑대에게 안 죽음 (죽음 {npc.Deaths - deaths0}, 체력 {npc.GetHealth():0}/{npc.GetMaxHealth():0})", npc.Deaths == deaths0);
            npc.Heal(npc.GetMaxHealth(), false);
            Character drake = Spawn("Hatchling", npc.transform.position + npc.transform.forward * 8f, spawned);
            Guard.Learn(drake);
            yield return TestKit.WaitFor(() => npc.CountItem("MeadFrostResist") == 1, 30f);
            checks.Add($"드레이크와 싸우기 전 냉기 저항 미드 마심 ({TestKit.Waited:0.0}초)", TestKit.Ok);
            checks.Add("냉기 저항 생김", npc.Resists("Frost"));
            if (drake != null && !drake.IsDead()) { var kill = new HitData { m_point = drake.GetCenterPoint() }; kill.m_damage.m_damage = 99999f; drake.Damage(kill); }
            yield return new WaitForSeconds(2f);
            checks.Add($"냉기도 한 병만 (냉기 {npc.CountItem("MeadFrostResist")}·독 {npc.CountItem("MeadPoisonResist")})",
                npc.CountItem("MeadFrostResist") == 1 && npc.CountItem("MeadPoisonResist") == 1);
        }

        static void Cleanup(System.Collections.Generic.List<GameObject> spawned)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
        }

        static Character Spawn(string name, Vector3 pos, System.Collections.Generic.List<GameObject> spawned)
        {
            GameObject go = Object.Instantiate(ZNetScene.instance.GetPrefab(name), GatherTest.Ground(pos) + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Add(go);
            Character ch = go.GetComponent<Character>();
            ch.SetLevel(1);
            return ch;
        }
    }
}
