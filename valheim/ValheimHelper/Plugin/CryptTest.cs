using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_crypttest (⑦ 철 1조각): 거점 110m 앞에 늪 지하묘지를 바닐라 TestSpawnLocation으로 세우고(월드 저장은 다시 켤 때까지 꺼짐 — 시험 월드 보호),
    /// Dungeons가 찾는지·열쇠가 없으면 닫힌 문은 안 고르는지·플레이어가 열쇠를 얻으면(주움 또는 보스 월드 키) 고르는지, 입구 Teleport로 일꾼이
    /// 안(y &gt; 3000)에 들어갔다 나오는지, "!철 3"에 일꾼이 열쇠 문을 직접 열고 들어가 고철을 캐 오는지 본다(사용자 결정 2026-10-22).
    /// 열쇠 상태(플레이어 알려진 재료·월드 키)는 시험 동안 바꾸고 끝에 되돌린다.
    /// </summary>
    static class CryptTest
    {
        public static void Register() => TestRunner.Register("vh_crypttest", "지하묘지 시험", () => TestKit.Flat(Run()));

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
            var before = new HashSet<ZDOID>();
            var spot = new Vector3[1];
            var keys = new KeyState();
            try { yield return Body(w, checks, before, spot, keys); }
            finally
            {
                if (w.Ragnar != null) w.Ragnar.TeleportTo(w.Home, w.Ragnar.transform.rotation, false);
                Cleanup(before, spot[0]);
                keys.Restore();
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        /// <summary>시험 전 열쇠 상태(플레이어가 열쇠를 알았나, 보스 월드 키가 있었나). 끝에 그대로 되돌린다(플레이어 프로필은 저장되므로).</summary>
        internal sealed class KeyState
        {
            public string SharedName, BossKey;
            public bool HadKnown, HadGlobal, Saved;

            static HashSet<string> Known(Player p) => (HashSet<string>)HarmonyLib.AccessTools.Field(typeof(Player), "m_knownMaterial").GetValue(p);

            public void Save(ItemDrop key)
            {
                SharedName = key.m_itemData.m_shared.m_name;
                BossKey = Unlocks.BossKey(Utils.GetPrefabName(key.gameObject));
                HadKnown = Player.m_localPlayer.IsMaterialKnown(SharedName);
                HadGlobal = BossKey != null && ZoneSystem.instance.GetGlobalKey(BossKey);
                Saved = true;
            }

            public void SetKnown(bool on)
            {
                var set = Known(Player.m_localPlayer);
                if (on) set.Add(SharedName); else set.Remove(SharedName);
            }

            public void SetGlobal(bool on)
            {
                if (BossKey == null) return;
                if (on) ZoneSystem.instance.SetGlobalKey(BossKey); else ZoneSystem.instance.RemoveGlobalKey(BossKey);
            }

            public bool Global => BossKey != null && ZoneSystem.instance.GetGlobalKey(BossKey);

            public void Restore()
            {
                if (!Saved || Player.m_localPlayer == null) return;
                SetKnown(HadKnown);
                if (Global != HadGlobal) SetGlobal(HadGlobal);
            }
        }

        internal static List<ZDO> Around(Vector3 at)
        {
            var list = new List<ZDO>();
            if (at != Vector3.zero) ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), new SimulationDistance(2, 0), list); // 2칸: 서리 동굴 안은 위치 중심에서 70m 넘게 퍼짐
            return list;
        }

        /// <summary>세운 지하묘지·동굴(시험 전에 없던 ZDO 중 70m 안 또는 던전 안 높이, 일꾼·플레이어 빼고)을 지운다.
        /// 던전 안 방은 70m 밖까지 퍼져 남으면 다음 시험이 같은 자리에 세운 던전과 겹쳤다(회차 168 frostcave→crypt 묶음 FAIL).</summary>
        internal static void Cleanup(HashSet<ZDOID> before, Vector3 at)
        {
            if (at == Vector3.zero) return;
            int n = 0;
            foreach (var z in Around(at))
            {
                if (before.Contains(z.m_uid) || (Utils.DistanceXZ(z.GetPosition(), at) > 70f && z.GetPosition().y < 4000f)) continue;
                var inst = ZNetScene.instance.FindInstance(z);
                if (inst != null && (inst.GetComponent<WorkerNpc>() != null || inst.GetComponent<Player>() != null)) continue;
                ZDOMan.instance.DestroyZDO(z);
                n++;
            }
            TestRunner.Log($"정리: 세운 지하묘지 물체 {n}개 지움");
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, HashSet<ZDOID> before, Vector3[] spot, KeyState keys)
        {
            w.CountsCreatures = true; // 생물을 부르거나 세는 시험
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC", w.Ragnar != null);
            if (w.Ragnar == null) yield break;

            var names = ZoneSystem.instance.m_locations.Where(l => l.m_prefabName != null && l.m_prefabName.Contains("Crypt"))
                .Select(l => $"{l.m_prefabName}({l.m_biome})").Distinct().ToList();
            TestRunner.Log("Crypt 위치 이름: " + string.Join(", ", names));
            string name = ZoneSystem.instance.m_locations.Where(l => l.m_enable && l.m_prefabName != null && l.m_prefabName.Contains("SunkenCrypt"))
                .Select(l => l.m_prefabName).OrderByDescending(n => n).FirstOrDefault();
            checks.Add($"늪 지하묘지 위치 이름 있음 ({name})", name != null);
            if (name == null) yield break;

            // 지하묘지 안 방은 자리마다 무작위 → 고철 더미가 없게 나오면(회차 136: 더미 0개) 지우고 깃발 둘레 110m 다른 방향에 다시(최대 3곳)
            Quaternion rot = w.Player.transform.rotation;
            Dungeons.Crypt mine = default;
            bool found = false;
            Vector3 pos = Vector3.zero;
            float[] angles = { 0f, 45f, -45f };
            for (int i = 0; i < angles.Length; i++)
            {
                if (i > 0)
                {
                    TestRunner.Log($"고철 더미 없는 지하묘지 → 지우고 {angles[i]:0}° 방향에 다시");
                    Cleanup(before, spot[0]);
                    spot[0] = Vector3.zero;
                    yield return new WaitForSeconds(1f);
                }
                pos = GatherTest.Ground(w.Home + rot * Quaternion.Euler(0f, angles[i], 0f) * new Vector3(0f, 0f, 110f));
                spot[0] = pos;
                foreach (var z in Around(pos)) before.Add(z.m_uid);
                bool spawned = ZoneSystem.instance.TestSpawnLocation(name, pos, disableSave: true);
                if (!spawned) { checks.Add("지하묘지 세움(TestSpawnLocation)", false); yield break; }
                yield return new WaitForSeconds(4f);

                found = false;
                foreach (var c in Dungeons.All())
                    if (Utils.DistanceXZ(c.Pos, pos) < 64f) { mine = c; found = true; }
                if (found && mine.Left > 0) break;
            }
            checks.Add($"지하묘지 세움(TestSpawnLocation, 깃발에서 {Utils.DistanceXZ(pos, w.Home):0}m)", true);
            checks.Add($"Dungeons.All이 찾음 ({(found ? mine.Name + " " + Utils.DistanceXZ(mine.Pos, pos).ToString("0") + "m" : "없음")})", found);
            if (!found) yield break;
            string doorName = mine.Door != null ? ZNetScene.instance.GetPrefab(mine.Door.GetPrefab())?.name : null;
            checks.Add($"입구 열쇠 문 찾음 ({doorName}), 처음엔 닫힘", mine.Door != null && !mine.Open);
            if (mine.Door == null) yield break;
            checks.Add($"안에 남은 고철 더미를 셈 ({mine.Left}개)", mine.Left > 0);
            if (mine.Left == 0) yield break; // 더미가 없으면 뒤 칸(거절·고름·철 일)은 다 헛돎
            int leftBefore = mine.Left;

            // 열쇠: 플레이어가 주운 적 있음(알려진 재료) 또는 그 열쇠를 떨구는 보스가 잡힘(월드 키). 시험 동안 둘 다 끄고 하나씩 켠다.
            Door doorPrefab = ZNetScene.instance.GetPrefab(mine.Door.GetPrefab())?.GetComponent<Door>();
            ItemDrop keyItem = doorPrefab != null ? doorPrefab.m_keyItem : null;
            checks.Add($"문 열쇠 물건 ({(keyItem != null ? Utils.GetPrefabName(keyItem.gameObject) : "없음")})", keyItem != null);
            if (keyItem == null) yield break;
            keys.Save(keyItem);
            checks.Add($"열쇠를 떨구는 보스 월드 키 찾음 ({keys.BossKey ?? "없음"}, 시험 전 주움 {keys.HadKnown}·월드 키 {keys.HadGlobal})", keys.BossKey != null);
            keys.SetKnown(false);
            if (keys.Global) keys.SetGlobal(false);
            yield return TestKit.WaitFor(() => !keys.Global, 5f);

            bool chosen = Dungeons.TryChoose(w.Home, out var pick) && Utils.DistanceXZ(pick.Pos, mine.Pos) < 1f;
            checks.Add("열쇠 없으면 닫힌 지하묘지는 안 고름", !chosen);
            var scrapCmd = new ValheimHelper.Core.Chat.ChatCommand { Kind = ValheimHelper.Core.Chat.CommandKind.Item, Item = Dungeons.ScrapItem, Amount = 3 };
            var closedJudge = Departures.JudgeFor(w.Ragnar, scrapCmd);
            // 다른 지하묘지가 다 캔 곳(더미 0)이면 Core 규칙(Crypts.MinedOut)대로 "다 캠"이 맞다 — 회차 164: 자리를 옮겨 세운 뒤 23/24(cryptsEmpty), 그때 목록을 남긴다
            var allNow = Dungeons.All();
            bool otherEmpty = allNow.Any(c => c.Left == 0);
            string wantKey = otherEmpty ? "depart.cryptsEmpty" : "depart.noCrypt";
            TestRunner.Log("지하묘지 목록: " + string.Join(", ", allNow.Select(c => $"{c.Name} {Utils.DistanceXZ(c.Pos, w.Home):0}m {(c.Open ? "열림" : "닫힘")} 더미 {c.Left}")));
            checks.Add($"열쇠 없고 문 닫힌 묘지뿐이면 철은 출발 전에 거절 ({closedJudge?.Key}, 기대 {wantKey})", closedJudge != null && closedJudge.Key == wantKey);

            keys.SetKnown(true);
            chosen = Dungeons.TryChoose(w.Home, out pick) && Utils.DistanceXZ(pick.Pos, mine.Pos) < 1f && !pick.Open && pick.CanOpen;
            checks.Add("플레이어가 열쇠를 주웠으면 닫힌 곳도 고름(열 수 있음)", chosen);
            keys.SetKnown(false);
            if (keys.BossKey != null)
            {
                keys.SetGlobal(true);
                yield return TestKit.WaitFor(() => keys.Global, 5f);
                chosen = Dungeons.TryChoose(w.Home, out pick) && Utils.DistanceXZ(pick.Pos, mine.Pos) < 1f && pick.CanOpen;
                checks.Add($"보스가 잡혔으면(월드 키) 닫힌 곳도 고름 ({TestKit.Waited:0.0}초)", chosen);
            }
            else keys.SetKnown(true);
            var openJudge = Departures.JudgeFor(w.Ragnar, scrapCmd);
            checks.Add($"열쇠를 얻었으면 철은 거절 안 함 ({openJudge?.Key})", openJudge != null && openJudge.Key != "depart.noCrypt" && openJudge.Key != "depart.cryptsEmpty");

            Teleport gate = Dungeons.Entrance(mine.Pos);
            checks.Add($"입구 Teleport 찾음 ({(gate != null ? Utils.DistanceXZ(gate.transform.position, mine.Pos).ToString("0") + "m" : "없음")})", gate != null);
            if (gate == null) yield break;
            Vector3 inside = Dungeons.ArrivePoint(gate, out _);
            TestRunner.Log($"입구 {gate.transform.position} → 안 {inside}");

            Dungeons.Use(w.Ragnar, gate);
            yield return TestKit.WaitFor(() => w.Ragnar.InInterior(), 10f);
            checks.Add($"일꾼이 안으로 들어감 (y {w.Ragnar.transform.position.y:0}, {TestKit.Waited:0.0}초)", TestKit.Ok);
            if (!TestKit.Ok) yield break;
            yield return new WaitForSeconds(3f);
            checks.Add($"안에서 떨어지지 않음 (y {w.Ragnar.transform.position.y:0}, 도착점과 {Vector3.Distance(w.Ragnar.transform.position, inside):0.0}m)",
                w.Ragnar.InInterior() && Vector3.Distance(w.Ragnar.transform.position, inside) < 15f);
            // 안의 고철 물체 조사: ResourceCatalog(Destructible → 부서지면 생기는 것까지 따라감)가 고철을 낸다고 보는 것
            Vector3 here = w.Ragnar.transform.position;
            var seen = new Dictionary<string, int>();
            var all = new Dictionary<string, int>();
            foreach (var v in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (!v.IsValid() || !Character.InInterior(v.transform.position) || Utils.DistanceXZ(v.transform.position, here) > 120f) continue;
                string k = v.gameObject.name.Replace("(Clone)", "");
                all[k] = all.TryGetValue(k, out int c) ? c + 1 : 1;
                var info = ResourceCatalog.Get(v.GetZDO().GetPrefab());
                if (info == null || !info.Produces.Contains("IronScrap")) continue;
                var de = v.GetComponent<Destructible>();
                string key = $"{info.Prefab} [{info.Tool} 단계 {info.MinToolTier}, 체력 {(de != null ? de.m_health : 0f)}, 부서지면 {(de != null && de.m_spawnWhenDestroyed != null ? de.m_spawnWhenDestroyed.name : "-")}]";
                seen[key] = seen.TryGetValue(key, out int s) ? s + 1 : 1;
            }
            TestRunner.Log("안의 고철 물체: " + (seen.Count == 0 ? "없음" : string.Join(", ", seen.Select(p => p.Key + " ×" + p.Value))));
            TestRunner.Log($"안의 ZNetView 물체 {all.Values.Sum()}개: " + string.Join(", ", all.OrderByDescending(p => p.Value).Take(25).Select(p => p.Key + " ×" + p.Value)));
            checks.Add($"안에 고철 나오는 물체 있음 ({seen.Values.Sum()}개)", seen.Count > 0);
            // 진단: 도착 자리에서 더미마다 묘지 안 길(InteriorNav)이 있는지
            InteriorNav.UseBody(w.Ragnar);
            TestRunner.Log("  안의 " + InteriorNav.WaterDiag(here, 90f));
            int reachable = 0, reachableOld = 0;
            foreach (var v in Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None).Select(d => d.GetComponent<ZNetView>())
                .Where(v => v != null && v.IsValid() && Character.InInterior(v.transform.position) && Utils.DistanceXZ(v.transform.position, here) < 120f)
                .Where(v => ResourceCatalog.Get(v.GetZDO().GetPrefab())?.Produces.Contains("IronScrap") == true)
                .OrderBy(v => Utils.DistanceXZ(v.transform.position, here)))
            {
                var col = v.GetComponentInChildren<Collider>();
                float reach = 1.7f + (col != null ? Mathf.Min(1.5f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) * 0.6f) : 0f); // GatherJob.Aim과 같게
                var path = InteriorNav.Plan(here, v.transform.position, reach, out string why);
                bool old = InteriorNav.CanReach(here, v.transform.position, reach, true);
                if (path != null) reachable++;
                if (old) reachableOld++;
                TestRunner.Log($"  더미 {Utils.DistanceXZ(v.transform.position, here):0}m 높이 {v.transform.position.y - here.y:+0.0;-0.0} 닿을 {reach:0.0}m: 길 {(path != null ? "있음" : "없음")} {why} / 예전 규칙(시작 높이로만 잼) {(old ? "있음" : "없음")}"
                    + (path == null ? " / " + InteriorNav.Explain(here, v.transform.position, reach) : ""));
            }
            TestRunner.Log($"  도착 자리에서 길 있는 더미 {reachable}개(예전 규칙 {reachableOld}개), 일꾼 m_swimDepth {w.Ragnar.m_swimDepth:0.00}(몸 캡슐 높이 {w.Ragnar.GetComponent<CapsuleCollider>()?.height:0.00}·반지름 {w.Ragnar.GetComponent<CapsuleCollider>()?.radius:0.00}), 깊은 물 기준 {InteriorNav.SwimDepth:0.00}m");
            TestRunner.Log("  도착 자리 둘레 지도(±14칸, 위 +z):" + InteriorNav.Map(here, 14));

            Teleport exit = Dungeons.Exit(w.Ragnar.transform.position);
            checks.Add($"나가는 Teleport 찾음 ({(exit != null ? Vector3.Distance(exit.transform.position, here).ToString("0") + "m" : "없음")})", exit != null);
            if (exit == null) yield break;
            Dungeons.Use(w.Ragnar, exit);
            yield return TestKit.WaitFor(() => !w.Ragnar.InInterior(), 10f);
            float toGate = Utils.DistanceXZ(w.Ragnar.transform.position, gate.transform.position);
            checks.Add($"밖으로 나옴 (입구와 {toGate:0.0}m, {TestKit.Waited:0.0}초)", TestKit.Ok && toGate < 15f);
            yield return new WaitForSeconds(2f);

            // ⑦ 철 2조각: "!철 3" → 걸어가 입구로 들어가 진흙 더미를 캐고 나와 거점 상자에 넣는다(고철 한 번에 얼마 나오는지는 바닐라 데이터)
            TestKit.ClearBaseStock(w, "IronScrap");
            // 약식: 거점에서 묘지까지 110m 가는 길은 빼고 입구 12m 앞(거점 쪽)에서 시작 — 문 열기·안 캐기·돌아와 넣기 칸은 같음
            Vector3 start = TestRunner.Quick ? GatherTest.Ground(gate.transform.position + Vector3.Scale(w.Home - gate.transform.position, new Vector3(1f, 0f, 1f)).normalized * 12f) : w.Home;
            w.Ragnar.TeleportTo(start, w.Ragnar.transform.rotation, false);
            yield return new WaitForSeconds(1.5f);
            int stockBefore = GatherTest.CountBase(w.Home, "IronScrap");
            GatherTest.Say($"!{w.Ragnar.NpcName} 철 3", null);
            bool wasInside = false;
            float t = 0f, inAt = -1f, outAt = -1f;
            while (t < 480f && GatherTest.CountBase(w.Home, "IronScrap") - stockBefore < 3)
            {
                yield return new WaitForSeconds(1f);
                t += 1f;
                if (w.Ragnar.InInterior()) { wasInside = true; if (inAt < 0f) inAt = t; }
                else if (wasInside && outAt < 0f) outAt = t;
            }
            int got = GatherTest.CountBase(w.Home, "IronScrap") - stockBefore;
            TestRunner.Log($"철 일: {t:0}초(들어감 {inAt:0}초·나옴 {outAt:0}초{(TestRunner.Quick ? ", 약식 입구 앞 시작" : "")}), 상자에 고철 {got}개, 단계 {(w.Ragnar.Ai.Job is GatherJob g ? g.StepName : "-")}");
            checks.Add($"일꾼이 열쇠 문을 직접 엶 (상태 {mine.Door.GetInt(ZDOVars.s_state)})", Dungeons.IsOpen(mine.Door));
            checks.Add("일꾼이 지하묘지 안으로 들어감", wasInside);
            checks.Add($"고철이 거점 상자에 들어옴 ({got}개, {t:0}초)", got >= 3);
            yield return new WaitForSeconds(3f);
            checks.Add("일꾼이 안에 남지 않음", !w.Ragnar.InInterior());
            int leftAfter = -1;
            foreach (var c in Dungeons.All())
                if (Utils.DistanceXZ(c.Pos, mine.Pos) < 1f) leftAfter = c.Left;
            TestRunner.Log($"남은 고철 더미: 처음 {leftBefore}개 → 캔 뒤 {leftAfter}개(부서진 더미 조각이 남으면 그대로일 수 있음)");
        }
    }
}
