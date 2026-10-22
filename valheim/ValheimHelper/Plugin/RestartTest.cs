using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ValheimHelper.Core.Npc;

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
            var w = new TestKit.World();
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
            checks.Add("깃발·NPC 2명·상자", w.Claude != null && w.Alarak != null && w.Chest != null);
            if (w.Claude == null || w.Alarak == null || w.Chest == null) yield break;
            if (!Features.SaveWork) { checks.Add("설정 Features.SaveWork 켜짐", false); yield break; }
            string n1 = w.Claude.NpcName;
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            GatherTest.Say($"!{n1} 나무 10", null);
            yield return new WaitForSeconds(1f);
            GatherTest.Say($"!{n1} 돌 3", null); // 급하지 않음 → 대기열
            yield return new WaitForSeconds(1f);
            checks.Add($"나무 10 하는 중·돌 3 대기 ({w.Claude.Ai.Job?.Label}, 대기 {w.Claude.Ai.Queue.Count}칸)",
                w.Claude.Ai.Job is GatherJob gj && gj.Item == "Wood" && w.Claude.Ai.Queue.Count == 1);
            if (!(w.Claude.Ai.Job is GatherJob)) yield break;
            yield return TestKit.WaitFor(() => Carried(w.Claude, "Wood") + Base(w.Home, "Wood") >= 2, 200f);
            checks.Add($"일 중간까지 감 (짐 {Carried(w.Claude, "Wood")}, 상자 {Base(w.Home, "Wood")}, {TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;

            // 이 프레임 안에서 저장·기록·종료(Application.Quit은 프레임 끝에 OnApplicationQuit → 프로필·월드 저장, 그 사이 일꾼 Update 없음)
            var g = w.Claude.Ai.Job as GatherJob;
            checks.Add($"아직 나무 하는 중 ({w.Claude.Ai.Job?.Label})", g != null && g.Item == "Wood");
            if (g == null) yield break;
            WorkSaves.SaveNow(w.Claude);
            WorkSaves.SaveNow(w.Alarak);
            var st = new List<string>
            {
                "home=" + V(w.Home), "chest=" + V(w.Chest.transform.position),
                "left=" + (g.Target - g.Delivered), "carried=" + Carried(w.Claude, "Wood"),
                "stocked=" + Base(w.Home, "Wood"), "queue=" + w.Claude.Ai.Queue.Count,
                "npcs=" + Workers.AllZdos().Count, "flags=" + BaseFlag.AllFlags().Count,
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
            if (!Server(checks)) yield break;
            var st = new Dictionary<string, string>();
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
            try { yield return Body2(w, checks, st); }
            finally { TestKit.Restore(w); }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body2(TestKit.World w, GatherTest.Checks checks, Dictionary<string, string> st)
        {
            int left = int.Parse(st["left"]), carried = int.Parse(st["carried"]), stocked = int.Parse(st["stocked"]);
            float d = Vector3.Distance(w.Player.transform.position, w.Home);
            checks.Add($"플레이어가 저장된 자리 곁에서 시작 (거점까지 {d:0}m)", d < 30f);
            yield return TestKit.WaitFor(() => TestKit.Npc(Personality.Scv) != null && TestKit.Npc(Personality.Alarak) != null, 30f);
            checks.Add($"NPC 2명 다시 나타남 ({TestKit.Waited:0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            w.Claude = TestKit.Npc(Personality.Scv);
            w.Alarak = TestKit.Npc(Personality.Alarak);
            yield return new WaitForSeconds(2f); // 상자·깃발 구역도 불러오게
            int npcs = Workers.AllZdos().Count, flags = BaseFlag.AllFlags().Count;
            checks.Add($"NPC·깃발 수 그대로 (NPC {npcs}/{st["npcs"]}, 깃발 {flags}/{st["flags"]})", npcs.ToString() == st["npcs"] && flags.ToString() == st["flags"]);
            w.Chest = GatherTest.ChestAt(P(st["chest"]));
            checks.Add("상자 그대로 있음", w.Chest != null);
            if (w.Chest == null) yield break;
            checks.Add($"상자 나무 그대로 ({Base(w.Home, "Wood")}/{stocked})", Base(w.Home, "Wood") == stocked);
            checks.Add($"짐 그대로 (나무 {Carried(w.Claude, "Wood")}/{carried}, 도끼 있음)",
                Carried(w.Claude, "Wood") == carried && Carried(w.Claude, "AxeFlint") == 1);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job is GatherJob, 10f); // WorkSaves가 불러오고 한가하면 첫 칸을 꺼냄
            var g = w.Claude.Ai.Job as GatherJob;
            checks.Add($"하던 나무 이어 하기 ({g?.Item} 목표 {g?.Target}, 남았던 {left})", g != null && g.Item == "Wood" && g.Target == left);
            checks.Add($"대기열 돌 3 그대로 ({w.Claude.Ai.Queue.Count}칸/{st["queue"]})", w.Claude.Ai.Queue.Count.ToString() == st["queue"]);
            if (g == null) yield break;
            yield return TestKit.WaitFor(() => Base(w.Home, "Stone") >= 3 && w.Claude.Ai.Job == null && w.Claude.Ai.Queue.Count == 0, 420f);
            int wood = Base(w.Home, "Wood"), stone = Base(w.Home, "Stone");
            checks.Add($"나무·돌 끝까지 ({TestKit.Waited:0}초, 지금 {w.Claude.Ai.Current})", TestKit.Ok);
            checks.Add($"수량: 나무 {wood} ≥ 10, 돌 {stone} ≥ 3, 짐에 안 남음 (나무 {Carried(w.Claude, "Wood")}·돌 {Carried(w.Claude, "Stone")})",
                wood >= 10 && stone >= 3 && Carried(w.Claude, "Wood") == 0 && Carried(w.Claude, "Stone") == 0);
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
