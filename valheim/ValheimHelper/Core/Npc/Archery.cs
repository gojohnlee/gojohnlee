using System;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 활 사냥(⑧, 구상서 6-2: 활은 사냥에만) 조준 계산. 게임 값 대신 좌표 숫자를 쓴다(y가 위).
    /// 화살은 곧게 날다 중력으로 떨어진다(바닐라 Projectile: m_vel += 아래 × m_gravity × dt) → 움직이는 짐승은 갈 곳을, 먼 곳은 조금 위를 겨눈다.
    /// </summary>
    public static class Archery
    {
        /// <summary>이 거리 안이면 쏜다. 넘으면 다가간다(Approach까지).</summary>
        public const float ShootRange = 30f;
        public const float Approach = 22f;
        /// <summary>들고 다닐 화살 수.</summary>
        public const int ArrowsToCarry = 40;
        /// <summary>이보다 오래 날아가야 하면 겨눌 수 없다고 본다(너무 멀거나 너무 빠름).</summary>
        public const float MaxFlight = 2f;

        /// <summary>
        /// 쏘는 곳에서 본 과녁 상대 위치(dx,dy,dz)·과녁 속도(vx,vy,vz)·화살 속력·중력으로 겨눌 점(상대 위치)을 돌려준다.
        /// 날아가는 시간을 몇 번 고쳐 가며 맞춘다. 못 겨누면(속력 0, 너무 오래 날아감) false.
        /// </summary>
        public static bool Aim(float dx, float dy, float dz, float vx, float vy, float vz, float speed, float gravity,
            out float ax, out float ay, out float az)
        {
            ax = dx; ay = dy; az = dz;
            if (speed <= 0f) return false;
            float t = Len(dx, dy, dz) / speed;
            for (int i = 0; i < 4; i++)
            {
                if (t > MaxFlight) return false;
                ax = dx + vx * t; ay = dy + vy * t; az = dz + vz * t;
                t = Len(ax, ay, az) / speed;
            }
            if (t > MaxFlight) return false;
            ay += 0.5f * Math.Max(0f, gravity) * t * t;
            return true;
        }

        static float Len(float x, float y, float z) => (float)Math.Sqrt(x * x + y * y + z * z);
    }
}
