using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_matetest (동료 부르기, 사용자 결정 2026-10-24 "더 공격적으로" 나): 1호는 체력을 깎아 혼자면 물러날 회색난쟁이 둘 앞에 두고,
    /// 2호는 60m 옆에서 나무를 하게 한다. A) 1호가 부름 → 2호가 대답하고 달려와 둘이 잡음 → 2호가 하던 나무 일을 이어 감·짐 그대로.
    /// B) 2호가 150m 밖이면 안 부름. 플레이어는 유령·갓 모드(한 방에 안 셈).
    /// </summary>
    static class MateTest
    {
        public static void Register() => TestRunner.Register("vh_matetest", "동료 부르기 시험", () => TestKit.Flat(Run()));

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
            var spawned = new List<GameObject>();
            bool ghost = Player.m_localPlayer.InGhostMode();
            try { yield return Body(w, checks, spawned); }
            finally
            {
                Cleanup(spawned, ghost);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void Cleanup(List<GameObject> spawned, bool ghost)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
            Player.m_localPlayer.SetGhostMode(ghost);
        }

        static Character Spawn(GameObject prefab, Vector3 pos, List<GameObject> spawned)
        {
            GameObject go = Object.Instantiate(prefab, GatherTest.Ground(pos) + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Add(go);
            Character ch = go.GetComponent<Character>();
            ch.SetLevel(1);
            return ch;
        }

        static void Arm(WorkerNpc n)
        {
            Inventory inv = n.GetInventory();
            foreach (var p in new[] { "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather", "AxeFlint", "Club" }) inv.AddItem(ObjectDB.instance.GetItemPrefab(p), 1);
            Prep.WearBest(n);
            n.Heal(1000f, false);
        }

        static string Diag(WorkerNpc n) => n == null ? "-" : $"{n.NpcName} 경계 {n.Ai.Guard?.StateName}/{n.Ai.Guard?.LastVerdict} 비율 {n.Ai.Guard?.LastRatio:0.00} 체력 {n.GetHealth():0}/{n.GetMaxHealth():0} ({n.Ai.Guard?.LastDetail})";

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned)
        {
            w.CountsCreatures = true; // 생물을 부르는 시험
            yield return TestKit.Setup(w);
            GameObject gd = ZNetScene.instance.GetPrefab("Greydwarf");
            checks.Add("깃발·NPC 둘·회색난쟁이 프리팹", w.Ragnar != null && w.Floki != null && gd != null);
            if (w.Ragnar == null || w.Floki == null || gd == null) yield break;
            Player p = w.Player;
            p.SetGhostMode(true); // 몬스터가 일꾼을 보고, 플레이어 한 방은 안 셈
            WorkerNpc r = w.Ragnar, f = w.Floki;
            Arm(r);
            Arm(f);
            Quaternion rot = p.transform.rotation;

            // 2호: 거점 60m 옆에서 나무 하기
            Vector3 work = GatherTest.Ground(w.Home + rot * new Vector3(-60f, 0f, 0f));
            f.TeleportTo(work + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(1f);
            var job = new GatherJob(f, "Wood", 30, ResourceCatalog.WaysFor("Wood"));
            f.Ai.StartJob(job);
            yield return new WaitForSeconds(3f);
            int fItems = f.GetInventory().NrOfItems();

            // 1호: 거점 앞 30m, 체력을 깎아 혼자면 물러날 만큼
            Vector3 arena = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 30f));
            r.TeleportTo(arena + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            r.SetHealth(r.GetMaxHealth() * 0.8f);
            r.Ai.Guard?.TestForgetMateCall();
            Guard.LastMateCall = "";
            TestRunner.Log($"시작: 2호까지 {Vector3.Distance(r.transform.position, f.transform.position):0}m, 2호 짐 {fItems}, 일 {f.Ai.Job?.GetType().Name}");

            // A) 회색난쟁이 둘 → 부름 → 둘이 잡음
            int deaths = r.Deaths;
            var foes = new List<Character>();
            for (int i = 0; i < 2; i++) foes.Add(Spawn(gd, arena + rot * new Vector3(i * 2f - 1f, 0f, 12f), spawned));
            yield return TestKit.WaitFor(() => Guard.LastMateCall != "", 15f);
            TestRunner.Log($"[A 부름] {Guard.LastMateCall} / {Diag(r)}");
            checks.Add($"1호가 2호를 부름 ({Guard.LastMateCall})", Guard.LastMateCall.StartsWith(r.NpcName + "→" + f.NpcName));
            bool wait = Guard.LastMateCall.EndsWith("Wait");
            yield return new WaitForSeconds(1f);
            checks.Add("2호가 대답함", w.Heard.Exists(h => h.StartsWith(f.NpcName + ":") && h.Contains(r.NpcName)));
            float near = 999f;
            bool together = false, alone = false;
            float t = 0f;
            while (t < 120f && foes.Exists(c => c != null && !c.IsDead()) && !r.IsDead() && !f.IsDead())
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                float d = Vector3.Distance(r.transform.position, f.transform.position);
                near = Mathf.Min(near, d);
                if (d <= Guard.AllyRange && f.Ai.Guard?.StateName == "Fight") together = true;
                if (!together && d > Guard.AllyRange + 2f && r.Ai.Guard?.StateName == "Fight") alone = true;
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[A {t:0}s] 거리 {d:0}m / {Diag(r)} / {Diag(f)}");
            }
            checks.Add($"2호가 20m 안으로 와서 싸움 (가장 가까이 {near:0}m)", together);
            if (wait) checks.Add("부른 1호는 2호가 오기 전엔 혼자 싸우지 않음", !alone);
            checks.Add($"1호가 죽지 않음 (죽음 {r.Deaths - deaths})", r.Deaths == deaths);
            checks.Add($"둘이 회색난쟁이를 다 잡음 ({t:0}초)", !foes.Exists(c => c != null && !c.IsDead()));
            checks.Add("둘 다 살아 있음", !r.IsDead() && !f.IsDead());
            yield return TestKit.WaitFor(() => f.Ai.Guard != null && !f.Ai.Guard.Engaged, 45f);
            yield return new WaitForSeconds(2f);
            checks.Add($"2호가 하던 나무 일을 이어 감 ({f.Ai.Current}, {f.Ai.Job?.GetType().Name}, 같은 일 {f.Ai.Job == job})", f.Ai.Job == job && f.Ai.Current == WorkerAI.Mode.Work);
            checks.Add($"2호 짐 그대로 이상 ({fItems} → {f.GetInventory().NrOfItems()})", f.GetInventory().NrOfItems() >= fItems);

            // B) 2호가 150m 밖이면 안 부름
            Vector3 farAway = GatherTest.Ground(w.Home + rot * new Vector3(-150f, 0f, 0f));
            f.TeleportTo(farAway + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            r.Heal(1000f, false);
            r.SetHealth(r.GetMaxHealth() * 0.8f);
            r.Ai.Guard?.TestForgetMateCall();
            Guard.LastMateCall = "";
            var foes2 = new List<Character>();
            for (int i = 0; i < 2; i++) foes2.Add(Spawn(gd, r.transform.position + rot * new Vector3(i * 2f - 1f, 0f, 12f), spawned));
            yield return new WaitForSeconds(8f);
            TestRunner.Log($"[B] 2호까지 {Vector3.Distance(r.transform.position, f.transform.position):0}m / {Diag(r)}");
            checks.Add($"2호가 {Guard.MateCallRange:0}m 밖이면 안 부름 ({(Guard.LastMateCall == "" ? "안 부름" : Guard.LastMateCall)})", Guard.LastMateCall == "");
            foreach (var c in foes2) if (c != null && !c.IsDead() && c.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
        }
    }
}
