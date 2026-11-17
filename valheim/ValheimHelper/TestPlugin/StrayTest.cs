using System.Collections;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_straytest (요청 지나가며 줍기 11-18): 쉬는 일꾼 둘레 땅에 몬스터 떨굼 같은 새 물건(줍기)·플레이어가 든 적 있는 물건(m_pickedUp, 안 줍기)·
    /// 일꾼이 일부러 놓은 물건(Guard.KeepKey, 안 줍기)을 두고 15초 안에 새 물건만 가방에 들어가는지.
    /// </summary>
    static class StrayTest
    {
        public static void Register() => TestRunner.Register("vh_straytest", "지나가며 떨어진 물건 줍기 시험", () => TestKit.Flat(Run()));

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
            var made = new System.Collections.Generic.List<ItemDrop>();
            try { yield return Body(w, checks, made); }
            finally
            {
                Guard.TestStray = false;
                foreach (var d in made) if (d != null && d.TryGetComponent(out ZNetView v) && v.IsValid()) ZNetScene.instance.Destroy(d.gameObject);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static ItemDrop Drop(string prefab, int n, Vector3 at, System.Collections.Generic.List<ItemDrop> made)
        {
            ItemDrop.ItemData it = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.Clone();
            it.m_dropPrefab = ObjectDB.instance.GetItemPrefab(prefab);
            ItemDrop d = ItemDrop.DropItem(it, n, GatherTest.Ground(at) + Vector3.up * 0.3f, Quaternion.identity);
            made.Add(d);
            return d;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Collections.Generic.List<ItemDrop> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;
            WorkerNpc r = w.Ragnar;
            yield return new WaitForSeconds(2f);
            checks.Add($"라그나 쉼 ({r.Ai.Current})", r.Ai.Current == WorkerAI.Mode.Idle);
            Vector3 me = r.transform.position;
            Vector3 away = me - w.Player.transform.position; away.y = 0f;
            away = away.sqrMagnitude < 0.01f ? Vector3.forward : away.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, away);
            Inventory inv = r.GetInventory();
            int had(string name) => inv.CountItems(ObjectDB.instance.GetItemPrefab(name).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
            int resin0 = had("Resin"), feather0 = had("Feathers"), leather0 = had("LeatherScraps");

            ItemDrop fresh = Drop("Resin", 3, me + away * 3.5f, made);
            ItemDrop held = Drop("Feathers", 2, me + away * 3f + side * 2.5f, made);
            if (held != null) { held.m_itemData.m_pickedUp = true; ItemDrop.SaveToZDO(held.m_itemData, held.GetComponent<ZNetView>().GetZDO()); }
            ItemDrop kept = Drop("LeatherScraps", 2, me + away * 3f - side * 2.5f, made);
            if (kept != null) kept.GetComponent<ZNetView>().GetZDO().Set(Guard.KeepKey, true);
            checks.Add("땅 물건 3개 놓음", fresh != null && held != null && kept != null);
            Guard.TestStray = true;
            float t = 0f;
            while (t < 15f && had("Resin") < resin0 + 3) { t += 0.5f; yield return new WaitForSeconds(0.5f); }
            yield return new WaitForSeconds(3f);
            checks.Add($"새 물건(송진 3) 주움 ({had("Resin") - resin0}, {t:0.0}초)", had("Resin") >= resin0 + 3);
            checks.Add($"플레이어가 든 적 있는 깃털 안 주움 ({had("Feathers") - feather0})", had("Feathers") == feather0 && held != null && held.GetComponent<ZNetView>().IsValid());
            checks.Add($"일꾼이 놓은 가죽 조각 안 주움 ({had("LeatherScraps") - leather0})", had("LeatherScraps") == leather0 && kept != null && kept.GetComponent<ZNetView>().IsValid());
            checks.Add($"주운 뒤 다시 쉼 ({r.Ai.Current})", r.Ai.Current == WorkerAI.Mode.Idle);
            inv.RemoveItem(ObjectDB.instance.GetItemPrefab("Resin").GetComponent<ItemDrop>().m_itemData.m_shared.m_name, had("Resin") - resin0);
        }
    }
}
