using System.Collections;
using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_birdshoottest (땅에 앉은 새 명중 셈): 낮·밤 각각 10·20·30m 앞 땅 새(Crow)를 Ragnar가 활로 3판씩 쏘고, 쏜 수·맞음(죽음)·앉아 있던 시간·쏘기 전 날아감·깃털 수를 셈.
    /// 칸마다 여러 판의 중간값을 표로 남긴다(측정만 — 기능 아님). 쏘기 자체가 나가는지(화살 한 발씩)가 PASS 기준.
    /// </summary>
    static class BirdShootTest
    {
        const int Trials = 3;
        static readonly float[] Distances = { 10f, 20f, 30f };

        class Shot
        {
            public bool Fired, Hit, FlewBeforeShot;
            public float Sat;
            public int Feathers;
        }

        public static void Register() => TestRunner.Register("vh_birdshoottest", "땅 앉은 새 명중 셈(낮·밤, 거리별)", () => TestKit.Flat(Run()));

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
            EnvMan env = EnvMan.instance;
            bool debugTime = env.m_debugTimeOfDay;
            float debugValue = env.m_debugTime;
            try { yield return Body(w, checks, env); }
            finally
            {
                env.m_debugTimeOfDay = debugTime;
                env.m_debugTime = debugValue;
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, EnvMan env)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);

            // 활·나무 화살 60: 18판 동안 떨어지지 않게 넉넉히
            var npc = w.Ragnar;
            npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Bow"), 1);
            npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("ArrowWood"), 60);
            bool held = npc.HoldBow();
            TestRunner.Log($"DIAG 활 준비: 활 {npc.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab("Bow").GetComponent<ItemDrop>().m_itemData.m_shared.m_name)} · 나무화살 {npc.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab("ArrowWood").GetComponent<ItemDrop>().m_itemData.m_shared.m_name)} · 든 것 {npc.GetCurrentWeapon()?.m_shared.m_name} · 가방 칸 {npc.GetInventory().GetAllItems().Count}");
            checks.Add("활 들기", held);
            ItemDrop.ItemData bow = npc.GetCurrentWeapon();

            int total = 0, fired = 0;
            foreach (bool night in new[] { false, true })
            {
                // 낮 = 정오(0.5), 밤 = 자정(0). 시간이 실제로 바뀔 때까지 기다린다
                env.m_debugTimeOfDay = true;
                env.m_debugTime = night ? 0f : 0.5f;
                yield return new WaitForSeconds(2f);
                string when = night ? "밤" : "낮";
                TestRunner.Log($"DIAG {when} 시각 날 비율 {EnvMan.instance.GetDayFraction():0.000} · 밤 {EnvMan.IsNight()}");
                checks.Add($"{when} 맞춤 (밤 {EnvMan.IsNight()})", EnvMan.IsNight() == night);

                foreach (float dist in Distances)
                {
                    var shots = new List<Shot>();
                    for (int i = 0; i < Trials; i++)
                    {
                        var s = new Shot();
                        yield return OneShot(npc, bow, dist, s);
                        shots.Add(s);
                        total++;
                        if (s.Fired) fired++;
                        TestRunner.Log($"DIAG {when} {dist:0}m #{i + 1}: 쏨 {(s.Fired ? "예" : "아니오")} · 맞음 {(s.Hit ? "예" : "아니오")} · 앉은 시간 {s.Sat:0.0}초 · 쏘기 전 날아감 {(s.FlewBeforeShot ? "예" : "아니오")} · 깃털 {s.Feathers}");
                    }
                    int hits = 0, flew = 0;
                    var sats = new List<float>();
                    var feathers = new List<float>();
                    foreach (var s in shots)
                    {
                        if (s.Hit) hits++;
                        if (s.FlewBeforeShot) flew++;
                        sats.Add(s.Sat);
                        feathers.Add(s.Feathers);
                    }
                    TestRunner.Log($"표 {when} {dist:0}m: 쏨 {shots.FindAll(s => s.Fired).Count}/{Trials} · 맞음 {hits}/{Trials} · 앉은 시간 중간 {Median(sats):0.0}초 · 쏘기 전 날아감 {flew}/{Trials} · 깃털 중간 {Median(feathers):0}");
                }
            }
            checks.Add($"쏘기마다 화살 한 발 나감 ({fired}/{total})", fired == total);
        }

        // 새 한 마리를 거리 dist에 놓고 활로 한 발 쏜다. 결과는 s에 쓴다
        static IEnumerator OneShot(WorkerNpc npc, ItemDrop.ItemData bow, float dist, Shot s)
        {
            Vector3 at = GatherTest.Ground(npc.transform.position + npc.transform.forward * dist) + Vector3.up * 0.5f;
            int feathersBefore = GatherTest.GroundCount(at, "Feather");
            int arrowsBefore = Gear.CountArrows(npc.GetInventory(), bow);
            GameObject prefab = ZNetScene.instance.GetPrefab("Crow");
            var bird = Object.Instantiate(prefab, at, Quaternion.identity).GetComponent<RandomFlyingBird>();
            if (bird == null) yield break;
            // 땅에 앉을 때까지 기다린다(ZDO landed, 8초 넘으면 그냥 감). 앉은 뒤부터 잰다
            yield return TestKit.WaitFor(() => bird == null || IsLanded(bird), 8f);
            float landedAt = Time.time;

            // 당기면서 새를 보고, 다 당기면 놓는다(3초 넘으면 그냥 놓음)
            float t = 0f;
            while (t < 3f && bird != null && npc.HoldingBow())
            {
                npc.Ai.Face(bird.transform.position);
                float pct = npc.DrawBow(Time.deltaTime);
                t += Time.deltaTime;
                if (pct >= 1f) break;
                yield return null;
            }
            s.FlewBeforeShot = bird != null && (!IsLanded(bird) || Vector3.Distance(bird.transform.position, at) > 1.5f);
            s.Sat = Time.time - landedAt;

            // 날아가는 값(속력·중력)으로 앞질러 겨눔 — Hunt.UpdateShoot과 같은 방식(새는 안 움직인다고 봄)
            Vector3 eye = npc.GetEyePoint();
            if (bird != null && npc.ProjectileFlight(bow, out float speed, out float gravity))
            {
                Vector3 rel = bird.transform.position - eye;
                if (Archery.Aim(rel.x, rel.y, rel.z, 0f, 0f, 0f, speed, gravity, out float ax, out float ay, out float az))
                    npc.Ai.Face(eye + new Vector3(ax, ay, az));
            }
            bool shot = npc.ReleaseBow();
            yield return new WaitForSeconds(3f);

            s.Fired = shot && Gear.CountArrows(npc.GetInventory(), bow) == arrowsBefore - 1;
            s.Hit = bird == null;
            s.Feathers = GatherTest.GroundCount(at, "Feather") - feathersBefore;
            if (bird != null) ZNetScene.instance.Destroy(bird.gameObject);
        }

        // 새가 땅에 앉았는지(ZNetView의 ZDO landed). 없으면 안 앉은 것으로 본다
        static bool IsLanded(RandomFlyingBird bird)
        {
            var nv = bird.GetComponent<ZNetView>();
            return nv != null && nv.GetZDO() != null && nv.GetZDO().GetBool("landed");
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return 0f;
            v.Sort();
            return v[v.Count / 2];
        }
    }
}
