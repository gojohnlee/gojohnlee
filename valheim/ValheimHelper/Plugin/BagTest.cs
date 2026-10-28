using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 다른 짐 시험(ChatGPT-02): 1호 가방을 나무로 운반 한도까지 채우고 뿔 곡괭이를 쥐여 준 뒤 "!라그나 돌 3".
    /// 1) 거점 상자 둘 다 꽉 참 → 짐을 못 넣으니 준비·귀환을 되풀이하지 않고 "넣을 데가 없어요"라고 알리고 끝내는지.
    /// 2) 상자를 비움 → 나무를 상자에 넣고(짐 정리) 돌을 모아 오는지. 물건 총량이 그대로인지.
    /// </summary>
    static class BagTest
    {
        public static void Register() => TestRunner.Register("vh_bagtest", "다른 짐으로 가방이 찬 채 채집 시험", Run);

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            bool god = player.InGodMode();
            player.SetGodMode(true);
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;

            GameObject wood = ObjectDB.instance.GetItemPrefab("Wood"), pick = ObjectDB.instance.GetItemPrefab("PickaxeAntler");
            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            checks.Add("프리팹 있음", wood != null && pick != null && chestPrefab != null && BaseFlag.Prefab != null);
            if (wood == null || pick == null || chestPrefab == null || BaseFlag.Prefab == null) { End(player, god, checks, onHeard, heard); yield break; }

            // 깃발, NPC, 상자 2개(나무·돌)
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
            Quaternion rot = player.transform.rotation;
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f); // 둘레 생물(적·동료 해골)을 치우고 시작(사용자 결정 2026-10-22 시험 환경)
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Ragnar);
            checks.Add("깃발에 NPC 나타남", claude != null && WorkerNpc.All.Count == 2);
            if (claude == null) { End(player, god, checks, onHeard, heard); yield break; }
            if (GatherTest.ClearOldChests(home) > 0) yield return new WaitForSeconds(0.5f); // 없앤 상자는 프레임 끝에 사라짐: 그 전에 땅 높이를 재면 옛 상자 위에 놓인다
            Piece chestPiece = chestPrefab.GetComponent<Piece>();
            Vector3 woodPos = GatherTest.Ground(home + rot * new Vector3(-4f, 0f, -1f));
            Vector3 stonePos = GatherTest.Ground(home + rot * new Vector3(4f, 0f, -1f));
            player.PlacePiece(chestPiece, woodPos, rot, doAttack: false);
            player.PlacePiece(chestPiece, stonePos, rot, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container woodChest = GatherTest.ChestAt(woodPos), stoneChest = GatherTest.ChestAt(stonePos);
            checks.Add("상자 2개 놓임", woodChest != null && stoneChest != null);
            if (woodChest == null || stoneChest == null) { End(player, god, checks, onHeard, heard); yield break; }

            // 가방: 곡괭이 + 나무를 운반 한도까지(무게로 참, 칸은 나무 상자에 다 들어갈 만큼만)
            Inventory mine = claude.GetInventory();
            mine.AddItem(pick, 1);
            int stack = wood.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
            for (int i = 0; i < 9 && mine.GetTotalWeight() < claude.MaxCarry; i++) mine.AddItem(wood, stack);
            int carried = claude.CountItem("Wood");
            checks.Add($"가방이 나무로 참 (나무 {carried}, 무게 {mine.GetTotalWeight():0}/{claude.MaxCarry:0})", mine.GetTotalWeight() >= claude.MaxCarry);

            // 1) 거점 상자 둘 다 꽉 참 → 알리고 끝낸다(상자는 지정하지 않으니 둘 다 채운다)
            foreach (var c in new[] { woodChest, stoneChest }) Chests.TryEdit(c, inv => { while (inv.GetEmptySlots() > 0) inv.AddItem(wood, stack); });
            int inChest = GatherTest.CountBase(home, "Wood");
            GatherTest.Say($"!{Workers.Name1} 돌 3", null);
            float t = 0f;
            yield return new WaitForSeconds(1f);
            while (t < 90f && claude != null && claude.Ai.Current == WorkerAI.Mode.Work)
            {
                yield return new WaitForSeconds(1f);
                t += 1f;
            }
            if (claude == null) { End(player, god, checks, onHeard, heard); yield break; }
            var bagFull = Core.Npc.Speech.Options(claude.Personality, "bagFull");
            checks.Add($"넣을 데가 없으면 알리고 끝냄 ({t:0}초)", claude.Ai.Current != WorkerAI.Mode.Work && heard.Exists(h => System.Array.Exists(bagFull, b => h.EndsWith(b))));
            checks.Add($"나무 그대로 (들고 {claude.CountItem("Wood")}/{carried}, 상자 {GatherTest.CountBase(home, "Wood")}/{inChest})",
                claude.CountItem("Wood") == carried && GatherTest.CountBase(home, "Wood") == inChest);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);

            // 2) 상자를 비운다 → 나무를 넣고 돌을 모아 온다
            foreach (var c in new[] { woodChest, stoneChest }) Chests.TryEdit(c, inv => inv.RemoveAll());
            GatherTest.Say($"!{Workers.Name1} 돌 3", null);
            var frames = new FrameMax();
            Plugin.Instance.StartCoroutine(frames.Run()); // 짐 정리(물건마다 125m 상자 찾기)·채집 동안 가장 긴 프레임
            t = 0f;
            int stones = 0;
            while (t < 240f)
            {
                yield return new WaitForSeconds(5f);
                t += 5f;
                stones = GatherTest.CountBase(home, "Stone");
                if ((int)t % 15 == 0) TestRunner.Log($"[{t:0}s] 상자 돌 {stones}, 들고 있는 나무 {(claude ? claude.CountItem("Wood") : -1)}, 상태 {(claude ? claude.Ai.Current + "/" + claude.Ai.Job?.StepName : "없음")}");
                if (stones >= 3 || claude == null || claude.Ai.Current != WorkerAI.Mode.Work) break;
            }
            checks.Add($"나무를 상자에 넣음 (상자 {GatherTest.CountBase(home, "Wood")}/{carried}, 들고 {(claude ? claude.CountItem("Wood") : -1)})",
                claude != null && claude.CountItem("Wood") == 0 && GatherTest.CountBase(home, "Wood") == carried);
            checks.Add($"상자에 돌 {stones}개 ({t:0}초)", stones >= 3);
            checks.Add("곡괭이는 들고 있음", claude != null && claude.CountItem("PickaxeAntler") == 1);
            frames.Stop = true;
            TestRunner.Log($"부하: 짐 정리·채집 {frames.Count}프레임, 가장 긴 {frames.MaxMs:0.0}ms, 평균 {frames.AvgMs:0.0}ms");
            LogSearchCost(home);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(0.5f);
            End(player, god, checks, onHeard, heard);
        }

        /// <summary>프레임 시간 재기(Time.unscaledDeltaTime): 최소화 화면이라 그리기 몫은 작다.</summary>
        sealed class FrameMax
        {
            public bool Stop;
            public int Count;
            public float MaxMs, SumMs;
            public float AvgMs => Count > 0 ? SumMs / Count : 0f;

            public IEnumerator Run()
            {
                yield return null; // 시작 프레임(명령 처리) 다음부터
                while (!Stop)
                {
                    float ms = Time.unscaledDeltaTime * 1000f;
                    Count++;
                    SumMs += ms;
                    if (ms > MaxMs) MaxMs = ms;
                    yield return null;
                }
            }
        }

        /// <summary>거점 125m 상자 찾기 한 번의 값(대화 세션 10-23 부하 검토): Senses.Nearby 25구역·NearBase·ForItem, 짐 정리 최악(물건 32칸 × 2번).</summary>
        static void LogSearchCost(Vector3 home)
        {
            const int n = 50;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int zdos = 0;
            for (int i = 0; i < n; i++) zdos = Senses.Nearby(home, Chests.BaseRange).Count;
            double nearby = sw.Elapsed.TotalMilliseconds / n;
            sw.Restart();
            int chests = 0;
            for (int i = 0; i < n; i++) chests = Chests.NearBase(home).Count;
            double nearBase = sw.Elapsed.TotalMilliseconds / n;
            sw.Restart();
            for (int i = 0; i < n; i++) Chests.ForItem("Wood", home, ChestRole.Mats);
            double forItem = sw.Elapsed.TotalMilliseconds / n;
            TestRunner.Log($"부하: 125m ZDO {zdos}개·상자 {chests}개, 한 번에 Nearby {nearby:0.00}ms·NearBase {nearBase:0.00}ms·ForItem {forItem:0.00}ms → 짐 정리 최악(64번) {forItem * 64:0.0}ms");
        }

        static void End(Player player, bool god, GatherTest.Checks checks, System.Action<string, string> onHeard, List<string> heard)
        {
            ChatNet.Heard -= onHeard;
            player.SetGodMode(god);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
