using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_resisttest (D 저항 미드): 준비 때 거점 상자의 독·냉기 저항 미드를 종류마다 둘씩 챙기고,
    /// 독에 맞으면 독 저항 미드를 마시는지(초원이라 적이 보여도 바이옴 몫은 안 마심 — 냉기 미드는 그대로).
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
            try { yield return Body(w, checks); }
            finally
            {
                if (w.Claude != null) TestKit.ClearBaseStock(w, Meads);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
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
            var npc = w.Claude;

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

            // 2) 독에 맞음 → 독 저항 미드 마심
            var hit = new HitData();
            hit.m_damage.m_poison = 10f;
            hit.m_point = npc.transform.position;
            npc.Damage(hit);
            yield return TestKit.WaitFor(() => npc.CountItem("MeadPoisonResist") == 1, 10f);
            checks.Add($"중독되자 독 저항 미드 마심 ({TestKit.Waited:0.0}초, 중독 {npc.Poisoned})", TestKit.Ok);
            checks.Add("독 저항 생김", npc.Resists("Poison"));
            yield return new WaitForSeconds(2f);
            checks.Add($"한 병만 마심 (독 {npc.CountItem("MeadPoisonResist")})", npc.CountItem("MeadPoisonResist") == 1);
            checks.Add($"초원이라 냉기 미드는 안 마심 ({npc.CountItem("MeadFrostResist")})", npc.CountItem("MeadFrostResist") == 2);
            checks.Add("저항 대사", w.Heard.Exists(h => h.Contains("저항 미드")));
        }
    }
}
