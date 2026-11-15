using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_housefind(길 기억 ④ "다른 구조 집 두 판", decisions "시험 집" 11-15): 지금 월드(사용자 월드 복사본)의 침대·문·작업대·상자 ZDO를 40m로 묶어
    /// 기지마다 조각 수·문·침대·상자·작업대·바닥 층 수·높이 폭을 diag에 적는다(조각 많은 순 10개). 고르는 것은 사람이 diag를 보고 decisions에.
    /// 서버는 월드 ZDO를 다 들고 있어 어디든 훑을 수 있다. 월드는 고치지 않는다(읽기만).
    /// </summary>
    static class HouseFind
    {
        public static void Register() => TestRunner.Register("vh_housefind", "월드의 기지(침대·문·작업대 무리)를 찾아 구조 요약", () => TestKit.Flat(Run()));

        static readonly string[] Anchors = { "bed", "piece_bed02", "wood_door", "iron_grate", "wood_gate", "darkwood_gate", "piece_workbench", "piece_chest_wood", "piece_chest", "forge", "hearth" };
        const float Join = 40f, Radius = 30f;

        class Base { public Vector3 Pos; public int N; }

        static IEnumerator Run()
        {
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("판정: FAIL (0/1) 서버로 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            yield return null;
            var bases = new List<Base>();
            foreach (string a in Anchors)
                foreach (ZDO z in ZdoScan.All(a))
                {
                    if (z.GetLong(ZDOVars.s_creator, 0L) == 0L) continue;   // 폐허·버려진 마을(월드가 만든 조각, 만든이 0)은 뺌 — 회차 700: 고른 "탑"이 폐허였음
                    Vector3 p = z.GetPosition();
                    Base b = bases.FirstOrDefault(x => Utils.DistanceXZ(x.Pos, p) < Join);
                    if (b == null) bases.Add(new Base { Pos = p, N = 1 });
                    else { b.Pos = (b.Pos * b.N + p) / (b.N + 1); b.N++; }
                }
            TestRunner.Log($"월드 {ZNet.instance.GetWorldName()}: 기지 후보 {bases.Count}곳(침대·문·작업대·상자 {bases.Sum(b => b.N)}개)");
            var rows = new List<KeyValuePair<int, string>>();
            foreach (Base b in bases)
            {
                var near = new List<ZDO>(); var distant = new List<ZDO>();
                ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(b.Pos), new SimulationDistance(1, 0, true), near, distant);
                near.AddRange(distant);
                int pieces = 0, doors = 0, beds = 0, chests = 0, stations = 0, stairs = 0;
                var floors = new HashSet<int>();
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (ZDO z in near)
                {
                    if (z == null || !z.Persistent || z.GetLong(ZDOVars.s_creator, 0L) == 0L || Utils.DistanceXZ(z.GetPosition(), b.Pos) > Radius) continue;
                    GameObject pf = ZNetScene.instance.GetPrefab(z.GetPrefab());
                    if (pf == null || pf.GetComponent<Piece>() == null || pf.GetComponent<Character>() != null) continue;
                    pieces++;
                    float y = z.GetPosition().y;
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    string n = pf.name;
                    if (pf.GetComponent<Door>() != null) doors++;
                    if (pf.GetComponent<Bed>() != null) beds++;
                    if (pf.GetComponent<Container>() != null) chests++;
                    if (pf.GetComponent<CraftingStation>() != null) stations++;
                    if (n.Contains("stair") || n.Contains("ladder")) stairs++;
                    if (n.Contains("floor")) floors.Add(Mathf.RoundToInt(y / 2f));
                }
                if (pieces == 0) continue;
                rows.Add(new KeyValuePair<int, string>(pieces, $"  기지 ({b.Pos.x:F0},{b.Pos.z:F0}) 높이 {minY:F0}~{maxY:F0}: 조각 {pieces}·문 {doors}·침대 {beds}·상자 {chests}·작업대 {stations}·계단 {stairs}·바닥 층 {floors.Count}"));
            }
            foreach (var r in rows.OrderByDescending(r => r.Key).Take(14)) TestRunner.Log(r.Value);
            TestRunner.Log($"판정: {(rows.Count > 0 ? "PASS" : "FAIL")} ({(rows.Count > 0 ? 1 : 0)}/1) 조각 있는 기지 {rows.Count}곳");
            TestRunner.Finish();
        }
    }
}
