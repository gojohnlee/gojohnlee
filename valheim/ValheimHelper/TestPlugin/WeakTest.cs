using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_weaktest (전투 보강 나 — 적 약점·저항): 1호에게 청동 칼·몽둥이를 주고 해골과 싸운다(거점에서 60m 앞).
    /// A) 해골의 실제 배율(GetDamageModifiers)로 두 무기의 들어가는 한 방을 셈 → 싸울 때 든 무기가 더 센 쪽(Guard.BestVs).
    /// B) 위험 판단의 해골 체력이 그 비만큼 보정됨(LastDetail의 체력 = 실제 체력 × WeakScale).
    /// C) 잡음 + 살아 있음. D) 배율 없이 고르면(HoldWeapon()) 예전처럼 피해 합이 큰 무기.
    /// </summary>
    static class WeakTest
    {
        public static void Register() => TestRunner.Register("vh_weaktest", "적 약점 시험", () => TestKit.Flat(Run()));

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
            Player.m_localPlayer.SetGhostMode(true);
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
            GameObject sk = ZNetScene.instance.GetPrefab("Skeleton");
            checks.Add("해골 프리팹 있음", sk != null);
            if (w.Ragnar == null || sk == null) yield break;
            WorkerNpc r = w.Ragnar;
            Inventory inv = r.GetInventory();
            foreach (var n in new[] { "SwordBronze", "Club", "ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze" }) inv.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
            inv.AddItem(ObjectDB.instance.GetItemPrefab("MeadHealthMinor"), 3);
            Prep.WearBest(r);
            r.Heal(1000f, false);
            var items = inv.GetAllItems();
            ItemDrop.ItemData sword = items.Find(it => it.m_dropPrefab != null && it.m_dropPrefab.name == "SwordBronze");
            ItemDrop.ItemData club = items.Find(it => it.m_dropPrefab != null && it.m_dropPrefab.name == "Club");
            checks.Add("칼·몽둥이 있음", sword != null && club != null);
            if (sword == null || club == null) yield break;

            // D) 배율 없이: 예전처럼 피해 합이 큰 무기
            r.HoldWeapon();
            ItemDrop.ItemData plain = Gear.Best(items, Gear.IsMelee, Gear.WeaponScore);
            checks.Add($"배율 없이 고르면 피해 합이 큰 무기 ({r.GetCurrentWeapon()?.m_shared.m_name})", r.GetCurrentWeapon() == plain);

            Quaternion rot = w.Player.transform.rotation;
            Vector3 arena = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 60f));
            r.TeleportTo(arena + Vector3.up * 0.3f, rot, false);
            yield return new WaitForSeconds(2f);
            Character s = Spawn(sk, r.transform.position + r.transform.forward * 8f, spawned);
            yield return new WaitForSeconds(0.5f);
            float[] mults = Guard.Mults(s.GetDamageModifiers());
            TestRunner.Log("해골 배율(둔기·베기·찌르기·불·냉기·번개·독·영): " + string.Join(", ", System.Array.ConvertAll(mults, m => m.ToString("0.00"))));
            float swordHit = Guard.HitWith(r, sword, mults), clubHit = Guard.HitWith(r, club, mults);
            ItemDrop.ItemData expect = clubHit > swordHit ? club : sword;
            TestRunner.Log($"해골에게 들어가는 한 방: 칼 {swordHit:0.0}(배율 없이 {Guard.HitWith(r, sword):0.0}), 몽둥이 {clubHit:0.0}(배율 없이 {Guard.HitWith(r, club):0.0})");
            float scale = Guard.WeakScale(r, items, mults);

            bool fought = false, held = false, judged = false;
            string heldName = "", detail = "";
            float t = 0f;
            while (t < 60f && s != null && !s.IsDead() && !r.IsDead())
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                var guard = r.Ai.Guard;
                if (guard != null && guard.StateName == "Fight")
                {
                    fought = true;
                    heldName = r.GetCurrentWeapon()?.m_shared.m_name ?? "맨손";
                    if (r.GetCurrentWeapon() == expect) held = true;
                }
                // B) LastDetail "Skeleton 체력/한 방" — 판단 때 체력은 실제 × scale
                if (guard != null && !judged && s != null && FoeHealth(guard.LastDetail, "Skeleton", out float hp))
                {
                    detail = guard.LastDetail;
                    judged = Mathf.Abs(hp - s.GetHealth() * scale) <= Mathf.Max(2f, s.GetHealth() * scale * 0.1f);
                }
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[{t:0}s] 경계 {guard?.StateName}/{guard?.LastVerdict} 비율 {guard?.LastRatio:0.00} ({guard?.LastDetail}), 무기 {r.GetCurrentWeapon()?.m_shared.m_name}, 체력 {r.GetHealth():0}");
            }
            checks.Add("해골과 싸움", fought);
            checks.Add($"해골에게 더 세게 들어가는 무기를 듦 (기대 {expect.m_shared.m_name}, 든 것 {heldName})", held);
            checks.Add($"위험 판단의 해골 체력이 약점·저항만큼 보정 (배 {scale:0.00}, {detail})", judged);
            bool killed = s == null || s.IsDead();
            if (!killed) Kill(s);
            checks.Add($"해골 잡음 ({t:0}초)", killed);
            checks.Add($"살아 있음 (체력 {r.GetHealth():0})", !r.IsDead() && r.GetHealth() > 0f);
        }

        /// <summary>LastDetail "적 [Skeleton 60/12.0→…" 에서 그 적의 체력.</summary>
        static bool FoeHealth(string detail, string kind, out float hp)
        {
            hp = 0f;
            int i = detail.IndexOf(kind + " ");
            if (i < 0) return false;
            i += kind.Length + 1;
            int j = detail.IndexOf('/', i);
            return j > i && float.TryParse(detail.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hp);
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
