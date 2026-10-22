using System.Collections;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// vh_basetest (기반 대표 시험, "지금 먼저" 4): 일 중간 저장·다시 불러 이어 하기(빠른 복원 — 실제 재시작 시험을 대신하지 않음),
    /// 급한 일 끼어들기 뒤 하던 일로 복귀, 두 일꾼이 같은 상자에 동시에 넣기, 수량 보존(받은 만큼 상자에, 짐·사용 중 표시 안 남음).
    /// </summary>
    static class BaseTest
    {
        public static void Register() => TestRunner.Register("vh_basetest", "기반 대표 시험(이어 하기·끼어들기·같은 상자 동시)", () => TestKit.Flat(Run()));

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
            try { yield return Body(w, checks); }
            finally { TestKit.Restore(w); }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static int Base(TestKit.World w, string prefab) => GatherTest.CountBase(w.Home, prefab);

        static int Carried(WorkerNpc npc, string prefab) =>
            npc == null ? 0 : npc.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);

        static bool Idle(WorkerNpc npc) => npc != null && npc.Ai.Job == null && npc.Ai.Queue.Count == 0;

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Claude != null && w.Alarak != null && w.Chest != null);
            if (w.Claude == null || w.Alarak == null || w.Chest == null) yield break;
            string n1 = w.Claude.NpcName, n2 = w.Alarak.NpcName;
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            checks.Add("거점 나무·돌 0으로 시작", Base(w, "Wood") == 0 && Base(w, "Stone") == 0);

            // 1) 나무 10 시작 → 나무를 들거나 넣은 뒤(일 중간) 저장하고 인스턴스를 내렸다가 다시 만든다
            GatherTest.Say($"!{n1} 나무 10", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"나무 10 시작 ({w.Claude.Ai.Job?.Label})", w.Claude.Ai.Job is GatherJob);
            if (!(w.Claude.Ai.Job is GatherJob)) yield break;
            yield return TestKit.WaitFor(() => Carried(w.Claude, "Wood") + Base(w, "Wood") >= 2, 150f);
            checks.Add($"일 중간까지 감 (짐 {Carried(w.Claude, "Wood")}, 상자 {Base(w, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            if (Features.SaveWork)
            {
                var g = (GatherJob)w.Claude.Ai.Job;
                int left = g.Target - g.Delivered, carried = Carried(w.Claude, "Wood"), stocked = Base(w, "Wood");
                WorkSaves.SaveNow(w.Claude);
                TestRunner.Log($"내리기 전: 남은 {left}, 짐 나무 {carried}, 상자 나무 {stocked}");
                WorkerNpc old = w.Claude;
                TestKit.Unload(old.GetComponent<ZNetView>());
                yield return TestKit.WaitFor(() => TestKit.Npc(Personality.Scv) is WorkerNpc c && c != old, 5f);
                checks.Add($"NPC 다시 생김 ({TestKit.Waited:0.0}초)", TestKit.Ok);
                if (!TestKit.Ok) yield break;
                w.Claude = TestKit.Npc(Personality.Scv);
                checks.Add($"짐 그대로 (나무 {Carried(w.Claude, "Wood")}/{carried})", Carried(w.Claude, "Wood") == carried);
                yield return TestKit.WaitFor(() => w.Claude.Ai.Job is GatherJob, 5f); // WorkSaves가 불러오고(1초마다) 한가하면 첫 칸을 꺼냄
                var g2 = w.Claude.Ai.Job as GatherJob;
                checks.Add($"이어 하기 ({g2?.Item} 목표 {g2?.Target}, 남았던 {left})", g2 != null && g2.Item == "Wood" && g2.Target == left);
            }
            else TestRunner.Log("이어 하기 건너뜀: 설정 Features.SaveWork 꺼짐");

            // 2) 급한 일 끼어들기 → 돌 5 먼저, 끝나면 나무로 돌아감
            GatherTest.Say($"!{n1} 빨리 돌 5", null);
            yield return new WaitForSeconds(1f);
            var q = w.Claude.Ai.Queue;
            checks.Add($"급함 → 끼어듦 (지금 {w.Claude.Ai.Job?.Label}, 대기 {q.Count}칸)",
                w.Claude.Ai.Job is GatherJob gs && gs.Item == "Stone" && q.Count == 1 && q[0].Resume is GatherJob gw && gw.Item == "Wood");
            yield return TestKit.WaitFor(() => Base(w, "Stone") >= 5, 240f);
            checks.Add($"돌 5가 상자에 ({Base(w, "Stone")}, {TestKit.Waited:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job is GatherJob gb && gb.Item == "Wood", 30f);
            checks.Add("하던 나무로 돌아감", TestKit.Ok);
            yield return TestKit.WaitFor(() => Base(w, "Wood") >= 10 && Idle(w.Claude), 300f);
            checks.Add($"나무 10이 상자에, 일 끝 ({Base(w, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);
            int wood1 = Base(w, "Wood"), stone1 = Base(w, "Stone");
            checks.Add($"수량 보존: 나무 {wood1}·돌 {stone1}(받은 만큼 이상), 짐에 안 남음 (나무 {Carried(w.Claude, "Wood")}·돌 {Carried(w.Claude, "Stone")})",
                wood1 >= 10 && stone1 >= 5 && Carried(w.Claude, "Wood") == 0 && Carried(w.Claude, "Stone") == 0);

            // 3) 두 일꾼이 같은 상자에 동시에: 상자는 하나만 남기고(거점 둘레 지난 시험 상자는 Setup이 치움) 둘에게 나무 10씩
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            GatherTest.Say($"!{n1} 나무 10", null);
            GatherTest.Say($"!{n2} 나무 10", null);
            yield return new WaitForSeconds(1f);
            checks.Add("둘 다 나무 시작", w.Claude.Ai.Job is GatherJob && w.Alarak.Ai.Job is GatherJob);
            yield return TestKit.WaitFor(() => Idle(w.Claude) && Idle(w.Alarak), 360f);
            int wood2 = Base(w, "Wood");
            checks.Add($"둘 다 끝 ({TestKit.Waited:0}초)", TestKit.Ok);
            checks.Add($"같은 상자 동시: 나무 {wood2} ≥ 20, 짐에 안 남음 ({Carried(w.Claude, "Wood")}·{Carried(w.Alarak, "Wood")})",
                wood2 >= 20 && Carried(w.Claude, "Wood") == 0 && Carried(w.Alarak, "Wood") == 0);
            checks.Add("상자 사용 중 표시 안 남음", !w.Chest.IsInUse());
            TestKit.ClearBaseStock(w, "Wood", "Stone");
        }
    }
}
