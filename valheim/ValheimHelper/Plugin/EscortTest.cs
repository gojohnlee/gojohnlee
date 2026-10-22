using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_escorttest (C 동행 앞장서기, 구상서 8-1): 먼 곳(192m)에서 죽은 기록이 있는 구리를 시키고 "같이 가자"로 대답하면
    /// 플레이어가 30m 밖에 있는 동안 출발하지 않고 기다림 → 옆에 오면 앞장섬 → 플레이어가 서 있어 20m 넘게 벌어지면 멈춰 기다림
    /// → 다시 옆에 오면 이어 감. 기다림 한도·포기는 Core EscortTests.
    /// </summary>
    static class EscortTest
    {
        public static void Register() => TestRunner.Register("vh_escorttest", "동행 앞장서기 시험", () => TestKit.Flat(Run()));

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
            Departures.TestAutoGo = false;
            Features.OverrideKnowExplored(false);
            try { yield return Body(w, checks); }
            finally
            {
                Departures.TestAutoGo = true;
                Features.OverrideKnowExplored(null);
                GatherTest.Say("!멈춰", null);
                TestKit.Restore(w);
            }
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator MovePlayer(Player p, Vector3 to)
        {
            to = GatherTest.Ground(to) + Vector3.up * 0.3f;
            p.TeleportTo(to, p.transform.rotation, false);
            float t = 0f;
            while (t < 10f && (p.IsTeleporting() || Vector3.Distance(p.transform.position, to) > 3f)) { yield return new WaitForSeconds(0.5f); t += 0.5f; }
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
            WorkerNpc npc = w.Claude;
            WorkerAI ai = npc.Ai;
            Player player = w.Player;
            ZoneXY hz = Senses.ZoneOf(w.Home);
            TestKit.ClearBaseStock(w, "CopperOre", "MeadHealthMinor", "MeadHealthMedium");

            // 먼 곳(3구역 동쪽)의 구리만 알고, 거기서 죽은 기록 → 죽은 곳 질문
            var far = new ZoneXY(hz.X + 3, hz.Y);
            Brain.ReplaceForTest("", "");
            Brain.Get().Saw(new Dictionary<ZoneXY, Dictionary<string, int>> { [far] = new Dictionary<string, int> { ["CopperOre"] = 5 } },
                new List<ZoneXY> { far }, ZNet.instance.GetTimeSeconds());
            Brain.Places.Record(far, new List<GroupFoe> { new GroupFoe { Kind = "Greydwarf", Count = 1 } }, Outcome.Died, ZNet.instance.GetTimeSeconds(), Guard.Resolve, "Greydwarf");

            // 플레이어는 거점 뒤 30m로(앞장서기 전 기다림을 보려고)
            Vector3 back = -(Senses.ZoneCenter(far) - w.Home); back.y = 0f; back.Normalize();
            yield return MovePlayer(player, w.Home + back * 30f);
            float pd = Vector3.Distance(player.transform.position, npc.transform.position);
            checks.Add($"플레이어가 일꾼에게서 떨어짐 ({pd:0}m)", pd > Escort.Near + 2f);

            foreach (var m in Brain.Get().ZonesWith("CopperOre", hz)) if (!m.Zone.Equals(far)) Brain.Get().Forget(m.Zone, "CopperOre");
            GatherTest.Say($"!{npc.NpcName} 구리 10", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"죽은 곳 질문 ({(Departures.AskedAt(npc) >= 0 ? "기다림" : "안 물음")})", Departures.AskedAt(npc) >= 0);
            GatherTest.Say("같이 가자", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"\"같이 가자\" → 일 + 동행 ({ai.Current}, {ai.EscortName ?? "동행 없음"}, {ai.EscortPhase})",
                ai.Current == WorkerAI.Mode.Work && ai.EscortName == player.GetPlayerName() && ai.EscortPhase == Escort.Phase.Waiting);

            Vector3 start = npc.transform.position;
            yield return new WaitForSeconds(5f);
            float moved = Utils.DistanceXZ(start, npc.transform.position);
            checks.Add($"멀리 있는 동안 출발 안 함 ({moved:0.0}m 움직임, {ai.EscortPhase})", moved < 2f && ai.EscortPhase == Escort.Phase.Waiting);

            yield return MovePlayer(player, npc.transform.position - back * 2f);
            yield return TestKit.WaitFor(() => ai.EscortPhase == Escort.Phase.Leading, 3f);
            checks.Add($"옆에 오면 앞장섬 ({TestKit.Waited:0.0}초, {ai.EscortPhase})", TestKit.Ok);

            // 플레이어가 서 있으면 일꾼이 가다가 20m 넘어 멈춤(준비·상자 들르기가 길 수 있어 넉넉히)
            yield return TestKit.WaitFor(() => ai.EscortPhase == Escort.Phase.Paused || ai.Current != WorkerAI.Mode.Work, 120f);
            pd = Vector3.Distance(player.transform.position, npc.transform.position);
            checks.Add($"20m 넘게 벌어지면 멈춤 ({TestKit.Waited:0}초, {pd:0}m, {ai.EscortPhase}, {ai.Job?.StepName})", ai.EscortPhase == Escort.Phase.Paused);
            if (ai.EscortPhase != Escort.Phase.Paused) yield break;
            yield return new WaitForSeconds(1f);
            start = npc.transform.position;
            yield return new WaitForSeconds(4f);
            moved = Utils.DistanceXZ(start, npc.transform.position);
            checks.Add($"멈춘 채 기다림 ({moved:0.0}m 움직임, {ai.EscortPhase})", moved < 1.5f && ai.EscortPhase == Escort.Phase.Paused);

            yield return MovePlayer(player, npc.transform.position - back * 2f);
            yield return TestKit.WaitFor(() => ai.EscortPhase == Escort.Phase.Leading, 3f);
            checks.Add($"다시 옆에 오면 이어 감 ({TestKit.Waited:0.0}초, {ai.EscortPhase})", TestKit.Ok);
            start = npc.transform.position;
            yield return new WaitForSeconds(4f);
            moved = Utils.DistanceXZ(start, npc.transform.position);
            checks.Add($"이어 가며 움직임 ({moved:0.0}m)", moved > 2f);
        }
    }
}
