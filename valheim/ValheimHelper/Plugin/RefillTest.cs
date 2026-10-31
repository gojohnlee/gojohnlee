using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_refilltest (⑩ 자동 보충): "!채워 나무 20" 기준치를 두면 한가한 일꾼이 스스로 나가 나무를 상자에 채우고,
    /// 다 채워지면 더 나가지 않는지. 1분 시계는 시험이 바로 돌린다(Departures.TestRefillNow).
    /// </summary>
    static class RefillTest
    {
        public static void Register() => TestRunner.Register("vh_refilltest", "자동 보충(!채워) 시험", () => TestKit.Flat(Run()));

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
                Brain.Refill.Off();
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static int Wood(TestKit.World w) => GatherTest.CountBase(w.Home, "Wood");

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            TestKit.ClearBaseStock(w, "Wood");
            checks.Add("거점 나무 0으로 시작", Wood(w) == 0);

            GatherTest.Say($"!계속채워 나무 20", null);
            yield return new WaitForSeconds(1f);
            checks.Add("기준치 등록됨", Brain.Refill.Count == 1);
            GatherTest.Say("!계속채워 목록", null);
            yield return new WaitForSeconds(1f);
            var status = Brain.Refill.Status(it => Departures.RefillStock(it, w.Ragnar.Home), Brain.Sets, GatherJob.ItemLabel);
            checks.Add($"!계속채워 목록: 기준치 그대로·현황 줄 ({string.Join(", ", status.ToArray())})",
                Brain.Refill.Count == 1 && status.Count == 1 && status[0].Contains("0/20 □□□□□□□□□□"));
            RefillWindow.Toggle();
            bool shown = RefillWindow.Open && RefillWindow.Shown.Count == 1 && RefillWindow.Shown[0].Contains("0/20 □□□□□□□□□□");
            string shownText = string.Join(", ", new List<string>(RefillWindow.Shown).ToArray());
            RefillWindow.Toggle();
            checks.Add($"F7 창: 같은 줄 ({shownText})", shown && !RefillWindow.Open);

            Departures.TestRefillNow();
            yield return new WaitForSeconds(1f);
            bool started = w.Ragnar.Ai.Job is GatherJob || w.Floki.Ai.Job is GatherJob;
            checks.Add("한가한 일꾼이 채집 시작", started);
            if (!started) yield break;
            yield return TestKit.WaitFor(() => Wood(w) >= 18, 180f);
            checks.Add($"나무가 기준치 근처까지 찼음 ({Wood(w)}, {TestKit.Waited:0}초)", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null && w.Floki.Ai.Job == null, 60f);
            checks.Add("일 끝남", TestKit.Ok);
            int full = Wood(w);
            Departures.TestRefillNow();
            yield return new WaitForSeconds(2f);
            checks.Add($"채워진 뒤엔 다시 안 나감 (나무 {full})", !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob));
            TestKit.ClearBaseStock(w, "Wood");
            Brain.Refill.Off();
            yield return Make(w, checks, made);
        }

        /// <summary>
        /// 2) 만드는 물건(⑩): "!채워 구리주괴 2" + 상자 광석 2·석탄 10 + 제련소 → 한가한 일꾼이 제작 계획(PlanJob)으로 제련해 상자에 주괴 2,
        /// 다른 일꾼이 같은 걸 겹쳐 만들지 않고, 채워진 뒤엔 다시 안 만들고, 광석+주괴가 늘지 않음.
        /// </summary>
        static IEnumerator Make(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            if (ZNetScene.instance.GetPrefab("smelter") == null) { checks.Add("제련소 프리팹 있음", false); yield break; }
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
            TestKit.PutInChest(w.Chest, "CopperOre", 2);
            TestKit.PutInChest(w.Chest, "Coal", 10);
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            made.Add(Object.Instantiate(ZNetScene.instance.GetPrefab("smelter"), GatherTest.Ground(w.Home - right * 7f), Quaternion.identity));
            yield return new WaitForSeconds(1f);

            GatherTest.Say("!계속채워 구리주괴 2", null);
            yield return new WaitForSeconds(1f);
            checks.Add("만드는 물건 기준치 등록됨", Brain.Refill.Count == 1);
            Departures.TestRefillNow();
            yield return new WaitForSeconds(1f);
            WorkerNpc maker = w.Ragnar.Ai.Job is PlanJob ? w.Ragnar : w.Floki.Ai.Job is PlanJob ? w.Floki : null;
            checks.Add($"한가한 일꾼이 제작 계획 시작 ({w.Ragnar.Ai.Job?.GetType().Name}, {w.Floki.Ai.Job?.GetType().Name})", maker != null);
            if (maker == null) yield break;
            Departures.TestRefillNow();
            yield return new WaitForSeconds(1f);
            checks.Add("다른 일꾼은 같은 걸 겹쳐 안 만듦", !(w.Ragnar.Ai.Job is PlanJob && w.Floki.Ai.Job is PlanJob));
            yield return TestKit.WaitFor(() => Count(w, "Copper") >= 2, 240f);
            checks.Add($"구리 주괴 2개가 상자에 ({TestKit.Waited:0}초, 광석 {Count(w, "CopperOre")})", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null && w.Floki.Ai.Job == null, 30f);
            checks.Add("일 끝남", TestKit.Ok);
            Departures.TestRefillNow();
            yield return new WaitForSeconds(2f);
            int ore = Count(w, "CopperOre"), bars = Count(w, "Copper");
            checks.Add($"채워진 뒤엔 다시 안 만듦 ({w.Ragnar.Ai.Job?.GetType().Name}, {w.Floki.Ai.Job?.GetType().Name})", w.Ragnar.Ai.Job == null && w.Floki.Ai.Job == null);
            checks.Add($"제련 뒤 개수 보존 (광석 {ore} + 주괴 {bars} ≤ 2)", ore + bars <= 2);
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
            Brain.Refill.Off();
            yield return Set(w, checks, made);
        }

        const int SetNo = 10;
        static readonly string[] SetItems = { "Bow", "Club" };

        /// <summary>
        /// 3) 세트(⑩ 5c): 세트 10번(활·곤봉) + "!채워 세트 10 1" + 작업대·재료 → Due가 세트 물건을 펼쳐 한가한 일꾼이 하나씩 만들어
        /// 장비 상자에 활 1·곤봉 1, 채워진 뒤엔 다시 안 만들고 재료가 다 쓰였는지(개수 보존). 세트 10번은 끝나면 원래대로.
        /// </summary>
        static IEnumerator Set(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            var need = new Dictionary<string, int>();
            foreach (string item in SetItems)
                foreach (var r in ObjectDB.instance.m_recipes)
                    if (r.m_item != null && Utils.GetPrefabName(r.m_item.gameObject) == item)
                        foreach (var req in r.m_resources)
                        {
                            string key = Utils.GetPrefabName(req.m_resItem.gameObject);
                            need.TryGetValue(key, out int n);
                            need[key] = n + req.m_amount;
                        }
            checks.Add("세트 재료 레시피 있음", need.Count > 0);
            if (need.Count == 0) yield break;
            var clear = new List<string>(SetItems);
            clear.AddRange(need.Keys);
            TestKit.ClearBaseStock(w, clear.ToArray());
            foreach (var kv in need) TestKit.PutInChest(w.Chest, kv.Key, kv.Value);
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            made.Add(Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"), GatherTest.Ground(w.Home + right * 5f), Quaternion.identity));
            SetMenu old = Brain.Sets.Get(SetNo);
            string oldItems = old == null ? null : string.Join(";", old.Items.ToArray());
            try
            {
                checks.Add("세트 10번 저장", Brain.Sets.Save(SetNo, SetItems, "시험 세트", "시험") == SetNo);
                yield return new WaitForSeconds(1f);
                GatherTest.Say($"!계속채워 세트 {SetNo} 1", null);
                yield return new WaitForSeconds(1f);
                checks.Add("세트 기준치 등록됨", Brain.Refill.Count == 1);
                float start = Time.time;
                while (Time.time - start < 240f && !(Count(w, "Bow") >= 1 && Count(w, "Club") >= 1))
                {
                    if (w.Ragnar.Ai.Job == null || w.Floki.Ai.Job == null) Departures.TestRefillNow();
                    yield return new WaitForSeconds(3f);
                }
                checks.Add($"세트 물건이 상자에 (활 {Count(w, "Bow")}, 곤봉 {Count(w, "Club")}, {Time.time - start:0}초)", Count(w, "Bow") >= 1 && Count(w, "Club") >= 1);
                yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job == null && w.Floki.Ai.Job == null, 30f);
                checks.Add("일 끝남", TestKit.Ok);
                Departures.TestRefillNow();
                yield return new WaitForSeconds(2f);
                checks.Add($"채워진 뒤엔 다시 안 만듦 ({w.Ragnar.Ai.Job?.GetType().Name}, {w.Floki.Ai.Job?.GetType().Name})", w.Ragnar.Ai.Job == null && w.Floki.Ai.Job == null);
                string left = "";
                bool conserved = Count(w, "Bow") == 1 && Count(w, "Club") == 1;
                foreach (var kv in need)
                {
                    int c = Count(w, kv.Key);
                    left += $" {kv.Key}={c}";
                    if (c != 0) conserved = false;
                }
                checks.Add($"개수 보존 (활 {Count(w, "Bow")}, 곤봉 {Count(w, "Club")},{left})", conserved);
            }
            finally
            {
                Brain.Sets.Remove(SetNo);
                if (oldItems != null) Brain.Sets.Save(SetNo, oldItems.Split(';'), old.Name, old.By);
            }
            TestKit.ClearBaseStock(w, clear.ToArray());
        }

        static int Count(TestKit.World w, string prefab)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(w.Home)) n += GatherTest.Count(c, prefab);
            return n;
        }
    }
}
