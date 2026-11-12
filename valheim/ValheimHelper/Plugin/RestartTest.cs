using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 실제 재시작 시험("지금 먼저" 4): 시험 실행 두 번. vh_restart1 = 나무 10 중간(+대기열 돌 3)에 저장하고 바로 게임 종료(정리 안 함),
    /// vh_restart2 = 원본 되돌리기 없이(VH_RESET=0) 다시 켜서 NPC·깃발·상자·짐·하던 일·대기열이 그대로 불러와져 끝까지 하는지.
    /// 두 실행 사이 상태는 플러그인 폴더 restart-state.txt(2번째가 읽고 지움). restart1은 묶음의 마지막이어야 한다(바로 끔).
    /// </summary>
    static class RestartTest
    {
        const string StateFile = "restart-state.txt";
        static string StatePath => Path.Combine(TestRunner.PluginDir, StateFile);

        public static void Register()
        {
            TestRunner.Register("vh_restart1", "재시작 시험 1: 일 중간 저장·게임 종료", () => TestKit.Flat(Run1()));
            TestRunner.Register("vh_restart2", "재시작 시험 2: 다시 켜서 이어 하기(VH_RESET=0)", () => TestKit.Flat(Run2()));
        }

        static bool Server(GatherTest.Checks checks)
        {
            if (Player.m_localPlayer != null && ZNet.instance.IsServer()) return true;
            TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
            TestRunner.Finish();
            return false;
        }

        static int Base(Vector3 home, string prefab) => GatherTest.CountBase(home, prefab);

        static int Carried(WorkerNpc npc, string prefab) =>
            npc == null ? 0 : npc.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);

        static IEnumerator Run1()
        {
            var checks = new GatherTest.Checks();
            if (!Server(checks)) yield break;
            try { File.Delete(StatePath); } catch (IOException) { }
            var w = new TestKit.World { House = true }; // 사용자 11-12: VH_HOUSE=1이면 TheNewEra 집 곁 깃발·집 상자에 넣다가 저장·종료
            s_quit = false;
            try { yield return Body1(w, checks); }
            finally { TestKit.Restore(w); }
            bool quit = s_quit;
            // End는 끝 정리(땅 물건·생물)를 하므로 쓰지 않는다 — 일꾼·짐·하던 일을 그대로 저장해야 함
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            if (quit) TestRunner.Log("게임을 바로 끕니다(프로필·월드 저장).");
            TestRunner.Finish();
            if (quit) Application.Quit();
        }

        static bool s_quit;

        static IEnumerator Body1(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            File.WriteAllText(Path.Combine(TestRunner.PluginDir, "flags-0.txt"), PortalList());
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            if (!Features.SaveWork) { checks.Add("설정 Features.SaveWork 켜짐", false); yield break; }
            string n1 = w.Ragnar.NpcName;
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            PortalBuildTest.ClearMine();
            var cost = PortalBuilder.Cost();
            TestKit.ClearBaseStock(w, cost.ConvertAll(c => c.Item).ToArray());
            Inventory inv = w.Ragnar.GetInventory();
            foreach (var k in cost) inv.RemoveItem(CraftStep.SharedName(k.Item), inv.CountItems(CraftStep.SharedName(k.Item)));
            GatherTest.Say($"!{n1} 나무 10", null);
            yield return new WaitForSeconds(1f);
            GatherTest.Say($"!{n1} 돌 3", null); // 급하지 않음 → 대기열
            yield return new WaitForSeconds(1f);
            checks.Add($"나무 10 하는 중·돌 3 대기 ({w.Ragnar.Ai.Job?.Label}, 대기 {w.Ragnar.Ai.Queue.Count}칸)",
                w.Ragnar.Ai.Job is GatherJob gj && gj.Item == "Wood" && w.Ragnar.Ai.Queue.Count == 1);
            if (!(w.Ragnar.Ai.Job is GatherJob)) yield break;
            yield return TestKit.WaitFor(() => Carried(w.Ragnar, "Wood") + Base(w.Home, "Wood") >= 2, 200f);
            checks.Add($"일 중간까지 감 (짐 {Carried(w.Ragnar, "Wood")}, 상자 {Base(w.Home, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;

            // 이 프레임 안에서 저장·기록·종료(Application.Quit은 프레임 끝에 OnApplicationQuit → 프로필·월드 저장, 그 사이 일꾼 Update 없음)
            var g = w.Ragnar.Ai.Job as GatherJob;
            checks.Add($"아직 나무 하는 중 ({w.Ragnar.Ai.Job?.Label})", g != null && g.Item == "Wood");
            if (g == null) yield break;
            // 포탈 운반 칸(요청 10-23 09:09 나): 거점 쪽 1개를 놓고 일터 쪽 한 몫을 든 채 종료 → 2번째가 이름표·짐·포탈 수를 이어 받는지
            Vector3 farPt = w.Home + new Vector3(0f, 0f, 400f);
            string tag = WorkerPortal.Tag(Senses.ZoneOf(farPt).ToString());
            PortalBuildTest.Fill(w.Chest, cost, 2);
            bool placed = PortalBuilder.StartPair(tag, w.Home, inv, true, out _, out string why);
            g.LoadPortal(tag);
            checks.Add($"거점 쪽 포탈 놓고 한 몫 듦 ({why}, 포탈 {PortalBuilder.MineWithTag(tag)}개)",
                placed && g.PortalCarryTag == tag && PortalBuilder.MineWithTag(tag) == 1 && PortalBuilder.Carrying(inv));
            // 쓴 재료 다시 캐기 몫도 ZDO에 남는지(사용자 11-10): 캘 길 없는 가짜 이름이라 Repay는 건너뜀, restart2가 지움
            Brain.Debt.Add("vh_test_debt", 7);
            Brain.Save();
            WorkSaves.SaveNow(w.Ragnar);
            WorkSaves.SaveNow(w.Floki);
            try { File.Delete(Path.Combine(TestRunner.PluginDir, "e072-save.txt")); } catch (IOException) { } // 끄며 하는 저장 기록만 남게
            var st = new List<string>
            {
                "home=" + V(w.Home), "chest=" + V(w.Chest.transform.position),
                "left=" + (g.Target - g.Delivered), "carried=" + Carried(w.Ragnar, "Wood"),
                "stocked=" + Base(w.Home, "Wood"), "queue=" + w.Ragnar.Ai.Queue.Count,
                "npcs=" + Workers.AllZdos().Count, "flags=" + BaseFlag.AllFlags().Count,
                "ptag=" + tag, "pfar=" + V(farPt), "biome=" + TestKit.RunBiome, "know=" + Brain.Get().Count,
            };
            File.WriteAllLines(StatePath, st.ToArray());
            TestRunner.Log("저장한 상태: " + string.Join(", ", st.ToArray()));
            File.WriteAllText(Path.Combine(TestRunner.PluginDir, "flags-1.txt"), NearFlags(w.Home, 1e6f) + "\n--- 포탈 목록\n" + PortalList());
            checks.Add("상태 파일 씀", File.Exists(StatePath));
            w.Knowledge0 = null; // 시험 중 지식을 그대로 저장(2번째 실행이 같은 지식으로 이어 감)
            w.Site = null; // 집을 떠나는 순간이동(Restore)을 하지 않음 — 플레이어가 거점 곁에서 저장돼야 2번째가 그 자리에서 시작
            s_quit = true;
        }

        static IEnumerator Run2()
        {
            var checks = new GatherTest.Checks();
            if (!Server(checks)) yield break;
            var st = new Dictionary<string, string>();
            // E072 진단(회차 380 간헐 FAIL: 불러온 월드가 1번째 저장보다 뒤 상태 — 나무 일이 끝나 있음): 월드 파일이 상태 파일보다 얼마나 늦게 쓰였나(늦으면 종료가 늦은 것)
            try
            {
                // 게임 1.0.21: 월드는 worlds_local/<월드>/ 폴더의 *.chunk(바뀐 조각만 따로 씀) — 가장 늦게 쓴 조각과 가장 이른 조각
                string dir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "IronGate", "Valheim", "worlds_local", ZNet.instance.GetWorldName());
                System.DateTime state = File.GetLastWriteTime(StatePath), newest = System.DateTime.MinValue, oldest = System.DateTime.MaxValue;
                foreach (string c in Directory.GetFiles(dir)) { var t = File.GetLastWriteTime(c); if (t > newest) newest = t; if (t < oldest) oldest = t; }
                TestRunner.Log($"E072 진단: 월드 조각 가장 늦게 쓴 시각 − 상태 파일 = {(newest - state).TotalSeconds:0.0}초, 가장 이른 조각 − 상태 파일 = {(oldest - state).TotalSeconds:0.0}초 ({Directory.GetFiles(dir).Length}조각)");
            }
            catch (System.Exception e) { TestRunner.Log("E072 진단 못 함: " + e.Message); }
            try
            {
                string sp = Path.Combine(TestRunner.PluginDir, "e072-save.txt");
                if (File.Exists(sp)) { foreach (var l in File.ReadAllLines(sp)) TestRunner.Log("앞 실행 " + l); File.Delete(sp); }
                TestRunner.Log($"E072 불러온 저장 번호 {SaveSystem.GetSaveNumber()}");
            }
            catch (System.Exception e) { TestRunner.Log("E072 저장 기록 못 읽음: " + e.Message); }
            try
            {
                foreach (var l in File.ReadAllLines(StatePath))
                {
                    int i = l.IndexOf('=');
                    if (i > 0) st[l.Substring(0, i)] = l.Substring(i + 1);
                }
                File.Delete(StatePath); // 한 번만 씀(지난 상태로 다시 돌지 않게)
            }
            catch (IOException) { }
            if (!st.ContainsKey("left")) throw new TestKit.EnvBadException("restart-state.txt 없음 — vh_restart1을 먼저(원본 되돌리기 켜고), 그다음 VH_RESET=0으로 vh_restart2");
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
            // 바이옴 회귀(1번째가 RunBiome으로 옮겨 저장): 2번째는 VH_BIOME 없이 저장된 자리에서 시작하니 다른 채집 시험처럼 시험 자리 생물을 치움
            // (회차 144 검은숲: 회색난쟁이에 도망만 되풀이해 돌 3을 420초 안에 못 함)
            bool keepClear = st.TryGetValue("biome", out string biome) && biome != Heightmap.Biome.None.ToString();
            if (keepClear) Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(w.Home, () => keepClear));
            try { yield return Body2(w, checks, st); }
            finally { keepClear = false; TestKit.Restore(w); PortalBuildTest.ClearMine(); }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body2(TestKit.World w, GatherTest.Checks checks, Dictionary<string, string> st)
        {
            int left = int.Parse(st["left"]), carried = int.Parse(st["carried"]), stocked = int.Parse(st["stocked"]);
            float d = Vector3.Distance(w.Player.transform.position, w.Home);
            checks.Add($"플레이어가 저장된 자리 곁에서 시작 (거점까지 {d:0}m)", d < 30f);
            yield return TestKit.WaitFor(() => TestKit.Npc(Personality.Ragnar) != null && TestKit.Npc(Personality.Floki) != null, 30f);
            checks.Add($"NPC 2명 다시 나타남 ({TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            // 일꾼 기억은 월드 저장 순간에만 ZDO에 넣음(Brain.SaveMemory, E052) → 실제 끄고 켠 뒤에도 그대로인지
            int know = st.TryGetValue("know", out var ks) ? int.Parse(ks) : 0;
            Brain.Get();
            checks.Add($"일꾼 지식 그대로 (불러옴 {Brain.LoadedZones}/저장 {know}구역)", know > 0 && Brain.LoadedZones >= know);
            int debt = Brain.Debt.Get("vh_test_debt");
            checks.Add($"쓴 재료 다시 캘 몫 그대로 ({debt}/7)", debt == 7);
            Brain.Debt.Repaid("vh_test_debt", debt);
            w.Ragnar = TestKit.Npc(Personality.Ragnar);
            w.Floki = TestKit.Npc(Personality.Floki);
            yield return new WaitForSeconds(2f); // 상자·깃발 구역도 불러오게
            int npcs = Workers.AllZdos().Count, flags = BaseFlag.AllFlags().Count;
            File.WriteAllText(Path.Combine(TestRunner.PluginDir, "flags-2.txt"), NearFlags(w.Home, 1e6f));
            checks.Add($"NPC·깃발 수 그대로 (NPC {npcs}/{st["npcs"]}, 깃발 {flags}/{st["flags"]})", npcs.ToString() == st["npcs"] && flags.ToString() == st["flags"]);
            w.Chest = GatherTest.ChestAt(P(st["chest"]));
            checks.Add("상자 그대로 있음", w.Chest != null);
            if (w.Chest == null) yield break;
            checks.Add($"상자 나무 그대로 ({Base(w.Home, "Wood")}/{stocked})", Base(w.Home, "Wood") == stocked);
            // 게임이 실제로 꺼지기까지(로그아웃 저장) 몇 프레임 일꾼이 더 움직여 나무를 더 주울 수 있음 → 저장 때보다 적지만 않으면 됨(작업 저장도 1초마다라 그 짐과 맞음)
            checks.Add($"짐 그대로 (나무 {Carried(w.Ragnar, "Wood")} ≥ {carried}, 도끼 있음)",
                Carried(w.Ragnar, "Wood") >= carried && Carried(w.Ragnar, "AxeFlint") == 1);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Job is GatherJob, 10f); // WorkSaves가 불러오고 한가하면 첫 칸을 꺼냄
            var g = w.Ragnar.Ai.Job as GatherJob;
            checks.Add($"하던 나무 이어 하기 ({g?.Item} 목표 {g?.Target}, 남았던 {left})", g != null && g.Item == "Wood" && g.Target == left);
            checks.Add($"대기열 돌 3 그대로 ({w.Ragnar.Ai.Queue.Count}칸/{st["queue"]})", w.Ragnar.Ai.Queue.Count.ToString() == st["queue"]);
            if (g == null) yield break;
            if (st.TryGetValue("ptag", out string ptag))
            {
                var cost = PortalBuilder.Cost();
                Inventory inv = w.Ragnar.GetInventory();
                bool ChestEmpty() => cost.TrueForAll(k => GatherTest.Count(w.Chest, k.Item) == 0);
                if (g.PortalCarryTag == null && g.LoadedPortalTag == ptag)
                {
                    // 집 모드(사용자 11-12): 집 곁 나무라 시험이 보기 전에 귀환해 한 몫을 거점 상자로 돌려놓음(PortalHome) — 이어 받은 뒤의 정상 동작. 짝 완성은 집 모드 끔 판·portalbuildtest
                    checks.Add($"포탈 운반 이어 받고 귀환 때 한 몫 상자로 (이름표 {g.LoadedPortalTag}, 포탈 {PortalBuilder.MineWithTag(ptag)}개)",
                        PortalBuilder.MineWithTag(ptag) == 1 && !PortalBuilder.Carrying(inv) && cost.TrueForAll(k => Base(w.Home, k.Item) > 0));
                    ptag = null;
                }
                if (ptag != null) {
                checks.Add($"포탈 운반 이어 감 (이름표 {g.PortalCarryTag}, 포탈 {PortalBuilder.MineWithTag(ptag)}개, 한 몫 {PortalBuilder.Carrying(inv)})",
                    g.PortalCarryTag == ptag && PortalBuilder.MineWithTag(ptag) == 1 && PortalBuilder.Carrying(inv) && ChestEmpty());
                // 도착(시험은 35m 밖 자리로 옮김) → 일터 쪽을 놓아 짝, 재료는 든 한 몫만 씀. 끝나면 원래 자리로 돌려 나무 일을 잇게 함
                Vector3 at = w.Ragnar.transform.position;
                Quaternion rot = w.Ragnar.transform.rotation;
                w.Ragnar.TeleportTo(GatherTest.Ground(w.Home + new Vector3(0f, 0f, 35f)), rot, false);
                yield return new WaitForSeconds(1f);
                g.TestPortalArrive(Senses.ZoneOf(P(st["pfar"])));
                yield return new WaitForSeconds(1f);
                var pair = PortalBuilder.Mine().FindAll(z => z.GetString(ZDOVars.s_tag) == ptag);
                bool linked = pair.Count == 2 && pair[0].GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) == pair[1].m_uid;
                checks.Add($"재시작 뒤 도착해 짝 완성 ({pair.Count}개, 재료 합 그대로)",
                    linked && g.PortalCarryTag == null && !PortalBuilder.Carrying(inv) && ChestEmpty());
                w.Ragnar.TeleportTo(at, rot, false);
                }
            }
            yield return TestKit.WaitFor(() => Base(w.Home, "Stone") >= 3 && w.Ragnar.Ai.Job == null && w.Ragnar.Ai.Queue.Count == 0, 420f);
            int wood = Base(w.Home, "Wood"), stone = Base(w.Home, "Stone");
            checks.Add($"나무·돌 끝까지 ({TestKit.Waited:0}초, 지금 {w.Ragnar.Ai.Current})", TestKit.Ok);
            checks.Add($"수량: 나무 {wood} ≥ 10, 돌 {stone} ≥ 3, 짐에 안 남음 (나무 {Carried(w.Ragnar, "Wood")}·돌 {Carried(w.Ragnar, "Stone")})",
                wood >= 10 && stone >= 3 && Carried(w.Ragnar, "Wood") == 0 && Carried(w.Ragnar, "Stone") == 0);
            checks.Add("상자 사용 중 표시 안 남음", !w.Chest.IsInUse());
        }

        /// <summary>진단(회차 470): 포탈 목록(ZDOMan.m_portalObjects)의 ZDO — 이름·구역 키/실제 구역. 포탈 아닌 것이 끼면 저장 때 00_01 조각에도 들어가 불러오면 둘.</summary>
        static string PortalList()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in ZDOMan.instance.GetPortals())
                foreach (var z in kv.Value)
                {
                    var go = ZNetScene.instance.GetPrefab(z.GetPrefab());
                    sb.Append($"{z.m_uid} {(go ? go.name : z.GetPrefab().ToString())} 키 {kv.Key.Sector}/실제 {z.GetSectorIndex().Sector} {V(z.GetPosition())}\n");
                }
            return sb.ToString();
        }

        /// <summary>진단(회차 469 집 모드 깃발 317→318): 거점 300m 안 깃발 자리·켜짐.</summary>
        static string NearFlags(Vector3 home, float r) =>
            string.Join("\n", BaseFlag.AllFlags().FindAll(z => Vector3.Distance(z.GetPosition(), home) < r)
                .ConvertAll(z => z.m_uid + " " + V(z.GetPosition()) + (z.GetBool(BaseFlag.KeyActive) ? "*" : "")).ToArray());

        static string V(Vector3 v) => System.FormattableString.Invariant($"{v.x:0.###},{v.y:0.###},{v.z:0.###}");

        static Vector3 P(string s)
        {
            var p = s.Split(',');
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(p[0], c), float.Parse(p[1], c), float.Parse(p[2], c));
        }
    }
}
