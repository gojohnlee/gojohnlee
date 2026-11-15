using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_blocktest (전투 보강 가 — 방패 막기): 1호에게 몽둥이·나무 방패를 주고 거점에서 60m 앞(동료 부르기 50m 밖)에서 회색난쟁이와 싸운다.
    /// A) 위험 판단에 막기가 들어감(LastDetail "막기 N"). B) 실제로 막음(WorkerNpc.Blocks, 회색난쟁이 최대 3마리까지 차례로) + 잡음 + 살아 있음.
    /// C) 싸움이 끝나면 방패를 내림(IsBlocking 거짓). D) 방패를 빼면 막기 수치가 없음. 플레이어는 신·유령 모드.
    /// </summary>
    static class BlockTest
    {
        public static void Register() => TestRunner.Register("vh_blocktest", "방패 막기 시험", () => TestKit.Flat(Run()));

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
            var spawned = new List<GameObject>();
            bool ghost = Player.m_localPlayer.InGhostMode();
            Player.m_localPlayer.SetGhostMode(true); // 몬스터가 플레이어 말고 일꾼을 보게
            try { yield return Body(w, checks, spawned); }
            finally
            {
                foreach (var go in spawned)
                    if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
                Player.m_localPlayer.SetGhostMode(ghost);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned)
        {
            w.CountsCreatures = true;
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            GameObject gd = ZNetScene.instance.GetPrefab("Greydwarf");
            checks.Add("회색난쟁이 프리팹 있음", gd != null);
            if (w.Ragnar == null || gd == null) yield break;
            WorkerNpc r = w.Ragnar;
            Inventory inv = r.GetInventory();
            foreach (var n in new[] { "Club", "ShieldWood", "HelmetLeather" }) inv.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
            inv.AddItem(ObjectDB.instance.GetItemPrefab("MeadHealthMinor"), 3);
            Prep.WearBest(r);
            r.HoldWeapon();
            r.Heal(1000f, false);
            Quaternion rot = w.Player.transform.rotation;
            Vector3 arena = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 60f));
            r.TeleportTo(arena + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            Core.Npc.Shield? s = r.BlockStats();
            checks.Add($"방패 듦·막기 수치 (왼손 {r.HoldingShield}, 막기 {(s.HasValue ? s.Value.Power : 0f):0.0}, 비틀 한계 {(s.HasValue ? s.Value.StaggerLimit : 0f):0})", r.HoldingShield && s.HasValue && s.Value.Power > 0f);

            // A·B) 회색난쟁이와 싸움: 판단에 막기가 들어가고, 실제로 막는다(안 막았으면 다음 놈, 최대 3마리)
            bool judged = false, fought = false, killedAll = true;
            int blocks0 = r.Blocks;
            for (int i = 0; i < 3 && r.Blocks == blocks0 && !r.IsDead(); i++)
            {
                r.HoldWeapon();
                r.Heal(1000f, false);
                Character g = Spawn(gd, r.transform.position + r.transform.forward * 8f, spawned);
                float t = 0f;
                while (t < 60f && g != null && !g.IsDead() && !r.IsDead())
                {
                    yield return new WaitForSeconds(0.5f);
                    t += 0.5f;
                    var guard = r.Ai.Guard;
                    if (guard != null && guard.StateName == "Fight") fought = true;
                    if (guard != null && guard.LastDetail.Contains("막기 ")) judged = true;
                    if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[{i + 1}번째 {t:0}s] 경계 {guard?.StateName}/{guard?.LastVerdict} 비율 {guard?.LastRatio:0.00} ({guard?.LastDetail}), 체력 {r.GetHealth():0}, 막음 {r.Blocks} (마지막 {r.LastBlocked:0.0}), 막는 중 {r.IsBlocking()}");
                }
                if (g != null && !g.IsDead()) { killedAll = false; Kill(g); }
                yield return new WaitForSeconds(1f);
            }
            checks.Add("위험 판단에 막기가 들어감 (LastDetail \"막기\")", judged);
            checks.Add("회색난쟁이와 싸움", fought);
            checks.Add($"방패로 막음 ({r.Blocks - blocks0}번, 마지막에 줄인 피해 {r.LastBlocked:0.0})", r.Blocks > blocks0);
            checks.Add("회색난쟁이 잡음(60초 안)", killedAll);
            checks.Add($"살아 있음 (체력 {r.GetHealth():0})", !r.IsDead() && r.GetHealth() > 0f);

            // C) 싸움이 끝나면 방패를 내림(들린 채면 못 달린다)
            yield return TestKit.WaitFor(() => !r.IsBlocking(), 3f);
            checks.Add($"싸움 끝나면 방패 내림 (경계 {r.Ai.Guard?.StateName})", !r.IsBlocking());

            // D) 방패를 빼면 막기 수치 없음
            ItemDrop.ItemData shield = inv.GetAllItems().Find(Gear.IsShield);
            if (shield != null) { r.UnequipItem(shield, false); inv.RemoveItem(shield); }
            checks.Add("방패를 빼면 막기 수치 없음", !r.BlockStats().HasValue && !r.HoldingShield);
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
    }
}
