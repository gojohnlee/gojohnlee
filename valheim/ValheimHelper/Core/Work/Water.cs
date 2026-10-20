using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 물 건너기 판단 (구상서 8장 환경 위험: 깊은 물은 피하고 바다는 건너지 않는다).
    /// [확정 2026-10-20] 좁은 강(깊은 물이 이어진 길이 24m 이하)은 헤엄쳐 건넌다. 늪·바다의 깊은 물(거머리·바다뱀)과
    /// 그보다 긴 물(호수·해협)은 건너지 않는다. 0.3.6 시험에서 NPC가 해협을 2m/s로 헤엄쳐 건넜다.
    /// </summary>
    public static class WaterRule
    {
        /// <summary>이보다 깊으면 헤엄친다(바닐라 수영 시작 깊이 근처).</summary>
        public const double DeepDepth = 1.2;
        public const double MaxSwim = 24;

        /// <param name="depthAt">(x, z)의 물 깊이(물 높이 - 땅 높이, 땅이 물보다 높으면 0 이하).</param>
        /// <param name="noSwimAt">(x, z)에서 헤엄치면 안 되는지(늪·바다).</param>
        /// <param name="step">재는 간격(m).</param>
        /// <returns>(x0, z0)에서 (x1, z1)까지 곧장 가도 되는지.</returns>
        public static bool CanCross(double x0, double z0, double x1, double z1,
            Func<double, double, double> depthAt, Func<double, double, bool> noSwimAt, double step)
        {
            double dx = x1 - x0, dz = z1 - z0;
            double len = Math.Sqrt(dx * dx + dz * dz);
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            double seg = len / n, run = 0;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double x = x0 + dx * t, z = z0 + dz * t;
                if (depthAt(x, z) <= DeepDepth) { run = 0; continue; }
                if (noSwimAt(x, z)) return false;
                if (i > 0) run += seg;
                if (run > MaxSwim) return false;
            }
            return true;
        }
    }
}
