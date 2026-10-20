using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 콘솔 명령 vh_npctest: ① 단계 첫 조각 시험.
    /// NPC 2명 소환 → 외형·부품 확인 → 채팅 명령(!상태, !Claude 같이 가자, !멈춰, !나무 50, 형식 틀린 ! 문장)을
    /// 실제 채팅 RPC 경로로 보내고, 따라오기·집에 가기·대답을 확인한다. NPC는 시험 뒤에도 월드에 남긴다.
    /// </summary>
    static class NpcTest
    {
        public static void Register()
        {
            new Terminal.ConsoleCommand("vh_npctest", "[Valheim Helper] 일꾼 NPC 시험", args =>
            {
                if (TestRunner.Begin(args.Context, "vh_npctest")) Plugin.Instance.StartCoroutine(Run());
            });
        }

        static readonly List<string> s_heard = new List<string>();
        static void OnHeard(string name, string text) => s_heard.Add($"{name}: {text}");

        static IEnumerator Run()
        {
            var checks = new Checks();
            void Check(string what, bool ok) => checks.Add(what, ok);

            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            s_heard.Clear();
            ChatNet.Heard += OnHeard;
            bool god = player.InGodMode();
            player.SetGodMode(true);

            TestRunner.Log($"프리팹 부품 지움 [{string.Join(", ", WorkerPrefab.Removed)}]");
            TestRunner.Log($"프리팹 부품 남음 [{string.Join(", ", WorkerPrefab.Kept)}]");
            foreach (var n in WorkerNpc.LookHair) TestRunner.Log($"머리 {n} 있음 {ObjectDB.instance.GetItemPrefab(n) != null}");
            foreach (var n in WorkerNpc.LookBeard) TestRunner.Log($"수염 {n} 있음 {ObjectDB.instance.GetItemPrefab(n) != null}");

            // 1) 소환 (이전 시험의 NPC는 지우고 새로)
            int removed = Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            TestRunner.Log($"이전 NPC {removed}명 지움. {Workers.Spawn(out var spawned)}");
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = spawned.Count == 2 ? Workers.Find(spawned[0]) : null;
            WorkerNpc alarak = spawned.Count == 2 ? Workers.Find(spawned[1]) : null;
            Check("NPC 2명 소환", claude != null && alarak != null && WorkerNpc.All.Count == 2);
            if (claude == null || alarak == null) { End(player, god, checks); yield break; }
            Check($"이름 {claude.GetHoverName()}, {alarak.GetHoverName()}", claude.GetHoverName() == Workers.Name1 && alarak.GetHoverName() == Workers.Name2);
            Check($"체력 {claude.GetHealth():0}/{claude.GetMaxHealth():0}", claude.GetHealth() > 0f);
            Check("호스트 소유", claude.IsOwner() && alarak.IsOwner());
            Check("소환 인사", s_heard.Count >= 2);

            // 2) 상태
            Say("!상태");
            yield return new WaitForSeconds(1f);
            Check("!상태 → 둘 다 대답", Count(Workers.Name1) >= 2 && Count(Workers.Name2) >= 2);

            // 3) 따라가기: 1호만 부른 뒤 플레이어가 25m 떨어진 곳으로 이동
            Vector3 alarakStart = alarak.transform.position;
            Say($"!{Workers.Name1} 같이 가자");
            yield return new WaitForSeconds(1f);
            Check("1호만 따라가기 시작", claude.Ai.Current == WorkerAI.Mode.Follow && alarak.Ai.Current == WorkerAI.Mode.Idle);
            if (FindGround(player.transform.position, 25f, out Vector3 spot))
            {
                // 0.1.1 시험은 순간이동이 끝나기 전에 거리를 재서 따라오기·집 복귀를 잘못 통과시켰다.
                // 이제 플레이어가 실제로 도착한 뒤 재고, NPC가 실제로 움직였는지도 본다.
                Vector3 claudeStart = claude.transform.position;
                bool started = player.TeleportTo(spot, player.transform.rotation, false);
                float t = 0f;
                while (t < 15f && (player.IsTeleporting() || Vector3.Distance(player.transform.position, spot) > 3f))
                {
                    yield return new WaitForSeconds(0.5f);
                    t += 0.5f;
                }
                bool arrived = Vector3.Distance(player.transform.position, spot) <= 3f;
                Check($"플레이어 이동 (시작 {started}, {t:0.0}초, NPC와 {Vector3.Distance(claude.transform.position, player.transform.position):0}m)", arrived);
                float best = float.MaxValue;
                for (float w = 0f; arrived && w < 20f; w += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    best = Mathf.Min(best, Vector3.Distance(claude.transform.position, player.transform.position));
                    if (best < 5f) break;
                }
                float claudeMoved = Vector3.Distance(claudeStart, claude.transform.position);
                Check($"1호가 따라옴 (가장 가까이 {best:0.0}m, 이동 {claudeMoved:0}m)", best < 5f && claudeMoved > 10f);
                Check($"2호는 제자리 (움직임 {Vector3.Distance(alarakStart, alarak.transform.position):0.0}m)", Vector3.Distance(alarakStart, alarak.transform.position) < 3f);
            }
            else Check("따라가기 시험할 땅 찾기", false);

            // 4) 멈춰 → 집으로 (집에서 멀리 있었는지도 확인)
            float fromHome = Utils.DistanceXZ(claude.Home, claude.transform.position);
            Say("!멈춰");
            float waited = 0f;
            while (waited < 30f && (claude.Ai.Current != WorkerAI.Mode.Idle || alarak.Ai.Current != WorkerAI.Mode.Idle))
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
            }
            float homeDist = Utils.DistanceXZ(claude.Home, claude.transform.position);
            Check($"!멈춰 → 집 도착 (집에서 {fromHome:0}m → {homeDist:0.0}m, {waited:0}초)", fromHome > 10f && claude.Ai.Current == WorkerAI.Mode.Idle && homeDist < 3f);

            // 5) 아직 못 하는 명령, 형식 틀린 ! 문장
            int before = s_heard.Count;
            Say("!나무 50");
            Say("!나무 좀 많이 가져와");
            yield return new WaitForSeconds(1f);
            Check("!나무 50 / 형식 틀린 ! 문장 → 대답 2개", s_heard.Count - before == 2);

            End(player, god, checks);
        }

        static void Say(string text)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text);
        }

        static int Count(string name) => s_heard.FindAll(h => h.StartsWith(name + ":")).Count;

        // ValueTuple은 쓰지 않는다: 게임 런타임에 System.ValueTuple 어셈블리가 없어 플러그인 로드가 깨졌다(0.1.0).
        sealed class Checks
        {
            public int Total, Passed;
            public void Add(string what, bool ok)
            {
                Total++;
                if (ok) Passed++;
                TestRunner.Log($"{(ok ? "OK  " : "FAIL")} {what}");
            }
        }

        static void End(Player player, bool god, Checks checks)
        {
            ChatNet.Heard -= OnHeard;
            player.SetGodMode(god);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in s_heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }

        static bool FindGround(Vector3 from, float dist, out Vector3 spot)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI / 4f;
                Vector3 p = from + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
                if (!ZoneSystem.instance.GetSolidHeight(p, out float h) || h < water + 1f) continue;
                if (Mathf.Abs(h - from.y) > 6f) continue; // 너무 가파르면 다른 방향
                p.y = h + 0.3f;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }
    }
}
