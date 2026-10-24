using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_tartest (⑫ 평원 바): 타르 = 웅덩이 안에 떠 있는 Pickable_Tar 맨손 줍기(그때만 들어감), 지나갈 때는 타르 웅덩이(Tared 느려짐)를 피해 다님.
    /// 가장 가까운 실제 타르 웅덩이(ZoneSystem 장소 TarPit*) 옆 마른 땅에 거점을 세운다.
    /// A) "!Tar 3" → 웅덩이에 들어가 주워 상자에, 일 끝나면 타르 밖.
    /// B) 플레이어가 웅덩이 건너편으로 → 따라오는 일꾼은 웅덩이를 가로지르지 않고 돌아서 닿음.
    /// </summary>
    static class TarTest
    {
        public static void Register() => TestRunner.Register("vh_tartest", "타르 줍기·타르 웅덩이 피하기 시험", () => TestKit.Flat(Run()));

        static readonly string[] PlainsGear = { "ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake", "CapeWolf", "SwordIron" };

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
            s_keep = true;
            try { yield return Body(w, checks); }
            finally
            {
                Cleanup(w);
            }
            TestKit.End(w, checks);
        }

        static bool s_keep;

        static void Cleanup(TestKit.World w)
        {
            s_keep = false;
            foreach (var npc in new[] { w.Ragnar, w.Floki }) if (npc != null) npc.Ai.GoHome(false);
            TestKit.Restore(w);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            Player player = Player.m_localPlayer;
            // 가장 가까운 타르 웅덩이 장소
            Vector3 from = player.transform.position;
            Vector3 pit = Vector3.zero;
            float best = float.MaxValue;
            string pitName = null;
            foreach (var li in ZoneSystem.instance.GetLocationList())
            {
                string n = li.m_location?.m_prefabName ?? "";
                if (n.IndexOf("TarPit", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = Utils.DistanceXZ(li.m_position, from);
                if (d < best) { best = d; pit = li.m_position; pitName = n; }
            }
            checks.Add($"타르 웅덩이 장소 ({pitName ?? "없음"} {best:0}m)", pitName != null);
            if (pitName == null) yield break;
            // 웅덩이에서 40m 떨어진 곳으로 가서 불러오기를 기다림
            Vector3 near = pit + new Vector3(40f, 0f, 0f);
            near.y = WorldGenerator.instance.GetHeight(near.x, near.z) + 1f;
            yield return Tp(player, near, player.transform.rotation, true);
            LiquidVolume tar = null;
            foreach (var lv in Object.FindObjectsByType<LiquidVolume>(FindObjectsSortMode.None))
                if (lv.m_liquidType == LiquidType.Tar && Utils.DistanceXZ(lv.transform.position, pit) < 40f) { tar = lv; break; }
            checks.Add("타르 웅덩이(LiquidVolume) 불러옴", tar != null);
            if (tar == null) yield break;
            // 웅덩이 가운데 근처 가장 깊은 곳(일꾼 판단과 같은 TarDepth)
            Vector3 deep = pit;
            float deepest = 0f;
            for (float x = -20f; x <= 20f; x += 2f)
                for (float z = -20f; z <= 20f; z += 2f)
                {
                    Vector3 p = pit + new Vector3(x, 0f, z);
                    float dd = WorkerAI.TarDepth(p);
                    if (dd > deepest) { deepest = dd; deep = p; }
                }
            checks.Add($"타르 깊이 읽힘 (가장 깊은 곳 {deepest:0.0}m, 장소 가운데에서 {Utils.DistanceXZ(deep, pit):0}m)", deepest > WorkerAI.TarWade);
            if (deepest <= WorkerAI.TarWade) yield break;

            // 거점 자리: 깊은 곳에서 25~50m, 8m 둘레에 타르 없고 물 위·평평
            Vector3? site = DrySpot(deep);
            checks.Add("웅덩이 옆 마른 거점 자리", site != null);
            if (site == null) yield break;
            Quaternion face = Quaternion.LookRotation(Flat(deep - site.Value));
            yield return Tp(player, site.Value + Vector3.up * 0.5f, face);

            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            Plugin.Instance.StartCoroutine(TestKit.KeepSiteClear(w.Home, () => s_keep)); // 웅덩이 둘레 성장체(BlobTar)·풀링
            TestKit.ClearBaseStock(w, "Tar");
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                foreach (var n in PlainsGear) npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                Prep.WearBest(npc);
                var food = new Inventory("vh_test_food", null, 4, 2);
                foreach (var n in new[] { "CookedLoxMeat", "Sausages", "SerpentStew" }) food.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                npc.ResetMeals();
                npc.EatFrom(food);
                npc.Heal(1000f, false);
            }

            // 웅덩이 둘레 줍는 타르
            int all = 0, stuck = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var pk in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
            {
                if (!Utils.GetPrefabName(pk.gameObject).StartsWith("Pickable_Tar") || Utils.DistanceXZ(pk.transform.position, deep) > 45f) continue;
                var view = pk.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || view.GetZDO().GetBool(ZDOVars.s_picked)) continue;
                all++;
                bool sunk = pk.m_tarPreventsPicking && pk.GetComponent<Floating>() is Floating fl && fl.IsInTar();
                if (sunk) stuck++;
                if (all <= 8) sb.Append($"[{Utils.GetPrefabName(pk.gameObject)} {Utils.DistanceXZ(pk.transform.position, w.Home):0}m 타르 {WorkerAI.TarDepth(pk.transform.position):0.0}m{(sunk ? " 잠김" : "")}]");
            }
            TestRunner.Log($"줍는 타르 {all}개(지금 잠김 {stuck}): {sb}");
            checks.Add($"줍는 타르 있음 ({all}개)", all > 0);
            if (all == 0) yield break;

            // A) !Tar 3
            float t0 = w.Ragnar.Ai.TarTime + w.Floki.Ai.TarTime;
            int det0 = w.Ragnar.Ai.TarDetours + w.Floki.Ai.TarDetours, deaths0 = w.Ragnar.Deaths + w.Floki.Deaths;
            GatherTest.Say("!Tar 3", null);
            yield return TestKit.WaitFor(() => GatherTest.CountBase(w.Home, "Tar") >= 3, 240f);
            checks.Add($"줍은 타르 3개가 상자에 ({GatherTest.CountBase(w.Home, "Tar")}개, {TestKit.Waited:0}초)", TestKit.Ok);
            float inTar = w.Ragnar.Ai.TarTime + w.Floki.Ai.TarTime - t0;
            TestRunner.Log($"A: 타르 비켜 감 {w.Ragnar.Ai.TarDetours + w.Floki.Ai.TarDetours - det0}번, 깊은 타르 {inTar:0.0}초");
            checks.Add("안 죽음", w.Ragnar.Deaths + w.Floki.Deaths == deaths0);
            yield return TestKit.WaitFor(() => !(w.Ragnar.Ai.Job is GatherJob) && !(w.Floki.Ai.Job is GatherJob), 60f);
            // 일이 끝나면(집으로 감) 타르 밖으로 나와 있음 — 웅덩이 안에 떠 있는 타르를 주우러 들어갔다 나옴
            System.Func<WorkerNpc, bool> outOfTar = n => WorkerAI.TarDepth(n.transform.position) <= WorkerAI.TarWade;
            yield return TestKit.WaitFor(() => outOfTar(w.Ragnar) && outOfTar(w.Floki), 30f);
            checks.Add($"일 끝나고 타르 밖 ({TestKit.Waited:0}초, 라그나 {WorkerAI.TarDepth(w.Ragnar.transform.position):0.0}m·플로키 {WorkerAI.TarDepth(w.Floki.transform.position):0.0}m)", TestKit.Ok);
            TestKit.ClearBaseStock(w, "Tar");

            // B) 건너편으로 따라오기: 곧장 오면 깊은 타르를 지나는 자리
            Vector3? across = DrySpot(deep, w.Home);
            checks.Add("건너편 마른 자리(곧장 가면 깊은 타르)", across != null);
            if (across == null) yield break;
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                npc.Ai.Interrupt();
                npc.Ai.Queue.Clear();
                npc.TeleportTo(w.Home + face * new Vector3(npc == w.Ragnar ? 2f : -2f, 0f, 0f), face, false);
            }
            yield return new WaitForSeconds(2f);
            t0 = w.Ragnar.Ai.TarTime + w.Floki.Ai.TarTime;
            det0 = w.Ragnar.Ai.TarDetours + w.Floki.Ai.TarDetours;
            yield return Tp(player, across.Value + Vector3.up * 0.5f, player.transform.rotation);
            checks.Add($"플레이어가 건너편에 감 ({Utils.DistanceXZ(player.transform.position, across.Value):0}m, 거점에서 {Utils.DistanceXZ(player.transform.position, w.Home):0}m)", Utils.DistanceXZ(player.transform.position, across.Value) < 3f);
            foreach (var npc in new[] { w.Ragnar, w.Floki }) npc.Ai.Follow(player.GetZDOID(), player.GetPlayerName());
            System.Func<WorkerNpc, bool> arrived = n => Utils.DistanceXZ(n.transform.position, player.transform.position) < 6f;
            yield return TestKit.WaitFor(() => arrived(w.Ragnar) && arrived(w.Floki), 120f);
            checks.Add($"웅덩이 건너편 플레이어에게 닿음 ({TestKit.Waited:0}초, 라그나 {Utils.DistanceXZ(w.Ragnar.transform.position, player.transform.position):0}m·플로키 {Utils.DistanceXZ(w.Floki.transform.position, player.transform.position):0}m)", TestKit.Ok);
            inTar = w.Ragnar.Ai.TarTime + w.Floki.Ai.TarTime - t0;
            int det = w.Ragnar.Ai.TarDetours + w.Floki.Ai.TarDetours - det0;
            TestRunner.Log($"B: 타르 비켜 감 {det}번, 깊은 타르 {inTar:0.0}초");
            checks.Add($"웅덩이를 가로지르지 않음 (깊은 타르 {inTar:0.0}초)", inTar < 2f);
            foreach (var npc in new[] { w.Ragnar, w.Floki }) npc.Ai.GoHome(false);
            yield return Tp(player, w.Home + Vector3.up * 0.5f, player.transform.rotation);
            s_keep = false;
        }

        /// <summary>
        /// 깊은 곳에서 25~50m, 8m 둘레에 타르가 없고 물보다 1m 높고 평평한(2m 안) 자리. other가 있으면 그 반대쪽(120도 넘게 벌어짐)이고
        /// other에서 곧장 오는 선이 깊은 타르를 지나는 자리만.
        /// </summary>
        static Vector3? DrySpot(Vector3 deep, Vector3? other = null)
        {
            float wl = ZoneSystem.instance.m_waterLevel;
            for (float r = 25f; r <= 50f; r += 5f)
                for (int k = 0; k < 24; k++)
                {
                    float a = k * Mathf.PI / 12f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 p = deep + dir * r;
                    if (other.HasValue && Vector3.Angle(dir, Flat(other.Value - deep)) < 120f) continue;
                    if (!ZoneSystem.instance.GetGroundHeight(p, out float h) || h < wl + 1f) continue;
                    if (WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.Plains) continue;
                    bool ok = true;
                    for (int j = 0; j < 9 && ok; j++)
                    {
                        Vector3 q = j == 0 ? p : p + new Vector3(Mathf.Cos(j * Mathf.PI / 4f), 0f, Mathf.Sin(j * Mathf.PI / 4f)) * 8f;
                        ok = WorkerAI.TarDepth(q) <= 0f && ZoneSystem.instance.GetGroundHeight(q, out float hq) && Mathf.Abs(hq - h) < 2f;
                    }
                    if (!ok) continue;
                    if (other.HasValue)
                    {
                        bool crosses = false;
                        for (float s = 0f; s <= 1f && !crosses; s += 0.02f) crosses = WorkerAI.TarDepth(Vector3.Lerp(other.Value, p, s)) > WorkerAI.TarWade;
                        if (!crosses) continue;
                    }
                    p.y = h;
                    return p;
                }
            return null;
        }

        /// <summary>플레이어 순간이동(바닐라: 쿨다운 2초면 거절, 옮기기는 2초 뒤) — 받아질 때까지 다시 부르고 끝날 때까지 기다림.</summary>
        static IEnumerator Tp(Player player, Vector3 pos, Quaternion rot, bool distant = false)
        {
            yield return TestKit.WaitFor(() => player.TeleportTo(pos, rot, distant), 10f);
            yield return TestKit.WaitFor(() => !player.IsTeleporting(), 60f);
            yield return new WaitForSeconds(distant ? 5f : 1f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
