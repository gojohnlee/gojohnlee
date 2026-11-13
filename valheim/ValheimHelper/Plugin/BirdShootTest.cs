using System.Collections;
using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_birdshoottest (땅에 앉은 새 명중 셈): 낮·밤 각각 10·20·30m 앞 땅 새(Crow)를 Ragnar가 활로 3판씩 쏘고, 쏜 수·맞음(죽음)·앉아 있던 시간·쏘기 전 날아감·깃털 수를 셈.
    /// 새는 놓은 자리가 아니라 날아가 앉은 자리에 앉으니, 거리는 앉은 자리까지 잰다(DIAG 착지 m). 당기기는 새가 앉기 전부터 시작해 앉은 뒤 놓는다.
    /// 칸마다 여러 판의 중간값을 표로 남긴다(측정만 — 기능 아님). 쏘기 자체가 나가는지(화살 한 발씩)가 PASS 기준.
    /// </summary>
    static class BirdShootTest
    {
        const int Trials = 6;
        static readonly float[] Distances = { 10f, 20f, 30f };

        class Shot
        {
            public bool Fired, Hit, FlewBeforeShot;
            public float Sat, Dist, Pct;
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
            // Humanoid.EquipItem은 휘두르는 중(InAttack)이면 거절한다. 시작 직전 거점에 온 멧돼지와 싸우던 휘두름이
            // 생물 치운 뒤에도 남아 있을 수 있다(회차 542) → 다 휘두를 때까지 3초 안에서 다시 든다(GatherJob.Hunt도 틱마다 다시 듦).
            bool attacking = npc.InAttack(), held = npc.HoldBow();
            float waited = 0f;
            while (!held && waited < 3f) { yield return null; waited += Time.deltaTime; held = npc.HoldBow(); }
            TestRunner.Log($"DIAG 활 준비: 활 {npc.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab("Bow").GetComponent<ItemDrop>().m_itemData.m_shared.m_name)} · 나무화살 {npc.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab("ArrowWood").GetComponent<ItemDrop>().m_itemData.m_shared.m_name)} · 든 것 {npc.GetCurrentWeapon()?.m_shared.m_name} · 가방 칸 {npc.GetInventory().GetAllItems().Count} · 처음 공격 중 {attacking} · 기다림 {waited:0.0}초 · 쏠 활 {(npc.ShootingBow() != null)}");
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
                        TestRunner.Log($"DIAG {when} {dist:0}m #{i + 1}: 쏨 {(s.Fired ? "예" : "아니오")} · 맞음 {(s.Hit ? "예" : "아니오")} · 앉은 시간 {s.Sat:0.0}초 · 착지 {s.Dist:0.0}m · 당김 {s.Pct:0.00} · 쏘기 전 날아감 {(s.FlewBeforeShot ? "예" : "아니오")} · 깃털 {s.Feathers}");
                    }
                    int hits = 0, flew = 0;
                    var sats = new List<float>();
                    var feathers = new List<float>();
                    var dists = new List<float>();
                    foreach (var s in shots)
                    {
                        if (s.Hit) hits++;
                        if (s.FlewBeforeShot) flew++;
                        sats.Add(s.Sat);
                        feathers.Add(s.Feathers);
                        dists.Add(s.Dist);
                    }
                    TestRunner.Log($"표 {when} {dist:0}m: 쏨 {shots.FindAll(s => s.Fired).Count}/{Trials} · 맞음 {hits}/{Trials} · 착지 중간 {Median(dists):0.0}m · 앉은 시간 중간 {Median(sats):0.0}초 · 쏘기 전 날아감 {flew}/{Trials} · 깃털 중간 {Median(feathers):0}");
                }
            }
            checks.Add($"쏘기마다 화살 한 발 나감 ({fired}/{total})", fired == total);
        }

        // 새 한 마리를 거리 dist 근처에 놓고 활로 한 발 쏜다. 결과는 s에 쓴다.
        // 새가 날아다니는 동안 다 당기고, 다 당긴 뒤 놓은 자리에 앉힌다(ZDO landed·위치 강제) 그리고 바로 쏜다.
        // 앉기를 기다리면 착지 자리가 흩어져 거리가 안 맞는다(회차 541).
        static IEnumerator OneShot(WorkerNpc npc, ItemDrop.ItemData bow, float dist, Shot s)
        {
            Vector3 at = GatherTest.Ground(npc.transform.position + npc.transform.forward * dist) + Vector3.up * 0.5f;
            int arrowsBefore = Gear.CountArrows(npc.GetInventory(), bow);
            GameObject prefab = ZNetScene.instance.GetPrefab("Crow");
            var bird = Object.Instantiate(prefab, at, Quaternion.identity).GetComponent<RandomFlyingBird>();
            if (bird == null) yield break;

            // 최대 8초 안에 다 당긴다
            float t = 0f, pct = 0f;
            while (bird != null && npc.HoldingBow() && t < 8f)
            {
                npc.Ai.Face(bird.transform.position);
                pct = npc.DrawBow(Time.deltaTime);
                t += Time.deltaTime;
                if (pct >= 1f) break;
                yield return null;
            }

            // 스스로 앉았는지(강제 전) — 아니면 "쏘기 전 날아감"
            bool natural = bird != null && IsLanded(bird);
            Vector3 landAt = at;
            if (bird != null)
            {
                var nv = bird.GetComponent<ZNetView>();
                if (nv != null && nv.GetZDO() != null) nv.GetZDO().Set("landed", true);
                bird.transform.position = landAt;
            }
            s.Pct = pct;
            s.Dist = Vector3.Distance(npc.GetEyePoint(), landAt);
            s.Sat = 0f;
            s.FlewBeforeShot = !natural;
            int feathersBefore = GatherTest.GroundCount(landAt, "Feather");
            TestRunner.Log($"DIAG 진단 {dist:0}m: 새 {(bird == null ? "없음" : "있음")} · 스스로 앉음 {natural} · 당김 {pct:0.00}");

            // 날아가는 값(속력·중력)으로 앞질러 겨눔 — Hunt.UpdateShoot과 같은 방식(새는 안 움직인다고 봄)
            // 몸이 겨눔 쪽으로 천천히 돈다 → 10° 안에 들어올 때까지 다시 겨누고 기다린 뒤 놓는다(Hunt.UpdateShoot와 같음, 최대 2초).
            // 안 기다리고 놓으면 몸이 돌기 전에 나가 빗나갔다(회차 544).
            // 겨눌 곳은 새 몸(콜라이더) 가운데 — 사냥이 GetCenterPoint를 겨누는 것과 같게(자리 = 발밑일 수 있음)
            // 화살은 눈 아래 0.31m에서 나간다(진단 회차 545: 발사 자리 y −0.31) → 눈에서 겨누면 그만큼 아래로 빗나갔다.
            // Archery.Aim은 발사 자리(from)에서 본 겨눌 점을 돌려주고 화살 방향은 눈의 앞이라, 점을 눈 기준으로 옮긴다(eye + 오프셋) — 눈 기준으로 겨눈 것보다 맞힘이 늘었다(회차 546: 낮 0→10/18·밤 1→5/18).
            // 바닐라 공격 자리(GetProjectileSpawnPoint 식)로 from을 바꾼 판은 낮 3/18·밤 1/18로 줄어 되돌림(회차 546).
            Vector3 eye = npc.GetEyePoint(), from = eye + Vector3.down * 0.31f, aim = eye + npc.transform.forward;
            if (bird != null && npc.ProjectileFlight(bow, out float speed, out float gravity))
            {
                Vector3 rel = BirdCenter(bird) - from;
                if (Archery.Aim(rel.x, rel.y, rel.z, 0f, 0f, 0f, speed, gravity, out float ax, out float ay, out float az))
                    aim = eye + new Vector3(ax, ay, az);
            }
            npc.Ai.Face(aim);
            float aimWait = 0f;
            while (aimWait < 2f && !npc.Ai.IsLookingAt(aim, 10f))
            {
                yield return null;
                aimWait += Time.deltaTime;
                npc.Ai.Face(aim);
            }
            // 화살은 놓는 순간이 아니라 공격 애니메이션의 발사 순간에 생기고, 방향은 그때의 m_eye.forward(Attack.GetProjectileSpawnPoint → Humanoid.GetAimDir).
            // 쉬는 일꾼은 잡담 중 WorkerBanter.Facing으로 동료를 본다 → 화살이 생길 때까지 겨눔 유지(Hunt.UpdateShoot도 공격 중 틱마다 Face).
            GameObject prev = bow.m_lastProjectile, arrow = null;
            bool shot = npc.ReleaseBow();
            string hitWhat = "없음", colInfo = "화살 못 봄";
            Vector3 spawn = Vector3.zero, lastPos = Vector3.zero, dir0 = Vector3.zero;
            float minD = float.MaxValue, waitT = 0f;
            while (waitT < 3f)
            {
                if (arrow == null && bow.m_lastProjectile != null && bow.m_lastProjectile != prev)
                {
                    arrow = bow.m_lastProjectile;
                    spawn = lastPos = arrow.transform.position;
                    colInfo = ColliderInfo(bird);
                    var pj = arrow.GetComponent<Projectile>();
                    if (pj != null)
                    {
                        dir0 = pj.GetVelocity();
                        // 맞은 것(바닐라 Projectile.OnHit 끝의 m_onHit — 피해 판정까지 간 것만 불림)
                        pj.m_onHit += (c, p, water) => hitWhat = c == null ? (water ? "물" : "?")
                            : $"{c.name}(층 {LayerMask.LayerToName(c.gameObject.layer)}) {Utils.DistanceXZ(eye, p):0.0}m 땅 위 {p.y - GatherTest.Ground(p).y:0.00}m";
                    }
                }
                if (arrow == null) npc.Ai.Face(aim);
                yield return null;
                waitT += Time.deltaTime;
                if (arrow != null)
                {
                    Vector3 now = arrow.transform.position;
                    if (bird != null) minD = Mathf.Min(minD, SegDist(lastPos, now, BirdCenter(bird)));
                    lastPos = now;
                }
            }
            Vector3 aimDir = (aim - eye).normalized;
            TestRunner.Log($"DIAG 화살 {dist:0}m: 맞은 것 {hitWhat} · 새 몸까지 가장 가까이 {(minD == float.MaxValue ? "-" : minD.ToString("0.00"))}m" +
                           $" · 발사 자리 눈 기준(오른·위·앞) {npc.transform.InverseTransformDirection(spawn - eye):F2} · 겨눔 선에서 {Vector3.Cross(aimDir, spawn - eye).magnitude:0.00}m" +
                           $" · 쏜 방향 어긋남 {(dir0 == Vector3.zero ? "-" : Vector3.Angle(dir0, aimDir).ToString("0.0"))}° · 겨눔 기다림 {aimWait:0.0}초" +
                           $" · 새 앉음 {(bird != null && IsLanded(bird))} 옮겨감 {(bird == null ? "-" : Vector3.Distance(bird.transform.position, landAt).ToString("0.0"))}m · 콜라이더 {colInfo}");

            s.Fired = shot && Gear.CountArrows(npc.GetInventory(), bow) == arrowsBefore - 1;
            s.Hit = bird == null;
            s.Feathers = GatherTest.GroundCount(landAt, "Feather") - feathersBefore;
            if (bird != null) ZNetScene.instance.Destroy(bird.gameObject);
        }

        // 새가 땅에 앉았는지(ZNetView의 ZDO landed). 없으면 안 앉은 것으로 본다
        static bool IsLanded(RandomFlyingBird bird)
        {
            var nv = bird.GetComponent<ZNetView>();
            return nv != null && nv.GetZDO() != null && nv.GetZDO().GetBool("landed");
        }

        // 새 몸 가운데: 콜라이더(SphereCollider 1개 — vh_birdprobetest) 중심. 없으면 자리
        static Vector3 BirdCenter(RandomFlyingBird bird)
        {
            var c = bird.GetComponentInChildren<Collider>(true);
            if (c is SphereCollider sc) return sc.transform.TransformPoint(sc.center);
            return c != null ? c.bounds.center : bird.transform.position;
        }

        // 진단: 새 콜라이더(꺼진 것 포함) — 켜짐·붙은 곳·층·트리거·반지름·자리에서 중심까지.
        // 앉으면 RandomFlyingBird가 m_landedModel/m_flyingModel을 바꿔 켜므로 맞을 몸이 켜져 있는지 본다
        static string ColliderInfo(RandomFlyingBird bird)
        {
            if (bird == null) return "새 없음";
            var parts = new List<string>();
            foreach (var c in bird.GetComponentsInChildren<Collider>(true))
            {
                Vector3 sc3 = c.transform.lossyScale;
                float r = c is SphereCollider sc ? sc.radius * Mathf.Max(sc3.x, Mathf.Max(sc3.y, sc3.z)) : c.bounds.extents.magnitude;
                parts.Add($"{c.GetType().Name} {(c.transform == bird.transform ? "몸통" : c.transform.parent?.name + "/" + c.name)} 켜짐 {c.enabled && c.gameObject.activeInHierarchy}" +
                          $" 층 {LayerMask.LayerToName(c.gameObject.layer)} 트리거 {c.isTrigger} 반지름 {r:0.00} 중심-자리 {(BirdCenter(bird) - bird.transform.position):F2}");
            }
            return parts.Count == 0 ? "없음" : string.Join(", ", parts);
        }

        // 점 p와 선분 a-b 사이 거리(화살은 물리 틱마다 ~0.7m씩 뛰니 점끼리 말고 선분으로 잰다)
        static float SegDist(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(a + ab * t, p);
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return 0f;
            v.Sort();
            return v[v.Count / 2];
        }
    }
}
