using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_teamtest (5번 같이 싸움): 20m 안에 무기를 든 플레이어가 있으면 위험 판단의 "내 한 방"이 커지는지(Guard.MyHit).
    /// 같은 트롤을 두고 일꾼을 플레이어에게서 멀리/가까이 둬 비교한다. 플레이어는 갓 모드.
    /// </summary>
    static class TeamTest
    {
        public static void Register() => TestRunner.Register("vh_teamtest", "같이 싸움 시험", () => TestKit.Flat(Run()));

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
            bool god = Player.m_localPlayer.InGodMode();
            Player.m_localPlayer.SetGodMode(true);
            s_sword = null;
            try { yield return Body(w, checks, spawned); }
            finally
            {
                Cleanup(spawned);
                Player.m_localPlayer.SetGodMode(god);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static ItemDrop.ItemData s_sword;

        static void Cleanup(List<GameObject> spawned)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
            if (s_sword != null)
            {
                Player.m_localPlayer.UnequipItem(s_sword, false);
                Player.m_localPlayer.GetInventory().RemoveItem(s_sword);
            }
        }

        static float HitOf(Guard g)
        {
            string d = g.LastDetail;
            const string key = "내 한 방 ";
            int i = d.IndexOf(key);
            if (i < 0) return -1f;
            i += key.Length;
            int j = d.IndexOf(',', i);
            return j > i && float.TryParse(d.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : -1f;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Claude != null);
            GameObject troll = ZNetScene.instance.GetPrefab("Troll");
            checks.Add("트롤 프리팹 있음", troll != null);
            if (w.Claude == null || troll == null) yield break;
            var ai = w.Claude.Ai;
            Quaternion rot = w.Player.transform.rotation;

            // A) 일꾼이 플레이어에게서 40m 앞, 트롤은 그 앞 20m
            Vector3 far = GatherTest.Ground(w.Player.transform.position + rot * new Vector3(0f, 0f, 40f));
            w.Claude.TeleportTo(far + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            GameObject t1 = Object.Instantiate(troll, GatherTest.Ground(far + rot * new Vector3(0f, 0f, 20f)) + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Add(t1);
            t1.GetComponent<Character>().SetLevel(1);
            yield return TestKit.WaitFor(() => ai.Guard != null && HitOf(ai.Guard) > 0f, 10f);
            float alone = ai.Guard != null ? HitOf(ai.Guard) : -1f;
            checks.Add($"혼자일 때 판단 수치 나옴 (한 방 {alone:0.0})", alone > 0f);
            if (t1 != null && t1.GetComponent<ZNetView>() is ZNetView v1 && v1.IsValid()) v1.Destroy();
            spawned.Remove(t1);

            // B) 플레이어가 철검을 들고, 일꾼은 플레이어 5m 앞, 트롤은 그 앞 20m
            Player p = w.Player;
            ItemDrop.ItemData sw = p.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("SwordIron"), 1) is bool ok && ok ? p.GetInventory().GetItem("$item_sword_iron") : null;
            s_sword = sw;
            checks.Add("플레이어에 철검", sw != null);
            if (sw != null) p.EquipItem(sw, false);
            yield return new WaitForSeconds(1f);
            Vector3 near = GatherTest.Ground(p.transform.position + rot * new Vector3(0f, 0f, 5f));
            w.Claude.TeleportTo(near + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            GameObject t2 = Object.Instantiate(troll, GatherTest.Ground(near + rot * new Vector3(0f, 0f, 22f)) + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Add(t2);
            t2.GetComponent<Character>().SetLevel(1);
            float with = -1f;
            yield return TestKit.WaitFor(() => { with = ai.Guard != null ? HitOf(ai.Guard) : -1f; return with > alone; }, 10f);
            checks.Add($"무기 든 플레이어가 곁에 있으면 한 방이 커짐 ({alone:0.0} → {with:0.0})", with > alone);
        }
    }
}
