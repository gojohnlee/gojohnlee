using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 시험 기본 집(사용자 11-12 "시험 서버 기본 집 = TheNewEra 복사본"): 원본 te4st41의 HouseCopy.Place() 자리에 있는 집을 시험 거점으로.
    /// 깃발은 집 밖 빈 땅(집 반대쪽 보기)에, 상자는 집 1층 상자 둘 — 시험이 상자를 놓거나 치우지 않는다(ClearOldChests 금지: 집 상자가 사라짐).
    /// 끝나면 Back으로 플레이어를 시작 자리로(다음 시험이 집 곁에 거점을 잡아 집을 부수지 않게).
    /// </summary>
    internal static class HouseBase
    {
        internal sealed class Site
        {
            public Vector3 Center, Start;
            public Container Chest1, Chest2;
            public int Pieces;
            public readonly Dictionary<ZDOID, string> Seen = new Dictionary<ZDOID, string>(); // 시작 때 조각(사라진 것 이름·자리 — 회차 457 1144→1017)
            public string Why = "";
            public bool Ok => Chest1 != null && Chest2 != null;
            public bool Done; // Leave가 켬 → Watch 그침
        }

        /// <summary>집 밖 빈 땅: 집에서 16~52m, 위에 건축 조각이 없고 2.5m 안에 건축 조각이 없는 곳. why = 못 쓴 까닭 셈.</summary>
        internal static Vector3? OpenGround(Vector3 center, out string why)
        {
            float wl = ZoneSystem.instance.m_waterLevel;
            int water = 0, roof = 0, near = 0;
            why = "";
            for (float d = 16f; d <= 52f; d += 3f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                    if (!ZoneSystem.instance.GetGroundHeight(p, out float h) || h < wl + 1f) { water++; continue; }
                    p.y = h;
                    if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out RaycastHit hit, 39.5f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Piece>() != null) { roof++; continue; }
                    bool clear = true;
                    foreach (var col in Physics.OverlapSphere(p + Vector3.up, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                        if (col.GetComponentInParent<Piece>() != null) { clear = false; break; }
                    if (clear) return p;
                    near++;
                }
            why = $"물·못 읽음 {water}·위 조각 {roof}·곁 조각 {near}";
            return null;
        }

        /// <summary>te4st41이면 집으로 가서 1층 상자 둘(가운데에서 가까운 차례)을 고르고 집 밖 빈 땅에 선다. 못 쓰면 s.Ok = false·s.Why(시작 자리로 돌아옴).</summary>
        internal static IEnumerator Go(Site s)
        {
            Player pl = Player.m_localPlayer;
            s.Start = pl.transform.position;
            if (ZNet.instance.GetWorldName() != "te4st41") { s.Why = $"월드 {ZNet.instance.GetWorldName()}"; yield break; }
            Vector3? at = HouseCopy.Place(out string why);
            if (at == null) { s.Why = why; yield break; }
            s.Center = at.Value;
            pl.TeleportTo(s.Center + new Vector3(HouseCopy.Radius + 8f, 2f, 0f), pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            int last = -1; // 집 조각이 다 생길 때까지: 2초 사이 수가 그대로면 끝, 최대 20초(회차 457 두 번째 시험이 5초 뒤 996개로 셈)
            for (int t = 0; t < 10; t++) { yield return new WaitForSeconds(2f); int n = Count(s.Center, out _); if (n == last) break; last = n; }
            var beds = new List<Bed>(); var chests = new List<Container>();
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (Utils.DistanceXZ(p.transform.position, s.Center) > HouseCopy.Radius) continue;
                s.Pieces++;
                Remember(s, p);
                if (p.GetComponent<Bed>() is Bed b) beds.Add(b);
                if (p.GetComponent<Container>() is Container c) chests.Add(c);
            }
            Vector3? open = OpenGround(s.Center, out string whyNot);
            if (s.Pieces < 500 || beds.Count == 0 || open == null) { s.Why = $"조각 {s.Pieces}·침대 {beds.Count}·빈 땅 {(open != null ? "있음" : whyNot)}"; yield return Back(s.Start); yield break; }
            float upper = beds[0].transform.position.y - 3f; // 침대가 2층 → 3m 아래부터 1층
            int full = chests.RemoveAll(c => c.transform.position.y < upper && c.GetInventory().GetEmptySlots() < 4); // 사용자 살림으로 찬 상자엔 시험 도구를 못 넣음(회차 456 첫 판 "장비 상자에 AxeFlint" FAIL)
            chests.RemoveAll(c => c.transform.position.y >= upper);
            TestRunner.Log($"1층 상자 중 빈칸 4 미만 {full}개 뺌");
            chests.Sort((a, b) => Utils.DistanceXZ(a.transform.position, s.Center).CompareTo(Utils.DistanceXZ(b.transform.position, s.Center)));
            if (chests.Count < 2) { s.Why = $"1층 상자 {chests.Count}개"; yield return Back(s.Start); yield break; }
            s.Chest1 = chests[0]; s.Chest2 = chests[1];
            // 사용자 살림이 든 상자면 일꾼이 그 나무를 꺼내 "가져왔다"가 됨(회차 457 woodtest 쓰러뜨린 곳 0) → 시험 복사본이라 두 상자를 비움
            int took = s.Chest1.GetInventory().NrOfItems() + s.Chest2.GetInventory().NrOfItems();
            s.Chest1.GetInventory().RemoveAll(); s.Chest2.GetInventory().RemoveAll();
            Count(s.Center, out int drops);
            TestRunner.Log($"두 상자 비움(물건 {took}칸), 집 안 땅 물건 {drops}");
            Vector3 out_ = open.Value - s.Center; out_.y = 0f;
            // 깃발은 플레이어 앞 4m = 집 반대쪽. Player.UpdateTeleport는 m_teleportTimer > 2초(FixedUpdate)가 지나야 자리를 옮기고,
            // TeleportTo는 쿨다운 2초 안이면 false — 2초만 기다리면 아직 옛 자리(집 동쪽 38m)라 깃발이 거기 놓였다(회차 459 wood·stone 거점 (284.6,430.9), 상자까지 31m).
            Quaternion face = Quaternion.LookRotation(out_.normalized);
            for (float w = 0f; w < 10f && !pl.TeleportTo(open.Value + Vector3.up * 0.3f, face, false); w += 0.5f) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(0.5f);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 30f);
            yield return new WaitForSeconds(1f);
            if (Utils.DistanceXZ(pl.transform.position, open.Value) > 2f)
            {
                s.Why = $"빈 땅으로 못 옮김(플레이어 {pl.transform.position:F1}, 빈 땅 {open.Value:F1})";
                s.Chest1 = s.Chest2 = null;
                yield return Back(s.Start);
                yield break;
            }
            TestRunner.Log($"시험 기본 집 (자리 {s.Center.x:0},{s.Center.z:0}): 조각 {s.Pieces}, 1층 상자 {chests.Count}개 중 {Utils.GetPrefabName(s.Chest1.gameObject)} {s.Chest1.transform.position:F1}·{Utils.GetPrefabName(s.Chest2.gameObject)} {s.Chest2.transform.position:F1}, 깃발 쪽 빈 땅 {open.Value:F1}");
            Plugin.Instance.StartCoroutine(Watch(s));
        }

        /// <summary>집 둘레(Radius) 생긴 건축 조각 수, drops = 땅 물건 수.</summary>
        internal static int Count(Vector3 center, out int drops)
        {
            int n = 0; drops = 0;
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (Utils.DistanceXZ(p.transform.position, center) <= HouseCopy.Radius) n++;
            foreach (var d in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
                if (Utils.DistanceXZ(d.transform.position, center) <= HouseCopy.Radius) drops++;
            return n;
        }

        /// <summary>조각 하나를 Seen에: 이름 (8m 칸)@높이 틈(맨 아래 콜라이더 - 땅 높이, 회차 458 일꾼 없이도 무너짐 — 땅에 안 닿은 바닥 조각인지).</summary>
        internal static void Remember(Site s, Piece p)
        {
            if (!(p.GetComponent<ZNetView>()?.GetZDO() is ZDO pz)) return;
            Vector3 d = p.transform.position - s.Center;
            float low = float.MaxValue;
            foreach (var c in p.GetComponentsInChildren<Collider>()) if (!c.isTrigger) low = Mathf.Min(low, c.bounds.min.y);
            string gap = low < float.MaxValue ? $" 틈{low - ZoneSystem.instance.GetGroundHeight(p.transform.position):0.00}" : "";
            s.Seen[pz.m_uid] = $"{Utils.GetPrefabName(p.gameObject)} ({Mathf.Round(d.x / 8f) * 8f:0},{Mathf.Round(d.z / 8f) * 8f:0})@{d.y:0}{gap}";
        }

        /// <summary>시험 동안 1초마다 사라진 조각 ZDO를 세어 늘면 시각·새로 사라진 것 3개 한 줄(회차 457: wood 시험 중 북쪽 152개 무너짐, 458: 일꾼 없이도).</summary>
        internal static IEnumerator Watch(Site s)
        {
            float t0 = Time.time; var gone = new HashSet<ZDOID>();
            while (!s.Done && Time.time - t0 < 900f)
            {
                yield return new WaitForSeconds(1f);
                var fresh = new List<string>();
                foreach (var kv in s.Seen) if (!gone.Contains(kv.Key) && ZDOMan.instance.GetZDO(kv.Key) == null) { gone.Add(kv.Key); if (fresh.Count < 3) fresh.Add(kv.Value); }
                if (fresh.Count == 0) continue;
                Player pl = Player.m_localPlayer; WorkerNpc w = WorkerNpc.All.Count > 0 ? WorkerNpc.All[0] : null;
                TestRunner.Log($"[{Time.time - t0:0}s] 집 조각 사라짐 → {gone.Count} (새로 {string.Join(", ", fresh.ToArray())}), 플레이어 {(pl ? pl.transform.position - s.Center : Vector3.zero):F0}, 일꾼 {(w ? w.transform.position - s.Center : Vector3.zero):F0}");
            }
        }

        /// <summary>시험 끝: 집이 그대로인지 한 줄(조각·땅 물건 — 회차 457 wood 뒤 1144→996·땅 물건 130) 남기고 시작 자리로.</summary>
        internal static IEnumerator Leave(Site s)
        {
            s.Done = true;
            int n = Count(s.Center, out int drops);
            var gone = new Dictionary<string, int>(); int kept = 0;
            foreach (var kv in s.Seen)
            {
                if (ZDOMan.instance.GetZDO(kv.Key) != null) { kept++; continue; }
                string key = kv.Value.Split(new[] { " 틈" }, System.StringSplitOptions.None)[0]; gone.TryGetValue(key, out int c); gone[key] = c + 1;
            }
            var top = new List<KeyValuePair<string, int>>(gone);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (top.Count > 12) top.RemoveRange(12, top.Count - 12);
            TestRunner.Log($"시험 기본 집 끝: 조각 {s.Pieces} → {n}(ZDO 남음 {kept}), 집 안 땅 물건 {drops}{(gone.Count > 0 ? ", 사라진 것(8m 칸 (x,z)@높이) " + string.Join(", ", top.ConvertAll(kv => $"{kv.Key}×{kv.Value}").ToArray()) : "")}");
            yield return Back(s.Start);
        }

        /// <summary>플레이어를 시작 자리로.</summary>
        internal static IEnumerator Back(Vector3 start)
        {
            Player pl = Player.m_localPlayer;
            pl.TeleportTo(start + Vector3.up * 0.5f, pl.transform.rotation, true);
            yield return TestKit.WaitFor(() => !pl.IsTeleporting(), 60f);
            yield return new WaitForSeconds(3f);
        }
    }
}
