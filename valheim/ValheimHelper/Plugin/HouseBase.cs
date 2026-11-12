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
            public string Why = "";
            public bool Ok => Chest1 != null && Chest2 != null;
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
            yield return new WaitForSeconds(5f); // 집 조각 1,144개가 다 생길 때까지
            var beds = new List<Bed>(); var chests = new List<Container>();
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (Utils.DistanceXZ(p.transform.position, s.Center) > HouseCopy.Radius) continue;
                s.Pieces++;
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
            Vector3 out_ = open.Value - s.Center; out_.y = 0f;
            pl.TeleportTo(open.Value + Vector3.up * 0.3f, Quaternion.LookRotation(out_.normalized), false); // 깃발은 플레이어 앞 4m = 집 반대쪽
            yield return new WaitForSeconds(2f);
            TestRunner.Log($"시험 기본 집 (자리 {s.Center.x:0},{s.Center.z:0}): 조각 {s.Pieces}, 1층 상자 {chests.Count}개 중 {Utils.GetPrefabName(s.Chest1.gameObject)} {s.Chest1.transform.position:F1}·{Utils.GetPrefabName(s.Chest2.gameObject)} {s.Chest2.transform.position:F1}, 깃발 쪽 빈 땅 {open.Value:F1}");
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
