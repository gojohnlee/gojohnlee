using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_squitotest (⑫ 평원 라): 데스킷(날아드는 빠른 적, 체력 10·한 방 90 찌르기).
    /// A) 평원 차림(늑대 갑옷·철검)으로 데스킷 하나 → 싸워서 맞혀 잡는지, 안 죽는지, 데스킷을 배우는지.
    /// B) 데스킷은 일꾼보다 빨리 난다(Guard.ChaseSpeed = m_flyFastSpeed) → 물러남 판단이어도 등 뒤로 맞기만 하니 싸움.
    /// C) 약한 차림(가죽·몽둥이)에 데스킷 둘 → 가만있지 않고 반응(싸움·도망·물러남), 결과(죽음 포함)는 기록. 평원 자리는 VH_BIOME=plains.
    /// </summary>
    static class SquitoTest
    {
        public static void Register() => TestRunner.Register("vh_squitotest", "데스킷 싸움 시험", () => TestKit.Flat(Run()));

        static readonly string[] PlainsGear = { "ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake", "SwordIron" };

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
            w.CountsCreatures = true;
            var spawned = new List<GameObject>();
            try { yield return Body(w, checks, spawned); }
            finally
            {
                Cleanup(spawned);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;
            GameObject prefab = ZNetScene.instance.GetPrefab("Deathsquito");
            checks.Add("데스킷 프리팹", prefab != null);
            if (prefab == null) yield break;
            var npc = w.Ragnar;
            Character proto = prefab.GetComponent<Character>();
            TestRunner.Log($"데스킷: 날기 {proto.m_flying}, 빠르게 {proto.m_flyFastSpeed:0.0}·느리게 {proto.m_flySlowSpeed:0.0}·달리기 {proto.m_runSpeed:0.0}, 일꾼 달리기 {npc.m_runSpeed:0.0}, 한 방 데이터 {Guard.DataHit(proto):0.0}");
            checks.Add($"데스킷은 일꾼보다 빨리 남 ({Guard.ChaseSpeed(proto):0.0} > {npc.m_runSpeed:0.0})", Guard.ChaseSpeed(proto) > npc.m_runSpeed * 1.05f);

            // A) 평원 차림 → 싸워서 잡음
            foreach (var n in PlainsGear) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
            Prep.WearBest(npc);
            npc.Heal(npc.GetMaxHealth(), false);
            yield return new WaitForSeconds(1f);
            int deaths0 = npc.Deaths;
            Character a = Spawn(prefab, npc.transform.position + npc.transform.forward * 15f, spawned);
            bool fought = false;
            float t = 0f, low = npc.GetHealth();
            while (t < 40f && a != null && !a.IsDead() && npc.Deaths == deaths0)
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                if (npc.Ai.Guard?.StateName == "Fight") fought = true;
                low = Mathf.Min(low, npc.GetHealth());
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[A {t:0}s] 경계 {npc.Ai.Guard?.StateName}/{npc.Ai.Guard?.LastVerdict} 비율 {npc.Ai.Guard?.LastRatio:0.00} ({npc.Ai.Guard?.LastDetail}), 체력 {npc.GetHealth():0}, " +
                    $"데스킷 {(a ? a.GetHealth() : 0):0} 거리 {(a ? Utils.DistanceXZ(a.transform.position, npc.transform.position) : 0):0.0}m 높이차 {(a ? a.transform.position.y - npc.transform.position.y : 0):0.0}m");
            }
            checks.Add($"평원 차림으로 데스킷과 싸움 (판단 {npc.Ai.Guard?.LastVerdict}, 갑옷 {npc.GetBodyArmor():0})", fought);
            checks.Add($"날아다니는 데스킷을 맞혀 잡음 ({t:0.0}초)", a == null || a.IsDead());
            checks.Add($"안 죽음 (죽음 {npc.Deaths - deaths0}, 가장 낮은 체력 {low:0}/{npc.GetMaxHealth():0})", npc.Deaths == deaths0 && !npc.IsDead());
            checks.Add("데스킷을 배움", Brain.Foes.Knows("Deathsquito"));
            if (a != null && !a.IsDead()) Kill(a);
            yield return new WaitForSeconds(2f);

            // C) 약한 차림 + 데스킷 둘 → 반응(가만히 서서 맞지 않음). 데스킷이 더 빨라 도망은 못 떨치니 결과는 기록만.
            if (npc.IsDead()) yield break;
            foreach (var n in PlainsGear) { var it = npc.GetInventory().GetItem(ObjectDB.instance.GetItemPrefab(n).GetComponent<ItemDrop>().m_itemData.m_shared.m_name); if (it != null) { npc.UnequipItem(it, false); npc.GetInventory().RemoveItem(it); } }
            npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Club"), 1);
            Prep.WearBest(npc);
            npc.Heal(npc.GetMaxHealth(), false);
            yield return new WaitForSeconds(1f);
            deaths0 = npc.Deaths;
            var two = new List<Character>();
            for (int i = 0; i < 2; i++) two.Add(Spawn(prefab, npc.transform.position + npc.transform.forward * 20f + npc.transform.right * (i * 3f - 1.5f), spawned));
            var states = new HashSet<string>();
            t = 0f;
            while (t < 20f && npc.Deaths == deaths0 && two.Exists(c => c != null && !c.IsDead()))
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
                if (npc.Ai.Guard != null && npc.Ai.Guard.StateName != "Calm") states.Add(npc.Ai.Guard.StateName);
                if ((int)(t * 2) % 10 == 0) TestRunner.Log($"[C {t:0}s] 경계 {npc.Ai.Guard?.StateName}/{npc.Ai.Guard?.LastVerdict} 비율 {npc.Ai.Guard?.LastRatio:0.00}, 체력 {npc.GetHealth():0}/{npc.GetMaxHealth():0}·갑옷 {npc.GetBodyArmor():0}, 남은 데스킷 {two.FindAll(c => c != null && !c.IsDead()).Count}");
            }
            TestRunner.Log($"C 결과: 상태 [{string.Join(",", new List<string>(states).ToArray())}], 죽음 {npc.Deaths - deaths0}, 남은 데스킷 {two.FindAll(c => c != null && !c.IsDead()).Count}, {t:0.0}초");
            checks.Add($"약한 차림도 데스킷 둘에 반응함 (상태 {string.Join(",", new List<string>(states).ToArray())})", states.Count > 0);
            foreach (var c in two) if (c != null && !c.IsDead()) Kill(c);
            yield return new WaitForSeconds(2f);
        }

        static void Cleanup(List<GameObject> spawned)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
        }

        static Character Spawn(GameObject prefab, Vector3 pos, List<GameObject> spawned)
        {
            GameObject go = Object.Instantiate(prefab, GatherTest.Ground(pos) + Vector3.up * 2f, Quaternion.identity);
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
