using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 손 거리 넣기 시험(사용자 10-31 ③): 상자 3.5m 앞에 라그나르를 세우고 사이에 통나무(beech_log_half)를 눕힌 뒤 발을 묶어(HopTest.Speeds) 못 다가가게 한다.
    /// 고치기 전(Unload.TestNoReach)엔 4초 동안 못 넣고, 고친 뒤엔 0.5초 남짓에 그 자리에서 나무 10을 넣는지(넣기까지 시간 비교).
    /// </summary>
    static class UnloadReachTest
    {
        public static void Register() => TestRunner.Register("vh_unloadreachtest", "상자 앞 막힘 손 거리 넣기", () => TestKit.Flat(Run()));

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
            float[] saved = new float[6];
            Character held = null;
            try { yield return Body(w, checks, made, saved, c => held = c); }
            finally
            {
                Unload.TestNoReach = false;
                Prep.TestNoReach = false;
                TidyJob.TestNoReach = false;
                if (held != null) HopTest.Speeds(held, saved, false);
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                if (w.Ragnar != null) { w.Ragnar.GetInventory().RemoveItem("$item_wood", 999); DropMeads(w.Ragnar.GetInventory()); }
                if (w.Chest != null) Chests.TryEdit(w.Chest, DropMeads);
                TestKit.ClearBaseStock(w, "Wood");
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void DropMeads(Inventory inv) { foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems())) if (it.m_dropPrefab != null && it.m_dropPrefab.name == "MeadHealthMinor") inv.RemoveItem(it); }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made, float[] saved, System.Action<Character> hold)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            GameObject lp = ZNetScene.instance.GetPrefab("beech_log_half"), wood = ObjectDB.instance.GetItemPrefab("Wood");
            if (lp == null || wood == null) { checks.Add("통나무·나무 프리팹 있음", false); yield break; }
            TestKit.ClearBaseStock(w, "Wood");
            Vector3 c = w.Chest.transform.position, dir = w.Player.transform.forward;
            dir.y = 0f; dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            HopTest.Speeds(w.Ragnar, saved, true);
            hold(w.Ragnar);
            w.Ragnar.TeleportTo(GatherTest.Ground(c + dir * 3.5f), Quaternion.LookRotation(-dir), false);
            made.Add(Object.Instantiate(lp, GatherTest.Ground(c + dir * 1.7f) + Vector3.up * 0.4f, Quaternion.FromToRotation(Vector3.up, side)));
            yield return new WaitForSeconds(2f);
            float Dist() => Utils.DistanceXZ(c, w.Ragnar.transform.position);
            w.Ragnar.GetInventory().AddItem(wood, 10);
            checks.Add($"상자 앞 3.5m·나무 10 들고 묶임 ({Dist():0.0}m, 들고 {BaseTest.Carried(w.Ragnar, "Wood")})",
                Dist() > 2.5f && Dist() <= 4f && BaseTest.Carried(w.Ragnar, "Wood") == 10);

            Unload.TestNoReach = true;
            var before = new Unload(w.Ragnar);
            for (float t = 0f; t < 4f; t += Time.deltaTime) { before.Update(Time.deltaTime); yield return null; }
            checks.Add($"고치기 전: 4초 동안 못 넣음 (들고 {BaseTest.Carried(w.Ragnar, "Wood")}, 상자 {GatherTest.CountBase(w.Home, "Wood")})", BaseTest.Carried(w.Ragnar, "Wood") == 10);

            Unload.TestNoReach = false;
            int reached0 = Unload.Reached;
            var after = new Unload(w.Ragnar);
            float took = 0f;
            while (took < 10f && after.Update(Time.deltaTime)) { took += Time.deltaTime; yield return null; }
            checks.Add($"고친 뒤: 그 자리에서 넣음 ({took:0.0}초 ≤ 2, 손 거리 넣기 {Unload.Reached - reached0}, 상자까지 {Dist():0.0}m)",
                took <= 2f && Unload.Reached - reached0 >= 1);
            checks.Add($"나무 10 모두 상자로 (들고 {BaseTest.Carried(w.Ragnar, "Wood")}, 상자 {GatherTest.CountBase(w.Home, "Wood")})",
                BaseTest.Carried(w.Ragnar, "Wood") == 0 && GatherTest.CountBase(w.Home, "Wood") == 10);

            // 꺼내기(Prep)도 같은 자리에서: 상자에 체력 미드 5, 일꾼은 0
            GameObject mead = ObjectDB.instance.GetItemPrefab("MeadHealthMinor");
            if (mead == null) { checks.Add("미드 프리팹 있음", false); yield break; }
            DropMeads(w.Ragnar.GetInventory());
            Chests.TryEdit(w.Chest, inv => inv.AddItem(mead, 5));
            int Meads() => BaseTest.Carried(w.Ragnar, "MeadHealthMinor");
            Prep.TestNoReach = true;
            var pBefore = new Prep(w.Ragnar);
            for (float t = 0f; t < 4f; t += Time.deltaTime) { pBefore.Update(Time.deltaTime); yield return null; }
            checks.Add($"꺼내기 고치기 전: 4초 동안 못 챙김 (들고 {Meads()}, 상자까지 {Dist():0.0}m)", Meads() == 0);

            Prep.TestNoReach = false;
            int pReached0 = Prep.Reached;
            var pAfter = new Prep(w.Ragnar);
            float pTook = 0f;
            while (pTook < 10f && Meads() == 0 && pAfter.Update(Time.deltaTime)) { pTook += Time.deltaTime; yield return null; }
            checks.Add($"꺼내기 고친 뒤: 그 자리에서 미드 챙김 ({pTook:0.0}초 ≤ 2, 들고 {Meads()}, 손 거리 꺼내기 {Prep.Reached - pReached0})",
                pTook <= 2f && Meads() > 0 && Prep.Reached - pReached0 >= 1);

            // 정리(TidyJob)도 같은 자리에서: 상자 미드 5를 꺼내 같은 상자에 다시 넣는 한 번짜리 계획
            DropMeads(w.Ragnar.GetInventory());
            Chests.TryEdit(w.Chest, inv => { DropMeads(inv); inv.AddItem(mead, 5); });
            System.Func<Vector3, List<Tidy.Move>> Once()
            {
                bool used = false;
                return _ => { if (used) return new List<Tidy.Move>(); used = true; return new List<Tidy.Move> { new Tidy.Move { From = w.Chest, To = w.Chest, Name = "MeadHealthMinor" } }; };
            }
            TidyJob.TestNoReach = true;
            var tBefore = new TidyJob(w.Ragnar, Once());
            for (float t = 0f; t < 4f; t += Time.deltaTime) { tBefore.Update(Time.deltaTime); yield return null; }
            checks.Add($"정리 고치기 전: 4초 동안 못 꺼냄 (들고 {Meads()}, 옮김 {tBefore.Moved})", Meads() == 0 && tBefore.Moved == 0);

            TidyJob.TestNoReach = false;
            int tReached0 = TidyJob.Reached;
            var tAfter = new TidyJob(w.Ragnar, Once());
            float tTook = 0f;
            while (tTook < 10f && tAfter.Moved == 0 && tAfter.Update(Time.deltaTime)) { tTook += Time.deltaTime; yield return null; }
            checks.Add($"정리 고친 뒤: 그 자리에서 꺼내고 넣음 ({tTook:0.0}초 ≤ 3, 옮김 {tAfter.Moved}, 손 거리 {TidyJob.Reached - tReached0})",
                tTook <= 3f && tAfter.Moved > 0 && TidyJob.Reached - tReached0 >= 2);
        }
    }
}
