using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_portaltest (⑤ 포탈): 포탈 한 쌍(입구 가까이, 출구 500m 밖)을 세워 ZDO 짝을 잇고,
    /// Portals.Linked가 짝 출구를 바닐라와 같게 읽는지, 출구 쪽 목적지에는 포탈을 고르고 가까운 곳에는 걷는지,
    /// 일꾼이 TeleportTo(출구)로 실제 건너가는지 본다.
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
            checks.Add("깃발·NPC", w.Claude != null);
            GameObject prefab = ZNetScene.instance.GetPrefab(Portals.PrefabName);
            checks.Add("portal_wood 프리팹 있음", prefab != null);
            if (w.Claude == null || prefab == null) yield break;

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

            Vector3 npcPos = w.Claude.transform.position;
            Vector3 dest = exit + farRot * Vector3.forward * 20f;
            checks.Add("먼 목적지는 포탈을 고름", Portals.TryChoose(npcPos, dest, out PortalLink chosen) && Mathf.Abs(chosen.X - gatePos.x) < 0.5f);
            checks.Add("가까운 목적지는 걸음", !Portals.TryChoose(npcPos, npcPos + rot * new Vector3(0f, 0f, 30f), out _));

            Vector3 before = w.Claude.transform.position;
            w.Claude.TeleportTo(exit, w.Claude.transform.rotation, false);
            yield return new WaitForSeconds(3f);
            float moved = Vector3.Distance(before, w.Claude.transform.position);
            float toExit = Utils.DistanceXZ(exit, w.Claude.transform.position);
            checks.Add($"일꾼이 출구로 건너감 (이동 {moved:0}m, 출구와 {toExit:0.0}m)", moved > 400f && toExit < 6f);
            w.Claude.TeleportTo(w.Home, w.Claude.transform.rotation, false);
            yield return new WaitForSeconds(2f);
        }
    }
}
