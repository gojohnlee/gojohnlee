using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_escorttest (호위병 — 사용자 결정 2026-10-26): 아는 구리 자리가 죽은 기록이 있는 먼 곳뿐이면(덜 위험한 곳 없음) 묻지 않고 호위병을 데려감
    /// → 라그나는 호위병(60m 뒤)이 8m 안에 올 때까지 출발 안 함 → 오면 앞장섬 → 라그나만 멀리 건너가면(포탈·던전 문 흉내) 호위병을 곁으로 옮김.
    /// 호위병이 플로키를 따라가는 중이면 라그나는 거점에서 기다렸다가(대기열 "호위병이 쉴 때") 호위병이 쉬게 되면 같이 출발.
    /// 일하는 중 "!같이 가자" → 호위병이 와서 붙음. 호위병이 곁에 있으면 경계가 그 힘을 셈. 기다림 한도·포기는 Core EscortTests.
    /// </summary>
    static class EscortTest
    {
        public static void Register() => TestRunner.Register("vh_escorttest", "호위병 동행 시험", () => TestKit.Flat(Run()));

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

        static bool Together(WorkerNpc lead, WorkerNpc guard) =>
            lead.Ai.Current == WorkerAI.Mode.Work && lead.Ai.EscortName == guard.NpcName
            && guard.Ai.Current == WorkerAI.Mode.Follow && guard.Ai.FollowTarget == lead.GetZDOID();

        static IEnumerator StopAll(TestKit.World w)
        {
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            foreach (var n in new[] { w.Ragnar, w.Floki }) { n.Ai.Interrupt(); n.Ai.Queue.Clear(); n.Ai.GoHome(false); }
            yield return TestKit.WaitFor(() => Departures.GuardIdle(), 30f);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·일꾼 둘·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            yield return TestKit.SpawnGuard();
            WorkerNpc guard = WorkerNpc.TheGuard;
            checks.Add($"호위병 생김 ({guard?.NpcName ?? "없음"}, {TestKit.Waited:0}초)", TestKit.Ok && guard != null && guard.IsGuard && !WorkerNpc.All.Contains(guard));
            if (guard == null) yield break;
            WorkerNpc npc = w.Ragnar;
            WorkerAI ai = npc.Ai;
            ZoneXY hz = Senses.ZoneOf(w.Home);
            TestKit.ClearBaseStock(w, "CopperOre", "Wood", "MeadHealthMinor", "MeadHealthMedium");

            // 먼 곳(3구역 동쪽)의 구리만 알고, 거기서 죽은 기록 → 덜 위험한 곳이 없으니 호위병과
            var far = new ZoneXY(hz.X + 3, hz.Y);
            Brain.ReplaceForTest("", "");
            Brain.Get().Saw(new Dictionary<ZoneXY, Dictionary<string, int>> { [far] = new Dictionary<string, int> { ["CopperOre"] = 5 } },
                new List<ZoneXY> { far }, ZNet.instance.GetTimeSeconds());
            Brain.Places.Record(far, new List<GroupFoe> { new GroupFoe { Kind = "Greydwarf", Count = 1 } }, Outcome.Died, ZNet.instance.GetTimeSeconds(), Guard.Resolve, "Greydwarf");
            foreach (var m in Brain.Get().ZonesWith("CopperOre", hz)) if (!m.Zone.Equals(far)) Brain.Get().Forget(m.Zone, "CopperOre");

            // 1) 호위병은 거점 뒤 60m(앞장서기 전 기다림을 보려고)
            Vector3 back = -(Senses.ZoneCenter(far) - w.Home); back.y = 0f; back.Normalize();
            guard.TeleportTo(GatherTest.Ground(w.Home + back * 60f) + Vector3.up * 0.3f, guard.transform.rotation, false);
            yield return new WaitForSeconds(1.5f);
            float gd = Vector3.Distance(guard.transform.position, npc.transform.position);
            checks.Add($"호위병이 떨어짐 ({gd:0}m)", gd > 40f);
            var copper = new Core.Chat.ChatCommand { Kind = Core.Chat.CommandKind.Item, Item = "CopperOre", Amount = 10 };
            DepartureResult judged = Departures.JudgeFor(npc, copper);
            checks.Add($"판단: 묻지 않고 호위병과 ({judged?.Action}, {judged?.Reason})", judged != null && judged.Action == DepartAction.GoTogether && judged.Question == Question.None);
            GatherTest.Say($"!{npc.NpcName} 구리 10", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"질문 없이 일 + 호위병 동행 ({ai.Current}, {ai.EscortName ?? "동행 없음"}, {ai.EscortPhase}, 호위병 {guard.Ai.Current})",
                Departures.AskedAt(npc) < 0 && Together(npc, guard) && ai.EscortPhase == Escort.Phase.Waiting);
            Vector3 start = npc.transform.position;
            yield return new WaitForSeconds(3f);
            float moved = Utils.DistanceXZ(start, npc.transform.position);
            gd = Vector3.Distance(guard.transform.position, npc.transform.position);
            checks.Add($"호위병이 멀리 있는 동안 출발 안 함 ({moved:0.0}m 움직임, 호위병 {gd:0}m, {ai.EscortPhase})",
                moved < 2f && (ai.EscortPhase == Escort.Phase.Waiting || gd <= Escort.Near + 1f));
            yield return TestKit.WaitFor(() => ai.EscortPhase == Escort.Phase.Leading, 60f);
            checks.Add($"호위병이 곁에 오면 앞장섬 ({TestKit.Waited:0.0}초, {ai.EscortPhase})", TestKit.Ok);

            // 2) 라그나만 멀리 건너감(포탈·던전 문 흉내, 200m 넘게) → 호위병을 곁으로 옮기고 동행은 그대로
            int brings = WorkerAI.MateBrings;
            Vector3 jump = GatherTest.Ground(w.Home - back * 260f) + Vector3.up * 0.3f;
            npc.TeleportTo(jump, npc.transform.rotation, false);
            yield return TestKit.WaitFor(() => WorkerAI.MateBrings > brings && Vector3.Distance(guard.transform.position, npc.transform.position) < 15f, 25f);
            gd = Vector3.Distance(guard.transform.position, npc.transform.position);
            checks.Add($"멀리 건너가도 호위병을 곁으로 옮김 ({TestKit.Waited:0.0}초, 옮김 {WorkerAI.MateBrings - brings}번, 호위병 {gd:0}m, 동행 {ai.EscortName ?? "끊김"})",
                TestKit.Ok && ai.EscortName == guard.NpcName);

            // 3) 호위병이 플로키를 따라가는 중 → 라그나는 거점에서 기다렸다가 호위병이 쉬면 같이 출발
            yield return StopAll(w);
            checks.Add($"멈추면 호위병도 쉼 ({guard.Ai.Current})", Departures.GuardIdle());
            GatherTest.Say($"!{w.Floki.NpcName} 구리 10", null);
            yield return TestKit.WaitFor(() => Together(w.Floki, guard), 5f);
            checks.Add($"플로키가 호위병을 데려감 ({w.Floki.Ai.EscortName ?? "동행 없음"})", TestKit.Ok);
            GatherTest.Say($"!{npc.NpcName} 구리 10", null);
            yield return new WaitForSeconds(1f);
            bool queued = ai.Queue.Exists(q => q.Work != null && q.Work.Ready == ReadyWhen.GuardIdle);
            checks.Add($"호위병이 바쁘면 거점에서 기다림 ({ai.Current}, 대기열 {ai.Queue.Count}, 호위병 기다림 {queued})", queued && ai.Current != WorkerAI.Mode.Work);
            GatherTest.Say($"!{w.Floki.NpcName} 멈춰", null);
            yield return TestKit.WaitFor(() => Together(npc, guard), 40f);
            checks.Add($"호위병이 쉬게 되면 같이 출발 ({TestKit.Waited:0.0}초, {ai.Current}, {ai.EscortName ?? "동행 없음"})", TestKit.Ok);

            // 4) 일하는 중 "!같이 가자": 안전한 나무 일에도 호위병이 와서 붙음
            yield return StopAll(w);
            GatherTest.Say($"!{npc.NpcName} 나무 5", null);
            yield return TestKit.WaitFor(() => ai.Current == WorkerAI.Mode.Work && ai.Job != null, 10f);
            IWorkerJob job = ai.Job;
            checks.Add($"안전한 나무는 혼자 ({ai.EscortName ?? "동행 없음"})", ai.EscortName == null);
            GatherTest.Say($"!{npc.NpcName} 같이 가자", null);
            yield return new WaitForSeconds(1f);
            string state = Commands.StateOf(npc);
            checks.Add($"일하는 중 \"!같이 가자\" → 같은 일 + 호위병 동행 ({ai.Current}, {ai.EscortName ?? "동행 없음"})", ai.Job == job && Together(npc, guard));
            checks.Add($"!상태에 동행 이름 ({state})", state.Contains(guard.NpcName));
            yield return TestKit.WaitFor(() => ai.EscortPhase == Escort.Phase.Leading, 30f);

            // 5) 호위병이 곁에 있으면 경계가 그 힘을 셈(MyHit 20m)
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
            gd = Vector3.Distance(guard.transform.position, npc.transform.position);
            TestRunner.Log($"호위병 곁({gd:0}m): {g?.LastVerdict} 비율 {g?.LastRatio:0.00} ({withD})");
            checks.Add($"호위병 곁에선 그 힘까지 셈 (내 한 방 {withHit:0.0} > 혼자 {Guard.HitOf(npc):0.0}, 호위병 {gd:0}m)", gd > Guard.AllyRange || withHit > Guard.HitOf(npc) + 1f);
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
