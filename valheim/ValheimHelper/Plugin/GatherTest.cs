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
            new Terminal.ConsoleCommand("vh_woodtest", "[Valheim Helper] 나무 채집 시험", args =>
            {
                if (TestRunner.Begin(args.Context, "vh_woodtest")) Plugin.Instance.StartCoroutine(Run("AxeFlint", "Wood", "나무"));
            });
            new Terminal.ConsoleCommand("vh_stonetest", "[Valheim Helper] 돌 채집 시험", args =>
            {
                if (TestRunner.Begin(args.Context, "vh_stonetest")) Plugin.Instance.StartCoroutine(Run("PickaxeAntler", "Stone", "돌"));
            });
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

        static IEnumerator Run(string toolPrefab, string item, string word)
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
            bool put = Chests.TryEdit(eq, inv => inv.AddItem(tool, 1));
            checks.Add($"장비 상자에 {toolPrefab}", put && eq.GetInventory().HaveItem(toolToken));

            Say("!상자 장비", eq);
            Say($"!상자 {word}", res);
            yield return new WaitForSeconds(0.5f);
            checks.Add($"장비·{word} 상자 지정됨", Chests.Assigned(Chests.KeyEquip(0)) == eq && Chests.Assigned(Chests.KeyItem(item)) == res);

            // 3) 10개
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
            ItemDrop.ItemData held = claude ? claude.GetCurrentWeapon() : null;
            TestRunner.Log($"1호 손: {(held != null ? held.m_dropPrefab.name + $" 내구도 {held.m_durability:0}/{held.GetMaxDurability():0}" : "빈손")}");
            End(player, god, checks, onHeard, heard);
        }

        static void Say(string text, Container chest)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text, chest != null ? chest.GetComponent<ZNetView>().GetZDO().m_uid : ZDOID.None);
        }

        static int Count(Container c, string prefab)
        {
            int n = 0;
            foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) n += it.m_stack;
            return n;
        }

        static Vector3 Ground(Vector3 p)
        {
            if (ZoneSystem.instance.GetSolidHeight(p, out float h)) p.y = h;
            return p;
        }

        static Container ChestAt(Vector3 pos)
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
