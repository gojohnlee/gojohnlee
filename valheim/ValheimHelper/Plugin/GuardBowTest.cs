using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_guardbowtest (호위병 활·석궁 — 사용자 11-09): 상자에 석궁·볼트·활 하나·화살·곤봉 → 호위병은 석궁·볼트·곤봉만 챙김(활 하나는 일꾼 사냥 몫)
    /// → 12m 그레일링(멈춤)을 석궁으로 쏴 잡음 → 활·화살로 바꿔 쥐여 주면 활로 쏴 잡음 → 화살이 없으면 근접으로 잡음.
    /// </summary>
    static class GuardBowTest
    {
        public static void Register() => TestRunner.Register("vh_guardbowtest", "호위병 활·석궁", () => TestKit.Flat(Run()));

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
                foreach (var n in new[] { w.Ragnar, w.Floki }) if (n != null && n.Ai != null) n.Ai.enabled = true;
                GatherTest.Say("!멈춰", null);
                TestKit.Restore(w);
            }
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static int Count(Inventory inv, string prefab)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems()) if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) n += it.m_stack;
            return n;
        }

        static void Remove(Inventory inv, string prefab)
        {
            foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) inv.RemoveItem(it);
        }

        /// <summary>호위병 앞 12m에 멈춘 그레일링을 두고 죽을 때까지(최대 60초) 기다린다. 쏜 수를 돌려준다.</summary>
        static IEnumerator Foe(WorkerNpc guard, Vector3 f, List<GameObject> made, int[] shots, float[] closest)
        {
            // 앞 싸움 뒤 떨굼 줍기(Guard.Loot)·자리로 걷기가 끝나 멈출 때까지
            float still = 0f;
            for (float s = 0f; s < 30f && still < 1f; s += Time.deltaTime) { still = guard.GetVelocity().magnitude < 0.3f && !guard.Ai.Guard.Engaged ? still + Time.deltaTime : 0f; yield return null; }
            int before = Guard.Shots;
            Character foe = FulingTest.Spawn(ZNetScene.instance.GetPrefab("Greyling"), GatherTest.Ground(guard.transform.position + f * 12f), made);
            foe.GetBaseAI().enabled = false; // 그 자리에 서 있음(EscortTest와 같음)
            closest[0] = float.MaxValue;
            float t = 0f;
            while (foe != null && !foe.IsDead() && t < 60f)
            {
                closest[0] = Mathf.Min(closest[0], Utils.DistanceXZ(guard.transform.position, foe.transform.position));
                t += Time.deltaTime;
                yield return null;
            }
            TestKit.Ok = foe == null || foe.IsDead();
            TestKit.Waited = t;
            shots[0] = Guard.Shots - before;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·일꾼 둘·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null) yield break;
            // 일꾼 둘은 멈춰 둔다(BaseAI.enabled = false면 UpdateAI가 안 불림) — 곁에서 먼저 그레일링을 잡지 않게
            w.Ragnar.Ai.enabled = false;
            if (w.Floki != null) w.Floki.Ai.enabled = false;
            TestKit.PutInChest(w.Chest, "CrossbowArbalest", 1);
            TestKit.PutInChest(w.Chest, "BoltBone", 30);
            TestKit.PutInChest(w.Chest, "Feathers", 50); // 볼트는 깃털 2가 드는 것 — 깃털 50 넘어야 챙김(AmmoRule). 한 번에 50까지만 들어가 두 번 넣음
            TestKit.PutInChest(w.Chest, "Feathers", 50);
            TestKit.PutInChest(w.Chest, "Bow", 1);
            TestKit.PutInChest(w.Chest, "ArrowWood", 30);
            TestKit.PutInChest(w.Chest, "Club", 1);
            yield return TestKit.SpawnGuard();
            WorkerNpc guard = WorkerNpc.TheGuard;
            checks.Add($"호위병 생김 ({guard?.NpcName ?? "없음"}, {TestKit.Waited:0}초)", TestKit.Ok && guard != null);
            if (guard == null) yield break;
            Inventory inv = guard.GetInventory();

            // 1) 장비 챙기기: 석궁·볼트·곤봉, 활 하나는 상자에 남김(일꾼 사냥 몫)
            yield return TestKit.WaitFor(() => Count(inv, "CrossbowArbalest") > 0 && Count(inv, "BoltBone") > 0 && guard.Ai.Current == WorkerAI.Mode.Idle, 60f);
            checks.Add($"석궁·볼트 챙김 ({TestKit.Waited:0}초, 석궁 {Count(inv, "CrossbowArbalest")} 볼트 {Count(inv, "BoltBone")} 곤봉 {Count(inv, "Club")})",
                TestKit.Ok && Count(inv, "BoltBone") == 30 && Count(inv, "Club") == 1);
            checks.Add($"하나뿐인 활·화살은 안 챙김 (활 {Count(inv, "Bow")} 화살 {Count(inv, "ArrowWood")})", Count(inv, "Bow") == 0 && Count(inv, "ArrowWood") == 0);

            Vector3 f = w.Player.transform.forward; f.y = 0f; f.Normalize();
            var shots = new int[1];
            var closest = new float[1];
            // 2) 석궁으로 쏨
            yield return Foe(guard, f, made, shots, closest);
            checks.Add($"석궁으로 그레일링 잡음 ({TestKit.Waited:0.0}초, 쏨 {shots[0]}, 가장 가까이 {closest[0]:0.0}m, 볼트 {Count(inv, "BoltBone")})",
                TestKit.Ok && shots[0] > 0 && closest[0] > 9f); // 멈춘 적 12m — 다가가지 않고 쏨

            // 3) 석궁·볼트를 빼고 활·화살 → 활로 쏨
            yield return TestKit.WaitFor(() => !guard.Ai.Guard.Engaged, 20f);
            Remove(inv, "CrossbowArbalest");
            Remove(inv, "BoltBone");
            inv.AddItem(ObjectDB.instance.GetItemPrefab("Bow"), 1);
            inv.AddItem(ObjectDB.instance.GetItemPrefab("ArrowWood"), 20);
            yield return Foe(guard, f, made, shots, closest);
            checks.Add($"활로 그레일링 잡음 ({TestKit.Waited:0.0}초, 쏨 {shots[0]}, 가장 가까이 {closest[0]:0.0}m, 화살 {Count(inv, "ArrowWood")})",
                TestKit.Ok && shots[0] > 0 && closest[0] > 9f); // 멈춘 적 12m — 다가가지 않고 쏨

            // 4) 화살이 없으면 근접
            yield return TestKit.WaitFor(() => !guard.Ai.Guard.Engaged, 20f);
            Remove(inv, "ArrowWood");
            yield return Foe(guard, f, made, shots, closest);
            checks.Add($"화살 없으면 근접으로 잡음 ({TestKit.Waited:0.0}초, 쏨 {shots[0]}, 가장 가까이 {closest[0]:0.0}m, 든 것 {guard.GetCurrentWeapon()?.m_shared.m_name})",
                TestKit.Ok && shots[0] == 0 && closest[0] < 4f);
        }
    }
}
