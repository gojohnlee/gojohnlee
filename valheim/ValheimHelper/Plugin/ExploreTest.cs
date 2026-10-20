using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 탐험 시험(② 단계 마무리): 깃발 → "!지식", "!기억" 대답 → 지도 경계가 있는 방향으로 "!Claude 탐험 &lt;방향&gt;" →
    /// NPC가 그 방향으로 150m 넘게 가고 처음 고른 경계 구역이 지도에 밝혀지는지(150초, 경계가 멀면 닿을 때까지 최대 300초) → "!멈춰"로 그만두고 집에 오는지.
    /// 밤이면 탐험을 안 하므로 시험 동안만 낮으로 고정한다. 대사에 "{"가 남지 않았는지(조사 처리)도 본다.
    /// </summary>
    static class ExploreTest
    {
        public static void Register() => TestRunner.Register("vh_exploretest", "새 땅 탐험 시험", Run);

        static int s_total, s_passed;

        static void Check(string what, bool ok)
        {
            s_total++;
            if (ok) s_passed++;
            TestRunner.Log($"{(ok ? "OK  " : "FAIL")} {what}");
        }

        static IEnumerator Run()
        {
            s_total = s_passed = 0;
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            bool god = player.InGodMode();
            player.SetGodMode(true);
            EnvMan env = EnvMan.instance;
            bool debugTime = env.m_debugTimeOfDay;
            float debugValue = env.m_debugTime;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f; // 한낮
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;

            // 1) 깃발과 NPC
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = player.transform.position + player.transform.forward * 4f;
            if (ZoneSystem.instance.GetGroundHeight(home, out float gh)) home.y = gh;
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Scv);
            Check("깃발에 NPC 나타남", claude != null && WorkerNpc.All.Count == 2);
            if (claude == null) { End(player, god, env, debugTime, debugValue, onHeard, heard); yield break; }
            yield return new WaitForSeconds(3f); // 시야가 한 번 돌아 지식·바이옴이 차게
            TestRunner.Log($"지식: {Brain.Get().Count}구역, 여기 바이옴 {BiomeNames.Of(Brain.Get().BiomeOf(Senses.ZoneOf(claude.transform.position)))}");

            // 2) !지식, !기억
            int before = heard.Count;
            Say("!지식");
            yield return new WaitForSeconds(1f);
            Check("!지식 → 대답", heard.Count > before);
            before = heard.Count;
            Say("!기억");
            yield return new WaitForSeconds(1f);
            Check("!기억 → 대답", heard.Count > before);

            // 3) 지도 경계가 있는 방향 고르기
            string[] names = { "east", "north", "west", "south", "northeast", "northwest", "southeast", "southwest" };
            string dirName = null;
            int dx = 0, dy = 0;
            ZoneXY first = default;
            foreach (var n in names)
            {
                Bearing.Vector(n, out dx, out dy);
                if (Scout.NextFrontier(claude, dx, dy, 0, out first)) { dirName = n; break; }
            }
            Check($"지도 경계 있음 ({(dirName != null ? Bearing.KoreanOf(dirName) + $", 첫 구역 {first} 거점에서 {Utils.DistanceXZ(home, Senses.ZoneCenter(first)):0}m" : "없음")})", dirName != null);
            if (dirName == null) { End(player, god, env, debugTime, debugValue, onHeard, heard); yield break; }

            // 4) 탐험
            Say($"!{Workers.Name1} 탐험 {Bearing.KoreanOf(dirName)}");
            yield return new WaitForSeconds(1f);
            Check("1호가 탐험 시작", claude.Ai.Current == WorkerAI.Mode.Work && claude.Ai.Job is ExploreJob);
            float maxDist = 0f, t = 0f;
            Vector3 last = claude.transform.position;
            while ((t < 150f || (t < 300f && !Scout.Explored(first))) && claude != null && claude.Ai.Current == WorkerAI.Mode.Work)
            {
                yield return new WaitForSeconds(5f);
                t += 5f;
                if (claude == null) break;
                Vector3 p = claude.transform.position;
                maxDist = Mathf.Max(maxDist, Utils.DistanceXZ(home, p));
                Vector3 prev = last;
                last = p;
                if ((int)t % 15 == 0)
                {
                    var job = claude.Ai.Job as ExploreJob;
                    Scout.FromHome(home, p, out string bd, out string dist);
                    TestRunner.Log($"[{t:0}s] 거점에서 {bd} {dist}m, 속도 {Utils.DistanceXZ(prev, p) / 5f:0.0}m/s, 달림 {claude.IsRunning()}, {BiomeNames.Of(Scout.BiomeAt(p))}, {claude.MoveDebug}, 길 {claude.Ai.PathFound}, 상태 {claude.Ai.Current}/{job?.StepName}, 지나간 구역 {job?.Visited}");
                }
            }
            Check($"150m 넘게 나감 (최대 {maxDist:0}m)", maxDist > 150f);
            bool rightWay = false;
            if (claude != null)
            {
                Vector3 off = claude.transform.position - home;
                float len = Mathf.Sqrt(off.x * off.x + off.z * off.z), dl = Mathf.Sqrt(dx * dx + dy * dy);
                rightWay = len > 1f && (off.x * dx + off.z * dy) / (len * dl) > 0.5f; // 60° 안
            }
            Check($"{Bearing.KoreanOf(dirName)}으로 감", rightWay);
            Check($"처음 경계 구역 {first}이 지도에 밝혀짐", Scout.Explored(first));

            // 5) 멈춰 → 집
            Say("!멈춰");
            yield return new WaitForSeconds(1f);
            Check("!멈춰 → 탐험 그만", claude != null && claude.Ai.Current != WorkerAI.Mode.Work);
            float wait = 0f;
            // 걸어서 온다(400m면 1분 넘게 걸린다). 길이 막히면 WorkerAI가 15초 뒤 거점으로 옮긴다.
            float homeLimit = claude != null ? Mathf.Max(60f, Utils.DistanceXZ(claude.Home, claude.transform.position) / 2.5f) : 0f;
            while (wait < homeLimit && claude != null && claude.Ai.Current == WorkerAI.Mode.Home)
            {
                yield return new WaitForSeconds(2f);
                wait += 2f;
            }
            Check($"집 도착 ({wait:0}초, 남은 거리 {(claude ? Utils.DistanceXZ(claude.Home, claude.transform.position) : -1):0}m)", claude != null && Utils.DistanceXZ(claude.Home, claude.transform.position) < 5f);

            // 6) 채집: 아는 곳을 다 둘러봤으면 새 땅으로 (거점 둘레 밝혀진 구역을 "둘러봄"으로 표시하고, 모르는 맨손 채집물을 시킨다)
            Knowledge k = Brain.Get();
            ZoneXY hz = Senses.ZoneOf(home);
            for (int y = -10; y <= 10; y++)
                for (int x = -10; x <= 10; x++)
                {
                    var z = new ZoneXY(hz.X + x, hz.Y + y);
                    if (Scout.Explored(z)) k.MarkLooked(z);
                }
            // 앞 시험의 탐험에서 봤을 수 있으니 그 물건의 기억은 지운다(시험 월드)
            string item = null;
            foreach (var c in new[] { "Thistle", "Dandelion", "Mushroom", "Raspberry", "Flint" })
                if (ResourceCatalog.WaysFor(c).Contains(ToolKind.None)) { item = c; break; }
            if (item != null) foreach (var m in k.ZonesWith(item, hz)) k.Forget(m.Zone, item);
            Check($"모르는 맨손 채집물 ({item ?? "없음"})", item != null);
            if (item != null && claude != null)
            {
                Say($"!{Workers.Name1} {GatherJob.ItemLabel(item)} 1");
                bool exploring = false, found = false;
                for (float w = 0f; w < 40f && claude != null; w += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    var gj = claude.Ai.Job as GatherJob;
                    if (gj == null) { found = claude.Ai.Current != WorkerAI.Mode.Work; break; } // 끝났다(바로 보여서 주웠거나 실패)
                    if (gj.Exploring) { exploring = true; break; }
                    if (gj.StepName == "Approach" || gj.StepName == "Work") found = true; // 시야에 있어서 바로 감
                }
                TestRunner.Log($"채집 {item}: 새 땅 탐험 {exploring}, 시야에서 찾음 {found}, 상태 {(claude ? claude.Ai.Current + "/" + claude.Ai.Job?.StepName : "없음")}");
                Check("아는 곳에 없으면 새 땅으로 (또는 바로 보여서 감)", exploring || found);
                Say("!멈춰");
                yield return new WaitForSeconds(1f);
            }

            bool clean = true;
            foreach (var h in heard) if (h.Contains("{")) clean = false;
            Check("대사에 자리표시자 안 남음", clean);
            End(player, god, env, debugTime, debugValue, onHeard, heard);
        }

        static void Say(string text)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text, ZDOID.None);
        }

        static void End(Player player, bool god, EnvMan env, bool debugTime, float debugValue, System.Action<string, string> onHeard, List<string> heard)
        {
            ChatNet.Heard -= onHeard;
            player.SetGodMode(god);
            env.m_debugTimeOfDay = debugTime;
            env.m_debugTime = debugValue;
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(s_passed == s_total ? "PASS" : "FAIL")} ({s_passed}/{s_total})");
            TestRunner.Finish();
        }
    }
}
