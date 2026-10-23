using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_resisttest (D 저항 미드): 준비 때 거점 상자의 독·냉기 저항 미드를 종류마다 둘씩 챙기고,
    /// 중독돼도 안 마시고(늦음), 속성 없는 적과는 안 마시고, 독 공격 적(블롭)과 싸우기 직전에 독 저항만 마시는지(사용자 결정 2026-10-22).
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
