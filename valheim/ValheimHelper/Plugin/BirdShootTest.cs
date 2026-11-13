using System.Collections;
using ValheimHelper.Core.Npc;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_birdshoottest (땅에 앉은 새 명중 셈 — 쏘기 한 발): Ragnar가 활로 앞 10m 땅 새(Crow)를 한 발 쏘고, 맞았는지(죽음)·화살이 한 개 줄었는지 센다.
    /// 여러 판 표·잡음 셈은 다음 조각. 지금은 한 발이 게임 안에서 나가는지부터 본다.
    /// </summary>
    static class BirdShootTest
    {
        public static void Register() => TestRunner.Register("vh_birdshoottest", "땅 앉은 새 명중 셈(한 발)", () => TestKit.Flat(Run()));

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
            try { yield return Body(w, checks); }
            finally { TestKit.Restore(w); }
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Chest != null);

            // 쏘기 한 발: 호위병 대신 Ragnar가 활을 들고, 앞 10m 땅 새(Crow)를 겨눠 바닐라 활 흐름(DrawBow·ReleaseBow)으로 쏜다
            var npc = w.Ragnar;
            npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Bow"), 1);
            npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("ArrowWood"), 20);
            checks.Add("활 들기", npc.HoldBow());
            ItemDrop.ItemData bow = npc.GetCurrentWeapon();
            int before = Gear.CountArrows(npc.GetInventory(), bow);

            Vector3 at = GatherTest.Ground(npc.transform.position + npc.transform.forward * 10f) + Vector3.up * 0.5f;
            GameObject prefab = ZNetScene.instance.GetPrefab("Crow");
            var bird = Object.Instantiate(prefab, at, Quaternion.identity).GetComponent<RandomFlyingBird>();
            checks.Add("새 놓음", bird != null);
            yield return new WaitForSeconds(1f);

            // 당기면서 새를 보고, 다 당기면 놓는다(3초 넘으면 그냥 놓음)
            float t = 0f;
            while (t < 3f && bird != null && npc.HoldingBow())
            {
                npc.Ai.Face(bird.transform.position);
                float pct = npc.DrawBow(Time.deltaTime);
                t += Time.deltaTime;
                if (pct >= 1f) break;
                yield return null;
            }
            // 날아가는 값(속력·중력)으로 앞질러 겨눔 — Hunt.UpdateShoot과 같은 방식(새는 안 움직인다고 봄)
            Vector3 eye = npc.GetEyePoint();
            if (bird != null && npc.ProjectileFlight(bow, out float speed, out float gravity))
            {
                Vector3 rel = bird.transform.position - eye;
                if (Archery.Aim(rel.x, rel.y, rel.z, 0f, 0f, 0f, speed, gravity, out float ax, out float ay, out float az))
                    npc.Ai.Face(eye + new Vector3(ax, ay, az));
            }
            bool shot = npc.ReleaseBow();
            checks.Add("쏨", shot);
            yield return new WaitForSeconds(3f);

            int after = Gear.CountArrows(npc.GetInventory(), bow);
            bool dead = bird == null;
            checks.Add("화살 한 발 나감", after == before - 1);
            TestRunner.Log($"쏘기 1판: 쏨 {(shot ? "예" : "아니오")} · 화살 {before}→{after} · 새 {(dead ? "죽음(맞음)" : "살아 있음(빗나감)")}");
            if (bird != null) ZNetScene.instance.Destroy(bird.gameObject);
            yield break;
        }
    }
}
