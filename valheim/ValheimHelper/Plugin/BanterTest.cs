using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_bantertest (잡담 나)): 둘이 쉬고 플레이어 곁 → 주고받기(다른 NPC 두 줄, 말풍선만·채팅창엔 없음, 말하는 동안 서로 바라봄 30° 안),
    /// 한 명 죽였다 살림 → 10초 안에 상대 반응, 상대 멀리 → 바로 반응 없고 다음 잡담에 소식, 플레이어 40m 밖 → 잡담 없음.
    /// 잡담 때(약 2분)는 시험이 WorkerBanter.DueNow로 당긴다(고르기·간격은 Core BanterTests).
    /// </summary>
    static class BanterTest
    {
        public static void Register() => TestRunner.Register("vh_bantertest", "일꾼 잡담 시험", () => TestKit.Flat(Run()));

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
            var bubbles = new List<string>();
            System.Action<string, string> onBubble = (n, t) => bubbles.Add(n + ": " + t);
            ChatNet.Bubbled += onBubble;
            try { yield return Body(w, checks, bubbles); }
            finally
            {
                ChatNet.Bubbled -= onBubble;
                WorkerBanter.ResetForTest(false);
                TestKit.Restore(w);
            }
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static bool Resting(WorkerNpc n) => n != null && !n.IsDead() && !n.Ai.GuardBusy && n.Ai.Current == WorkerAI.Mode.Idle;

        static float FaceAngle(WorkerNpc a, WorkerNpc b)
        {
            Vector3 to = b.transform.position - a.transform.position;
            to.y = 0f;
            Vector3 f = a.transform.forward;
            f.y = 0f;
            return to.sqrMagnitude < 0.01f ? 0f : Vector3.Angle(f, to);
        }

        /// <summary>죽여도 묘비가 안 생기게(묘비 회수 일로 쉬는 상태가 깨지지 않게) 가방을 비우고 죽인다.</summary>
        static void Kill(WorkerNpc n)
        {
            n.UnequipAllItems();
            n.GetInventory().RemoveAll();
            var hit = new HitData { m_point = n.GetCenterPoint(), m_dir = Vector3.forward };
            hit.m_damage.m_damage = 9999f;
            n.Damage(hit);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<string> bubbles)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명", w.Ragnar != null && w.Floki != null);
            if (w.Ragnar == null || w.Floki == null) yield break;
            WorkerNpc r = w.Ragnar, f = w.Floki;
            string rn = r.NpcName, fn = f.NpcName;
            yield return TestKit.WaitFor(() => Resting(r) && Resting(f), 15f);
            checks.Add($"둘 다 쉬는 중 ({r.Ai.Current}/{f.Ai.Current})", TestKit.Ok);

            // 1) 주고받기
            bubbles.Clear();
            WorkerBanter.SaidKeys.Clear();
            WorkerBanter.ResetForTest(true);
            yield return TestKit.WaitFor(() => bubbles.Count >= 1, 5f);
            yield return new WaitForSeconds(1.5f);
            float ar = FaceAngle(r, f), af = FaceAngle(f, r);
            yield return TestKit.WaitFor(() => bubbles.Count >= 2, 10f);
            bool two = bubbles.Count >= 2 && bubbles[0].Split(':')[0] != bubbles[1].Split(':')[0];
            checks.Add($"주고받기: 다른 NPC 두 줄 ({string.Join(" / ", bubbles.ToArray())})", two);
            bool notChat = true;
            foreach (var b in bubbles) if (w.Heard.FindAll(h => h == b).Count != 1) notChat = false; // 채팅창(VH_Say)으로도 왔으면 Heard에 두 번
            checks.Add("잡담은 말풍선만(채팅창엔 없음)", bubbles.Count > 0 && notChat);
            checks.Add($"말하는 동안 서로 바라봄 ({ar:0}°/{af:0}°)", ar < 30f && af < 30f);
            TestRunner.Log("DIAG 잡담 키: " + string.Join(", ", WorkerBanter.SaidKeys.ToArray()));
            yield return new WaitForSeconds(4f);

            // 2) 곁에서 죽음 → 상대 반응
            WorkerBanter.ResetForTest(false);
            bubbles.Clear();
            WorkerBanter.SaidKeys.Clear();
            Kill(f);
            yield return new WaitForSeconds(1f);
            f = TestKit.Npc(Core.Npc.Personality.Floki) ?? f;
            yield return TestKit.WaitFor(() => WorkerBanter.SaidKeys.Exists(k => k == rn + ":bt.ev.died.react"), 10f);
            checks.Add($"죽었다 살아나면 {TestKit.Waited:0.0}초 뒤 상대 반응 ({string.Join(", ", WorkerBanter.SaidKeys.ToArray())})", TestKit.Ok);
            yield return TestKit.WaitFor(() => WorkerBanter.SaidKeys.Exists(k => k == fn + ":bt.ev.died.self"), 6f);
            checks.Add("겪은 쪽이 받음", TestKit.Ok);
            yield return new WaitForSeconds(3f);

            // 3) 상대가 멀리 있을 때 죽음 → 바로 반응 없음 → 돌아와 다음 잡담에 소식
            WorkerBanter.ResetForTest(false);
            WorkerBanter.SaidKeys.Clear();
            Vector3 away = GatherTest.Ground(w.Home + w.Player.transform.forward * 60f);
            r.TeleportTo(away + Vector3.up * 0.3f, r.transform.rotation, false);
            yield return new WaitForSeconds(2f);
            checks.Add($"1호 멀리 옮겨짐 ({Utils.DistanceXZ(r.transform.position, w.Home):0}m)", Utils.DistanceXZ(r.transform.position, w.Home) > 40f);
            Kill(f);
            yield return new WaitForSeconds(1f);
            f = TestKit.Npc(Core.Npc.Personality.Floki) ?? f;
            yield return new WaitForSeconds(7f);
            checks.Add($"상대 멀리면 바로 반응 없음 ({string.Join(", ", WorkerBanter.SaidKeys.ToArray())})", WorkerBanter.SaidKeys.Count == 0);
            r.TeleportTo(GatherTest.Ground(f.transform.position + w.Player.transform.right * 3f) + Vector3.up * 0.3f, r.transform.rotation, false);
            yield return new WaitForSeconds(2f);
            yield return TestKit.WaitFor(() => Resting(r) && Resting(f), 10f);
            WorkerBanter.DueNow();
            yield return TestKit.WaitFor(() => WorkerBanter.SaidKeys.Count >= 2, 10f);
            string first = WorkerBanter.SaidKeys.Count > 0 ? WorkerBanter.SaidKeys[0] : "없음";
            checks.Add($"다음 잡담에 소식 먼저 ({first})", first == rn + ":bt.ev.died.news");
            yield return new WaitForSeconds(4f);

            // 4) 플레이어가 40m 밖 → 잡담 없음
            WorkerBanter.SaidKeys.Clear();
            Vector3 far = GatherTest.Ground(w.Home - w.Player.transform.forward * 45f);
            Vector3 side = w.Player.transform.right * 1.5f;
            r.TeleportTo(GatherTest.Ground(far + side) + Vector3.up * 0.3f, r.transform.rotation, false);
            f.TeleportTo(GatherTest.Ground(far - side) + Vector3.up * 0.3f, f.transform.rotation, false);
            yield return new WaitForSeconds(2f);
            float pd = Mathf.Min(Vector3.Distance(w.Player.transform.position, r.transform.position), Vector3.Distance(w.Player.transform.position, f.transform.position));
            checks.Add($"둘 다 플레이어 40m 밖 ({pd:0}m)", pd > 40f);
            WorkerBanter.DueNow();
            yield return new WaitForSeconds(10f);
            checks.Add($"플레이어 멀면 잡담 없음 ({string.Join(", ", WorkerBanter.SaidKeys.ToArray())})", WorkerBanter.SaidKeys.Count == 0);
        }
    }
}
