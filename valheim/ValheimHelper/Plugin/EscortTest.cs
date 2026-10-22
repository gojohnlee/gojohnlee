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
    /// → 다시 옆에 오면 이어 감. 동행이 곁에 있으면 경계가 그 힘을 세고, 떠나면 혼자 힘으로 다시 판단해 물러남(회색난쟁이 우두머리). 기다림 한도·포기는 Core EscortTests.
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
                CleanUp();
                TestKit.Restore(w);
            }
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator MovePlayer(Player p, Vector3 to)
        {
            to = GatherTest.Ground(to) + Vector3.up * 0.3f;
            float t = 0f;
            while (!p.TeleportTo(to, p.transform.rotation, false) && t < 5f) { yield return new WaitForSeconds(0.25f); t += 0.25f; } // 순간이동 쿨다운 2초(회차 89)
            t = 0f;
            while (t < 10f && (p.IsTeleporting() || Vector3.Distance(p.transform.position, to) > 3f)) { yield return new WaitForSeconds(0.5f); t += 0.5f; }
        }

        /// <summary>플레이어를 일꾼 옆으로 옮기고 앞장서기를 기다림. 그새 일꾼이 움직였으면(자연 멧돼지와 싸움 등 — 회차 89) 다시 옆으로. TestKit.Waited·Ok를 남김.</summary>
        static IEnumerator ComeBeside(Player p, WorkerNpc npc, Vector3 back)
        {
            float t0 = Time.time;
            for (int i = 0; i < 6 && npc.Ai.EscortPhase != Escort.Phase.Leading; i++)
            {
                yield return MovePlayer(p, npc.transform.position - back * 2f);
                yield return TestKit.WaitFor(() => npc.Ai.EscortPhase == Escort.Phase.Leading, 3f);
            }
            yield return TestKit.WaitFor(() => npc.Ai.EscortPhase == Escort.Phase.Leading, 0f);
            TestRunner.Log($"옆으로 옮겨 앞장서기까지 {Time.time - t0:0.0}초");
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

            yield return ComeBeside(player, npc, back);
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

            yield return ComeBeside(player, npc, back);
            checks.Add($"다시 옆에 오면 이어 감 ({TestKit.Waited:0.0}초, {ai.EscortPhase})", TestKit.Ok);
            start = npc.transform.position;
            yield return new WaitForSeconds(4f);
            moved = Utils.DistanceXZ(start, npc.transform.position);
            checks.Add($"이어 가며 움직임 ({moved:0.0}m)", moved > 2f);

            // 일하는 중 "!같이 가자": 따라가기로 일을 버리지 않고 지금 일에 동행을 다시 붙임(쉬는 중 따라가기는 vh_npctest)
            IWorkerJob job = ai.Job;
            GatherTest.Say($"!{npc.NpcName} 같이 가자", null);
            yield return new WaitForSeconds(1f);
            string state = Commands.StateOf(npc);
            checks.Add($"일하는 중 \"!같이 가자\" → 같은 일 + 동행 ({ai.Current}, {ai.EscortName ?? "동행 없음"}, {ai.EscortPhase})",
                ai.Current == WorkerAI.Mode.Work && ai.Job == job && ai.EscortName == player.GetPlayerName());
            checks.Add($"!상태에 동행 이름 ({state})", state.Contains(player.GetPlayerName()));

            // 위험하면 물러남(구상서 8-1): 동행이 곁에 있으면 Guard가 그 힘을 셈(MyHit 20m) → 동행이 멀어지면 혼자 힘으로 다시 판단해 물러남·도망
            ItemDrop.ItemData sw = player.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("SwordIron"), 1) ? player.GetInventory().GetItem("$item_sword_iron") : null;
            s_sword = sw;
            if (sw != null) player.EquipItem(sw, false);
            yield return MovePlayer(player, npc.transform.position + back * 2f);
            GameObject brute = ZNetScene.instance.GetPrefab("Greydwarf_Elite");
            checks.Add("적 프리팹 있음", brute != null && sw != null);
            if (brute == null) yield break;
            Vector3 fwd = Senses.ZoneCenter(far) - npc.transform.position; fwd.y = 0f; fwd.Normalize();
            s_foe = Object.Instantiate(brute, GatherTest.Ground(npc.transform.position + fwd * 14f) + Vector3.up * 0.5f, Quaternion.identity);
            Character foe = s_foe.GetComponent<Character>();
            foe.SetLevel(1);
            if (foe.GetBaseAI() is BaseAI fai) fai.enabled = false; // 시험 플레이어는 안 싸워서, 적이 움직이면 순간이동 동안 일꾼이 혼자 맞아 죽음(회차 89) → 판단만 잰다
            Guard.Learn(foe); // 짐작이 아니라 실제 한 방으로 판단하게(짐작이 낮으면 동행 없이 붙었다가 죽음 — 회차 89 첫 실행)
            Guard g = ai.Guard;
            int deaths = npc.Deaths;
            yield return TestKit.WaitFor(() => g != null && g.LastDetail.Contains("Greydwarf_Elite"), 5f);
            float withR = g != null ? g.LastRatio : -1f;
            string withD = g?.LastDetail ?? "";
            float withHit = GetHit(withD);
            TestRunner.Log($"동행 곁: {g?.LastVerdict} 비율 {withR:0.00} ({withD})");
            checks.Add($"동행 곁에선 동행 힘까지 셈 (내 한 방 {withHit:0.0})", withHit > Guard.HitOf(npc) + 1f);
            // 적이 닿기 전에 동행이 40m 뒤로 → 앞장서기는 멈추고, 경계는 혼자 힘으로 다시 판단
            yield return MovePlayer(player, npc.transform.position + back * 40f);
            yield return TestKit.WaitFor(() => g.LastDetail.Contains("Greydwarf_Elite") && GetHit(g.LastDetail) < withHit - 1f || npc.Deaths != deaths, 10f);
            float aloneR = g.LastRatio;
            Verdict aloneV = g.LastVerdict;
            TestRunner.Log($"동행 떠남: {aloneV} 비율 {aloneR:0.00} ({g.LastDetail}), 경계 {g.StateName}, 앞장 {ai.EscortPhase}, 죽음 {npc.Deaths - deaths}, 플레이어 거리 {Vector3.Distance(player.transform.position, npc.transform.position):0}m(ZDO {(ZDOMan.instance.GetZDO(player.GetZDOID()) is ZDO pz ? Vector3.Distance(pz.GetPosition(), npc.transform.position) : -1f):0}m), 순간이동 {player.IsTeleporting()}");
            checks.Add($"동행이 떠나면 혼자 힘으로 다시 판단 (비율 {withR:0.00} → {aloneR:0.00}, 한 방 {withHit:0.0} → {GetHit(g.LastDetail):0.0})",
                npc.Deaths == deaths && aloneR > withR);
            bool mustBack = aloneR > Danger.FightScv;
            checks.Add($"혼자 무리면 물러남·도망, 아니면 싸움 (비율 {aloneR:0.00}, {aloneV})",
                mustBack ? aloneV == Verdict.Back || aloneV == Verdict.Flee || aloneV == Verdict.Drink : aloneV == Verdict.Fight);
            if (!mustBack || foe == null || foe.IsDead()) yield break;
            yield return TestKit.WaitFor(() => g.StateName == "Back" || g.StateName == "Flee", 3f);
            float d0 = Vector3.Distance(foe.transform.position, npc.transform.position);
            yield return new WaitForSeconds(3f);
            float d1 = foe != null ? Vector3.Distance(foe.transform.position, npc.transform.position) : 99f;
            checks.Add($"적에게서 물러나 살아 있음 ({d0:0}m → {d1:0}m, 경계 {g.StateName}, 체력 {npc.GetHealth():0})", npc.Deaths == deaths && d1 > d0);
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

        static ItemDrop.ItemData s_sword;
        static GameObject s_foe;

        static void CleanUp()
        {
            if (s_foe != null && s_foe.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
            s_foe = null;
            Player p = Player.m_localPlayer;
            if (s_sword != null && p != null) { p.UnequipItem(s_sword, false); p.GetInventory().RemoveItem(s_sword); }
            s_sword = null;
        }
    }
}
