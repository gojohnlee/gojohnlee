using System.Collections;
using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_birdshoottest (땅에 앉은 새 명중 셈): 낮·밤 각각 10·20·30m 앞 땅 새(Crow)를 Ragnar가 활로 3판씩 쏘고, 쏜 수·맞음(죽음)·스스로 앉아 있던 시간·강제 앉힘·깃털 수를 셈.
    /// 새는 놓은 자리가 아니라 날아가 앉은 자리에 앉으니, 거리는 앉은 자리까지 잰다(DIAG 착지 m). 당기기는 새가 앉기 전부터 시작해 앉은 뒤 놓는다.
    /// 칸마다 여러 판의 중간값을 표로 남긴다(측정만 — 기능 아님). 쏘기 자체가 나가는지(화살 한 발씩)가 PASS 기준.
    /// </summary>
    static class BirdShootTest
    {
        const int Trials = 6;
        static readonly float[] Distances = { 10f, 20f, 30f };

        class Shot
        {
            public bool Fired, Hit, Forced, Tree;
            public float Sat, Dist, Pct;
            public int Feathers;
        }

        public static void Register() => TestRunner.Register("vh_birdshoottest", "땅 앉은 새 명중 셈(낮·밤, 거리별)", () => TestKit.Flat(Run()), 2400f);

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
                        TestRunner.Log($"DIAG {when} {dist:0}m #{i + 1}: 쏨 {(s.Fired ? "예" : "아니오")} · 맞음 {(s.Hit ? "예" : "아니오")} · 스스로 앉은 시간 {s.Sat:0.0}초 · 강제 앉힘 {(s.Forced ? "예" : "아니오")} · 착지 {s.Dist:0.0}m · 당김 {s.Pct:0.00} · 깃털 {s.Feathers}");
                    }
                    int hits = 0, forced = 0, trees = 0;
                    var sats = new List<float>();
                    var feathers = new List<float>();
                    var dists = new List<float>();
                    foreach (var s in shots)
                    {
                        if (s.Hit) hits++;
                        if (s.Forced) forced++;
                        if (s.Tree) trees++;
                        sats.Add(s.Sat);
                        feathers.Add(s.Feathers);
                        dists.Add(s.Dist);
                    }
                    TestRunner.Log($"표 {when} {dist:0}m: 쏨 {shots.FindAll(s => s.Fired).Count}/{Trials} · 맞음 {hits}/{Trials} · 착지 중간 {Median(dists):0.0}m · 스스로 앉은 시간 중간 {Median(sats):0.0}초 · 강제 앉힘 {forced}/{Trials} · 나무에 막힘 {trees}/{Trials} · 깃털 중간 {Median(feathers):0}");
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
            // 떨굼 원인 진단(회차 551): 떨굼 부품이 있는지, 이 판에서 새를 내가 가진(ZDO 주인) 것인지
            var dropPart = bird.GetComponent<DropOnDestroyed>();
            TestRunner.Log($"DIAG 새 부품: 떨굼 부품 {(dropPart != null)} · 떨굼 표 {(dropPart != null && dropPart.m_dropWhenDestroyed != null)} · 내 것 {bird.GetComponent<ZNetView>()?.IsOwner()}");

            // 최대 8초 안에 다 당긴다
            // sitSince = 스스로 앉은 것을 처음 본 시각(날면 −1로 되돌림) — 앉은 시간은 이 값부터 잰다
            float t = 0f, pct = 0f, sitSince = -1f;
            while (bird != null && npc.HoldingBow() && t < 8f)
            {
                npc.Ai.Face(bird.transform.position);
                pct = npc.DrawBow(Time.deltaTime);
                if (IsLanded(bird)) { if (sitSince < 0f) sitSince = Time.time; }
                else sitSince = -1f;
                t += Time.deltaTime;
                if (pct >= 1f) break;
                yield return null;
            }

            // 다 당긴 채 스스로 앉기를 최대 20초 기다린다(회차 550: 강제 앉힘 없이 앉은 새만 쏘려고). 앉으면 바로 놓는다.
            // 안 앉으면 아래에서 강제 앉힘으로 넘어간다(Forced 표시).
            float waitSit = 0f;
            while (bird != null && npc.HoldingBow() && !IsLanded(bird) && waitSit < 20f)
            {
                npc.Ai.Face(bird.transform.position);
                pct = npc.DrawBow(Time.deltaTime);
                waitSit += Time.deltaTime;
                yield return null;
            }
            if (bird != null && IsLanded(bird) && sitSince < 0f) sitSince = Time.time;

            // 스스로 앉았는지(강제 전) — 아니면 강제 앉힘(시험이 자리에 앉힘, 앉은 시간 0)
            bool natural = bird != null && IsLanded(bird);
            Vector3 landAt = at;
            float landTime = 0f;
            if (bird != null)
            {
                var nv = bird.GetComponent<ZNetView>();
                if (nv != null && nv.GetZDO() != null) nv.GetZDO().Set("landed", true);
                bird.transform.position = landAt;
                landTime = Time.time;
            }
            s.Pct = pct;
            s.Dist = Vector3.Distance(npc.GetEyePoint(), landAt);
            s.Sat = natural && sitSince >= 0f ? Time.time - sitSince : 0f;
            s.Forced = !natural;
            int feathersBefore = GatherTest.GroundCount(landAt, "Feathers");
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
            // 스스로 앉은 새면 앉은 뒤 놓을 때까지의 시간(회차 550: 앉은 시간을 놓는 순간에 잼)
            if (natural && sitSince >= 0f) s.Sat = Time.time - sitSince;
            // 앉힌 뒤 놓기까지 걸린 시간과 그때 새가 아직 앉아 있는지(이륙은 landed 가지에서 m_landedTimer > 2초)
            TestRunner.Log($"DIAG 놓기 {dist:0}m: 앉힌 뒤 {Time.time - landTime:0.00}초 · 놓을 때 앉음 {(bird != null && IsLanded(bird))} · 겨눔 기다림 {aimWait:0.00}초 · 새 있음 {bird != null}");
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
                            : $"{c.name}(위 {c.transform.root.name}, 층 {LayerMask.LayerToName(c.gameObject.layer)}) {Utils.DistanceXZ(eye, p):0.0}m 땅 위 {p.y - GatherTest.Ground(p).y:0.00}m";
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
            // 화살이 새 앞의 나무(Lod0)에 막혔는지 따로 셈 — 빗나감 원인 가르기(시험 자리 문제)
            s.Tree = bird != null && hitWhat.StartsWith("Lod0");
            s.Feathers = GatherTest.GroundCount(landAt, "Feathers") - feathersBefore;
            TestRunner.Log($"DIAG 떨굼 {dist:0}m: 죽음 {s.Hit} · 깃털 셈 {s.Feathers} · 자리 10m 안 물건 {NearItems(landAt)}");
            if (bird != null) ZNetScene.instance.Destroy(bird.gameObject);
        }

        // 자리 10m 안 물건 이름(떨굼이 어디 떨어졌는지 봄 — 깃털 셈 자리·반경이 맞는지 확인용)
        static string NearItems(Vector3 center)
        {
            var names = new List<string>();
            foreach (var zdo in Senses.Nearby(center, 10f))
            {
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                if (v != null) names.Add(Utils.GetPrefabName(v.gameObject));
            }
            return names.Count == 0 ? "없음" : string.Join(", ", names);
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
