using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 채집 시험(② 단계). 깃발 → 상자 2개(장비·자원) → 장비 상자에 도구 → "!상자 장비", "!상자 &lt;자원&gt;"
    /// (바라보는 상자 대신 시험이 지정) → "!Claude &lt;자원&gt; 10" → 자원 상자에 10개 이상 들어오는지(최대 6분).
    /// vh_woodtest: 부싯돌 도끼 + 나무. vh_stonetest: 뿔 곡괭이 + 돌(줍기·캐기 둘 다 가능).
    /// 깃발·상자·NPC는 시험 뒤에도 남긴다.
    /// </summary>
    static class GatherTest
    {
        public static void Register()
        {
            TestRunner.Register("vh_woodtest", "나무 채집 시험", () => Run("AxeFlint", "Wood", "나무"));
            TestRunner.Register("vh_stonetest", "돌 채집 시험", () => Run("PickaxeAntler", "Stone", "돌"));
            TestRunner.Register("vh_coppertest", "구리 채집 시험(광맥을 거점 12m 앞에 놓음, 땅속 광석 파기·구덩이 탈출)", () => Run("PickaxeAntler", "CopperOre", "구리", true));
        }

        internal sealed class Checks
        {
            public int Total, Passed;
            public void Add(string what, bool ok)
            {
                Total++;
                if (ok) Passed++;
                TestRunner.Log($"{(ok ? "OK  " : "FAIL")} {what}");
            }
        }

        static IEnumerator Run(string toolPrefab, string item, string word, bool spawnDeposit = false)
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
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;

            var ways = ResourceCatalog.WaysFor(item);
            checks.Add($"자원 목록 {ResourceCatalog.Count}종, {word} 얻는 법 [{string.Join(",", ways)}]", ways.Count > 0);
            GameObject tool = ObjectDB.instance.GetItemPrefab(toolPrefab);
            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            checks.Add($"{toolPrefab}·상자 프리팹 있음", tool != null && chestPrefab != null && BaseFlag.Prefab != null);
            if (tool == null || chestPrefab == null || BaseFlag.Prefab == null) { End(player, god, checks, onHeard, heard); yield break; }
            string toolToken = tool.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

            // 1) 깃발과 NPC
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = Ground(player.transform.position + player.transform.forward * 4f);
            Quaternion rot = player.transform.rotation;
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Scv);
            checks.Add("깃발에 NPC 나타남", claude != null && WorkerNpc.All.Count == 2);
            if (claude == null) { End(player, god, checks, onHeard, heard); yield break; }

            // 2) 상자 2개
            Piece chestPiece = chestPrefab.GetComponent<Piece>();
            Vector3 eqPos = Ground(home + rot * new Vector3(-4f, 0f, -1f));
            Vector3 resPos = Ground(home + rot * new Vector3(4f, 0f, -1f));
            player.PlacePiece(chestPiece, eqPos, rot, doAttack: false);
            player.PlacePiece(chestPiece, resPos, rot, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container eq = ChestAt(eqPos), res = ChestAt(resPos);
            checks.Add("상자 2개 놓임", eq != null && res != null);
            if (eq == null || res == null) { End(player, god, checks, onHeard, heard); yield break; }
            // 광맥은 오래 캐서 곡괭이가 부러질 수 있다(바닐라 내구도) → 예비 하나 더
            bool put = Chests.TryEdit(eq, inv => { inv.AddItem(tool, 1); if (spawnDeposit) inv.AddItem(tool, 1); });
            checks.Add($"장비 상자에 {toolPrefab}", put && eq.GetInventory().HaveItem(toolToken));

            Say("!상자 장비", eq);
            Say($"!상자 {word}", res);
            yield return new WaitForSeconds(0.5f);
            checks.Add($"장비·{word} 상자 지정됨", Chests.Assigned(Chests.KeyEquip(0)) == eq && Chests.Assigned(Chests.KeyItem(item)) == res);

            // 3) 10개
            // 광맥(구리 등)은 시험 월드 근처에 없을 수 있어 거점 12m 앞에 놓는다. 지난 시험의 광맥·구덩이는 먼저 치운다.
            Vector3 depositPos = home + rot * new Vector3(0f, 0f, 12f);
            if (spawnDeposit)
            {
                // 건축물 4m 안의 바위는 일꾼이 안 고른다(Buildings) → 지난 시험들이 남긴 상자·깃발에서 떨어진 곳에 놓는다
                foreach (var off in new[] { new Vector3(0f, 0f, 12f), new Vector3(0f, 0f, 18f), new Vector3(8f, 0f, 14f), new Vector3(-8f, 0f, 14f), new Vector3(0f, 0f, 24f) })
                {
                    depositPos = home + rot * off;
                    if (!Buildings.Near(depositPos, 6f)) break;
                }
                GameObject dep = DepositFor(item);
                checks.Add($"{word} 광맥 프리팹 {(dep != null ? dep.name : "없음")}", dep != null);
                if (dep == null) { End(player, god, checks, onHeard, heard); yield break; }
                int hash = dep.name.GetStableHashCode();
                foreach (var zdo in Senses.Nearby(depositPos, 20f))
                {
                    if (zdo.GetPrefab() != hash) continue;
                    ZNetView v = ZNetScene.instance.FindInstance(zdo);
                    if (v == null) continue;
                    v.ClaimOwnership();
                    ZNetScene.instance.Destroy(v.gameObject);
                }
                int undone = TestTerrain.Reset(depositPos, 10f);
                yield return new WaitForSeconds(0.5f);
                depositPos = Ground(depositPos);
                Object.Instantiate(dep, depositPos, rot);
                TestRunner.Log($"광맥 놓음 {dep.name} (지난 구덩이 꼭짓점 {undone}개 되돌림)");
                yield return new WaitForSeconds(1f);
                // 일꾼이 그 광맥을 고를 수 있는지(0.3.8 첫 구리 시험: 12m 앞 광맥을 두고 580m 밖으로 감)
                ResourceInfo info = ResourceCatalog.Get(hash);
                int seen = Senses.Nearby(depositPos, 3f).FindAll(z => z.GetPrefab() == hash).Count;
                var near = new List<string>();
                foreach (var z in Senses.Nearby(depositPos, 4f))
                    if (Buildings.IsPiece(z.GetPrefab())) near.Add($"{ZNetScene.instance.GetPrefab(z.GetPrefab())?.name} {Utils.DistanceXZ(z.GetPosition(), depositPos):0.0}m");
                TestRunner.Log($"광맥 ZDO {seen}개, 목록 정보: 도구 {info?.Tool} 단계 {info?.MinToolTier} (곡괭이 단계 {tool.GetComponent<ItemDrop>().m_itemData.m_shared.m_toolTier}), " +
                               $"나오는 것 [{(info != null ? string.Join(",", info.Produces) : "")}], 4m 안 건축물 [{string.Join(", ", near)}]");
            }
            int dug0 = NpcPatches.NoDig.Dug, blocked0 = NpcPatches.NoDig.Blocked, tp0 = WorkerAI.StuckTeleports;
            Say($"!{Workers.Name1} {word} 10", null);
            int count = 0;
            float t = 0f;
            while (t < 360f)
            {
                yield return new WaitForSeconds(5f);
                t += 5f;
                count = Count(res, item);
                if ((int)t % 15 == 0)
                {
                    var job = claude ? claude.Ai.Job : null;
                    TestRunner.Log($"[{t:0}s] 상자 {word} {count}, 들고 있는 {word} {(claude ? claude.CountItem(item) : -1)}, 상태 {(claude ? claude.Ai.Current.ToString() : "없음")}/{job?.StepName}, 집에서 {(claude ? Utils.DistanceXZ(claude.Home, claude.transform.position) : -1):0}m, 지식 {Brain.Get().Count}구역");
                }
                if (count >= 10 || claude == null || claude.Ai.Current != WorkerAI.Mode.Work) break;
            }
            checks.Add($"{word} 상자에 {word} {count}개 ({t:0}초)", count >= 10);
            checks.Add("상자 2개 멀쩡함(일꾼 도구질에 안 부서짐)", eq != null && res != null && eq.GetComponent<ZNetView>().IsValid() && res.GetComponent<ZNetView>().IsValid());
            checks.Add($"일하다 죽지 않음(쓰러지는 나무 등, 죽은 횟수 {(claude ? claude.Deaths : -1)})", claude != null && claude.Deaths == 0);
            ItemDrop.ItemData held = claude ? claude.GetCurrentWeapon() : null;
            TestRunner.Log($"1호 손: {(held != null && held.m_dropPrefab != null ? held.m_dropPrefab.name + $" 내구도 {held.m_durability:0}/{held.GetMaxDurability():0}" : "빈손")}, 죽은 횟수(남은 묘비) {(claude ? claude.Graves().Count : -1)}");
            // 땅 파기(2026-10-21 사용자: 돌 캘 땐 땅 파기 자제, 구리는 파더라도 구덩이에서 나와야 함)
            int dug = NpcPatches.NoDig.Dug - dug0;
            TestRunner.Log($"땅 파기: 판 {dug}번, 막음 {NpcPatches.NoDig.Blocked - blocked0}번, 점프 {(claude ? claude.Ai.Jumps : -1)}번, 경사로 곡괭이질 {(claude ? claude.Ai.CarveSwings : -1)}번");
            if (!ResourceCatalog.WorthDigging(item)) checks.Add($"{word}{Core.Npc.Josa.For(word, "은")} 땅을 파지 않음(판 횟수 {dug})", dug == 0);
            if (spawnDeposit && claude != null)
            {
                // 판 구덩이에서 나와 거점으로 돌아오는지(일이 끝나면 집으로 간다)
                float w = 0f;
                for (; w < 60f && Utils.DistanceXZ(claude.Home, claude.transform.position) > 5f; w += 1f) yield return new WaitForSeconds(1f);
                checks.Add($"구덩이에서 나와 거점으로 돌아옴({w:0}초, 집에서 {Utils.DistanceXZ(claude.Home, claude.transform.position):0}m)", Utils.DistanceXZ(claude.Home, claude.transform.position) <= 5f);
            }
            checks.Add($"길이 막혀 순간이동하지 않음({WorkerAI.StuckTeleports - tp0}번)", WorkerAI.StuckTeleports == tp0);
            if (spawnDeposit)
            {
                Vector3 p = claude ? claude.transform.position : depositPos;
                TestRunner.Log($"광맥 둘레 가장 깊은 곳 {Deepest(depositPos, 4f):0.0}m, 1호 위치 집에서 {(claude ? Utils.DistanceXZ(claude.Home, p) : -1):0}m, {(claude ? claude.MoveDebug : "")}");
                int back = TestTerrain.Reset(depositPos, 10f); // 다음 시험을 위해 구덩이 되돌림
                TestRunner.Log($"구덩이 되돌림: 꼭짓점 {back}개");
            }
            End(player, god, checks, onHeard, heard);
        }

        /// <summary>그 물건이 나오는 광맥 프리팹(MineRock5 → MineRock 순, 이름에 frac 없는 것 먼저).</summary>
        static GameObject DepositFor(string item)
        {
            GameObject best = null;
            int bestScore = -1;
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                if (p == null) continue;
                DropTable drops = p.GetComponent<MineRock5>()?.m_dropItems ?? p.GetComponent<MineRock>()?.m_dropItems;
                if (drops?.m_drops == null || !drops.m_drops.Exists(d => d.m_item != null && d.m_item.name == item)) continue;
                int score = (p.GetComponent<MineRock5>() != null ? 2 : 0) + (p.name.Contains("frac") ? 0 : 1);
                TestRunner.Log($"  {item} 광맥 후보: {p.name} (점수 {score})");
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        /// <summary>둘레 r(m) 안에서 원래 땅보다 가장 깊이 파인 정도.</summary>
        static float Deepest(Vector3 c, float r)
        {
            float worst = 0f;
            for (float x = -r; x <= r; x += 1f)
                for (float z = -r; z <= r; z += 1f)
                {
                    Vector3 p = c + new Vector3(x, 0f, z);
                    worst = Mathf.Max(worst, WorldGenerator.instance.GetHeight(p.x, p.z) - TestTerrain.Height(p));
                }
            return worst;
        }

        internal static void Say(string text, Container chest)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text, chest != null ? chest.GetComponent<ZNetView>().GetZDO().m_uid : ZDOID.None);
        }

        internal static int Count(Container c, string prefab)
        {
            int n = 0;
            foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) n += it.m_stack;
            return n;
        }

        internal static Vector3 Ground(Vector3 p)
        {
            if (ZoneSystem.instance.GetGroundHeight(p, out float h)) p.y = h; // 지형(바위 위에 상자를 놓지 않게)
            return p;
        }

        internal static Container ChestAt(Vector3 pos)
        {
            foreach (var zdo in Senses.Nearby(pos, 1.5f))
            {
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                Container c = v != null ? v.GetComponent<Container>() : null;
                if (c != null) return c;
            }
            return null;
        }

        static void End(Player player, bool god, Checks checks, System.Action<string, string> onHeard, List<string> heard)
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
