using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 사망 시험(③): 깃발 → 장비 상자(예비 누더기 갑옷)·자원 상자 → 1호에게 가죽 갑옷·바지, 부싯돌 도끼, 나무 7개를 들려
    /// 거점에서 40m 떨어진 곳으로 옮기고 죽인다 → 사라지지 않고 같은 NPC가 거점에서 체력 가득으로 일어나는지,
    /// 죽은 자리에 묘비가 생기고 짐이 거기 있는지 → 몇 초 뒤 스스로 예비 장비를 차고 묘비에 가서 되찾아 입고,
    /// 나무와 예비 누더기를 거점 상자에 돌려놓는지(최대 3분, 상자는 지정하지 않음).
    /// </summary>
    static class DeathTest
    {
        public static void Register() => TestRunner.Register("vh_deathtest", "사망·묘비·부활 시험", Run);

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
            EnvMan env = EnvMan.instance;
            bool debugTime = env.m_debugTimeOfDay;
            float debugValue = env.m_debugTime;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;

            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            checks.Add("묘비·상자 프리팹 있음", WorkerPrefab.Tombstone != null && chestPrefab != null);
            if (WorkerPrefab.Tombstone == null || chestPrefab == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }

            // 1) 깃발, NPC, 상자 2개(조금 떨어진 새 자리: 앞 시험의 상자와 겹치지 않게)
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Quaternion rot = player.transform.rotation;
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f + player.transform.right * 8f);
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f); // 둘레 생물(적·동료 해골)을 치우고 시작(사용자 결정 2026-10-22 시험 환경)
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Ragnar);
            checks.Add("깃발에 NPC 나타남", claude != null && WorkerNpc.All.Count == 2);
            if (claude == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }
            if (GatherTest.ClearOldChests(home) > 0) yield return new WaitForSeconds(0.5f); // 없앤 상자는 프레임 끝에 사라짐: 그 전에 땅 높이를 재면 옛 상자 위에 놓인다
            Piece chestPiece = chestPrefab.GetComponent<Piece>();
            Vector3 eqPos = GatherTest.Ground(home + rot * new Vector3(-3f, 0f, 2f));
            Vector3 resPos = GatherTest.Ground(home + rot * new Vector3(3f, 0f, 2f));
            player.PlacePiece(chestPiece, eqPos, rot, doAttack: false);
            player.PlacePiece(chestPiece, resPos, rot, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container eq = GatherTest.ChestAt(eqPos), res = GatherTest.ChestAt(resPos);
            checks.Add("상자 2개 놓임", eq != null && res != null && eq != res);
            if (eq == null || res == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }
            Chests.TryEdit(eq, inv => inv.AddItem(ObjectDB.instance.GetItemPrefab("ArmorRagsChest"), 1));
            int wood0 = GatherTest.CountBase(home, "Wood"), rags0 = GatherTest.CountBase(home, "ArmorRagsChest"); // 지난 시험이 남긴 것

            // 2) 짐을 들려 40m 밖으로
            Inventory mine = claude.GetInventory();
            foreach (var n in new[] { "ArmorLeatherChest", "ArmorLeatherLegs", "AxeFlint" }) mine.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
            mine.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 7);
            Prep.WearBest(claude);
            ZDOID id = claude.GetComponent<ZNetView>().GetZDO().m_uid;
            Vector3 away = GatherTest.Ground(home + rot * new Vector3(0f, 0f, 40f));
            claude.TeleportTo(away + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            Vector3 deathPos = claude.transform.position;
            checks.Add($"40m 밖으로 옮겨짐 ({Utils.DistanceXZ(deathPos, home):0}m)", Utils.DistanceXZ(deathPos, home) > 30f);

            // 3) 죽인다
            var hit = new HitData { m_point = claude.GetCenterPoint(), m_dir = Vector3.forward };
            hit.m_damage.m_damage = 9999f;
            claude.Damage(hit);
            yield return new WaitForSeconds(1.5f);
            WorkerNpc after = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Ragnar);
            checks.Add("NPC가 사라지지 않음(같은 NPC)", after != null && after.GetComponent<ZNetView>().GetZDO().m_uid == id);
            if (after == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }
            claude = after;
            checks.Add($"거점에서 일어남 ({Utils.DistanceXZ(claude.transform.position, home):0}m)", Utils.DistanceXZ(claude.transform.position, home) < 5f);
            checks.Add($"살아 있고 체력 가득 ({claude.GetHealth():0}/{claude.GetMaxHealth():0})", !claude.IsDead() && claude.GetHealth() >= claude.GetMaxHealth() - 0.5f && claude.GetHealth() > 0f);
            var graves = claude.Graves();
            ZDO graveZdo = graves.Count > 0 ? ZDOMan.instance.GetZDO(graves[0].Id) : null;
            checks.Add($"죽은 자리에 묘비 ({(graveZdo != null ? Utils.DistanceXZ(graveZdo.GetPosition(), deathPos).ToString("0") + "m" : "없음")})",
                graveZdo != null && Utils.DistanceXZ(graveZdo.GetPosition(), deathPos) < 5f);
            Container grave = graves.Count > 0 ? Chests.FromId(graves[0].Id) : null;
            checks.Add($"짐은 묘비에 (나무 {(grave ? GatherTest.Count(grave, "Wood") : -1)}, 들고 있는 것 {mine.NrOfItems()}개)",
                grave != null && GatherTest.Count(grave, "Wood") == 7 && GatherTest.Count(grave, "ArmorLeatherChest") == 1 && mine.NrOfItems() == 0);
            checks.Add("죽었다고 알림", heard.Exists(h => h.Contains("묘비는")));

            // 4) 스스로 묘비 회수
            bool started = false, wasRags = false;
            float t = 0f;
            while (t < 180f && claude != null)
            {
                yield return new WaitForSeconds(1f);
                t += 1f;
                if (claude.Ai.Job is GraveJob)
                {
                    if (!started) TestRunner.Log($"[{t:0}s] 묘비 회수 시작");
                    started = true;
                    if (claude.CountItem("ArmorRagsChest") > 0) wasRags = true;
                }
                if ((int)t % 15 == 0) TestRunner.Log($"[{t:0}s] 상태 {claude.Ai.Current}/{claude.Ai.Job?.StepName}, 묘비까지 {(graveZdo != null ? Utils.DistanceXZ(graveZdo.GetPosition(), claude.transform.position) : -1):0}m, 남은 묘비 {claude.Graves().Count}");
                if (started && claude.Ai.Current != WorkerAI.Mode.Work) break;
            }
            checks.Add($"스스로 묘비 회수 시작 ({(started ? "예" : "아니오")})", started);
            checks.Add("가는 길에 예비 장비(누더기)를 참", wasRags);
            checks.Add("묘비가 비어 사라짐", claude.Graves().Count == 0 && (graveZdo == null || ZDOMan.instance.GetZDO(graveZdo.m_uid) == null));
            var worn = new List<string>();
            foreach (var it in mine.GetEquippedItems()) worn.Add(it.m_dropPrefab != null ? it.m_dropPrefab.name : "?");
            checks.Add($"되찾은 가죽 갑옷을 입음 ({string.Join(", ", worn.ToArray())})", worn.Contains("ArmorLeatherChest") && worn.Contains("ArmorLeatherLegs"));
            int wood = GatherTest.CountBase(home, "Wood") - wood0, rags = GatherTest.CountBase(home, "ArmorRagsChest") - rags0;
            checks.Add($"나무는 거점 상자에 ({wood})", wood == 7);
            checks.Add($"예비 누더기는 거점 상자로 ({rags})", rags == 0 && claude.CountItem("ArmorRagsChest") == 0);
            checks.Add($"도끼는 들고 있음", claude.CountItem("AxeFlint") == 1);
            End(player, god, env, debugTime, debugValue, checks, onHeard, heard);
        }

        static void End(Player player, bool god, EnvMan env, bool debugTime, float debugValue, GatherTest.Checks checks, System.Action<string, string> onHeard, List<string> heard)
        {
            ChatNet.Heard -= onHeard;
            player.SetGodMode(god);
            env.m_debugTimeOfDay = debugTime;
            env.m_debugTime = debugValue;
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
