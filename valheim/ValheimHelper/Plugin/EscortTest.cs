using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_escorttest (동행은 일꾼끼리만 — 사용자 결정 2026-10-26): 아는 구리 자리가 죽은 기록이 있는 먼 곳뿐이면(덜 위험한 곳 없음)
    /// 묻지 않고 플로키와 같이 감 → 라그나는 플로키(60m 뒤)가 8m 안에 올 때까지 출발 안 함 → 오면 앞장섬.
    /// 플로키가 일하는 중이어도 그 일은 대기열 맨 앞에 두고 따라옴. 일하는 중 "!같이 가자" → 플로키가 와서 붙음.
    /// 동료가 곁에 있으면 경계가 그 힘을 셈(회색난쟁이 우두머리). 기다림 한도·포기는 Core EscortTests.
    /// </summary>
    static class EscortTest
    {
        public static void Register() => TestRunner.Register("vh_escorttest", "일꾼끼리 동행 시험", () => TestKit.Flat(Run()));

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
                CleanUp();
                TestKit.Restore(w);
            }
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static bool Together(WorkerNpc lead, WorkerNpc mate) =>
            lead.Ai.Current == WorkerAI.Mode.Work && lead.Ai.EscortName == mate.NpcName
            && mate.Ai.Current == WorkerAI.Mode.Follow && mate.Ai.FollowTarget == lead.GetZDOID();

        static IEnumerator StopBoth(TestKit.World w)
        {
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            foreach (var n in new[] { w.Ragnar, w.Floki }) { n.Ai.Interrupt(); n.Ai.Queue.Clear(); n.Ai.GoHome(false); }
            yield return new WaitForSeconds(1f);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 둘·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            WorkerNpc npc = w.Ragnar, mate = w.Floki;
            WorkerAI ai = npc.Ai;
            ZoneXY hz = Senses.ZoneOf(w.Home);
            TestKit.ClearBaseStock(w, "CopperOre", "Wood", "MeadHealthMinor", "MeadHealthMedium");

            // 먼 곳(3구역 동쪽)의 구리만 알고, 거기서 죽은 기록 → 덜 위험한 곳이 없으니 둘이 같이
            var far = new ZoneXY(hz.X + 3, hz.Y);
            Brain.ReplaceForTest("", "");
            Brain.Get().Saw(new Dictionary<ZoneXY, Dictionary<string, int>> { [far] = new Dictionary<string, int> { ["CopperOre"] = 5 } },
                new List<ZoneXY> { far }, ZNet.instance.GetTimeSeconds());
            Brain.Places.Record(far, new List<GroupFoe> { new GroupFoe { Kind = "Greydwarf", Count = 1 } }, Outcome.Died, ZNet.instance.GetTimeSeconds(), Guard.Resolve, "Greydwarf");
            foreach (var m in Brain.Get().ZonesWith("CopperOre", hz)) if (!m.Zone.Equals(far)) Brain.Get().Forget(m.Zone, "CopperOre");

            // 1) 플로키는 거점 뒤 60m(앞장서기 전 기다림을 보려고)
            Vector3 back = -(Senses.ZoneCenter(far) - w.Home); back.y = 0f; back.Normalize();
            mate.TeleportTo(GatherTest.Ground(w.Home + back * 60f) + Vector3.up * 0.3f, mate.transform.rotation, false);
            yield return new WaitForSeconds(1.5f);
            float md = Vector3.Distance(mate.transform.position, npc.transform.position);
            checks.Add($"플로키가 떨어짐 ({md:0}m)", md > 40f);
            DepartureResult judged = Departures.JudgeFor(npc, new Core.Chat.ChatCommand { Kind = Core.Chat.CommandKind.Item, Item = "CopperOre", Amount = 10 });
            checks.Add($"판단: 묻지 않고 동료와 같이 ({judged?.Action}, {judged?.Reason})", judged != null && judged.Action == DepartAction.GoTogether && judged.Question == Question.None);
            GatherTest.Say($"!{npc.NpcName} 구리 10", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"질문 없이 일 + 플로키 동행 ({ai.Current}, {ai.EscortName ?? "동행 없음"}, {ai.EscortPhase}, 플로키 {mate.Ai.Current})",
                Departures.AskedAt(npc) < 0 && Together(npc, mate) && ai.EscortPhase == Escort.Phase.Waiting);
            Vector3 start = npc.transform.position;
            yield return new WaitForSeconds(3f);
            float moved = Utils.DistanceXZ(start, npc.transform.position);
            md = Vector3.Distance(mate.transform.position, npc.transform.position);
            checks.Add($"플로키가 멀리 있는 동안 출발 안 함 ({moved:0.0}m 움직임, 플로키 {md:0}m, {ai.EscortPhase})",
                moved < 2f && (ai.EscortPhase == Escort.Phase.Waiting || md <= Escort.Near + 1f));
            yield return TestKit.WaitFor(() => ai.EscortPhase == Escort.Phase.Leading, 60f);
            checks.Add($"플로키가 곁에 오면 앞장섬 ({TestKit.Waited:0.0}초, {ai.EscortPhase})", TestKit.Ok);
            start = npc.transform.position;
            yield return new WaitForSeconds(5f);
            md = Vector3.Distance(mate.transform.position, npc.transform.position);
            checks.Add($"이어 가며 움직이고 플로키가 따라옴 ({Utils.DistanceXZ(start, npc.transform.position):0.0}m, 플로키 {md:0}m)",
                Utils.DistanceXZ(start, npc.transform.position) > 2f && md < Escort.Far + 5f);

            // 2) 플로키가 일하는 중이어도: 하던 일은 대기열 맨 앞, 따라옴
            yield return StopBoth(w);
            GatherTest.Say($"!{mate.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() => mate.Ai.Current == WorkerAI.Mode.Work && mate.Ai.Job != null, 10f);
            string mateJob = mate.Ai.Job?.Label;
            GatherTest.Say($"!{npc.NpcName} 구리 10", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"일하던 플로키도 와서 같이 ({mateJob} → {mate.Ai.Current}, 대기열 {mate.Ai.Queue.Count}: {(mate.Ai.Queue.Count > 0 ? mate.Ai.Queue[0].Label : "")})",
                Together(npc, mate) && mate.Ai.Queue.Count > 0 && mate.Ai.Queue[0].Label == mateJob);

            // 3) 일하는 중 "!같이 가자": 플로키가 와서 지금 일에 붙음
            yield return StopBoth(w);
            GatherTest.Say($"!{npc.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() => ai.Current == WorkerAI.Mode.Work && ai.Job != null, 10f);
            IWorkerJob job = ai.Job;
            checks.Add($"안전한 나무는 혼자 ({ai.EscortName ?? "동행 없음"})", ai.EscortName == null);
            GatherTest.Say($"!{npc.NpcName} 같이 가자", null);
            yield return new WaitForSeconds(1f);
            string state = Commands.StateOf(npc);
            checks.Add($"일하는 중 \"!같이 가자\" → 같은 일 + 플로키 동행 ({ai.Current}, {ai.EscortName ?? "동행 없음"})", ai.Job == job && Together(npc, mate));
            checks.Add($"!상태에 동행 이름 ({state})", state.Contains(mate.NpcName));
            yield return TestKit.WaitFor(() => ai.EscortPhase == Escort.Phase.Leading, 30f);

            // 4) 동료가 곁에 있으면 경계가 그 힘을 셈(MyHit 20m)
            TestKit.Strict = true; // 싸움 판정을 몇 초 안에 보는 칸 — 시간 안 멈춤
            GameObject brute = ZNetScene.instance.GetPrefab("Greydwarf_Elite");
            checks.Add("적 프리팹 있음", brute != null);
            if (brute == null) yield break;
            Vector3 fwd = npc.transform.forward; fwd.y = 0f; fwd.Normalize();
            s_foe = Object.Instantiate(brute, GatherTest.Ground(npc.transform.position + fwd * 14f) + Vector3.up * 0.5f, Quaternion.identity);
            Character foe = s_foe.GetComponent<Character>();
            foe.SetLevel(1);
            if (foe.GetBaseAI() is BaseAI fai) fai.enabled = false; // 판단만 잰다
            Guard.Learn(foe);
            Guard g = ai.Guard;
            yield return TestKit.WaitFor(() => g != null && g.LastDetail.Contains("Greydwarf_Elite"), 5f);
            string withD = g?.LastDetail ?? "";
            float withHit = GetHit(withD);
            md = Vector3.Distance(mate.transform.position, npc.transform.position);
            TestRunner.Log($"동료 곁({md:0}m): {g?.LastVerdict} 비율 {g?.LastRatio:0.00} ({withD})");
            checks.Add($"동료 곁에선 동료 힘까지 셈 (내 한 방 {withHit:0.0} > 혼자 {Guard.HitOf(npc):0.0}, 플로키 {md:0}m)", md > Guard.AllyRange || withHit > Guard.HitOf(npc) + 1f);
        }

        /// <summary>Guard.LastDetail의 "내 한 방 N".</summary>
        static float GetHit(string d)
        {
            int i = d.IndexOf("내 한 방 ");
            if (i < 0) return -1f;
            i += 6;
            int j = d.IndexOf(',', i);
            return j > i && float.TryParse(d.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : -1f;
        }

        static GameObject s_foe;

        static void CleanUp()
        {
            if (s_foe != null && s_foe.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
            s_foe = null;
        }
    }
}
