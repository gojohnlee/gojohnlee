using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 콘솔 명령 vh_woodtest: ② 단계 첫 조각 시험.
    /// 깃발 → 상자 2개(장비·나무) → 장비 상자에 부싯돌 도끼 → "!상자 장비", "!상자 나무"(바라보는 상자 대신 시험이 지정) →
    /// "!Claude 나무 10" → 나무 상자에 10개 이상 들어오는지(최대 6분). 깃발·상자·NPC는 시험 뒤에도 남긴다.
    /// </summary>
    static class WoodTest
    {
        public static void Register()
        {
            new Terminal.ConsoleCommand("vh_woodtest", "[Valheim Helper] 나무 채집 시험", args =>
            {
                if (TestRunner.Begin(args.Context, "vh_woodtest")) Plugin.Instance.StartCoroutine(Run());
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
            var heard = new System.Collections.Generic.List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;

            checks.Add($"자원 목록 {ResourceCatalog.Count}종, 나무 도구 {ResourceCatalog.ToolFor("Wood")}", ResourceCatalog.ToolFor("Wood") == ToolKind.Axe);
            GameObject axe = ObjectDB.instance.GetItemPrefab("AxeFlint");
            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            checks.Add("부싯돌 도끼·나무 상자 프리팹 있음", axe != null && chestPrefab != null && BaseFlag.Prefab != null);
            if (axe == null || chestPrefab == null || BaseFlag.Prefab == null) { End(player, god, checks, onHeard, heard); yield break; }

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
            Vector3 woodPos = Ground(home + rot * new Vector3(4f, 0f, -1f));
            player.PlacePiece(chestPiece, eqPos, rot, doAttack: false);
            player.PlacePiece(chestPiece, woodPos, rot, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container eq = ChestAt(eqPos), wood = ChestAt(woodPos);
            checks.Add("상자 2개 놓임", eq != null && wood != null);
            if (eq == null || wood == null) { End(player, god, checks, onHeard, heard); yield break; }
            bool put = Chests.TryEdit(eq, inv => inv.AddItem(axe, 1));
            checks.Add("장비 상자에 부싯돌 도끼", put && eq.GetInventory().HaveItem(axe.GetComponent<ItemDrop>().m_itemData.m_shared.m_name));

            Say("!상자 장비", eq);
            Say("!상자 나무", wood);
            yield return new WaitForSeconds(0.5f);
            checks.Add("장비·나무 상자 지정됨", Chests.Assigned(Chests.KeyEquip(0)) == eq && Chests.Assigned(Chests.KeyItem("Wood")) == wood);

            // 3) 나무 10
            Say($"!{Workers.Name1} 나무 10", null);
            int count = 0;
            float t = 0f;
            while (t < 360f)
            {
                yield return new WaitForSeconds(5f);
                t += 5f;
                count = Count(wood, "Wood");
                if ((int)t % 15 == 0)
                {
                    var job = claude != null ? claude.Ai.Job : null;
                    TestRunner.Log($"[{t:0}s] 상자 나무 {count}, 들고 있는 나무 {(claude ? claude.CountItem("Wood") : -1)}, 상태 {(claude ? claude.Ai.Current.ToString() : "없음")}/{job?.StepName}, 집에서 {(claude ? Utils.DistanceXZ(claude.Home, claude.transform.position) : -1):0}m, 지식 {Brain.Get().Count}구역");
                }
                if (count >= 10 || claude == null || claude.Ai.Current != WorkerAI.Mode.Work) break;
            }
            checks.Add($"나무 상자에 나무 {count}개 ({t:0}초)", count >= 10);
            ItemDrop.ItemData held = claude ? claude.GetCurrentWeapon() : null;
            TestRunner.Log($"1호 손: {(held != null ? held.m_dropPrefab.name + $" 내구도 {held.m_durability:0}/{held.GetMaxDurability():0}" : "없음")}");
            checks.Add("장비 상자에서 도끼를 꺼냄", !eq.GetInventory().HaveItem(axe.GetComponent<ItemDrop>().m_itemData.m_shared.m_name));
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

        static void End(Player player, bool god, Checks checks, System.Action<string, string> onHeard, System.Collections.Generic.List<string> heard)
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
