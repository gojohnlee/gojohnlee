using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_portaltest (⑤ 포탈): 포탈 한 쌍(입구 가까이, 출구 500m 밖)을 세워 ZDO 짝을 잇고,
    /// Portals.Linked가 짝 출구를 바닐라와 같게 읽는지, 출구 쪽 목적지에는 포탈을 고르고 가까운 곳에는 걷는지,
    /// 일꾼이 TeleportTo(출구)로 실제 건너가는지(구리 광석을 든 채로, R2 다) 본다.
    /// </summary>
    static class PortalTest
    {
        public static void Register() => TestRunner.Register("vh_portaltest", "포탈 시험", () => TestKit.Flat(Run()));

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
            var zdos = new List<ZDO>();
            try { yield return Body(w, checks, spawned, zdos); }
            finally
            {
                Cleanup(spawned, zdos);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void Cleanup(List<GameObject> spawned, List<ZDO> zdos)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
            foreach (var z in zdos)
                if (z != null && z.IsValid()) ZDOMan.instance.DestroyZDO(z);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned, List<ZDO> zdos)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            GameObject prefab = ZNetScene.instance.GetPrefab(Portals.PrefabName);
            checks.Add("portal_wood 프리팹 있음", prefab != null);
            if (w.Ragnar == null || prefab == null) yield break;
            yield return Sea(w, checks, prefab, spawned, zdos); // 마) 바다 건너 일터 — 이 시험 포탈을 놓기 전에(그 출구가 바다 건너일 수 있음)

            Quaternion rot = w.Player.transform.rotation;
            Vector3 gatePos = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 25f));
            Vector3 farPos = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 520f));
            Quaternion farRot = Quaternion.Euler(0f, 90f, 0f);
            GameObject a = Object.Instantiate(prefab, gatePos, rot);
            ZDO zb = ZDOMan.instance.CreateNewZDO(farPos, Portals.PrefabName.GetStableHashCode()); // 먼 구역은 안 불러와져 ZDO만
            zb.SetRotation(farRot);
            spawned.Add(a);
            yield return null;
            ZDO za = a.GetComponent<ZNetView>().GetZDO();
            checks.Add($"포탈 둘 생김 (입구 {za != null}, 출구 {zb != null})", za != null && zb != null);
            if (za == null || zb == null) yield break;
            zdos.Add(za);
            zdos.Add(zb);
            za.Set("tag", "vhtest");
            zb.Set("tag", "vhtest");
            za.SetConnection(ZDOExtraData.ConnectionType.Portal, zb.m_uid);
            zb.SetConnection(ZDOExtraData.ConnectionType.Portal, za.m_uid);

            var all = Portals.Linked();
            PortalLink? mine = null;
            foreach (var l in all)
                if (Mathf.Abs(l.X - gatePos.x) < 0.5f && Mathf.Abs(l.Z - gatePos.z) < 0.5f) mine = l;
            checks.Add($"Linked가 입구 포탈을 읽음 (전체 {all.Count}개)", mine.HasValue);
            if (!mine.HasValue) yield break;
            Vector3 expect = farPos + farRot * Vector3.forward + Vector3.up;
            var link = mine.Value;
            var exit = new Vector3(link.ExitX, link.ExitY, link.ExitZ);
            checks.Add($"출구 = 짝 앞 1m·위 1m (차이 {Vector3.Distance(exit, expect):0.00}m)", Vector3.Distance(exit, expect) < 0.5f);

            Vector3 npcPos = w.Ragnar.transform.position;
            Vector3 dest = exit + farRot * Vector3.forward * 20f;
            checks.Add("먼 목적지는 포탈을 고름", Portals.TryChoose(npcPos, dest, out PortalLink chosen) && Mathf.Abs(chosen.X - gatePos.x) < 0.5f);
            checks.Add("가까운 목적지는 걸음", !Portals.TryChoose(npcPos, npcPos + rot * new Vector3(0f, 0f, 30f), out _));

            // R2 다) 바닐라 포탈은 광석·금속을 막지만(Player.IsTeleportable) 일꾼은 TeleportTo라 든 채로 건너야 함
            var inv = w.Ragnar.GetInventory();
            GameObject orePrefab = ObjectDB.instance.GetItemPrefab("CopperOre");
            string oreName = orePrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            inv.AddItem(orePrefab, 5);
            int oreBefore = inv.CountItems(oreName);
            Vector3 before = w.Ragnar.transform.position;
            w.Ragnar.TeleportTo(exit, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(3f);
            int oreAfter = inv.CountItems(oreName);
            checks.Add($"구리 광석 들고 건넘 (전 {oreBefore}, 뒤 {oreAfter})", oreBefore >= 5 && oreAfter == oreBefore);
            inv.RemoveItem(oreName, oreAfter);
            float moved = Vector3.Distance(before, w.Ragnar.transform.position);
            float toExit = Utils.DistanceXZ(exit, w.Ragnar.transform.position);
            checks.Add($"일꾼이 출구로 건너감 (이동 {moved:0}m, 출구와 {toExit:0.0}m)", moved > 400f && toExit < 6f);
            w.Ragnar.TeleportTo(w.Home, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(2f);
        }

        /// <summary>
        /// 마) 바다 건너 일터는 포탈로만(사용자 결정 2026-10-23): 거점에서 곧게 바다를 건너야 닿는 땅을 찾아 그 구역에 클라우드베리가 있다고 알려 준 뒤
        /// 1) 이어진 포탈 없음 → 그 구역을 안 고르고 "바다 건너" 말 2) 거점 20m 앞 → 그 땅에 이어진 포탈 → 그 구역을 골라 포탈로 건너감.
        /// </summary>
        static IEnumerator Sea(TestKit.World w, GatherTest.Checks checks, GameObject prefab, List<GameObject> spawned, List<ZDO> zdos)
        {
            const string Item = "Cloudberry";
            Vector3 home = w.Home, land = Vector3.zero;
            bool found = false;
            for (float d = 150f; d <= 2000f && !found; d += 25f)
                for (int i = 0; i < 16 && !found; i++)
                {
                    Vector3 p = Senses.ZoneCenter(Senses.ZoneOf(home + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * d)); // 구역 중심이 땅(Reachable은 중심까지 봄)
                    p.y = WorldGenerator.instance.GetHeight(p.x, p.z);
                    if (p.y < ZoneSystem.instance.m_waterLevel + 3f || WorldGenerator.instance.GetBiome(p.x, p.z) == Heightmap.Biome.Ocean) continue;
                    if (Scout.WalkFar(home.x, home.z, p.x, p.z) || !Scout.OceanBetween(home, p)) continue;
                    land = p;
                    found = true;
                }
            if (!found) { TestRunner.Log("바다 건너 칸: 2km 안에 바다 건너 땅 없음 → 건너뜀(시험 월드 탓)"); checks.Add("바다 건너 땅 찾음(2km 안)", false); yield break; }
            ZoneXY zone = Senses.ZoneOf(land);
            Vector3 center = Senses.ZoneCenter(zone);
            TestRunner.Log($"바다 건너 땅: 거점에서 {Utils.DistanceXZ(home, land):0}m ({land.x:0},{land.z:0}), 바이옴 {WorldGenerator.instance.GetBiome(land.x, land.z)}, 구역 중심 걷기 {Scout.WalkFar(home.x, home.z, center.x, center.z)}");
            Knowledge k = Brain.Get();
            foreach (var m in k.ZonesWith(Item, zone)) k.Forget(m.Zone, Item);
            k.Saw(new Dictionary<ZoneXY, Dictionary<string, int>> { [zone] = new Dictionary<string, int> { [Item] = 5 } }, new List<ZoneXY>(), ZNet.instance.GetTimeSeconds());
            WorkerNpc npc = w.Ragnar;
            var ways = new HashSet<ToolKind> { ToolKind.None }; // 맨손(클라우드베리는 바위·나무 목록에 없어 WaysFor가 비어 noTool)
            try
            {
                // 1) 포탈 없음
                checks.Add("포탈 없으면 바다 건너 구역은 못 감(Reachable)", !Scout.Reachable(npc.transform.position, zone));
                int sea0 = GatherJob.SeaSkips, heard0 = w.Heard.Count;
                var job = new GatherJob(npc, Item, 3, ways);
                npc.Ai.StartJob(job);
                float t = 0f;
                for (; t < 15f && GatherJob.SeaSkips == sea0; t += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(1f);
                bool said = false;
                for (int i = heard0; i < w.Heard.Count; i++) if (w.Heard[i].Contains("바다")) said = true;
                checks.Add($"포탈 없으면 안 가고 \"바다 건너\" 말({t:0.0}초, 고른 구역 {job.GoalZone.X},{job.GoalZone.Y}, 말 {said})", GatherJob.SeaSkips == sea0 + 1 && said && !job.GoalZone.Equals(zone));
                npc.Ai.GoHome(false);
                yield return new WaitForSeconds(1f);
                npc.TeleportTo(home, npc.transform.rotation, false);
                yield return new WaitForSeconds(2f);

                // 2) 바다 건너 땅에 이어진 포탈
                Quaternion rot = w.Player.transform.rotation;
                Vector3 gate = GatherTest.Ground(home + rot * new Vector3(0f, 0f, 20f));
                GameObject a = Object.Instantiate(prefab, gate, rot);
                spawned.Add(a);
                ZDO zb = ZDOMan.instance.CreateNewZDO(land, Portals.PrefabName.GetStableHashCode());
                yield return null;
                ZDO za = a.GetComponent<ZNetView>().GetZDO();
                zdos.Add(za);
                zdos.Add(zb);
                za.Set("tag", "vhsea");
                zb.Set("tag", "vhsea");
                za.SetConnection(ZDOExtraData.ConnectionType.Portal, zb.m_uid);
                zb.SetConnection(ZDOExtraData.ConnectionType.Portal, za.m_uid);
                yield return new WaitForSeconds(2.5f); // LinkedRecent 2초 캐시
                checks.Add("이어진 포탈이 있으면 바다 건너 구역도 감(Reachable)", Scout.Reachable(npc.transform.position, zone));
                job = new GatherJob(npc, Item, 3, ways);
                npc.Ai.StartJob(job);
                Vector3 start = npc.transform.position;
                for (t = 0f; t < 40f && Utils.DistanceXZ(npc.transform.position, land) > 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                float near = Utils.DistanceXZ(npc.transform.position, land);
                checks.Add($"포탈로 바다 건너 구역에 감({t:0.0}초, 고른 구역 {job.GoalZone.X},{job.GoalZone.Y}, 출구와 {near:0}m, 바다 말 {GatherJob.SeaSkips - sea0 - 1}번 더)",
                    job.GoalZone.Equals(zone) && near <= 15f && GatherJob.SeaSkips == sea0 + 1);
                npc.Ai.GoHome(false);
                yield return new WaitForSeconds(1f);
                npc.TeleportTo(home, npc.transform.rotation, false);
                yield return new WaitForSeconds(3f);
            }
            finally
            {
                k.Forget(zone, Item);
                Cleanup(spawned, zdos); // 이 칸 포탈만 들어 있음(Body가 먼저 부름) — 뒤 칸 포탈 고르기에 안 끼게
                spawned.Clear();
                zdos.Clear();
            }
        }
    }
}
