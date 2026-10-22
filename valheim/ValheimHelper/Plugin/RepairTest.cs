using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_repairtest (E 장비 수리): 부러진 돌도끼·반 넘게 닳은 누더기 옷을 짐에 넣고 거점 10m 옆에 제작대를 세운 뒤 일을 시키면
    /// 출발 준비가 제작대에 들러 둘 다 최대 내구도로 고치는지. 제작대가 없거나 지붕이 없으면 못 고침(바닐라 규칙).
    /// 조금 닳은 것(90%)은 고치러 가지 않는다. 제작대는 시험이 만들고 끝나면 지운다.
    /// </summary>
    static class RepairTest
    {
        public static void Register() => TestRunner.Register("vh_repairtest", "장비 수리 시험", () => TestKit.Flat(Run()));

        static readonly string[] Items = { "AxeFlint", "ArmorRagsChest", "ArmorRagsLegs" };

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
            GameObject bench = null;
            try { yield return Body(w, checks, g => bench = g); }
            finally
            {
                MachineStep.TestIgnoreRoof = false;
                if (bench != null) ZNetScene.instance.Destroy(bench);
                if (w.Claude != null) { Clear(w.Claude); TestKit.ClearBaseStock(w, Items); }
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void Clear(WorkerNpc npc)
        {
            foreach (var it in new System.Collections.Generic.List<ItemDrop.ItemData>(npc.GetInventory().GetAllItems()))
                if (it.m_dropPrefab != null && System.Array.IndexOf(Items, it.m_dropPrefab.name) >= 0)
                {
                    if (npc.IsItemEquiped(it)) npc.UnequipItem(it, false);
                    npc.GetInventory().RemoveItem(it);
                }
        }

        static ItemDrop.ItemData Give(WorkerNpc npc, string prefab, float part)
        {
            ItemDrop.ItemData it = npc.GetInventory().AddItem(prefab, 1, 1, 0, 0L, "", false);
            if (it != null) it.m_durability = it.GetMaxDurability() * part;
            return it;
        }

        static string Dur(ItemDrop.ItemData it) => it == null ? "없음" : $"{it.m_durability:0}/{it.GetMaxDurability():0}";

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Action<GameObject> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
            var npc = w.Claude;
            Clear(npc);
            TestKit.ClearBaseStock(w, Items);
            ItemDrop.ItemData axe = Give(npc, "AxeFlint", 0f), chest = Give(npc, "ArmorRagsChest", 0.2f), legs = Give(npc, "ArmorRagsLegs", 0.9f);
            checks.Add($"짐에 부러진 돌도끼·닳은 옷 ({Dur(axe)}, {Dur(chest)}, {Dur(legs)})", axe != null && chest != null && legs != null);
            if (axe == null || chest == null || legs == null) yield break;
            checks.Add("제작대 없으면 못 고침", Repair.StationFor(axe, npc.Home) == null);

            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            GameObject g = Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"), GatherTest.Ground(w.Home - right * 10f), Quaternion.identity);
            made(g);
            yield return new WaitForSeconds(1f);
            MachineStep.TestIgnoreRoof = false;
            checks.Add("지붕 없는 제작대에선 못 고침", Repair.StationFor(axe, npc.Home) == null);
            MachineStep.TestIgnoreRoof = true;
            checks.Add("지붕 판단 끄면 고칠 설비 = 제작대", Repair.StationFor(axe, npc.Home) != null && Repair.StationFor(chest, npc.Home) != null);
            checks.Add("90% 남은 바지는 고칠 필요 없음", Repair.StationFor(legs, npc.Home) == null);

            GatherTest.Say($"!{Workers.Name1} 나무 5", null);
            yield return TestKit.WaitFor(() => axe.m_durability >= axe.GetMaxDurability() && chest.m_durability >= chest.GetMaxDurability(), 60f);
            checks.Add($"준비 때 제작대에서 고침 ({TestKit.Waited:0}초, 도끼 {Dur(axe)}, 옷 {Dur(chest)})", TestKit.Ok);
            checks.Add($"바지는 그대로 ({Dur(legs)})", legs.m_durability < legs.GetMaxDurability());
            yield return new WaitForSeconds(2f);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            checks.Add($"고친 도끼·옷이 짐에 남음", npc.GetInventory().ContainsItem(axe) && npc.GetInventory().ContainsItem(chest));
        }
    }
}
