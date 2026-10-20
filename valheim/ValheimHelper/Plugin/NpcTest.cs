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
            var checks = new List<(string, bool)>();
            void Check(string what, bool ok) { checks.Add((what, ok)); TestRunner.Log($"{(ok ? "OK  " : "FAIL")} {what}"); }

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
            Check("NPC 2명 소환", spawned.Count == 2 && WorkerNpc.All.Count == 2);
            if (spawned.Count != 2) { End(player, god, checks); yield break; }
            WorkerNpc claude = spawned[0], alarak = spawned[1];
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
                player.TeleportTo(spot, player.transform.rotation, false);
                TestRunner.Log($"플레이어 이동 {Vector3.Distance(claude.transform.position, spot):0}m 앞으로");
                float best = float.MaxValue;
                for (float t = 0f; t < 20f; t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    best = Mathf.Min(best, Vector3.Distance(claude.transform.position, player.transform.position));
                    if (best < 5f) break;
                }
                Check($"1호가 따라옴 (가장 가까이 {best:0.0}m)", best < 5f);
                Check($"2호는 제자리 (움직임 {Vector3.Distance(alarakStart, alarak.transform.position):0.0}m)", Vector3.Distance(alarakStart, alarak.transform.position) < 3f);
            }
            else Check("따라가기 시험할 땅 찾기", false);

            // 4) 멈춰 → 집으로
            Say("!멈춰");
            float waited = 0f;
            while (waited < 30f && (claude.Ai.Current != WorkerAI.Mode.Idle || alarak.Ai.Current != WorkerAI.Mode.Idle))
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
            }
            float homeDist = Utils.DistanceXZ(claude.Home, claude.transform.position);
            Check($"!멈춰 → 집 도착 ({waited:0}초, 집까지 {homeDist:0.0}m)", claude.Ai.Current == WorkerAI.Mode.Idle && homeDist < 3f);

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

        static void End(Player player, bool god, List<(string what, bool ok)> checks)
        {
            ChatNet.Heard -= OnHeard;
            player.SetGodMode(god);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in s_heard) TestRunner.Log(h);
            int ok = checks.FindAll(c => c.ok).Count;
            TestRunner.Log($"판정: {(ok == checks.Count ? "PASS" : "FAIL")} ({ok}/{checks.Count})");
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
