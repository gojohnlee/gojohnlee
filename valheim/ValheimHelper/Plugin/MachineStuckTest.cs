using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 설비 접근 막힘 복구 시험(E035, 10-26): "!구리주괴 1" 뒤 제련소로 가는 라그나르 발을 묶어(vh_hoptest처럼 걷기·점프 힘 0) 못 다가가게 한다.
    /// 5초마다 옆·옆·뒤로 돌아 본 뒤(MachineStep.SideTries 3) 제련소 곁 2.5m 안으로 한 번 옮겨(Hops 1, 플레이어 곁이라 "rescue") 넣기를 마치는지.
    /// 발을 풀면 끝까지(상자 주괴 1, 광석 0).
    /// </summary>
    static class MachineStuckTest
    {
        public static void Register() => TestRunner.Register("vh_machinestucktest", "설비 접근 막힘 복구(E035)", () => TestKit.Flat(Run()));

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
            bool running = true;
            try { yield return Body(w, checks, made, saved, c => held = c, () => held, () => running); }
            finally
            {
                running = false;
                if (held != null) HopTest.Speeds(held, saved, false);
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made, float[] saved,
            System.Action<Character> hold, System.Func<Character> held, System.Func<bool> running)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            GameObject sp = ZNetScene.instance.GetPrefab("smelter");
            if (sp == null) { checks.Add("제련소 프리팹 있음", false); yield break; }
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
            TestKit.PutInChest(w.Chest, "CopperOre", 1);
            TestKit.PutInChest(w.Chest, "Coal", 10);
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            var sm = Object.Instantiate(sp, GatherTest.Ground(w.Home - right * 9f), Quaternion.identity);
            made.Add(sm);
            ZDO z = sm.GetComponent<ZNetView>().GetZDO();
            yield return new WaitForSeconds(1f);
            int sides0 = MachineStep.SideTries, hops0 = MachineStep.Hops;
            float Dist() => Utils.DistanceXZ(sm.transform.position, w.Ragnar.transform.position);
            GatherTest.Say($"!{w.Ragnar.NpcName} 구리주괴 1", null);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is PlanJob p && p.StepName == "Load", 10f);
            checks.Add($"제련 계획 넣기 단계 ({w.Ragnar.Ai.Job?.GetType().Name} {w.Ragnar.Ai.Job?.StepName}, 제련소까지 {Dist():0.0}m)", TestKit.Ok && Dist() > 3f);
            if (!TestKit.Ok || Dist() <= 3f) yield break;
            HopTest.Speeds(w.Ragnar, saved, true);
            hold(w.Ragnar);
            Plugin.Instance.StartCoroutine(TestKit.ClearWhileHeld(w, held, running));
            yield return TestKit.WaitFor(() => MachineStep.Hops > hops0, 45f);
            checks.Add($"옆·옆·뒤로 돌아 본 뒤 곁으로 옮김 (돌아 봄 {MachineStep.SideTries - sides0}, 옮김 {MachineStep.Hops - hops0}, {TestKit.Waited:0}초)",
                TestKit.Ok && MachineStep.SideTries - sides0 == 3 && MachineStep.Hops - hops0 == 1);
            checks.Add($"옮긴 자리는 설비 앞 일 거리 안 ({Dist():0.0}m ≤ 2.5)", Dist() <= 2.5f);
            yield return TestKit.WaitFor(() => z.GetInt(ZDOVars.s_queued) > 0 || GatherTest.CountBase(w.Home, "Copper") > 0, 10f);
            checks.Add($"묶인 채로 넣기 마침 (제련소 안 {z.GetInt(ZDOVars.s_queued)}, {TestKit.Waited:0}초)", TestKit.Ok);
            HopTest.Speeds(w.Ragnar, saved, false);
            hold(null);
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "Copper") >= 1 && w.Ragnar.Ai.Job == null, 150f);
            int ore = GatherTest.CountBase(w.Home, "CopperOre") + BaseTest.Carried(w.Ragnar, "CopperOre") + z.GetInt(ZDOVars.s_queued);
            checks.Add($"끝까지: 상자 주괴 {GatherTest.CountBase(w.Home, "Copper")}·광석 남음 {ore} ({TestKit.Waited:0}초, {w.Ragnar.Ai.Current})",
                TestKit.Ok && GatherTest.CountBase(w.Home, "Copper") == 1 && ore == 0);
            checks.Add($"옮김은 한 번뿐 ({MachineStep.Hops - hops0})", MachineStep.Hops - hops0 == 1);
        }
    }
}
