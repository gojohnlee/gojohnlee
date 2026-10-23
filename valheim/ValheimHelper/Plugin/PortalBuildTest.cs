using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_portalbuildtest (R2 가, 일꾼 포탈 놓기): 재료 모자라면 아무것도 안 뺌 → 한 쌍 놓기(재료 정확히 두 몫, 같은 이름표, 짝 이어짐, Linked에 나옴)
    /// → 3쌍 한도(넷째 거절·재료 그대로) → 오래 안 쓴 쌍 철거(재료 돌려받음) → 플레이어 포탈(표시 없음)은 안 건드림 → 나머지 철거 후 전체 개수 보존
    /// → 남의 와드 안에는 안 놓음.
    /// </summary>
    static class PortalBuildTest
    {
        public static void Register() => TestRunner.Register("vh_portalbuildtest", "일꾼 포탈 놓기 시험", () => TestKit.Flat(Run()));

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
            try { yield return Body(w, checks, spawned); }
            finally
            {
                Cleanup(spawned);
                TestKit.Restore(w);
            }
            TestKit.End(w, checks);
        }

        static void Cleanup(List<GameObject> spawned)
        {
            foreach (var go in spawned)
                if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
            ClearMine();
        }

        /// <summary>남은 일꾼 포탈(지난 시험 것 포함)을 재료 없이 지운다.</summary>
        internal static int ClearMine()
        {
            int n = 0;
            foreach (var z in PortalBuilder.Mine())
            {
                ZNetView view = ZNetScene.instance.FindInstance(z);
                if (view != null) { view.ClaimOwnership(); ZNetScene.instance.Destroy(view.gameObject); }
                else { z.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(z); }
                n++;
            }
            return n;
        }

        static Dictionary<string, int> Stock(Container c, List<PortalCost> cost)
        {
            var d = new Dictionary<string, int>();
            foreach (var k in cost) d[k.Item] = GatherTest.Count(c, k.Item);
            return d;
        }

        static string Show(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (var kv in d) parts.Add(kv.Key + " " + kv.Value);
            return string.Join(", ", parts);
        }

        internal static void Fill(Container c, List<PortalCost> cost, int portals)
        {
            foreach (var kv in WorkerPortal.Need(cost, portals))
            {
                int max = Mathf.Max(1, ObjectDB.instance.GetItemPrefab(kv.Key).GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
                for (int left = kv.Value; left > 0; left -= max) TestKit.PutInChest(c, kv.Key, Mathf.Min(max, left)); // AddItem(프리팹, n)은 한 더미(최대 묶음)까지만 넣음
            }
        }

        static bool Same(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            foreach (var kv in a) if (!b.TryGetValue(kv.Key, out int n) || n != kv.Value) return false;
            return true;
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, List<GameObject> spawned)
        {
            int old = ClearMine();
            if (old > 0) TestRunner.Log($"지난 일꾼 포탈 {old}개 지움");
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Chest == null) yield break;
            var cost = PortalBuilder.Cost();
            TestRunner.Log("포탈 재료(하나): " + string.Join(", ", cost.ConvertAll(c => c.Item + " " + c.Amount)));
            Piece piece = PortalBuilder.Prefab();
            TestRunner.Log($"작업대 요구: {(piece != null && piece.m_craftingStation != null ? piece.m_craftingStation.m_name : "없음")}");
            checks.Add("제작법 재료 읽음", cost.Count > 0);
            if (cost.Count == 0) yield break;
            var items = cost.ConvertAll(c => c.Item).ToArray();
            TestKit.ClearBaseStock(w, items);

            Quaternion rot = w.Player.transform.rotation;
            Vector3 Site(float deg) => GatherTest.Ground(w.Home + Quaternion.Euler(0f, deg, 0f) * rot * new Vector3(0f, 0f, 35f));

            // 1) 하나 모자람 → 아무것도 안 뺌
            Fill(w.Chest, cost, 2);
            string lastItem = cost[cost.Count - 1].Item;
            TestKit.TakeFromChest(w.Chest, lastItem);
            Fill(w.Chest, new List<PortalCost> { new PortalCost(lastItem, WorkerPortal.Need(cost, 2)[lastItem] - 1) }, 1);
            var before = Stock(w.Chest, cost);
            bool ok = PortalBuilder.PlacePair("시험A", w.Home, w.Home, Site(0f), out var placed, out var missing, out string why);
            checks.Add($"모자라면 거절 ({why}, 모자람 {Show(missing)})", !ok && missing.Count == 1 && missing.TryGetValue(lastItem, out int m1) && m1 == 1);
            checks.Add("거절하면 재료 그대로·포탈 없음", Same(before, Stock(w.Chest, cost)) && PortalBuilder.Mine().Count == 0);

            // 2) 채우고 한 쌍. 먼저 놓기 실패(시험 스위치: 첫째에서·둘째에서 = 반쪽 생김) → 재료 그대로·포탈 0개(요청 10-23 09:09)
            TestKit.PutInChest(w.Chest, lastItem, 1);
            before = Stock(w.Chest, cost);
            for (int fail = 0; fail < 2; fail++)
            {
                PortalBuilder.TestFailAfter = fail;
                ok = PortalBuilder.PlacePair("시험A", w.Home, w.Home, Site(0f), out placed, out missing, out why);
                PortalBuilder.TestFailAfter = -1;
                yield return new WaitForSeconds(0.5f);
                int left = PortalBuilder.Mine().Count + ZdoScan.All(Portals.PrefabName).FindAll(z => z.GetString(ZDOVars.s_tag) == WorkerPortal.Tag("시험A")).Count;
                checks.Add($"놓기 실패({(fail == 0 ? "첫째" : "둘째, 반쪽 지움")}) → 재료 그대로·포탈 0 ({why}, 남은 {left})",
                    !ok && Same(before, Stock(w.Chest, cost)) && left == 0);
            }
            ok = PortalBuilder.PlacePair("시험A", w.Home, w.Home, Site(0f), out placed, out missing, out why);
            TestRunner.Log("놓기: " + why);
            yield return new WaitForSeconds(1f);
            var after = Stock(w.Chest, cost);
            bool allUsed = true;
            foreach (var kv in after) if (kv.Value != 0) allUsed = false;
            checks.Add($"한 쌍 놓음 ({placed.Count}개)", ok && placed.Count == 2);
            checks.Add($"재료 두 몫 정확히 빠짐 (남음 {Show(after)})", allUsed);
            if (!ok || placed.Count != 2) yield break;
            string tagA = WorkerPortal.Tag("시험A");
            checks.Add("같은 이름표 " + tagA, placed[0].GetString(ZDOVars.s_tag) == tagA && placed[1].GetString(ZDOVars.s_tag) == tagA);
            bool linked = placed[0].GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) == placed[1].m_uid
                && placed[1].GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) == placed[0].m_uid;
            checks.Add("짝 이어짐", linked);
            int inLinked = 0;
            foreach (var l in Portals.Linked())
                foreach (var z in placed)
                    if (Mathf.Abs(l.X - z.GetPosition().x) < 0.3f && Mathf.Abs(l.Z - z.GetPosition().z) < 0.3f) inLinked++;
            checks.Add($"Portals.Linked에 둘 다 ({inLinked})", inLinked == 2);
            if (inLinked != 2)
            {
                var all = ZdoScan.All(Portals.PrefabName);
                foreach (var z in all)
                    TestRunner.Log($"  DIAG 포탈 {z.m_uid} {z.GetPosition():F1} tag={z.GetString(ZDOVars.s_tag)} 내것={z.GetBool(PortalBuilder.MarkKey)} 짝={z.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal)} 물체={ZNetScene.instance.FindInstance(z) != null}");
            }
            bool safe = true;
            foreach (var z in placed)
            {
                Vector3 p = z.GetPosition();
                if (Location.IsInsideNoBuildLocation(p) || p.y < ZoneSystem.instance.m_waterLevel + 0.3f) safe = false;
            }
            checks.Add("놓은 자리 물·건축 금지 아님", safe);

            // 3) 3쌍 한도
            Fill(w.Chest, cost, 6);
            bool b = PortalBuilder.PlacePair("시험B", w.Home, w.Home, Site(120f), out _, out _, out string wb);
            bool c = PortalBuilder.PlacePair("시험C", w.Home, w.Home, Site(240f), out _, out _, out string wc);
            TestRunner.Log($"B: {wb} / C: {wc}");
            yield return new WaitForSeconds(1f);
            checks.Add($"셋째 쌍까지 놓음 (쌍 {PortalBuilder.Pairs()})", b && c && PortalBuilder.Pairs() == 3);
            Fill(w.Chest, cost, 2);
            before = Stock(w.Chest, cost);
            ok = PortalBuilder.PlacePair("시험D", w.Home, w.Home, Site(60f), out _, out _, out why);
            checks.Add($"넷째 쌍 거절 ({why})", !ok && PortalBuilder.Pairs() == 3);
            checks.Add("거절하면 재료 그대로", Same(before, Stock(w.Chest, cost)));

            // 4) 플레이어 포탈(표시 없음)은 철거 안 함
            Vector3 pp = GatherTest.Ground(w.Home + rot * new Vector3(-8f, 0f, -6f));
            GameObject player = Object.Instantiate(ZNetScene.instance.GetPrefab(Portals.PrefabName), pp, rot);
            spawned.Add(player);
            yield return null;
            ZDO pz = player.GetComponent<ZNetView>().GetZDO();
            pz.Set(ZDOVars.s_tag, WorkerPortal.Tag("플레이어"));
            pz.Set(PortalBuilder.UsedDayKey, -100L);

            // 5) 오래 안 쓴 쌍 철거 → 재료 돌려받음
            var refundOne = new Dictionary<string, int>();
            foreach (var r in piece.m_resources)
                if (r.m_resItem != null && r.m_recover && r.m_amount > 0) refundOne[Utils.GetPrefabName(r.m_resItem.gameObject)] = r.m_amount;
            long today = EnvMan.instance.GetDay();
            foreach (var z in PortalBuilder.Mine())
                z.Set(PortalBuilder.UsedDayKey, z.GetString(ZDOVars.s_tag) == tagA ? today - WorkerPortal.IdleDays : today);
            before = Stock(w.Chest, cost);
            var removed = PortalBuilder.RemoveIdle(w.Home);
            yield return new WaitForSeconds(1f);
            after = Stock(w.Chest, cost);
            bool refundOk = true;
            foreach (var kv in before)
            {
                refundOne.TryGetValue(kv.Key, out int r1);
                if (after[kv.Key] != kv.Value + r1 * 2) refundOk = false;
            }
            checks.Add($"오래 안 쓴 쌍만 철거 ({string.Join(",", removed)}, 쌍 {PortalBuilder.Pairs()})", removed.Count == 1 && removed[0] == tagA && PortalBuilder.Pairs() == 2);
            checks.Add($"철거 재료 상자로 ({Show(before)} → {Show(after)})", refundOk);
            checks.Add("플레이어 포탈 그대로", player != null && pz.IsValid() && !PortalBuilder.Mine().Exists(z => z == pz));

            // 6) 나머지 철거 → 전체 개수: 넣은 것 − 놓은 것 + 돌려받은 것
            bool rb = PortalBuilder.RemovePair(WorkerPortal.Tag("시험B"), w.Home, out _);
            bool rc = PortalBuilder.RemovePair(WorkerPortal.Tag("시험C"), w.Home, out _);
            yield return new WaitForSeconds(1f);
            var end = Stock(w.Chest, cost);
            var put = WorkerPortal.Need(cost, 10);
            bool total = true;
            foreach (var kv in WorkerPortal.Need(cost, 1))
            {
                refundOne.TryGetValue(kv.Key, out int r1);
                int expect = put[kv.Key] - kv.Value * 6 + r1 * 6;
                if (end[kv.Key] != expect) { total = false; TestRunner.Log($"개수 어긋남 {kv.Key}: {end[kv.Key]} (기대 {expect})"); }
            }
            checks.Add($"전부 철거 (쌍 {PortalBuilder.Pairs()})", rb && rc && PortalBuilder.Pairs() == 0 && PortalBuilder.Mine().Count == 0);
            checks.Add($"전체 개수 보존 ({Show(end)})", total);

            // 7) 남의 와드 안에는 안 놓음
            Vector3 wardAt = GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, -30f));
            GameObject ward = Object.Instantiate(ZNetScene.instance.GetPrefab("guard_stone"), wardAt, rot);
            spawned.Add(ward);
            yield return null;
            Traverse.Create(ward.GetComponent<Piece>()).Field("m_creator").SetValue(987654321L);
            ward.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_enabled, true);
            float radius = ward.GetComponent<PrivateArea>().m_radius;
            bool found = PortalBuilder.FindSpot(wardAt, out Vector3 spot, out _, out string ws);
            float dist = Utils.DistanceXZ(spot, wardAt);
            checks.Add($"남의 와드 안 피함 ({ws}, 와드에서 {dist:0.0}m, 반경 {radius:0})", found ? dist >= radius + 3f : ws.Contains("남의 와드"));

            // 8) 일에 연결(R2 나): 먼 일터 두 번째 출발 → 거점 쪽 놓고 한 몫 들고 감 → 도착해 일터 쪽 놓음(짝) / 못 놓고 귀환하면 상자로 / 거점 쪽만 있으면 한 몫만
            yield return PortalJobPart(w, checks, cost, rot);
        }

        static IEnumerator PortalJobPart(TestKit.World w, GatherTest.Checks checks, List<PortalCost> cost, Quaternion rot)
        {
            ClearMine();
            var items = cost.ConvertAll(c => c.Item).ToArray();
            TestKit.ClearBaseStock(w, items);
            Inventory inv = w.Claude.GetInventory();
            foreach (var k in cost) inv.RemoveItem(CraftStep.SharedName(k.Item), inv.CountItems(CraftStep.SharedName(k.Item)));
            int Carried(string item) => inv.CountItems(CraftStep.SharedName(item));
            bool ChestEmpty() { foreach (var k in cost) if (GatherTest.Count(w.Chest, k.Item) != 0) return false; return true; }
            ZoneXY far = Senses.ZoneOf(w.Home + rot * new Vector3(0f, 0f, 400f));
            string tag = WorkerPortal.Tag(far.ToString());
            var ways = new HashSet<ToolKind>();

            // 재료 없음 → 안 놓음, 짐 그대로
            Brain.Get().Visit(far);
            var job = new GatherJob(w.Claude, "Wood", 1, ways);
            job.TestPortalDepart(far);
            checks.Add("재료 없으면 안 놓고 안 들고 감", PortalBuilder.Mine().Count == 0 && job.PortalCarryTag == null && Carried(cost[0].Item) == 0);

            // 두 번째 출발 → 거점 쪽 + 한 몫
            Fill(w.Chest, cost, 2);
            job = new GatherJob(w.Claude, "Wood", 1, ways);
            job.TestPortalDepart(far);
            yield return new WaitForSeconds(1f);
            checks.Add($"떠날 때 거점 쪽 놓음 ({PortalBuilder.MineWithTag(tag)}개)", PortalBuilder.MineWithTag(tag) == 1 && job.PortalCarryTag == tag);
            checks.Add("한 몫 들고 감·상자 비음", PortalBuilder.Carrying(inv) && ChestEmpty());
            job.TestPortalDepart(far);
            checks.Add("두 번 불러도 더 안 놓음", PortalBuilder.MineWithTag(tag) == 1);

            // 도착(시험은 35m 밖 자리로 옮겨 놓음) → 일터 쪽, 짝 이어짐, 방문 2
            w.Claude.TeleportTo(GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 35f)), rot, false);
            yield return new WaitForSeconds(1f);
            // 일터 쪽 놓기 실패(시험 스위치) → 들고 간 한 몫 그대로·포탈 늘지 않음·이어 짓기 유지(요청 10-23 09:09)
            PortalBuilder.TestFailAfter = 0;
            job.TestPortalArrive(far);
            PortalBuilder.TestFailAfter = -1;
            yield return new WaitForSeconds(1f);
            checks.Add($"일터 쪽 놓기 실패하면 한 몫 그대로·포탈 {PortalBuilder.MineWithTag(tag)}개(1)",
                PortalBuilder.Carrying(inv) && PortalBuilder.MineWithTag(tag) == 1 && job.PortalCarryTag == tag && ChestEmpty());
            job.TestPortalArrive(far);
            job.TestPortalArrive(far);
            yield return new WaitForSeconds(1f);
            var pair = PortalBuilder.Mine().FindAll(z => z.GetString(ZDOVars.s_tag) == tag);
            bool linked = pair.Count == 2 && pair[0].GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) == pair[1].m_uid;
            checks.Add($"도착해 일터 쪽 놓음·짝 이어짐 ({pair.Count}개)", linked && job.PortalCarryTag == null);
            checks.Add("들고 간 몫 다 씀", Carried(cost[0].Item) == 0);
            checks.Add($"먼 일터 방문 셈 한 일에 한 번 ({Brain.Get().VisitsOf(far)})", Brain.Get().VisitsOf(far) == 2);

            // 못 놓고 귀환 → 한 몫 상자로
            ClearMine();
            Fill(w.Chest, cost, 2);
            w.Claude.TeleportTo(w.Home + rot * new Vector3(2f, 0f, 2f), rot, false);
            yield return new WaitForSeconds(1f);
            job = new GatherJob(w.Claude, "Wood", 1, ways);
            job.TestPortalDepart(far);
            job.TestPortalHome();
            yield return new WaitForSeconds(1f);
            bool back = true;
            foreach (var kv in WorkerPortal.Need(cost, 1)) if (GatherTest.Count(w.Chest, kv.Key) != kv.Value || Carried(kv.Key) != 0) back = false;
            checks.Add("못 놓은 몫 귀환 때 상자로", back && job.PortalCarryTag == null && PortalBuilder.MineWithTag(tag) == 1);

            // 거점 쪽만 있음 → 다음 출발에 한 몫만 들고 감
            job = new GatherJob(w.Claude, "Wood", 1, ways);
            job.TestPortalDepart(far);
            checks.Add("거점 쪽만 있으면 한 몫만 들고 감", job.PortalCarryTag == tag && PortalBuilder.Carrying(inv) && ChestEmpty() && PortalBuilder.MineWithTag(tag) == 1);
            job.TestPortalHome();
            checks.Add("다시 상자로", !PortalBuilder.Carrying(inv) && !ChestEmpty());

            // 운반 중 저장 → 불러오기(작업 저장 문자열을 거쳐 새 일): 거점 쪽 1개 + 한 몫 → 이어 감, 도착하면 짝(요청 10-23 09:09)
            GatherJob Reload(GatherJob from)
            {
                var back = WorkState.LoadQueue(WorkState.SaveQueue(new[] { WorkSaves.FromJob(from) }));
                var j = new GatherJob(w.Claude, "Wood", 1, ways);
                j.LoadPortal(back.Count == 1 ? back[0].PortalTag : null);
                return j;
            }
            job = new GatherJob(w.Claude, "Wood", 1, ways);
            job.TestPortalDepart(far);
            job = Reload(job);
            checks.Add($"불러온 뒤 이어 감 (이름표 {job.PortalCarryTag})", job.PortalCarryTag == tag && PortalBuilder.Carrying(inv) && PortalBuilder.MineWithTag(tag) == 1);
            w.Claude.TeleportTo(GatherTest.Ground(w.Home + rot * new Vector3(0f, 0f, 35f)), rot, false);
            yield return new WaitForSeconds(1f);
            job.TestPortalArrive(far);
            yield return new WaitForSeconds(1f);
            checks.Add($"불러온 뒤 도착해 짝 완성 ({PortalBuilder.MineWithTag(tag)}개, 재료 합 그대로)",
                PortalBuilder.MineWithTag(tag) == 2 && job.PortalCarryTag == null && Carried(cost[0].Item) == 0 && ChestEmpty());

            // 짝이 이미 다 있는데 한 몫을 들고 불러옴 → 더 안 놓고 귀환 때 상자로
            Fill(w.Chest, cost, 1);
            w.Claude.TeleportTo(w.Home + rot * new Vector3(2f, 0f, 2f), rot, false);
            yield return new WaitForSeconds(1f);
            PortalBuilder.StartPair(tag, w.Home, inv, false, out _, out _);
            job = new GatherJob(w.Claude, "Wood", 1, ways);
            job.LoadPortal(tag);
            job.TestPortalArrive(far);
            yield return new WaitForSeconds(1f);
            checks.Add($"짝이 있으면 더 안 놓음 ({PortalBuilder.MineWithTag(tag)}개)", PortalBuilder.MineWithTag(tag) == 2 && PortalBuilder.Carrying(inv));
            job.TestPortalHome();
            checks.Add("그 한 몫은 귀환 때 상자로", !PortalBuilder.Carrying(inv) && !ChestEmpty() && job.PortalCarryTag == null);

            // 짐에 한 몫이 없으면 이름표를 잊음
            job = new GatherJob(w.Claude, "Wood", 1, ways);
            job.LoadPortal(tag);
            checks.Add("짐에 없으면 잊음", job.PortalCarryTag == null);
            ClearMine();
        }
    }
}
