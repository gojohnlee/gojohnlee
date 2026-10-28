using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_mychesttest (상자 기능 ③ 나-1·나-2): 챗으로 내 개인 상자 준비. 개인 상자가 없으면 "없어" 말·안 적음, "!준비 나무 5" → 쉬는 일꾼이 걸어서 거점 상자에서 5개만 내 개인 상자로,
    /// "!채워 개인 돌 4"(플레이어 멀면 바로) → 4개 유지(빼면 다시 채움), 상자에 모자란 한 번 준비는 있는 만큼 넣고 "모자라" 말·지움(구하러 못 갈 때), 구할 수 있으면 자동 보충이 채집해 와서 개인 상자로(나-2), "!채워 개인 끔" → 다 지움.
    /// 남이 지은 개인 상자·총량은 그대로.
    /// </summary>
    static class MyChestTest
    {
        public static void Register() => TestRunner.Register("vh_mychesttest", "챗으로 내 개인 상자 준비 시험", () => TestKit.Flat(Run()));

        static long s_owner;

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
            var made = new List<Container>();
            try { yield return Body(w, checks, made); }
            finally
            {
                Undo(made);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void Undo(List<Container> made)
        {
            PersonalChests.TestAllow = false;
            PersonalChests.CheckEvery = 60f;
            GatherJob.TestIgnorePlayers = false;
            if (s_owner != 0) Brain.Personal.Off(s_owner);
            foreach (var c in made) if (c != null) ZNetScene.instance.Destroy(c.gameObject);
        }

        static Container Chest(TestKit.World w, List<Container> made, Vector3 at)
        {
            at = GatherTest.Ground(at);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), at, w.Player.transform.rotation, doAttack: false);
            Container c = GatherTest.ChestAt(at);
            if (c != null) made.Add(c);
            return c;
        }

        static int N(Container c, string p) => GatherTest.Count(c, p);

        static int Bag(string p)
        {
            int s = 0;
            foreach (var n in WorkerNpc.All) if (n != null) s += Tidy.Count(n.GetInventory(), p);
            return s;
        }

        static WorkerNpc Filler()
        {
            foreach (var n in WorkerNpc.All) if (n != null && n.Ai != null && n.Ai.Job is TidyJob) return n;
            return null;
        }

        static void Say(Speaker who, string text)
        {
            TestRunner.Log($"> {text}");
            Commands.Handle(who, text);
        }

        static bool HeardAny(TestKit.World w, int from, params string[] parts)
        {
            for (int i = from; i < w.Heard.Count; i++)
                foreach (var p in parts) if (w.Heard[i].Contains(p)) return true;
            return false;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<Container> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);
            if (w.Ragnar == null || w.Chest == null) yield break;
            TestKit.ClearBaseStock(w, "Wood", "Stone");
            var who = new Speaker(w.Player.GetPlayerName(), w.Player.GetZDOID(), ZDOID.None);
            s_owner = PersonalChests.OwnerOf(who);
            Brain.Personal.Off(s_owner);
            Vector3 f = w.Player.transform.forward, r = Vector3.Cross(Vector3.up, f).normalized;
            Container src = Chest(w, made, w.Home + f * 6f + r * 3f);
            Container mine = Chest(w, made, w.Home + f * 12f - r * 4f);
            Container other = Chest(w, made, w.Home + f * 12f + r * 4f);
            yield return new WaitForSeconds(1f);
            checks.Add("시험 상자 셋", src != null && mine != null && other != null);
            if (src == null || mine == null || other == null) yield break;
            ZDO mineZdo = mine.GetComponent<ZNetView>().GetZDO(), otherZdo = other.GetComponent<ZNetView>().GetZDO();
            otherZdo.Set("ChestName", "개인_남");
            otherZdo.Set(ZDOVars.s_creator, 777L); // 남이 지은 개인 상자
            TestKit.PutInChest(src, "Wood", 20);
            TestKit.PutInChest(src, "Stone", 10);
            yield return new WaitForSeconds(0.6f);
            checks.Add($"내 플레이어 ID·내가 지은 상자 ({s_owner}, 만든 이 {mineZdo.GetLong(ZDOVars.s_creator, 0L)})", s_owner != 0 && mineZdo.GetLong(ZDOVars.s_creator, 0L) == s_owner);
            int wood0 = N(src, "Wood"), stone0 = N(src, "Stone");
            PersonalChests.TestAllow = true;
            PersonalChests.CheckEvery = 2f;

            // 0) 내 개인 상자가 없으면(이름 없음) 말하고 안 적음
            int h0 = w.Heard.Count;
            Say(who, "!채워 개인 나무 5");
            yield return new WaitForSeconds(1f);
            checks.Add($"개인 상자 없음 → 말·안 적음 (적힘 {Brain.Personal.Find(s_owner, "Wood") != null})",
                Brain.Personal.Find(s_owner, "Wood") == null && HeardAny(w, h0, "\"개인\""));

            // 1) 한 번 준비: 플레이어 근처 → 쉬는 일꾼이 걸어서 5개만
            mineZdo.Set("ChestName", "개인_시험");
            yield return new WaitForSeconds(0.3f);
            Say(who, "!준비 나무 5");
            checks.Add("한 번 준비 적힘", Brain.Personal.Find(s_owner, "Wood") != null);
            WorkerNpc t = null;
            for (float s = 0f; s < 10f && t == null; s += 0.2f) { yield return new WaitForSeconds(0.2f); t = Filler(); }
            checks.Add($"쉬던 일꾼이 걸어서 채우러 감 ({(t != null ? t.NpcName + " " + t.Ai.Job.StateLine() : "없음")})", t != null);
            if (t == null) yield break;
            Vector3 start = t.transform.position;
            float walked = 0f, sec = 0f;
            for (; sec < 120f && t.Ai.Job is TidyJob; sec += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                walked = Mathf.Max(walked, Utils.DistanceXZ(start, t.transform.position));
            }
            TestRunner.Log($"채움 끝 {sec:0}초, 가장 멀리 {walked:0.0}m, 거점 상자 나무 {N(src, "Wood")}, 내 상자 {N(mine, "Wood")}, 남 상자 {N(other, "Wood")}");
            checks.Add($"걸어서 끝남 ({sec:0}초/120, 움직임 {walked:0.0}m)", !(t.Ai.Job is TidyJob) && walked > 2f);
            checks.Add($"내 개인 상자에 5개만 (내 상자 {N(mine, "Wood")}/5, 거점 상자 {N(src, "Wood")}/{wood0 - 5}, 남 상자 {N(other, "Wood")}/0, 짐 {Bag("Wood")})",
                N(mine, "Wood") == 5 && N(src, "Wood") == wood0 - 5 && N(other, "Wood") == 0 && Bag("Wood") == 0);
            yield return new WaitForSeconds(2.5f);
            checks.Add($"다 찬 한 번 준비는 지움 ({Brain.Personal.Find(s_owner, "Wood") != null})", Brain.Personal.Find(s_owner, "Wood") == null);

            // 2) 유지: 플레이어 멀면 걷지 않고 바로, 빼면 다시 채움
            GatherJob.TestIgnorePlayers = true;
            GatherJob.TestFakePlayer = null;
            Say(who, "!계속채워 개인 돌 4");
            bool walkedJob = false;
            for (float s = 0f; s < 3f; s += 0.2f) { yield return new WaitForSeconds(0.2f); if (Filler() != null) walkedJob = true; }
            checks.Add($"유지 4개, 멀면 바로 (걸음 {walkedJob}, 내 상자 돌 {N(mine, "Stone")}/4, 거점 {N(src, "Stone")}/{stone0 - 4}, 남게 적힘 {Brain.Personal.Find(s_owner, "Stone") != null})",
                !walkedJob && N(mine, "Stone") == 4 && N(src, "Stone") == stone0 - 4 && Brain.Personal.Find(s_owner, "Stone") != null);
            string stoneName = ObjectDB.instance.GetItemPrefab("Stone").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            Chests.TryEdit(mine, inv => inv.RemoveItem(stoneName, 3)); // 사람이 3개 씀
            PersonalChests.TestNow();
            yield return new WaitForSeconds(1f);
            checks.Add($"쓴 만큼 다시 채움 (내 상자 돌 {N(mine, "Stone")}/4, 거점 {N(src, "Stone")}/{stone0 - 7})", N(mine, "Stone") == 4 && N(src, "Stone") == stone0 - 7);

            // 3) 거점 상자에 모자란 한 번 준비, 구하러도 못 감(출발 판단이 막은 것처럼): 있는 만큼 넣고 "모자라"·지움
            int h3 = w.Heard.Count;
            Departures.TestSkip("Wood", true);
            Say(who, "!채워 개인 나무 30");
            yield return new WaitForSeconds(5f);
            checks.Add($"있는 만큼 넣음 (내 상자 나무 {N(mine, "Wood")}/{wood0}, 거점 {N(src, "Wood")}/0)", N(mine, "Wood") == wood0 && N(src, "Wood") == 0);
            checks.Add($"모자라 말·지움 (적힘 {Brain.Personal.Find(s_owner, "Wood") != null})", Brain.Personal.Find(s_owner, "Wood") == null && HeardAny(w, h3, "모자라", "더 없어"));

            // 3-2) 나-2: 거점 상자에 없지만 구할 수 있으면 자동 보충이 채집해 오고 → 개인 상자로
            Departures.TestSkip("Wood", false);
            int base0 = GatherTest.CountBase(w.Home, "Wood"), mine0 = N(mine, "Wood");
            Say(who, $"!준비 나무 {mine0 + 3}");
            yield return new WaitForSeconds(3f);
            checks.Add($"구할 수 있는 건 기다림 (적힘 {Brain.Personal.Find(s_owner, "Wood") != null}, 거점 나무 {base0})", Brain.Personal.Find(s_owner, "Wood") != null);
            Departures.TestRefillNow();
            WorkerNpc g = null;
            foreach (var n in WorkerNpc.All) if (n != null && n.Ai != null && n.Ai.Job is GatherJob gj && gj.Item == "Wood") g = n;
            checks.Add($"모자란 3개 채집하러 감 ({(g != null ? g.NpcName + " 목표 " + ((GatherJob)g.Ai.Job).Target : "없음")})", g != null && ((GatherJob)g.Ai.Job).Target == 3);
            if (g != null)
            {
                float gs = 0f;
                for (; gs < 240f && g.Ai.Job is GatherJob; gs += 0.5f) yield return new WaitForSeconds(0.5f);
                TestRunner.Log($"채집 끝 {gs:0}초, 거점 나무 {GatherTest.CountBase(w.Home, "Wood")}");
                for (float s = 0f; s < 20f && Brain.Personal.Find(s_owner, "Wood") != null; s += 0.5f) { PersonalChests.TestNow(); yield return new WaitForSeconds(0.5f); }
                checks.Add($"구해 온 것 내 상자로 (내 상자 나무 {N(mine, "Wood")}/{mine0 + 3}, 지움 {Brain.Personal.Find(s_owner, "Wood") == null})",
                    N(mine, "Wood") == mine0 + 3 && Brain.Personal.Find(s_owner, "Wood") == null);
            }
            int gathered = N(mine, "Wood") - mine0;

            // 4) 끔 → 그 사람 것 전부 지움
            Say(who, "!계속채워 개인 끔");
            yield return new WaitForSeconds(0.5f);
            int left = 0;
            foreach (var e in Brain.Personal.Entries) if (e.Owner == s_owner) left++;
            checks.Add($"끔 → 다 지움 (남은 {left})", left == 0);
            GatherJob.TestIgnorePlayers = false;

            int woodAll = N(src, "Wood") + N(mine, "Wood") + N(other, "Wood") + Bag("Wood");
            int stoneAll = N(src, "Stone") + N(mine, "Stone") + N(other, "Stone") + Bag("Stone");
            checks.Add($"총량 그대로·남 상자 그대로 (나무 {woodAll}/{wood0 + gathered}, 돌 {stoneAll}/{stone0 - 3}, 남 상자 {N(other, "Wood") + N(other, "Stone")}/0)",
                woodAll == wood0 + gathered && stoneAll == stone0 - 3 && N(other, "Wood") + N(other, "Stone") == 0);
            foreach (var c in made) foreach (var p in new[] { "Wood", "Stone" }) TestKit.TakeFromChest(c, p);
        }
    }
}
