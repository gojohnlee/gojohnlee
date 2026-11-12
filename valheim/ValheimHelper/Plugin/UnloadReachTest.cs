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
            var w = new TestKit.World { House = true }; // 사용자 11-12: VH_HOUSE=1이면 집에서 나무를 넣을 상자와 그 앞 빈 바닥(층·문·좁은 자리)으로
            var made = new List<GameObject>();
            float[] saved = new float[6];
            Character held = null;
            try { yield return Body(w, checks, made, saved, c => held = c); }
            finally
            {
                Unload.TestNoReach = false;
                Prep.TestNoReach = false;
                Prep.TestOnly = null;
                TidyJob.TestNoReach = false;
                ChestHop.TestNoBump = false;
                if (held != null) HopTest.Speeds(held, saved, false);
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                if (w.Ragnar != null) { w.Ragnar.GetInventory().RemoveItem("$item_wood", 999); DropMeads(w.Ragnar.GetInventory()); }
                if (w.Chest != null) Chests.TryEdit(w.Chest, DropMeads);
                TestKit.ClearBaseStock(w, "Wood");
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static int Mask => LayerMask.GetMask("piece", "Default", "static_solid", "terrain", "vehicle");

        // 집 바닥: y0 위 1.6m에서 아래로 — 지형 높이(GatherTest.Ground)는 1층 돌 바닥 밑이라 못 씀
        static Vector3 Floor(Vector3 p, float y0)
        {
            p.y = y0 + 1.6f;
            return Physics.Raycast(p, Vector3.down, out RaycastHit h, 4f, Mask) ? h.point : new Vector3(p.x, y0, p.z);
        }

        // 상자 c 앞 24쪽마다 0.5m씩: 바닥이 상자 높이 ±0.6m이고 몸 높이(0.8~1.6m)에 막는 것이 없는 거리, 가장 긴 쪽
        static float OpenRun(Vector3 c, out Vector3 dir)
        {
            float best = 0f; dir = Vector3.forward;
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI / 12f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float run = 0f;
                for (float r = 1f; r <= 8.5f; r += 0.5f)
                {
                    Vector3 f = Floor(c + d * r, c.y);
                    if (Mathf.Abs(f.y - c.y) > 0.6f || Physics.CheckCapsule(f + Vector3.up * 0.8f, f + Vector3.up * 1.6f, 0.4f, Mask)) break;
                    run = r;
                }
                if (run > best) { best = run; dir = d; }
            }
            return best;
        }

        static void DropMeads(Inventory inv) { foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems())) if (Gear.IsHealthMead(it)) inv.RemoveItem(it); } // 집: 일꾼이 사용자 상자에서 챙긴 다른 체력 미드도(있으면 안 꺼냄 — 회차 466)

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made, float[] saved, System.Action<Character> hold)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            GameObject lp = ZNetScene.instance.GetPrefab("beech_log_half"), wood = ObjectDB.instance.GetItemPrefab("Wood");
            if (lp == null || wood == null) { checks.Add("통나무·나무 프리팹 있음", false); yield break; }
            TestKit.ClearBaseStock(w, "Wood");
            Vector3 dir = w.Player.transform.forward;
            float far = 8f;
            if (w.Site != null)
            {
                // 집: 일꾼이 나무를 넣을 상자(Unload.ChestFor와 같음) 앞에서 바닥이 같은 높이로 가장 길게 트인 쪽
                var pick = Chests.ForItem("Wood", w.Ragnar.Home, Chests.RoleFor(wood.GetComponent<ItemDrop>().m_itemData), true);
                if (pick != null) w.Chest = pick;
                float best = OpenRun(w.Chest.transform.position, out dir);
                far = Mathf.Min(8f, best);
                TestRunner.Log($"  집 상자 {Utils.GetPrefabName(w.Chest.gameObject)} {w.Chest.transform.position:F1}, 앞 빈 바닥 {best:0.0}m 쪽 {dir:F2}");
                checks.Add($"집 상자 앞 빈 바닥 5m 넘음 ({best:0.0}m)", best >= 5f);
                if (best < 5f) yield break;
            }
            Vector3 c = w.Chest.transform.position;
            dir.y = 0f; dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 Ground(Vector3 p) => w.Site != null ? Floor(p, c.y) : GatherTest.Ground(p);
            // 상자 앞 0~8m 길의 나무·통나무(앞 시험이 남긴 것 포함)를 치운다: 처음 자리마다 땅이 달라 8m 자리가 나무 위면 일꾼이 우듬지에 올라 막힘(E055 회차 343)
            if (GatherTest.ClearWood(c + dir * 5f, 6f) > 0) yield return new WaitForSeconds(0.5f);
            HopTest.Speeds(w.Ragnar, saved, true);
            hold(w.Ragnar);
            Back();
            made.Add(Object.Instantiate(lp, Ground(c + dir * 1.7f) + Vector3.up * 0.4f, Quaternion.FromToRotation(Vector3.up, side)));
            yield return new WaitForSeconds(2f);
            float Dist() => Utils.DistanceXZ(c, w.Ragnar.transform.position);
            // 단계마다 3.5m 자리로 다시 세운다: 묶여도 통나무에 밀려 손 거리(4m) 밖으로 나가면 다음 단계가 상자 앞 옮기기로 넘어가 FAIL(E055 회차 343 — 4.4m)
            void Back() => w.Ragnar.TeleportTo(Ground(c + dir * 3.5f), Quaternion.LookRotation(-dir), false);
            w.Ragnar.GetInventory().AddItem(wood, 10);
            checks.Add($"상자 앞 3.5m·나무 10 들고 묶임 ({Dist():0.0}m, 들고 {BaseTest.Carried(w.Ragnar, "Wood")})",
                Dist() > 2.5f && Dist() <= 4f && BaseTest.Carried(w.Ragnar, "Wood") == 10);

            Unload.TestNoReach = true;
            var before = new Unload(w.Ragnar);
            for (float t = 0f; t < 4f; t += Time.deltaTime) { before.Update(Time.deltaTime); yield return null; }
            checks.Add($"고치기 전: 4초 동안 못 넣음 (들고 {BaseTest.Carried(w.Ragnar, "Wood")}, 상자 {GatherTest.CountBase(w.Home, "Wood")})", BaseTest.Carried(w.Ragnar, "Wood") == 10);

            for (int i = 0; i < 3; i++) { Back(); yield return new WaitForSeconds(1f); if (Dist() <= 3.8f) break; } // E066: 손 거리(4m) 밖으로 밀린 채 시작하면 상자 앞으로 가는 게 맞음(회차 356 꺼내기 4.1m) → 안으로 다시 세움
            Unload.TestNoReach = false;
            int reached0 = Unload.Reached;
            var after = new Unload(w.Ragnar);
            float took = 0f, d0 = Dist();
            string at2 = null; // E066: 끝난 뒤엔 m_chest가 비어 "상자 안 고름"만 보임 → 2초 때 상태를 잡음
            while (took < 10f && after.Update(Time.deltaTime)) { took += Time.deltaTime; if (at2 == null && took > 2f) at2 = $"{after.Why()} 거리 {Dist():0.0}m 속도 {w.Ragnar.GetVelocity().magnitude:0.0}"; yield return null; }
            if (took > 2f) TestRunner.Log($"  E055 손 거리 넣기 건너뛴 까닭(시작 {d0:0.0}m, 2초 때): {at2}");
            checks.Add($"고친 뒤: 그 자리에서 넣음 ({took:0.0}초 ≤ 2, 손 거리 넣기 {Unload.Reached - reached0}, 상자까지 {Dist():0.0}m)",
                took <= 2f && Unload.Reached - reached0 >= 1);
            checks.Add($"나무 10 모두 상자로 (들고 {BaseTest.Carried(w.Ragnar, "Wood")}, 상자 {GatherTest.CountBase(w.Home, "Wood")})",
                BaseTest.Carried(w.Ragnar, "Wood") == 0 && GatherTest.CountBase(w.Home, "Wood") == 10);

            // 꺼내기(Prep)도 같은 자리에서: 상자에 체력 미드 5, 일꾼은 0
            GameObject mead = ObjectDB.instance.GetItemPrefab("MeadHealthMinor");
            if (mead == null) { checks.Add("미드 프리팹 있음", false); yield break; }
            DropMeads(w.Ragnar.GetInventory());
            if (w.Site != null) Prep.TestOnly = w.Chest; // 집: 사용자 상자의 장비·음식·미드를 먼저 챙기러 감(회차 466 첫 판들) → 이 상자만
            TestKit.ClearBaseStock(w, "Wood"); // 집 상자는 꽉 차 있어 넣은 나무 칸을 비워야 미드가 들어감(회차 466)
            Chests.TryEdit(w.Chest, inv => { DropMeads(inv); inv.AddItem(mead, 5); });
            checks.Add($"상자에 미드 5 ({GatherTest.Count(w.Chest, "MeadHealthMinor")})", GatherTest.Count(w.Chest, "MeadHealthMinor") == 5);
            int Meads() => BaseTest.Carried(w.Ragnar, "MeadHealthMinor");
            Back();
            yield return new WaitForSeconds(1f);
            Prep.TestNoReach = true;
            var pBefore = new Prep(w.Ragnar);
            for (float t = 0f; t < 4f; t += Time.deltaTime) { pBefore.Update(Time.deltaTime); yield return null; }
            checks.Add($"꺼내기 고치기 전: 4초 동안 못 챙김 (들고 {Meads()}, 상자까지 {Dist():0.0}m)", Meads() == 0);

            for (int i = 0; i < 3; i++) { Back(); yield return new WaitForSeconds(1f); if (Dist() <= 3.8f) break; } // E066: 손 거리(4m) 밖으로 밀린 채 시작하면 상자 앞으로 가는 게 맞음(회차 356 꺼내기 4.1m) → 안으로 다시 세움
            Prep.TestNoReach = false;
            int pReached0 = Prep.Reached;
            var pAfter = new Prep(w.Ragnar);
            float pTook = 0f, pd0 = Dist();
            string pAt2 = null; // E066 꺼내기: 2초 때 상태
            while (pTook < 10f && Meads() == 0 && pAfter.Update(Time.deltaTime)) { pTook += Time.deltaTime; if (pAt2 == null && pTook > 2f) pAt2 = $"{pAfter.Why()} 거리 {Dist():0.0}m 속도 {w.Ragnar.GetVelocity().magnitude:0.0}"; yield return null; }
            if (pTook > 2f) TestRunner.Log($"  E066 손 거리 꺼내기 건너뛴 까닭(시작 {pd0:0.0}m, 2초 때): {pAt2}");
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
            Back();
            yield return new WaitForSeconds(1f);
            TidyJob.TestNoReach = true;
            var tBefore = new TidyJob(w.Ragnar, Once());
            for (float t = 0f; t < 4f; t += Time.deltaTime) { tBefore.Update(Time.deltaTime); yield return null; }
            checks.Add($"정리 고치기 전: 4초 동안 못 꺼냄 (들고 {Meads()}, 옮김 {tBefore.Moved})", Meads() == 0 && tBefore.Moved == 0);

            for (int i = 0; i < 3; i++) { Back(); yield return new WaitForSeconds(1f); if (Dist() <= 3.8f) break; } // E066: 손 거리(4m) 밖으로 밀린 채 시작하면 상자 앞으로 가는 게 맞음(회차 356 꺼내기 4.1m) → 안으로 다시 세움
            TidyJob.TestNoReach = false;
            int tReached0 = TidyJob.Reached;
            var tAfter = new TidyJob(w.Ragnar, Once());
            float tTook = 0f;
            while (tTook < 10f && tAfter.Moved == 0 && tAfter.Update(Time.deltaTime)) { tTook += Time.deltaTime; yield return null; }
            checks.Add($"정리 고친 뒤: 그 자리에서 꺼내고 넣음 ({tTook:0.0}초 ≤ 3, 옮김 {tAfter.Moved}, 손 거리 {TidyJob.Reached - tReached0})",
                tTook <= 3f && tAfter.Moved > 0 && TidyJob.Reached - tReached0 >= 2);

            // 손 거리 밖 8m에서 묶여 거의 제자리: 5초에 상자 앞으로 옮겨 넣기·꺼내기(ChestHop, 사용자 10-31 — 전엔 90·60초 한도까지 섬)
            DropMeads(w.Ragnar.GetInventory());
            Chests.TryEdit(w.Chest, DropMeads);
            w.Ragnar.TeleportTo(Ground(c + dir * far), Quaternion.LookRotation(-dir), false);
            yield return new WaitForSeconds(1f);
            w.Ragnar.GetInventory().AddItem(wood, 10);
            int hops0 = ChestHop.Hops, sides0 = ChestHop.Sides;
            var uFar = new Unload(w.Ragnar);
            float hTook = 0f;
            while (hTook < 12f && uFar.Update(Time.deltaTime)) { hTook += Time.deltaTime; yield return null; }
            checks.Add($"{far:0.0}m 묶임 넣기: 5초에 상자 앞으로 옮겨 넣음 ({hTook:0.0}초, 옆 {ChestHop.Sides - sides0}, 옮김 {ChestHop.Hops - hops0}, 들고 {BaseTest.Carried(w.Ragnar, "Wood")})",
                hTook >= 4.5f && hTook <= 8f && ChestHop.Sides - sides0 >= 1 && ChestHop.Hops - hops0 == 1 && BaseTest.Carried(w.Ragnar, "Wood") == 0);

            TestKit.ClearBaseStock(w, "Wood");
            Chests.TryEdit(w.Chest, inv => inv.AddItem(mead, 5));
            w.Ragnar.TeleportTo(Ground(c + dir * far), Quaternion.LookRotation(-dir), false);
            yield return new WaitForSeconds(1f);
            hops0 = ChestHop.Hops; sides0 = ChestHop.Sides;
            var pFar = new Prep(w.Ragnar);
            float phTook = 0f;
            while (phTook < 12f && Meads() == 0 && pFar.Update(Time.deltaTime)) { phTook += Time.deltaTime; yield return null; }
            checks.Add($"{far:0.0}m 묶임 꺼내기: 5초에 상자 앞으로 옮겨 미드 챙김 ({phTook:0.0}초, 옆 {ChestHop.Sides - sides0}, 옮김 {ChestHop.Hops - hops0}, 들고 {Meads()})",
                phTook >= 4.5f && phTook <= 8f && ChestHop.Sides - sides0 >= 1 && ChestHop.Hops - hops0 == 1 && Meads() > 0);

            // 묶지 않고 8m 앞 상자, 일꾼 1.5m 앞에 통나무(beech_log)를 가로로: 닿으면 0.5초에 옆으로(ChestHop.Bumps) — 고치기 전(TestNoBump)·후 넣기까지 시간 비교
            HopTest.Speeds(w.Ragnar, saved, false);
            hold(null);
            GameObject big = ZNetScene.instance.GetPrefab("beech_log") ?? lp;
            float[] took2 = new float[2];
            int[] bumps2 = new int[2];
            for (int i = 0; i < 2; i++)
            {
                ChestHop.TestNoBump = i == 0;
                w.Ragnar.TeleportTo(Ground(c + dir * far), Quaternion.LookRotation(-dir), false);
                GameObject log = Object.Instantiate(big, Ground(c + dir * (far - 1.5f)) + Vector3.up * 0.5f, Quaternion.FromToRotation(Vector3.up, side));
                made.Add(log);
                yield return new WaitForSeconds(1.5f);
                w.Ragnar.GetInventory().AddItem(wood, 10);
                int b0 = ChestHop.Bumps;
                var u = new Unload(w.Ragnar);
                float t2 = 0f;
                string at8 = null; // E067: 8초 때 ChestHop 상태(5초 상자 앞 옮기기가 왜 안 걸렸나)
                while (t2 < 15f && u.Update(Time.deltaTime)) { t2 += Time.deltaTime; if (at8 == null && t2 > 8f) at8 = $"{u.Why()} 거리 {Dist():0.0}m 속도 {w.Ragnar.GetVelocity().magnitude:0.0}"; yield return null; }
                if (at8 != null) TestRunner.Log($"  E067 {i}번째 8초 때: {at8}");
                took2[i] = t2; bumps2[i] = ChestHop.Bumps - b0;
                if (log != null) ZNetScene.instance.Destroy(log);
                TestKit.ClearBaseStock(w, "Wood");
                yield return new WaitForSeconds(0.5f);
            }
            ChestHop.TestNoBump = false;
            checks.Add($"통나무 닿음(묶지 않음): 고치기 전 {took2[0]:0.0}초 → 고친 뒤 {took2[1]:0.0}초, 일찍 옆으로 {bumps2[1]}, 달리기 {w.Ragnar.m_runSpeed:0.0} (들고 {BaseTest.Carried(w.Ragnar, "Wood")})",
                took2[1] < 15f && took2[1] <= took2[0] + 0.5f && bumps2[0] == 0 && BaseTest.Carried(w.Ragnar, "Wood") == 0);
        }
    }
}
