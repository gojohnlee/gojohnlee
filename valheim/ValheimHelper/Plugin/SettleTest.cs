using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 밤사이 정산 시험(요청 R3 가): 1호 짐에 나무·돌, 2호는 80m 밖으로 보내고 실제 잠자기 넘김(EnvMan.SkipToMorning, 12초).
    /// 넘김이 끝나면 1호 짐이 걷지 않고 상자로(개수 보존, 대사), 2호(밖)는 그대로, 같은 아침 다시 불러도 안 함, 다음 아침이면 함.
    /// </summary>
    static class SettleTest
    {
        public static void Register() => TestRunner.Register("vh_settletest", "밤사이 정산(아침 정리) 시험", Run);

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer() || EnvMan.instance == null)
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

            GameObject wood = ObjectDB.instance.GetItemPrefab("Wood"), stone = ObjectDB.instance.GetItemPrefab("Stone");
            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            checks.Add("프리팹 있음", wood != null && stone != null && chestPrefab != null && BaseFlag.Prefab != null);
            if (wood == null || stone == null || chestPrefab == null || BaseFlag.Prefab == null) { End(player, god, checks, onHeard, heard); yield break; }

            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
            Quaternion rot = player.transform.rotation;
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f);
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Scv);
            WorkerNpc alarak = WorkerNpc.All.Find(n => n.Personality != Core.Npc.Personality.Scv);
            checks.Add("깃발에 NPC 둘 나타남", claude != null && alarak != null);
            if (claude == null || alarak == null) { End(player, god, checks, onHeard, heard); yield break; }
            if (GatherTest.ClearOldChests(home) > 0) yield return new WaitForSeconds(0.5f);
            Vector3 chestPos = GatherTest.Ground(home + rot * new Vector3(-4f, 0f, -1f));
            player.PlacePiece(chestPrefab.GetComponent<Piece>(), chestPos, rot, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container chest = GatherTest.ChestAt(chestPos);
            checks.Add("상자 놓임", chest != null);
            if (chest == null) { End(player, god, checks, onHeard, heard); yield break; }

            // 1호: 짐에 나무 20·돌 10, 2호: 나무 10 들고 80m 밖
            claude.GetInventory().AddItem(wood, 20);
            claude.GetInventory().AddItem(stone, 10);
            alarak.GetInventory().AddItem(wood, 10);
            Vector3 far = GatherTest.Ground(home + rot * new Vector3(0f, 0f, -80f));
            alarak.TeleportTo(far + Vector3.up * 0.5f, rot, false);
            yield return new WaitForSeconds(1f);
            int alarakDay = alarak.Zdo.GetInt(MorningSettle.KeyDay, -1);
            int woodAll = GatherTest.CountBase(home, "Wood") + claude.CountItem("Wood");
            int stoneAll = GatherTest.CountBase(home, "Stone") + claude.CountItem("Stone");
            Vector3 claudeAt = claude.transform.position;
            TestRunner.Log($"시작: 1호 나무 {claude.CountItem("Wood")}·돌 {claude.CountItem("Stone")}, 2호 거리 {Utils.DistanceXZ(alarak.transform.position, home):0}m, 하루 {EnvMan.instance.GetDay()}");

            // 실제 잠자기 넘김
            EnvMan.instance.SkipToMorning();
            float t = 0f;
            while (t < 25f && EnvMan.instance.IsTimeSkipping()) { yield return new WaitForSeconds(0.5f); t += 0.5f; }
            yield return new WaitForSeconds(1f);
            int day = EnvMan.instance.GetDay();
            checks.Add($"넘김 끝남 ({t:0.0}초)", !EnvMan.instance.IsTimeSkipping());
            checks.Add($"1호 짐 → 상자 (짐 나무 {claude.CountItem("Wood")}·돌 {claude.CountItem("Stone")})", claude.CountItem("Wood") == 0 && claude.CountItem("Stone") == 0);
            int woodNow = GatherTest.CountBase(home, "Wood") + claude.CountItem("Wood"), stoneNow = GatherTest.CountBase(home, "Stone") + claude.CountItem("Stone");
            checks.Add($"개수 그대로 (나무 {woodNow}/{woodAll}, 돌 {stoneNow}/{stoneAll})", woodNow == woodAll && stoneNow == stoneAll);
            checks.Add($"걷지 않음 (움직인 거리 {Utils.DistanceXZ(claude.transform.position, claudeAt):0.0}m)", Utils.DistanceXZ(claude.transform.position, claudeAt) < 3f);
            checks.Add($"1호 아침 날짜 기록 ({claude.Zdo.GetInt(MorningSettle.KeyDay, -1)}/{day})", claude.Zdo.GetInt(MorningSettle.KeyDay, -1) == day);
            string said = claude.Line("settle", new Dictionary<string, string> { ["what"] = "" });
            string head = said.Substring(0, Mathf.Min(6, said.Length));
            checks.Add("1호가 정리 요약을 말함", heard.Exists(h => h.Contains(head)));
            checks.Add($"2호(밖)는 빠짐 (나무 {alarak.CountItem("Wood")}/10, 날짜 {alarak.Zdo.GetInt(MorningSettle.KeyDay, -1)}/{alarakDay})",
                alarak.CountItem("Wood") == 10 && alarak.Zdo.GetInt(MorningSettle.KeyDay, -1) == alarakDay);

            // 같은 아침 다시(불러온 것처럼 저장된 날짜만 보고) → 안 함, 다음 아침 → 함
            claude.GetInventory().AddItem(wood, 5);
            MorningSettle.Snapshot();
            MorningSettle.SettleAll(day);
            checks.Add($"같은 아침 두 번 안 함 (짐 나무 {claude.CountItem("Wood")}/5)", claude.CountItem("Wood") == 5);
            MorningSettle.Snapshot();
            MorningSettle.SettleAll(day + 1);
            checks.Add($"다음 아침엔 함 (짐 나무 {claude.CountItem("Wood")}/0, 상자 {GatherTest.CountBase(home, "Wood")}/{woodAll + 5})",
                claude.CountItem("Wood") == 0 && GatherTest.CountBase(home, "Wood") == woodAll + 5);
            claude.Zdo.Set(MorningSettle.KeyDay, day); // 시험이 앞당긴 날짜를 되돌림
            End(player, god, checks, onHeard, heard);
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
