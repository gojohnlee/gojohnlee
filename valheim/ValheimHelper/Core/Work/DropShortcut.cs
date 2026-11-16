using System;
using System.Collections.Generic;
using P = ValheimHelper.Core.Work.PathMemory.P;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 뛰어내리기 지름길(요청 11-11 01:16): 높은 층 → 낮은 층으로 갈 때 계단보다 빠르면 뚫린 바닥 끝("내려가기 점")으로 걸어가 뛰어내린다. 올라갈 땐 늘 계단.
    /// 내려가기 점 = 바닥 끝 Edge(벽 없음)와 그 아래 착지 Land(둘레 빔). 높이 MinDrop~MaxDrop(사람처럼 보이게 8m 이하 — 일꾼은 낙하 피해 없음).
    /// 게임 무관 — 점 찾기(레이캐스트)·걷기는 Plugin, 여기는 점이 쓸 만한지·계단보다 빠른지·점 모음(상한·건축 바뀐 근처 지움)만.
    /// </summary>
    public sealed class DropShortcut
    {
        public const float MaxDrop = 8f;
        /// <summary>이보다 낮으면 턱 — 그냥 걸어 내려감(지름길로 안 셈).</summary>
        public const float MinDrop = 1.5f;
        /// <summary>낙하 시간 계산 중력(m/s²). 게임 값은 확인 안 함 — 지구 값으로 낮게 잡아 낙하를 길게 셈(지름길을 덜 고르는 쪽).</summary>
        public const float Gravity = 9.81f;
        /// <summary>끝에서 내딛고 착지 뒤 다시 뛰기까지 더하는 시간(초).</summary>
        public const float Pause = 0.5f;
        /// <summary>계단보다 이만큼(초) 넘게 빨라야 뛰어내림 — 비슷하면 계단(자연스러움).</summary>
        public const float MinSave = 1f;
        /// <summary>같은 층으로 보는 높이 차(m) — 출발과 끝, 착지와 목표.</summary>
        public const float FloorTol = 1.5f;
        public const int Max = 2000;

        public readonly struct Spot
        {
            public readonly P Edge, Land;
            public Spot(P edge, P land) { Edge = edge; Land = land; }
            public float Height => Edge.Y - Land.Y;
        }

        /// <summary>찾은 바닥 끝이 내려가기 점이 되는지: 높이 MinDrop~MaxDrop, 끝 바로 밖 벽 없음, 착지 둘레 빔(기계·상자 위·틈이면 낌).</summary>
        public static bool Ok(float edgeY, float landY, bool wallAhead, bool landClear)
        {
            float h = edgeY - landY;
            return !wallAhead && landClear && h >= MinDrop && h <= MaxDrop;
        }

        public static float FallTime(float height) => height <= 0f ? 0f : (float)Math.Sqrt(2f * height / Gravity);

        static float Xz(P a, P b) { float dx = a.X - b.X, dz = a.Z - b.Z; return (float)Math.Sqrt(dx * dx + dz * dz); }

        /// <summary>이 점으로 from → goal 걸리는 시간(초): 끝까지 걷기 + 낙하 + 내딛기·착지 + 착지→목표 걷기. 층이 안 맞으면 무한.</summary>
        public static float TimeVia(Spot s, P from, P goal, float speed)
        {
            if (speed <= 0f || Math.Abs(s.Edge.Y - from.Y) > FloorTol || Math.Abs(s.Land.Y - goal.Y) > FloorTol) return float.MaxValue;
            return (Xz(from, s.Edge) + Xz(s.Land, goal)) / speed + FallTime(s.Height) + Pause;
        }

        /// <summary>내려가는 길이면(goal이 MinDrop 넘게 낮음) 계단 길(stairLen m, 게임 길 m_path 합)보다 MinSave 넘게 빠른 가장 빠른 점. 없으면 false.</summary>
        public static bool Pick(IEnumerable<Spot> spots, P from, P goal, float stairLen, float speed, out Spot best)
        {
            best = default;
            if (speed <= 0f || from.Y - goal.Y < MinDrop) return false;
            float limit = stairLen / speed - MinSave, bestT = float.MaxValue;
            bool found = false;
            foreach (Spot s in spots)
            {
                float t = TimeVia(s, from, goal, speed);
                if (t < limit && t < bestT) { bestT = t; best = s; found = true; }
            }
            return found;
        }

        readonly List<Spot> m_spots = new List<Spot>();
        readonly int m_max;

        public DropShortcut(int max = Max) { m_max = Math.Max(1, max); }

        public int Count => m_spots.Count;
        public IReadOnlyList<Spot> Spots => m_spots;

        /// <summary>점 넣기 — 끝이 1m 안인 같은 층 점이 있으면 안 넣음. 상한을 넘으면 가장 먼저 넣은 것부터 지움.</summary>
        public bool Add(Spot s)
        {
            foreach (Spot o in m_spots)
                if (Xz(o.Edge, s.Edge) < 1f && Math.Abs(o.Edge.Y - s.Edge.Y) < FloorTol) return false;
            m_spots.Add(s);
            if (m_spots.Count > m_max) m_spots.RemoveAt(0);
            return true;
        }

        /// <summary>건축이 바뀐 자리 radius(m) 안에 끝이나 착지가 있는 점을 지움(PathMemory.ForgetNear와 같은 쓰임). 지운 수.</summary>
        public int ForgetNear(P at, float radius)
        {
            return m_spots.RemoveAll(s => Near(s.Edge, at, radius) || Near(s.Land, at, radius));
        }

        static bool Near(P a, P b, float r) => Xz(a, b) <= r && Math.Abs(a.Y - b.Y) <= r + PathMemory.Floor;
    }
}
