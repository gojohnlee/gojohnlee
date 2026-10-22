using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 위험 감지 시험(③): 1호에게 가죽 갑옷·부싯돌 도끼·몽둥이·체력 미드 3·음식 3종을 주고 플레이어에게서 30m 떨어진 곳에 둔다.
    /// A) 회색난쟁이 하나 → 싸워서 잡는지, 그 적을 배우는지. B) 트롤 → 도망쳐서 멀어지는지(살아 있는지).
    /// C) 체력 25%에서 회색난쟁이 → 미드를 마시는지. 소환한 몬스터는 시험 뒤 지운다.
    /// </summary>
    static class FightTest
    {
        public static void Register() => TestRunner.Register("vh_fighttest", "위험 감지·싸움 시험", Run);

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
            bool ghost = player.InGhostMode();
            player.SetGhostMode(true); // 몬스터가 플레이어 말고 NPC를 보게
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;
            EnvMan env = EnvMan.instance;
            bool debugTime = env.m_debugTimeOfDay;
            float debugValue = env.m_debugTime;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;
            var spawned = new List<GameObject>();

            GameObject gd = ZNetScene.instance.GetPrefab("Greydwarf"), troll = ZNetScene.instance.GetPrefab("Troll");
            checks.Add("몬스터 프리팹 있음", gd != null && troll != null);
            if (gd == null || troll == null) { End(player, god, ghost, env, debugTime, debugValue, checks, onHeard, heard, spawned); yield break; }

            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Quaternion rot = player.transform.rotation;
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f); // 둘레 생물(적·동료 해골)을 치우고 시작(사용자 결정 2026-10-22 시험 환경)
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Scv);
            checks.Add("깃발에 NPC 나타남", claude != null && WorkerNpc.All.Count == 2);
            if (claude == null) { End(player, god, ghost, env, debugTime, debugValue, checks, onHeard, heard, spawned); yield break; }

            // 장비·음식·미드
            Inventory mine = claude.GetInventory();
            foreach (var n in new[] { "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather", "AxeFlint", "Club" }) mine.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
            mine.AddItem(ObjectDB.instance.GetItemPrefab("MeadHealthMinor"), 3);
            Prep.WearBest(claude);
            var food = new Inventory("vh_test_food", null, 4, 2);
            foreach (var n in new[] { "CookedMeat", "Honey", "Blueberries" }) food.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
            claude.EatFrom(food);
            claude.Heal(1000f, false);
            Vector3 arena = GatherTest.Ground(home + rot * new Vector3(0f, 0f, 30f));
            claude.TeleportTo(arena + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            TestRunner.Log($"1호: 체력 {claude.GetHealth():0}/{claude.GetMaxHealth():0}, 방어력 {claude.GetBodyArmor():0}, 거점에서 {Utils.DistanceXZ(claude.transform.position, home):0}m");

            // A) 회색난쟁이 하나 → 싸움
            Character a = Spawn(gd, claude.transform.position + rot * new Vector3(0f, 0f, 8f), spawned);
            bool fought = false;
            float t = 0f;
            while (t < 45f && a != null && !a.IsDead() && claude != null)
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                if (claude.Ai.Guard != null && claude.Ai.Guard.StateName == "Fight") fought = true;
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[A {t:0}s] 경계 {claude.Ai.Guard?.StateName}/{claude.Ai.Guard?.LastVerdict} 비율 {claude.Ai.Guard?.LastRatio:0.00} ({claude.Ai.Guard?.LastDetail}), 1호 체력 {claude.GetHealth():0}, 적 체력 {(a ? a.GetHealth() : 0):0}, 거리 {(a ? Vector3.Distance(a.transform.position, claude.transform.position) : 0):0.0}m");
            }
            checks.Add($"회색난쟁이와 싸움 (판단 {claude.Ai.Guard?.LastVerdict})", fought);
            checks.Add($"회색난쟁이 잡음 ({t:0}초)", a == null || a.IsDead());
            checks.Add($"살아 있음 (체력 {claude.GetHealth():0})", claude != null && !claude.IsDead() && claude.GetHealth() > 0f);
            checks.Add("회색난쟁이를 배움", Brain.Foes.Knows("Greydwarf"));
            yield return new WaitForSeconds(2f);

            // C) 체력 25% → 미드
            claude.GetSEMan().RemoveAllStatusEffects(true); // A에서 마신 미드 대기시간을 지운다(시험)
            int meads = claude.CountItem("MeadHealthMinor");
            claude.SetHealth(claude.GetMaxHealth() * 0.25f);
            Character c = Spawn(gd, claude.transform.position + rot * new Vector3(0f, 0f, 7f), spawned);
            t = 0f;
            while (t < 10f && claude.CountItem("MeadHealthMinor") == meads)
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
            }
            checks.Add($"체력 낮으면 미드 마심 ({meads} → {claude.CountItem("MeadHealthMinor")}, {t:0.0}초)", claude.CountItem("MeadHealthMinor") == meads - 1);
            t = 0f;
            while (t < 40f && c != null && !c.IsDead()) { yield return new WaitForSeconds(0.5f); t += 0.5f; }
            if (c != null && !c.IsDead()) Kill(c);
            claude.Heal(1000f, false);
            yield return new WaitForSeconds(2f);

            // B) 트롤 → 도망
            Vector3 trollPos = claude.transform.position + rot * new Vector3(0f, 0f, 20f);
            Character b = Spawn(troll, trollPos, spawned);
            float start = b ? Vector3.Distance(b.transform.position, claude.transform.position) : 0f, far = start;
            bool fled = false;
            t = 0f;
            while (t < 25f && b != null && claude != null)
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                if (claude.Ai.Guard != null && claude.Ai.Guard.StateName == "Flee") fled = true;
                far = Mathf.Max(far, Vector3.Distance(b.transform.position, claude.transform.position));
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[B {t:0}s] 경계 {claude.Ai.Guard?.StateName}/{claude.Ai.Guard?.LastVerdict} 비율 {claude.Ai.Guard?.LastRatio:0.00}, 1호 체력 {claude.GetHealth():0}, 트롤까지 {Vector3.Distance(b.transform.position, claude.transform.position):0}m");
            }
            checks.Add($"트롤 보고 도망 (판단 {claude.Ai.Guard?.LastVerdict})", fled);
            checks.Add($"트롤 감지 거리 밖으로 멀어짐 ({start:0}m → 최대 {far:0}m)", far > Guard.SenseRange);
            checks.Add($"살아 있음 (체력 {claude.GetHealth():0})", !claude.IsDead() && claude.GetHealth() > 0f && claude.Graves().Count == 0);

            bool clean = true;
            foreach (var h in heard) if (h.Contains("{")) clean = false;
            checks.Add("대사에 자리표시자 안 남음", clean);
            End(player, god, ghost, env, debugTime, debugValue, checks, onHeard, heard, spawned);
        }

        static Character Spawn(GameObject prefab, Vector3 pos, List<GameObject> spawned)
        {
            GameObject go = Object.Instantiate(prefab, GatherTest.Ground(pos) + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Add(go);
            Character ch = go.GetComponent<Character>();
            ch.SetLevel(1);
            return ch;
        }

        static void Kill(Character c)
        {
            var hit = new HitData { m_point = c.GetCenterPoint() };
            hit.m_damage.m_damage = 99999f;
            c.Damage(hit);
        }

        static void End(Player player, bool god, bool ghost, EnvMan env, bool debugTime, float debugValue, GatherTest.Checks checks,
            System.Action<string, string> onHeard, List<string> heard, List<GameObject> spawned)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
            ChatNet.Heard -= onHeard;
            player.SetGodMode(god);
            player.SetGhostMode(ghost);
            env.m_debugTimeOfDay = debugTime;
            env.m_debugTime = debugValue;
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
