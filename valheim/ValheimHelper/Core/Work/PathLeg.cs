using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 먼 목표 길 토막(회차 657 vh_finaltest 사냥 FAIL): 바닐라 길찾기가 목표까지 길을 못 찾으면 곧장 걷지 않고
    /// 목표 쪽 Length m 앞 토막 점까지 바닐라 길로 간다.
    /// 까닭(1.0.21 디컴파일): Pathfinding.GetPath는 목표 둘레 navmesh 타일이 안 구워졌으면(처음 고른 곳 — 타일은 0.1초마다 하나씩 비동기로 굽고,
    /// 30초 안 찔리면 지움, 구역이 안 불러와진 곳은 못 구움) SnapToNavMesh(to)가 실패해 false, BaseAI.FindPath는 같은 목표(1m) 실패를 5초 들고 있고
    /// MoveTo는 그동안 StopMoving·true → 일꾼이 곧장 걸어 집 가구·문·바위·나무에 박혔다(빠른 막힘 1초 → 구역 뺌 4곳 → cantReach).
    /// 토막 점은 일꾼 둘레 3×3 타일(32m) 안이라 이미 구워진 길이 있다. 토막 점은 Next m 안에 다가갈 때까지 그대로(남은 길 = 토막까지 길 + 토막~목표가 줄어듦),
    /// Retry초마다 목표까지 바로 길을 다시 본다(그새 타일이 구워졌으면 바로 길).
    /// </summary>
    public sealed class PathLeg
    {
        /// <summary>토막 길이(m): 바닐라 GetPath가 출발점 둘레로 찌르는 3×3 타일(타일 32m) 안.</summary>
        public const double Length = 24.0;
        /// <summary>토막 점에 이만큼(m) 다가가면 다음 토막.</summary>
        public const double Next = 8.0;
        /// <summary>토막으로 가는 동안 이만큼(초)마다 목표까지 바로 길을 다시 본다(BaseAI.FindPath 실패 들고 있는 시간과 같음).</summary>
        public const double Retry = 5.0;
        /// <summary>목표가 이보다 가까우면 토막을 안 쓴다(목표 둘레 타일이 일꾼 둘레 타일 안).</summary>
        public const double MinDistance = Length + Next;

        double m_until;

        /// <summary>토막으로 가는 중.</summary>
        public bool Active { get; private set; }
        /// <summary>지금 토막 점(수평).</summary>
        public double X { get; private set; }
        public double Z { get; private set; }
        /// <summary>토막 점을 고른 횟수(바뀌었는지 부른 쪽이 견줌).</summary>
        public int Picks { get; private set; }

        public void Reset() => Active = false;

        /// <summary>목표까지 바로 길을 못 찾았다. true = 토막으로 간다(토막 점 새로), false = 목표가 가까워 안 씀.</summary>
        public bool DirectFailed(double now, double px, double pz, double gx, double gz)
        {
            if (Dist(px, pz, gx, gz) <= MinDistance) { Active = false; return false; }
            Active = true;
            m_until = now + Retry;
            Pick(px, pz, gx, gz);
            return true;
        }

        /// <summary>매 틱(토막 중). false = 토막 끝(다시 바로 길을 볼 때·목표가 가까워짐), true = 토막 점(X,Z)으로 계속 —
        /// 다가갔거나, 이미 지나쳤거나(목표에 토막 점보다 가까움), 멀리 옮겨졌으면(순간이동, Length + 2×Next 밖) 다음 토막 점을 고름.</summary>
        public bool Keep(double now, double px, double pz, double gx, double gz)
        {
            if (!Active) return false;
            double toGoal = Dist(px, pz, gx, gz);
            if (now >= m_until || toGoal <= MinDistance) { Active = false; return false; }
            double toLeg = Dist(px, pz, X, Z);
            if (toLeg < Next || toLeg > Length + 2 * Next || toGoal < Dist(X, Z, gx, gz)) Pick(px, pz, gx, gz);
            return true;
        }

        void Pick(double px, double pz, double gx, double gz)
        {
            double d = Dist(px, pz, gx, gz);
            X = px + (gx - px) / d * Length;
            Z = pz + (gz - pz) / d * Length;
            Picks++;
        }

        static double Dist(double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
