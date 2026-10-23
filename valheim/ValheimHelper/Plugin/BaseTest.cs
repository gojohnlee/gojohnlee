using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// vh_basetest (기반 대표 시험, "지금 먼저" 4): 일 중간 저장·다시 불러 이어 하기(빠른 복원 — 실제 재시작 시험을 대신하지 않음),
    /// 급한 일 끼어들기 뒤 하던 일로 복귀, 두 일꾼이 같은 상자에 동시에 넣기·같은 제련소 동시에 쓰기, 수량 보존(받은 만큼 상자에, 짐·사용 중 표시 안 남음).
    /// </summary>
    static class BaseTest
    {
        public static void Register() => TestRunner.Register("vh_basetest", "기반 대표 시험(이어 하기·끼어들기·같은 상자·제련소 동시)", () => TestKit.Flat(Run()));

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
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static int Base(TestKit.World w, string prefab) => GatherTest.CountBase(w.Home, prefab);

        internal static int Carried(WorkerNpc npc, string prefab) =>
            npc == null ? 0 : npc.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);

        static bool Idle(WorkerNpc npc) => npc != null && npc.Ai.Job == null && npc.Ai.Queue.Count == 0;

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            string n1 = w.Ragnar.NpcName, n2 = w.Floki.NpcName;
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            checks.Add("거점 나무·돌 0으로 시작", Base(w, "Wood") == 0 && Base(w, "Stone") == 0);

            // 1) 나무 10 시작 → 나무를 들거나 넣은 뒤(일 중간) 저장하고 인스턴스를 내렸다가 다시 만든다
            GatherTest.Say($"!{n1} 나무 10", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"나무 10 시작 ({w.Ragnar.Ai.Job?.Label})", w.Ragnar.Ai.Job is GatherJob);
            if (!(w.Ragnar.Ai.Job is GatherJob)) yield break;
            yield return TestKit.WaitFor(() => Carried(w.Ragnar, "Wood") + Base(w, "Wood") >= 2, 150f);
            checks.Add($"일 중간까지 감 (짐 {Carried(w.Ragnar, "Wood")}, 상자 {Base(w, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            if (Features.SaveWork)
            {
                var g = (GatherJob)w.Ragnar.Ai.Job;
                int left = g.Target - g.Delivered, carried = Carried(w.Ragnar, "Wood"), stocked = Base(w, "Wood");
                WorkSaves.SaveNow(w.Ragnar);
                TestRunner.Log($"내리기 전: 남은 {left}, 짐 나무 {carried}, 상자 나무 {stocked}");
                WorkerNpc old = w.Ragnar;
                TestKit.Unload(old.GetComponent<ZNetView>());
                yield return TestKit.WaitFor(() => TestKit.Npc(Personality.Ragnar) is WorkerNpc c && c != old, 5f);
                checks.Add($"NPC 다시 생김 ({TestKit.Waited:0.0}초)", TestKit.Ok);
                if (!TestKit.Ok) yield break;
                w.Ragnar = TestKit.Npc(Personality.Ragnar);
                checks.Add($"짐 그대로 (나무 {Carried(w.Ragnar, "Wood")}/{carried})", Carried(w.Ragnar, "Wood") == carried);
                yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is GatherJob, 5f); // WorkSaves가 불러오고(1초마다) 한가하면 첫 칸을 꺼냄
                var g2 = w.Ragnar.Ai.Job as GatherJob;
                checks.Add($"이어 하기 ({g2?.Item} 목표 {g2?.Target}, 남았던 {left})", g2 != null && g2.Item == "Wood" && g2.Target == left);
            }
            else TestRunner.Log("이어 하기 건너뜀: 설정 Features.SaveWork 꺼짐");

            // 2) 급한 일 끼어들기 → 돌 5 먼저, 끝나면 나무로 돌아감
            GatherTest.Say($"!{n1} 빨리 돌 5", null);
            yield return new WaitForSeconds(1f);
            var q = w.Ragnar.Ai.Queue;
            checks.Add($"급함 → 끼어듦 (지금 {w.Ragnar.Ai.Job?.Label}, 대기 {q.Count}칸)",
                w.Ragnar.Ai.Job is GatherJob gs && gs.Item == "Stone" && q.Count == 1 && q[0].Resume is GatherJob gw && gw.Item == "Wood");
            yield return TestKit.WaitFor(() => Base(w, "Stone") >= 5, 240f);
            checks.Add($"돌 5가 상자에 ({Base(w, "Stone")}, {TestKit.Waited:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is GatherJob gb && gb.Item == "Wood", 30f);
            checks.Add("하던 나무로 돌아감", TestKit.Ok);
            yield return TestKit.WaitFor(() => Base(w, "Wood") >= 10 && Idle(w.Ragnar), 300f);
            checks.Add($"나무 10이 상자에, 일 끝 ({Base(w, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);
            int wood1 = Base(w, "Wood"), stone1 = Base(w, "Stone");
            checks.Add($"수량 보존: 나무 {wood1}·돌 {stone1}(받은 만큼 이상), 짐에 안 남음 (나무 {Carried(w.Ragnar, "Wood")}·돌 {Carried(w.Ragnar, "Stone")})",
                wood1 >= 10 && stone1 >= 5 && Carried(w.Ragnar, "Wood") == 0 && Carried(w.Ragnar, "Stone") == 0);

            // 3) 두 일꾼이 같은 상자에 동시에: 상자는 하나만 남기고(거점 둘레 지난 시험 상자는 Setup이 치움) 둘에게 나무 10씩
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            GatherTest.Say($"!{n1} 나무 10", null);
            GatherTest.Say($"!{n2} 나무 10", null);
            yield return new WaitForSeconds(1f);
            checks.Add("둘 다 나무 시작", w.Ragnar.Ai.Job is GatherJob && w.Floki.Ai.Job is GatherJob);
            yield return TestKit.WaitFor(() => Idle(w.Ragnar) && Idle(w.Floki), 360f);
            int wood2 = Base(w, "Wood");
            checks.Add($"둘 다 끝 ({TestKit.Waited:0}초)", TestKit.Ok);
            checks.Add($"같은 상자 동시: 나무 {wood2} ≥ 20, 짐에 안 남음 ({Carried(w.Ragnar, "Wood")}·{Carried(w.Floki, "Wood")})",
                wood2 >= 20 && Carried(w.Ragnar, "Wood") == 0 && Carried(w.Floki, "Wood") == 0);
            checks.Add("상자 사용 중 표시 안 남음", !w.Chest.IsInUse());
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            yield return Smelt(w, checks, made);
        }

        /// <summary>
        /// 4) 두 일꾼이 같은 제련소를 동시에: 광석 4·석탄 20, 둘에게 "!구리주괴 2"씩 → 한 제련소에 둘 다 넣고 꺼냄.
        /// 먼저 꺼내는 쪽이 남의 몫까지 가져가도 된다(합만 봄). 개수 보존 = 상자+두 짐+제련소 둘레 땅+제련소 안(대기 광석·내보낼 것).
        /// </summary>
        static IEnumerator Smelt(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            GameObject sp = ZNetScene.instance.GetPrefab("smelter");
            if (sp == null) { checks.Add("제련소 프리팹 있음", false); yield break; }
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
            TestKit.PutInChest(w.Chest, "CopperOre", 4);
            TestKit.PutInChest(w.Chest, "Coal", 20);
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            var sm = Object.Instantiate(sp, GatherTest.Ground(w.Home - right * 7f), Quaternion.identity);
            made.Add(sm);
            ZDO z = sm.GetComponent<ZNetView>().GetZDO();
            yield return new WaitForSeconds(1f);
            GatherTest.Say($"!{w.Ragnar.NpcName} 구리주괴 2", null);
            GatherTest.Say($"!{w.Floki.NpcName} 구리주괴 2", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"둘 다 제련 계획 시작 ({w.Ragnar.Ai.Job?.GetType().Name}·{w.Floki.Ai.Job?.GetType().Name})",
                w.Ragnar.Ai.Job is PlanJob && w.Floki.Ai.Job is PlanJob);
            yield return TestKit.WaitFor(() => Idle(w.Ragnar) && Idle(w.Floki), 360f);
            checks.Add($"둘 다 끝 ({TestKit.Waited:0}초, {w.Ragnar.Ai.Current}·{w.Floki.Ai.Current})", TestKit.Ok);
            int bars = Base(w, "Copper"), ore = Base(w, "CopperOre");
            int bag = Carried(w.Ragnar, "Copper") + Carried(w.Floki, "Copper") + Carried(w.Ragnar, "CopperOre") + Carried(w.Floki, "CopperOre");
            int ground = OnGround(sm.transform.position, "Copper") + OnGround(sm.transform.position, "CopperOre");
            int inside = z.GetInt(ZDOVars.s_queued) + z.GetInt(ZDOVars.s_spawnAmount);
            checks.Add($"같은 제련소 동시: 주괴 {bars} ≥ 4 (광석 남음 {ore})", bars >= 4);
            checks.Add($"제련 개수 보존 (상자 주괴 {bars}+광석 {ore}, 짐 {bag}, 땅 {ground}, 제련소 안 {inside} → 합 4)",
                bars + ore + bag + ground + inside == 4);
            checks.Add("상자 사용 중 표시 안 남음", !w.Chest.IsInUse());
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
        }

        internal static int OnGround(Vector3 at, string prefab)
        {
            int n = 0;
            var seen = new HashSet<ItemDrop>();
            foreach (Collider col in Physics.OverlapSphere(at, 6f, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d != null && seen.Add(d) && Utils.GetPrefabName(d.gameObject) == prefab) n += d.m_itemData.m_stack;
            }
            return n;
        }
    }
}
