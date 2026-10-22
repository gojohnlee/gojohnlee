using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 지하묘지 안(y&gt;3000) 걷기 길. 바닐라 길 지도는 안에서 1m 턱(흙바닥)·진흙 더미·벽으로 작은 섬들로 쪼개져 길이 늘 "부분"이고,
    /// 끝에서 목표로 곧장 걸으면 벽에 막혔다(2026-10-23 vh_crypttest 진단: 도착 자리 섬이 2칸, 둘레 칸은 길 지도가 있어도 안 이어짐).
    /// 1m 칸마다 물리로 바닥·몸 자리·벽을 재서 Core StepGrid로 길을 찾는다(걸어 넘기 0.4m, 뛰어 오르기 1m, 뛰어내리기 3m).
    /// </summary>
    public static class InteriorNav
    {
        public const float Cell = 1f;
        const int MaxExpand = 6000;
        /// <summary>바닥을 찾는 범위: 지금 높이에서 위로 이만큼(묘지 지붕 위 면을 바닥으로 잡지 않게 낮게), 아래로 DownRange.</summary>
        const float UpRange = 2.6f, DownRange = 9f;
        const float BodyRadius = 0.3f;

        static readonly RaycastHit[] s_hits = new RaycastHit[16];
        static int s_mask;
        static int Mask => s_mask != 0 ? s_mask : (s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"));

        /// <summary>
        /// from에서 to의 dist m 안(XZ) 칸까지의 길(세계 좌표, 바닥 높이, 첫 점 = from). 못 찾으면 null과 까닭.
        /// 같은 높이로 곧게 트인 구간은 끝점만 남긴다(턱 앞뒤 점은 남음 — 걷는 쪽이 높이로 뛸지 정한다).
        /// </summary>
        public static List<Vector3> Plan(Vector3 from, Vector3 to, float dist, out string why)
        {
            float x0 = from.x, z0 = from.z, yRef = from.y;
            float reach = Mathf.Max(dist + 0.3f, 0.8f);
            var aim = new StepGrid.Cell(Mathf.RoundToInt((to.x - x0) / Cell), Mathf.RoundToInt((to.z - z0) / Cell));
            Vector3 At(StepGrid.Cell c, double y) => new Vector3(x0 + c.X * Cell, (float)y, z0 + c.Z * Cell);
            var floors = new Dictionary<StepGrid.Cell, double?>();
            double? Floor(int x, int z)
            {
                var c = new StepGrid.Cell(x, z);
                if (!floors.TryGetValue(c, out double? h)) floors[c] = h = Probe(x0 + x * Cell, z0 + z * Cell, yRef);
                return h;
            }
            bool Open(StepGrid.Cell a, StepGrid.Cell b)
            {
                double ha = a.X == 0 && a.Z == 0 ? yRef : Floor(a.X, a.Z) ?? yRef;
                double hb = b.X == 0 && b.Z == 0 ? yRef : Floor(b.X, b.Z) ?? yRef;
                float top = (float)System.Math.Max(ha, hb);
                Vector3 pa = At(a, top), pb = At(b, top);
                return !Physics.Linecast(pa + Vector3.up * 0.9f, pb + Vector3.up * 0.9f, Mask, QueryTriggerInteraction.Ignore)
                    && !Physics.Linecast(pa + Vector3.up * 1.6f, pb + Vector3.up * 1.6f, Mask, QueryTriggerInteraction.Ignore);
            }
            bool Goal(StepGrid.Cell c)
            {
                Vector3 p = At(c, 0);
                return Utils.DistanceXZ(p, to) <= reach;
            }
            var cells = StepGrid.Find(new StepGrid.Cell(0, 0), yRef, Goal, aim, Floor, Open, MaxExpand);
            if (cells == null) { why = $"길 없음(칸 {floors.Count}개 봄)"; return null; }

            var pts = new List<Vector3>(cells.Count);
            foreach (var c in cells) pts.Add(At(c, c.X == 0 && c.Z == 0 ? yRef : Floor(c.X, c.Z).Value));
            var path = new List<Vector3> { pts[0] };
            int i = 0;
            while (i < pts.Count - 1)
            {
                int j = i + 1;
                for (int k = pts.Count - 1; k > i + 1; k--)
                    if (Flat(pts, i, k) && Clear(pts[i], pts[k])) { j = k; break; }
                path.Add(pts[j]);
                i = j;
            }
            why = $"칸 {cells.Count}개·점 {path.Count}개(칸 {floors.Count}개 봄)";
            return path;
        }

        /// <summary>i~k 칸이 모두 i와 같은 높이(걸어 넘는 턱 안)인가.</summary>
        static bool Flat(List<Vector3> pts, int i, int k)
        {
            for (int n = i + 1; n <= k; n++) if (Mathf.Abs(pts[n].y - pts[i].y) > (float)StepGrid.StepUp) return false;
            return true;
        }

        /// <summary>a에서 b까지 몸(반지름 BodyRadius)이 무릎~가슴 높이에서 걸리지 않는가.</summary>
        static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            d /= len;
            return !Physics.SphereCast(a + Vector3.up * 0.9f, BodyRadius, d, out _, len, Mask, QueryTriggerInteraction.Ignore)
                && !Physics.Linecast(a + Vector3.up * 0.5f, b + Vector3.up * 0.5f, Mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// (x,z) 칸의 설 수 있는 바닥 높이: yRef+UpRange에서 아래로 쏴 처음 만난 완만한 면(법선 y ≥ 0.55). 그 위 몸 자리(0.35~2m)가 비어야 한다.
        /// 진흙 더미·광맥·움직이는 물체 위는 바닥으로 안 친다(장애물). 벽·기둥 속에서 쏘면 그 아래 바닥은 몸 자리가 막혀 null.
        /// </summary>
        static double? Probe(float x, float z, float yRef)
        {
            var origin = new Vector3(x, yRef + UpRange, z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, UpRange + DownRange, Mask, QueryTriggerInteraction.Ignore);
            for (int a = 1; a < n; a++)
            {
                RaycastHit h = s_hits[a];
                int b = a - 1;
                while (b >= 0 && s_hits[b].distance > h.distance) { s_hits[b + 1] = s_hits[b]; b--; }
                s_hits[b + 1] = h;
            }
            for (int a = 0; a < n; a++)
            {
                RaycastHit h = s_hits[a];
                if (h.normal.y < 0.55f) continue; // 벽면·가파른 면은 지나 아래를 본다
                Collider c = h.collider;
                if (c.attachedRigidbody != null || c.GetComponentInParent<Destructible>() != null
                    || c.GetComponentInParent<MineRock5>() != null || c.GetComponentInParent<MineRock>() != null) return null;
                Vector3 p = h.point;
                if (Physics.CheckCapsule(p + Vector3.up * (0.35f + BodyRadius), p + Vector3.up * (2f - BodyRadius), BodyRadius, Mask, QueryTriggerInteraction.Ignore))
                    return null;
                return p.y;
            }
            return null;
        }
    }
}
