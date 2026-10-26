using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 제작 재시작 시험(외부 검토 + 사용자 승인 10-26): 라그나르 "구리주괴 4"·플로키 "구리주괴 2"가 제련소 하나를 같이 쓰는 중에 저장하고 바로 끄고,
    /// 다시 켜서(VH_RESET=0, vh_restartcraft2) 저장한 계획으로 이어 가는지. 끄는 때 셋(1번째 실행 이름):
    /// vh_restartcraft_load = 둘 다 넣은 직후, vh_restartcraft_part = 일부 완성(제련소 안 1~5), vh_restartcraft_carry = 꺼내기 단계 일꾼이 주괴를 든 채.
    /// 2번째: 계획을 다시 세우지 않음·다시 넣지 않음·광석 채집 없음, 끝에 상자 주괴 6·광석 0(어디에도), 석탄은 계획이 넣은 만큼만, 계획 단계 전부 끝.
    /// 두 실행 사이 상태는 플러그인 폴더 restartcraft-state.txt(2번째가 읽고 지움). 1번째는 묶음의 마지막이어야 한다(바로 끔).
    /// </summary>
    static class RestartCraftTest
    {
        const string StateFile = "restartcraft-state.txt";
        const int Ore = 6, CoalIn = 20;
        static string StatePath => Path.Combine(TestRunner.PluginDir, StateFile);
        static bool s_quit;

        public static void Register()
        {
            TestRunner.Register("vh_restartcraft_load", "제작 재시작 1: 둘이 제련소에 넣은 직후 종료", () => TestKit.Flat(Run1("load")));
            TestRunner.Register("vh_restartcraft_part", "제작 재시작 1: 일부 완성 때 종료", () => TestKit.Flat(Run1("part")));
            TestRunner.Register("vh_restartcraft_carry", "제작 재시작 1: 꺼낸 주괴를 든 채 종료", () => TestKit.Flat(Run1("carry")));
            TestRunner.Register("vh_restartcraft2", "제작 재시작 2: 다시 켜서 계획 이어 하기(VH_RESET=0)", () => TestKit.Flat(Run2()));
        }

        static bool Server()
        {
            if (Player.m_localPlayer != null && ZNet.instance.IsServer()) return true;
            TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
            TestRunner.Finish();
            return false;
        }

        static int Stock(TestKit.World w, string prefab) => GatherTest.CountBase(w.Home, prefab);
        static int Carried(WorkerNpc npc, string prefab) => BaseTest.Carried(npc, prefab);
        static bool Idle(WorkerNpc npc) => npc != null && npc.Ai.Job == null && npc.Ai.Queue.Count == 0;

        static void RemoveFromBag(WorkerNpc npc, string prefab)
        {
            string name = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            int n = npc.GetInventory().CountItems(name);
            if (n > 0) npc.GetInventory().RemoveItem(name, n);
        }

        static bool AllLoadsDone(PlanJob p)
        {
            foreach (var s in p.Runner.Plan.Steps)
                if (s.Kind == StepKind.Load && p.Runner.StateOf(s.Id) != StepState.Done) return false;
            return true;
        }

        static int LoadFuel(PlanJob p)
        {
            int n = 0;
            foreach (var s in p.Runner.Plan.Steps) if (s.Kind == StepKind.Load) n += s.FuelAmount;
            return n;
        }

        /// <summary>꺼내기 단계에서 꺼낸 주괴를 들고 있음.</summary>
        static bool Holding(WorkerNpc npc) => npc.Ai.Job is PlanJob p && p.StepName == "Unload" && Carried(npc, "Copper") > 0;

        static List<Smelter> Smelters(Vector3 home)
        {
            var list = new List<Smelter>();
            foreach (Collider col in Physics.OverlapSphere(home, Recipes.BaseRange, Recipes.MachineMask))
            {
                Smelter s = col.GetComponentInParent<Smelter>();
                if (s != null && !list.Contains(s) && Utils.GetPrefabName(s.gameObject) == "smelter") list.Add(s);
            }
            return list;
        }

        static Smelter SmelterAt(Vector3 home, Vector3 at)
        {
            foreach (var s in Smelters(home)) if (Vector3.Distance(s.transform.position, at) < 2f) return s;
            return null;
        }

        static IEnumerator Run1(string stage)
        {
            var checks = new GatherTest.Checks();
            if (!Server()) yield break;
            try { File.Delete(StatePath); } catch (IOException) { }
            var w = new TestKit.World();
            s_quit = false;
            try { yield return Body1(w, checks, stage); }
            finally { TestKit.Restore(w); }
            bool quit = s_quit;
            // End는 끝 정리(땅 물건·생물)를 하므로 쓰지 않는다 — 일꾼·짐·제련소·하던 일을 그대로 저장해야 함
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            if (quit) TestRunner.Log("게임을 바로 끕니다(프로필·월드 저장).");
            TestRunner.Finish();
            if (quit) Application.Quit();
        }

        static IEnumerator Body1(TestKit.World w, GatherTest.Checks checks, string stage)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            if (!Features.SaveWork || !Features.RunPlans) { checks.Add("설정 Features.SaveWork·RunPlans 켜짐", false); yield break; }
            GameObject sp = ZNetScene.instance.GetPrefab("smelter");
            if (sp == null) { checks.Add("제련소 프리팹 있음", false); yield break; }
            TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
            foreach (var npc in new[] { w.Ragnar, w.Floki })
                foreach (var p in new[] { "CopperOre", "Copper", "Coal" }) RemoveFromBag(npc, p);
            TestKit.PutInChest(w.Chest, "CopperOre", Ore);
            TestKit.PutInChest(w.Chest, "Coal", CoalIn);
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            var sm = Object.Instantiate(sp, GatherTest.Ground(w.Home - right * 7f), Quaternion.identity); // 지우지 않음: 2번째가 이어 쓰고 끝에 지움
            ZDO z = sm.GetComponent<ZNetView>().GetZDO();
            yield return new WaitForSeconds(1f);
            int count = Smelters(w.Home).Count;
            checks.Add($"거점 제련소 하나 ({count}개)", count == 1);
            GatherTest.Say($"!{w.Ragnar.NpcName} 구리주괴 4", null);
            GatherTest.Say($"!{w.Floki.NpcName} 구리주괴 2", null);
            yield return new WaitForSeconds(1f);
            var pr = w.Ragnar.Ai.Job as PlanJob;
            var pf = w.Floki.Ai.Job as PlanJob;
            checks.Add($"둘 다 제련 계획 시작 ({w.Ragnar.Ai.Job?.GetType().Name}·{w.Floki.Ai.Job?.GetType().Name})", pr != null && pf != null);
            if (pr == null || pf == null) yield break;
            bool Loaded() => AllLoadsDone(pr) && AllLoadsDone(pf);
            System.Func<bool> ready;
            float max;
            if (stage == "load") { ready = Loaded; max = 120f; } // 둘째가 넣는 사이 첫째 것 하나가 나왔을 수 있음(제련소 안 5~6)
            else if (stage == "part") { ready = () => { int q = z.GetInt(ZDOVars.s_queued); return Loaded() && q > 0 && q < Ore; }; max = 240f; }
            else { ready = () => Holding(w.Ragnar) || Holding(w.Floki); max = 360f; }
            yield return TestKit.WaitFor(ready, max);
            checks.Add($"끌 때가 됨: {stage} ({TestKit.Waited:0}초, 제련소 안 {z.GetInt(ZDOVars.s_queued)}, 상자 주괴 {Stock(w, "Copper")}, " +
                $"든 주괴 {Carried(w.Ragnar, "Copper")}·{Carried(w.Floki, "Copper")}, 단계 {w.Ragnar.Ai.Job?.StepName}·{w.Floki.Ai.Job?.StepName})", TestKit.Ok);
            if (!TestKit.Ok) yield break;

            // 이 프레임 안에서 저장·기록·종료(Application.Quit은 프레임 끝에 OnApplicationQuit → 월드 저장, 그 사이 일꾼 Update 없음)
            WorkSaves.SaveNow(w.Ragnar);
            WorkSaves.SaveNow(w.Floki);
            var st = new List<string>
            {
                "stage=" + stage, "home=" + V(w.Home), "chest=" + V(w.Chest.transform.position), "smelter=" + V(sm.transform.position),
                "queued=" + z.GetInt(ZDOVars.s_queued), "bars=" + Stock(w, "Copper"),
                "carried=" + Carried(w.Ragnar, "Copper") + "+" + Carried(w.Floki, "Copper"),
                "fuel=" + (LoadFuel(pr) + LoadFuel(pf)), "npcs=" + Workers.AllZdos().Count,
            };
            File.WriteAllLines(StatePath, st.ToArray());
            TestRunner.Log("저장한 상태: " + string.Join(", ", st.ToArray()));
            checks.Add("상태 파일 씀", File.Exists(StatePath));
            w.Knowledge0 = null; // 시험 중 지식을 그대로 저장(2번째 실행이 같은 지식으로 이어 감)
            s_quit = true;
        }

        static IEnumerator Run2()
        {
            var checks = new GatherTest.Checks();
            if (!Server()) yield break;
            var st = new Dictionary<string, string>();
            try
            {
                foreach (var l in File.ReadAllLines(StatePath))
                {
                    int i = l.IndexOf('=');
                    if (i > 0) st[l.Substring(0, i)] = l.Substring(i + 1);
                }
                File.Delete(StatePath); // 한 번만 씀
            }
            catch (IOException) { }
            if (!st.ContainsKey("stage"))
                throw new TestKit.EnvBadException("restartcraft-state.txt 없음 — vh_restartcraft_load(또는 _part·_carry)를 먼저(원본 되돌리기 켜고), 그다음 VH_RESET=0으로 vh_restartcraft2");
            TestRunner.Log("1번째 실행 상태: " + string.Join(", ", new List<string>(st.Keys).ConvertAll(k => k + "=" + st[k]).ToArray()));

            var w = new TestKit.World { Player = Player.m_localPlayer, Home = P(st["home"]) };
            w.God = w.Player.InGodMode();
            w.Player.SetGodMode(true);
            w.Env = EnvMan.instance;
            w.DebugTime = w.Env.m_debugTimeOfDay;
            w.DebugValue = w.Env.m_debugTime;
            w.Env.m_debugTimeOfDay = true;
            w.Env.m_debugTime = 0.5f;
            w.OnHeard = (n, t) => w.Heard.Add(n + ": " + t);
            ChatNet.Heard += w.OnHeard;
            try { yield return Body2(w, checks, st); }
            finally
            {
                TestKit.Restore(w);
                Smelter sm = SmelterAt(w.Home, P(st["smelter"]));
                if (sm != null) ZNetScene.instance.Destroy(sm.gameObject);
                TestKit.ClearBaseStock(w, "CopperOre", "Copper", "Coal");
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body2(TestKit.World w, GatherTest.Checks checks, Dictionary<string, string> st)
        {
            float d = Vector3.Distance(w.Player.transform.position, w.Home);
            checks.Add($"플레이어가 저장된 자리 곁에서 시작 (거점까지 {d:0}m)", d < 30f);
            yield return TestKit.WaitFor(() => TestKit.Npc(Personality.Ragnar) != null && TestKit.Npc(Personality.Floki) != null, 30f);
            checks.Add($"NPC 2명 다시 나타남 ({TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            w.Ragnar = TestKit.Npc(Personality.Ragnar);
            w.Floki = TestKit.Npc(Personality.Floki);
            yield return new WaitForSeconds(2f); // 상자·제련소 구역도 불러오게
            w.Chest = GatherTest.ChestAt(P(st["chest"]));
            Smelter sm = SmelterAt(w.Home, P(st["smelter"]));
            checks.Add($"상자·제련소 그대로 있음 (상자 {w.Chest != null}, 제련소 {sm != null})", w.Chest != null && sm != null);
            if (w.Chest == null || sm == null) yield break;
            ZDO z = sm.GetComponent<ZNetView>().GetZDO();
            TestRunner.Log($"불러온 제련소 안 {z.GetInt(ZDOVars.s_queued)}(저장 때 {st["queued"]}), 상자 주괴 {Stock(w, "Copper")}(저장 때 {st["bars"]}), " +
                $"든 주괴 {Carried(w.Ragnar, "Copper")}+{Carried(w.Floki, "Copper")}(저장 때 {st["carried"]})");
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is PlanJob && w.Floki.Ai.Job is PlanJob, 15f);
            var pr = w.Ragnar.Ai.Job as PlanJob;
            var pf = w.Floki.Ai.Job as PlanJob;
            checks.Add($"둘 다 저장한 계획으로 이어 감 ({w.Ragnar.Ai.Job?.GetType().Name}·{w.Floki.Ai.Job?.GetType().Name}, 이어 함 {Departures.ResumedPlans}, 다시 세움 {Departures.ReplannedPlans})",
                pr != null && pf != null && Departures.ResumedPlans == 2 && Departures.ReplannedPlans == 0);
            if (pr == null || pf == null) yield break;
            checks.Add("넣기는 끝난 것으로 이어 받음", AllLoadsDone(pr) && AllLoadsDone(pf));
            PlanRunner rr = pr.Runner, rf = pf.Runner;
            int queued0 = z.GetInt(ZDOVars.s_queued);
            string bad = null;
            bool Over()
            {
                foreach (var npc in new[] { w.Ragnar, w.Floki })
                {
                    IWorkerJob j = npc.Ai.Job;
                    if (j is PlanJob p && (p.StepName == "Load" || p.StepName == "Gather" || p.StepName == "Hunt")) bad = bad ?? $"{npc.NpcName} {p.StepName}";
                    if (j is GatherJob g && g.Item == "CopperOre") bad = bad ?? $"{npc.NpcName} 광석 채집";
                }
                int q = z.GetInt(ZDOVars.s_queued);
                if (q > queued0) bad = bad ?? $"제련소 안이 늘어남 {queued0}→{q}";
                queued0 = Mathf.Min(queued0, q);
                return Idle(w.Ragnar) && Idle(w.Floki) && Stock(w, "Copper") >= Ore;
            }
            yield return TestKit.WaitFor(Over, 420f);
            checks.Add($"둘 다 끝·상자 주괴 {Stock(w, "Copper")} ({TestKit.Waited:0}초, {w.Ragnar.Ai.Current}·{w.Floki.Ai.Current})", TestKit.Ok);
            checks.Add($"다시 넣기·채집 없음 ({bad ?? "없음"})", bad == null);
            checks.Add($"계획 단계 전부 끝 (라그나르 {rr.Count(StepState.Done)}/{rr.Plan.Steps.Count}, 플로키 {rf.Count(StepState.Done)}/{rf.Plan.Steps.Count})", rr.Finished && rf.Finished);
            int bars = Stock(w, "Copper"), ore = Stock(w, "CopperOre");
            int bag = Carried(w.Ragnar, "Copper") + Carried(w.Floki, "Copper") + Carried(w.Ragnar, "CopperOre") + Carried(w.Floki, "CopperOre");
            int ground = BaseTest.OnGround(sm.transform.position, "Copper") + BaseTest.OnGround(sm.transform.position, "CopperOre")
                + BaseTest.OnGround(w.Home, "Copper") + BaseTest.OnGround(w.Home, "CopperOre");
            int inside = z.GetInt(ZDOVars.s_queued) + z.GetInt(ZDOVars.s_spawnAmount);
            checks.Add($"주괴 6·광석 0 (상자 주괴 {bars}·광석 {ore}, 짐 {bag}, 땅 {ground}, 제련소 안 {inside})", bars == Ore && ore + bag + ground + inside == 0);
            // 석탄: 계획이 넣은 만큼(저장 때 센 Load 단계 연료 합)만 상자에서 빠짐 — 다시 넣었으면 더 빠짐
            int coal = Stock(w, "Coal") + Carried(w.Ragnar, "Coal") + Carried(w.Floki, "Coal");
            int fuel = int.Parse(st["fuel"]);
            checks.Add($"석탄은 한 번만 넣음 (상자·짐 {coal} = {CoalIn} - {fuel}, 제련소 연료 남음 {z.GetFloat(ZDOVars.s_fuel):0.#})", coal == CoalIn - fuel);
            checks.Add("상자 사용 중 표시 안 남음", !w.Chest.IsInUse());
        }

        static string V(Vector3 v) => System.FormattableString.Invariant($"{v.x:0.###},{v.y:0.###},{v.z:0.###}");

        static Vector3 P(string s)
        {
            var p = s.Split(',');
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(p[0], c), float.Parse(p[1], c), float.Parse(p[2], c));
        }
    }
}
