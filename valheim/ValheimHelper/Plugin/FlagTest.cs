using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 콘솔 명령 vh_flagtest: 거점 깃발 시험.
    /// 망치 목록 확인 → NPC를 지운 뒤 깃발 A를 실제 건축 경로(Player.PlacePiece)로 세움 → 둘이 A에 나타나는지 →
    /// 15m 떨어진 곳에 깃발 B → 거점이 B로 옮겨 가고 둘이 걸어가는지, A는 "쓰지 않음"이 되는지.
    /// 깃발과 NPC는 시험 뒤에도 남긴다(직접 볼 수 있게).
    /// </summary>
    static class FlagTest
    {
        public static void Register()
        {
            TestRunner.Register("vh_flagtest", "거점 깃발 시험", Run);
        }

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

        static IEnumerator Run()
        {
            var checks = new Checks();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            bool god = player.InGodMode();
            player.SetGodMode(true);

            // 다음에 겉모습 고를 때 참고: 깃발로 쓸 만한 바닐라 건축물 이름
            var names = new System.Collections.Generic.List<string>();
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                if (p == null || p.GetComponent<Piece>() == null) continue;
                string n = p.name.ToLowerInvariant();
                if (n.Contains("banner") || n.Contains("flag") || n.Contains("pole") || n.Contains("sign") || n.Contains("totem") || n.Contains("standard"))
                    names.Add(p.name);
            }
            TestRunner.Log("후보 건축물: " + string.Join(", ", names));

            Piece piece = BaseFlag.Prefab != null ? BaseFlag.Prefab.GetComponent<Piece>() : null;
            checks.Add($"깃발 프리팹 ({BaseFlag.SourceUsed} 복제)", piece != null);
            if (piece == null) { End(player, god, checks); yield break; }
            checks.Add("망치 목록에 있음", BaseFlag.InHammer());
            checks.Add("플레이어가 지을 수 있는 것으로 앎", player.HaveRequirements(piece, Player.RequirementMode.IsKnown));
            TestRunner.Log($"재료: {string.Join(", ", System.Array.ConvertAll(piece.m_resources, r => r.m_resItem.name + " x" + r.m_amount))}");

            int removed = Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            TestRunner.Log($"이전 NPC {removed}명 지움");

            // 깃발 A
            Vector3 a = player.transform.position + player.transform.forward * 4f;
            if (ZoneSystem.instance.GetGroundHeight(a, out float ha)) a.y = ha; // 지형(바위·나무 위 말고)
            player.PlacePiece(piece, a, player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(3f);
            ZDO flagA = Nearest(a);
            checks.Add("깃발 A 세워짐·활성", flagA != null && flagA.GetBool(BaseFlag.KeyActive));
            var zdos = Workers.AllZdos();
            // 수평 거리로 본다(0.3.2: 높이 차이로 3D 거리가 커서 거짓 실패)
            checks.Add($"깃발 A에 NPC 나타남 ({zdos.Count}명)", zdos.Count == 2 && zdos.TrueForAll(z => Utils.DistanceXZ(z.GetPosition(), a) < 5f));
            checks.Add("NPC 인스턴스 2명", WorkerNpc.All.Count == 2);

            // 깃발 B (15m 떨어진 곳)
            if (!NpcTestGround(a, 15f, out Vector3 b)) { checks.Add("깃발 B 자리 찾기", false); End(player, god, checks); yield break; }
            player.PlacePiece(piece, b, player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(1f);
            ZDO flagB = Nearest(b);
            checks.Add("깃발 B 활성, A 비활성", flagB != null && flagB.GetBool(BaseFlag.KeyActive) && flagA != null && !flagA.GetBool(BaseFlag.KeyActive));
            float waited = 0f;
            bool allHome = false;
            while (waited < 30f && !allHome)
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
                allHome = WorkerNpc.All.Count == 2 && WorkerNpc.All.TrueForAll(n => Utils.DistanceXZ(n.Home, n.transform.position) < 2.5f && Utils.DistanceXZ(n.Home, b) < 4f);
            }
            checks.Add($"둘 다 깃발 B로 이사 ({waited:0}초)", allHome);
            foreach (var n in WorkerNpc.All) TestRunner.Log($"{n.NpcName}: 깃발 B에서 수평 {Utils.DistanceXZ(n.transform.position, b):0.0}m·높이차 {n.transform.position.y - b.y:0.0}m, 상태 {n.Ai.Current}");

            End(player, god, checks);
        }

        static bool NpcTestGround(Vector3 from, float dist, out Vector3 spot)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI / 4f;
                Vector3 p = from + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
                if (!ZoneSystem.instance.GetGroundHeight(p, out float h) || h < water + 1f || Mathf.Abs(h - from.y) > 5f) continue;
                p.y = h;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        static ZDO Nearest(Vector3 pos)
        {
            ZDO best = null;
            float bestD = 3f;
            foreach (var z in BaseFlag.AllFlags())
            {
                float d = Vector3.Distance(z.GetPosition(), pos);
                if (d < bestD) { best = z; bestD = d; }
            }
            return best;
        }

        static void End(Player player, bool god, Checks checks)
        {
            player.SetGodMode(god);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
