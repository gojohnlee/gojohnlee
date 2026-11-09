using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_livestocktest (사용자 11-09 요청 다 "가축", 첫 조각 = 확인): 가축 프리팹 수치(길들이기·먹이·번식·자람)를 적고,
    /// 야생 멧돼지가 일꾼을 적으로 보는지·일꾼이 멀리 있을 때 떨군 먹이를 먹고 길들이기가 줄어드는지·길들인 멧돼지가 SetFollowTarget(일꾼)으로 따라오는지 본다.
    /// </summary>
    static class LivestockTest
    {
        public static void Register() => TestRunner.Register("vh_livestocktest", "가축 확인(수치·먹이·따라오기)", () => TestKit.Flat(Run()), 600f);

        static readonly string[] Kinds = { "Boar", "Wolf", "Lox", "Hen", "Asksvin" };

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
            var made = new List<GameObject>();
            try { yield return Body(w, checks, made); }
            finally
            {
                foreach (var g in made) if (g != null) ZNetScene.instance.Destroy(g);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void LogPrefabs()
        {
            foreach (string k in Kinds)
            {
                var pf = ZNetScene.instance.GetPrefab(k);
                if (pf == null) { TestRunner.Log($"가축 {k}: 프리팹 없음"); continue; }
                var t = pf.GetComponent<Tameable>();
                var ai = pf.GetComponent<BaseAI>();
                var m = ai as MonsterAI;
                var pr = pf.GetComponent<Procreation>();
                var c = pf.GetComponent<Character>();
                string eats = m != null && m.m_consumeItems != null ? string.Join(",", m.m_consumeItems.ConvertAll(i => i != null ? i.name : "?")) : "-";
                string line = $"가축 {k}: AI {(ai != null ? ai.GetType().Name : "-")}, 진영 {(c != null ? c.m_faction.ToString() : "-")}, 체력 {(c != null ? c.m_health : 0):0}";
                if (ai != null) line += $", 시야 {ai.m_viewRange:0} 듣기 {ai.m_hearRange:0}";
                if (m != null) line += $", 경보 {m.m_alertRange:0}, 먹이 찾기 {m.m_consumeSearchRange:0}m/{m.m_consumeSearchInterval:0}초, 먹이 [{eats}]";
                if (t != null) line += $", 길들이기 {t.m_tamingTime:0}초, 배부름 {t.m_fedDuration:0}초, 명령 {t.m_commandable}";
                if (pr != null)
                {
                    var gu = pr.m_offspring != null ? pr.m_offspring.GetComponent<Growup>() : null;
                    line += $", 번식 {pr.m_updateInterval:0}초마다 확률 {pr.m_pregnancyChance:0.##} 사랑 {pr.m_requiredLovePoints} 임신 {pr.m_pregnancyDuration:0}초 최대 {pr.m_maxCreatures}({pr.m_totalCheckRange:0}m) 짝 {pr.m_partnerCheckRange:0}m, 새끼 {(pr.m_offspring != null ? pr.m_offspring.name : "-")} 자람 {(gu != null ? gu.m_growTime : 0):0}초";
                }
                TestRunner.Log(line);
            }
        }

        static Character Spawn(string prefab, Vector3 at, List<GameObject> made)
        {
            var g = Object.Instantiate(ZNetScene.instance.GetPrefab(prefab), GatherTest.Ground(at) + Vector3.up * 0.3f, Quaternion.identity);
            made.Add(g);
            return g.GetComponent<Character>();
        }

        static float TameLeft(Character c) => c.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, c.GetComponent<Tameable>().m_tamingTime);

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> made)
        {
            LogPrefabs();
            yield return TestKit.Setup(w);
            checks.Add("깃발·두 일꾼·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null) yield break;
            Vector3 home = w.Home;
            Vector3 side = Vector3.Cross(Vector3.up, (w.Ragnar.transform.position - home).sqrMagnitude > 1f ? (w.Ragnar.transform.position - home).normalized : Vector3.forward);

            // 1) 야생 멧돼지 — 일꾼(진영 Players)을 적으로 보는지, 6m 옆에서 경보·목표가 되는지
            var wild = Spawn("Boar", w.Ragnar.transform.position + side * 6f, made);
            checks.Add("야생 멧돼지는 일꾼을 적으로 봄(BaseAI.IsEnemy)", BaseAI.IsEnemy(wild, w.Ragnar));
            float hp0 = w.Ragnar.GetHealth();
            yield return new WaitForSeconds(6f);
            if (wild == null) TestRunner.Log($"야생 멧돼지 6m: 6초 안에 죽음(일꾼이 잡음), 라그나르 체력 {hp0:0}→{w.Ragnar.GetHealth():0}");
            else
            {
                TestRunner.Log($"야생 멧돼지 6m: 경보 {wild.GetComponent<MonsterAI>().IsAlerted()}, 거리 {Vector3.Distance(wild.transform.position, w.Ragnar.transform.position):0.0}m, 라그나르 체력 {hp0:0}→{w.Ragnar.GetHealth():0}");
                ZNetScene.instance.Destroy(wild.gameObject);
            }

            // 2) 일꾼이 멀리(40m) 있을 때 떨군 먹이를 먹고 길들이기가 줄어드는지
            Vector3 far = home + side * 40f;
            var boar = Spawn("Boar", far, made);
            var tm = boar.GetComponent<Tameable>();
            yield return new WaitForSeconds(1f);
            float left0 = TameLeft(boar);
            var berry = Object.Instantiate(ObjectDB.instance.GetItemPrefab("Raspberry"), boar.transform.position + side * 1.5f + Vector3.up, Quaternion.identity);
            berry.GetComponent<ItemDrop>().SetStack(3);
            made.Add(berry);
            yield return TestKit.WaitFor(() => boar != null && !tm.IsHungry(), 25f);
            checks.Add("떨군 산딸기를 먹음(배부름)", boar != null && !tm.IsHungry());
            yield return new WaitForSeconds(7f);
            if (boar == null) { checks.Add("먹이 시험 멧돼지 살아 있음", false); yield break; }
            float left1 = TameLeft(boar);
            TestRunner.Log($"길들이기 남은 시간 {left0:0}→{left1:0}초, 경보 {boar.GetComponent<MonsterAI>().IsAlerted()}, 일꾼까지 {Vector3.Distance(boar.transform.position, w.Ragnar.transform.position):0}m");
            checks.Add("배부르면 길들이기 시간이 줄어듦", left1 < left0);

            // 3) 길들인 멧돼지가 일꾼을 따라오는지(SetFollowTarget — 바닐라 Command는 Player만 받음)
            var mai = boar.GetComponent<MonsterAI>();
            mai.MakeTame();
            checks.Add("길들인 멧돼지는 일꾼을 적으로 안 봄", boar.IsTamed() && !BaseAI.IsEnemy(boar, w.Ragnar));
            mai.SetFollowTarget(w.Ragnar.gameObject);
            float d0 = Vector3.Distance(boar.transform.position, w.Ragnar.transform.position);
            yield return TestKit.WaitFor(() => boar == null || Vector3.Distance(boar.transform.position, w.Ragnar.transform.position) < 5f, 30f);
            if (boar == null) { checks.Add("따라오기 멧돼지 살아 있음", false); yield break; }
            float d1 = Vector3.Distance(boar.transform.position, w.Ragnar.transform.position);
            TestRunner.Log($"따라오기: {d0:0}m → {d1:0.0}m");
            checks.Add("SetFollowTarget(일꾼)으로 따라옴(5m 안)", d1 < 5f);
            mai.SetFollowTarget(null);
        }
    }
}
