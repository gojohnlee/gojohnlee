using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_tidywalktest (상자 기능 ② 가-2): 거점에서 쉬는 일꾼이 플레이어 근처면 걸어서 상자를 정리하는지 — 같은 물건은 가장 많이 든 상자로,
    /// 개인·열린 상자 그대로, 총량 그대로, 끝나면 짐 남김 없음. 들고 가다 다른 명령(집으로)이 오면 든 것을 상자에 되돌림.
    /// 플레이어가 멀면(64m 밖 흉내) 걷지 않고 바로.
    /// </summary>
    static class TidyWalkTest
    {
        public static void Register() => TestRunner.Register("vh_tidywalktest", "쉴 때 걸어서 상자 정리 시험", () => TestKit.Flat(Run()));

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
            try { yield return Body(w, checks, made); }
            finally
            {
                Undo(made);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        /// <summary>시험 상자 치우고 정리 시험 설정을 되돌림(코루틴 finally 밖 — ilspycmd 참조 검사가 그 안의 foreach를 못 풂).</summary>
        static void Undo(List<Container> made)
        {
            Tidy.TestAllow = false;
            Tidy.CheckEvery = 60f;
            GatherJob.TestIgnorePlayers = false;
            foreach (var c in made) if (c != null) ZNetScene.instance.Destroy(c.gameObject);
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

        static WorkerNpc Tidier()
        {
            foreach (var n in WorkerNpc.All) if (n != null && n.Ai != null && n.Ai.Job is TidyJob) return n;
            return null;
        }

        static int Bag(string p)
        {
            int s = 0;
            foreach (var n in WorkerNpc.All) if (n != null) s += Tidy.Count(n.GetInventory(), p);
            return s;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<Container> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            Vector3 f = w.Player.transform.forward, r = Vector3.Cross(Vector3.up, f).normalized;
            Container a = Chest(w, made, w.Home + f * 6f + r * 3f);
            Container b = Chest(w, made, w.Home + f * 10f - r * 4f);
            Container priv = Chest(w, made, w.Home + f * 12f + r * 3f);
            Container open = Chest(w, made, w.Home + f * 12f - r * 3f);
            yield return new WaitForSeconds(1f);
            checks.Add("시험 상자 넷", a != null && b != null && priv != null && open != null);
            if (a == null || b == null || priv == null || open == null) yield break;
            priv.GetComponent<ZNetView>().GetZDO().Set("ChestName", "개인_시험");
            TestKit.PutInChest(a, "Wood", 10);
            TestKit.PutInChest(a, "Stone", 3);
            TestKit.PutInChest(b, "Wood", 30);
            TestKit.PutInChest(b, "Stone", 5);
            TestKit.PutInChest(priv, "Wood", 7);
            TestKit.PutInChest(open, "Wood", 4);
            open.SetInUse(true); // ZDO만 바꾸면 Container.CheckForChanges가 되돌림
            yield return new WaitForSeconds(0.6f);
            int wood0 = GatherTest.CountBase(w.Home, "Wood") + N(priv, "Wood"), stone0 = GatherTest.CountBase(w.Home, "Stone");
            TestRunner.Log($"시작: 나무 A {N(a, "Wood")}·B {N(b, "Wood")}·개인 {N(priv, "Wood")}·열림 {N(open, "Wood")} (합 {wood0}), 돌 A {N(a, "Stone")}·B {N(b, "Stone")}, 플레이어-깃발 {Utils.DistanceXZ(w.Player.transform.position, w.Home):0}m");

            // 1) 플레이어 근처: 쉬는 일꾼이 걸어서
            Tidy.TestAllow = true;
            Tidy.CheckEvery = 2f;
            Tidy.TestNow();
            WorkerNpc t = null;
            for (float s = 0f; s < 10f && t == null; s += 0.2f) { yield return new WaitForSeconds(0.2f); t = Tidier(); }
            checks.Add($"쉬던 일꾼이 걸어서 정리 시작 ({(t != null ? t.NpcName : "없음")})", t != null);
            if (t == null) yield break;
            Vector3 start = t.transform.position;
            float walked = 0f, sec = 0f;
            bool carried = false;
            for (; sec < 150f && t.Ai.Job is TidyJob; sec += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                walked = Mathf.Max(walked, Utils.DistanceXZ(start, t.transform.position));
                if (Bag("Wood") + Bag("Stone") > 0) carried = true;
            }
            int wood1 = GatherTest.CountBase(w.Home, "Wood") + N(priv, "Wood") + Bag("Wood"), stone1 = GatherTest.CountBase(w.Home, "Stone") + Bag("Stone");
            TestRunner.Log($"정리 끝 {sec:0}초, 가장 멀리 {walked:0.0}m, 든 적 {carried}, 지금 {t.Ai.Current}/{t.Ai.Job?.StepName}");
            checks.Add($"정리가 끝남 ({sec:0}초/150)", !(t.Ai.Job is TidyJob));
            checks.Add($"걸어서 들고 감 (움직임 {walked:0.0}m, 든 적 {carried})", walked > 2f && carried);
            checks.Add($"나무·돌은 가장 많던 B로 (A 나무 {N(a, "Wood")}/0·돌 {N(a, "Stone")}/0, B 나무 {N(b, "Wood")}/40·돌 {N(b, "Stone")}/8)",
                N(a, "Wood") == 0 && N(a, "Stone") == 0 && N(b, "Wood") == 40 && N(b, "Stone") == 8);
            checks.Add($"개인·열린 상자 그대로 (개인 {N(priv, "Wood")}/7, 열림 {N(open, "Wood")}/4)", N(priv, "Wood") == 7 && N(open, "Wood") == 4);
            checks.Add($"총량 그대로 (나무 {wood1}/{wood0}, 돌 {stone1}/{stone0})", wood1 == wood0 && stone1 == stone0);
            checks.Add($"짐 남김 없음 (일꾼 가방 나무 {Bag("Wood")}·돌 {Bag("Stone")})", Bag("Wood") == 0 && Bag("Stone") == 0);

            // 2) 들고 가는 중 다른 명령(집으로) → 든 것을 상자에 되돌림
            TestKit.PutInChest(a, "Wood", 10);
            yield return new WaitForSeconds(0.6f);
            Tidy.TestNow();
            t = null;
            for (float s = 0f; s < 10f && t == null; s += 0.2f) { yield return new WaitForSeconds(0.2f); t = Tidier(); }
            bool holding = false;
            for (float s = 0f; s < 60f && t != null && t.Ai.Job is TidyJob && !holding; s += 0.1f) { yield return new WaitForSeconds(0.1f); holding = Bag("Wood") > 0; }
            checks.Add($"다시 정리 시작해 나무를 듦 ({(t != null ? t.NpcName : "없음")}, 듦 {holding})", t != null && holding);
            if (t == null) yield break;
            Tidy.TestAllow = false; // 집에 가는 동안 다른 일꾼이 걸어서 다시 정리하지 않게(3에서 바로 정리를 봄)
            t.Ai.GoHome(false);
            yield return new WaitForSeconds(0.5f);
            int wood2 = GatherTest.CountBase(w.Home, "Wood") + N(priv, "Wood") + Bag("Wood");
            checks.Add($"그만두면 든 것 상자로 (가방 나무 {Bag("Wood")}/0, 총량 {wood2}/{wood0 + 10}, A {N(a, "Wood")}·B {N(b, "Wood")})", Bag("Wood") == 0 && wood2 == wood0 + 10);
            yield return new WaitForSeconds(4f); // 집에 닿아 Idle로

            // 3) 플레이어가 멀면(64m 밖 흉내) 걷지 않고 바로
            GatherJob.TestIgnorePlayers = true;
            GatherJob.TestFakePlayer = null;
            Tidy.TestAllow = true;
            Tidy.TestNow();
            bool walkedJob = false;
            for (float s = 0f; s < 3f; s += 0.2f) { yield return new WaitForSeconds(0.2f); if (Tidier() != null) walkedJob = true; }
            GatherJob.TestIgnorePlayers = false;
            int wood3 = GatherTest.CountBase(w.Home, "Wood") + N(priv, "Wood") + Bag("Wood");
            checks.Add($"멀면 바로 정리 (걸음 {walkedJob}, A {N(a, "Wood")}/0·B {N(b, "Wood")}/50, 총량 {wood3}/{wood0 + 10})",
                !walkedJob && N(a, "Wood") == 0 && N(b, "Wood") == 50 && wood3 == wood0 + 10);

            open.SetInUse(false);
            foreach (var c in made) foreach (var p in new[] { "Wood", "Stone" }) TestKit.TakeFromChest(c, p);
        }
    }
}
