using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_tidytest (상자 기능 ② 가-1): 아침 정산 때 일꾼이 걷지 않고 상자를 정리하는지 — 같은 물건은 가장 많이 든 상자로,
    /// 개인 상자·열린 상자는 그대로, 물건 총량 그대로, 재료 이름표 상자가 생기면 그쪽으로, 다시 해도 안 움직임(왔다 갔다 없음).
    /// </summary>
    static class TidyTest
    {
        public static void Register() => TestRunner.Register("vh_tidytest", "상자 정리(아침 정산 때 한 번에) 시험", () => TestKit.Flat(Run()));

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
            var made = new List<Container>();
            var days = new Dictionary<WorkerNpc, int>();
            try { yield return Body(w, checks, made, days); }
            finally
            {
                Undo(made, days);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static Container Chest(TestKit.World w, List<Container> made, Vector3 at)
        {
            at = GatherTest.Ground(at);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), at, w.Player.transform.rotation, doAttack: false);
            Container c = GatherTest.ChestAt(at);
            if (c != null) made.Add(c);
            return c;
        }

        static int N(Container c, string p) => GatherTest.Count(c, p);

        /// <summary>시험 상자 치우고 시험이 앞당긴 정산 날짜를 되돌림(코루틴 finally 밖 — ilspycmd 참조 검사가 그 안의 foreach를 못 풂).</summary>
        static void Undo(List<Container> made, Dictionary<WorkerNpc, int> days)
        {
            foreach (var c in made) if (c != null) ZNetScene.instance.Destroy(c.gameObject);
            foreach (var kv in days) if (kv.Key != null && kv.Key.Zdo != null) kv.Key.Zdo.Set(MorningSettle.KeyDay, kv.Value);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<Container> made, Dictionary<WorkerNpc, int> days)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            TestKit.ClearBaseStock(w, "Wood", "Stone", "AxeFlint");
            Vector3 f = w.Player.transform.forward, r = Vector3.Cross(Vector3.up, f).normalized;
            Container a = Chest(w, made, w.Home + f * 6f + r * 2f);
            Container b = Chest(w, made, w.Home + f * 6f - r * 2f);
            Container priv = Chest(w, made, w.Home + f * 9f + r * 2f);
            Container open = Chest(w, made, w.Home + f * 9f - r * 2f);
            yield return new WaitForSeconds(1f);
            checks.Add("시험 상자 넷", a != null && b != null && priv != null && open != null);
            if (a == null || b == null || priv == null || open == null) yield break;
            priv.GetComponent<ZNetView>().GetZDO().Set("ChestName", "개인_시험");
            TestKit.PutInChest(a, "Wood", 10);
            TestKit.PutInChest(a, "Stone", 3);
            TestKit.PutInChest(b, "Wood", 30);
            TestKit.PutInChest(b, "Stone", 5);
            TestKit.PutInChest(b, "AxeFlint", 1);
            TestKit.PutInChest(priv, "Wood", 7);
            TestKit.PutInChest(open, "Wood", 4);
            open.SetInUse(true); // ZDO만 바꾸면 Container.CheckForChanges가 되돌림
            yield return new WaitForSeconds(0.6f);
            int wood0 = GatherTest.CountBase(w.Home, "Wood") + N(priv, "Wood"), stone0 = GatherTest.CountBase(w.Home, "Stone");
            TestRunner.Log($"시작: 나무 A {N(a, "Wood")}·B {N(b, "Wood")}·개인 {N(priv, "Wood")}·열림 {N(open, "Wood")} (합 {wood0}), 돌 A {N(a, "Stone")}·B {N(b, "Stone")}");

            // 1) 아침 정산 길로(넘김 없이): 쉬는 일꾼이 거점에 있으면 첫 일꾼이 정리
            int day = EnvMan.instance.GetDay() + 50;
            foreach (var npc in WorkerNpc.All) if (npc != null && npc.Zdo != null) days[npc] = npc.Zdo.GetInt(MorningSettle.KeyDay, -1);
            MorningSettle.Snapshot();
            MorningSettle.SettleAll(day);
            yield return new WaitForSeconds(0.3f);
            string said = "";
            foreach (var kv in MorningSettle.LastResult) said += kv.Key + ": " + kv.Value + " / ";
            TestRunner.Log($"정산 결과: {said}");
            int wood1 = GatherTest.CountBase(w.Home, "Wood") + N(priv, "Wood"), stone1 = GatherTest.CountBase(w.Home, "Stone");
            checks.Add($"나무는 가장 많던 B로 (A {N(a, "Wood")}/0, B {N(b, "Wood")}/40)", N(a, "Wood") == 0 && N(b, "Wood") == 40);
            checks.Add($"돌도 B로 (A {N(a, "Stone")}/0, B {N(b, "Stone")}/8)", N(a, "Stone") == 0 && N(b, "Stone") == 8);
            checks.Add($"개인 상자 그대로 (나무 {N(priv, "Wood")}/7)", N(priv, "Wood") == 7);
            checks.Add($"열린 상자 그대로 (나무 {N(open, "Wood")}/4)", N(open, "Wood") == 4);
            checks.Add($"총량 그대로 (나무 {wood1}/{wood0}, 돌 {stone1}/{stone0})", wood1 == wood0 && stone1 == stone0);
            checks.Add("정산 요약에 정리가 들어감", said.Contains("한 상자로"));

            // 2) 재료 이름표 상자가 생기면 그쪽으로(비어 있던 A), 장비(돌도끼)는 장비 상자가 없고 B 하나에만 있어 그대로
            open.SetInUse(false);
            a.GetComponent<ZNetView>().GetZDO().Set("ChestName", "재료_시험");
            yield return new WaitForSeconds(1.2f);
            int moved = Tidy.Instant(w.Home);
            int wood2 = GatherTest.CountBase(w.Home, "Wood") + N(priv, "Wood");
            checks.Add($"재료 상자로 모음 (옮김 {moved}, A 나무 {N(a, "Wood")}/44·돌 {N(a, "Stone")}/8, B 나무 {N(b, "Wood")}·열림 {N(open, "Wood")})",
                N(a, "Wood") == 44 && N(a, "Stone") == 8 && N(b, "Wood") == 0 && N(open, "Wood") == 0);
            checks.Add($"장비는 그대로 (B 돌도끼 {N(b, "AxeFlint")}/1)", N(b, "AxeFlint") == 1);
            checks.Add($"개인 상자 그대로·총량 그대로 (개인 {N(priv, "Wood")}/7, 나무 {wood2}/{wood0})", N(priv, "Wood") == 7 && wood2 == wood0);

            // 3) 다시 해도 안 움직임
            checks.Add("다시 하면 옮길 것 없음", Tidy.Instant(w.Home) == 0);

            foreach (var c in made) foreach (var p in new[] { "Wood", "Stone", "AxeFlint" }) TestKit.TakeFromChest(c, p);
        }
    }
}
