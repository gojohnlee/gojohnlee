using System;

namespace ShieldGuard;

// Kept independent of Unity so interception and overflow can be tested without the game.
public static class ShieldMath
{
    public static bool Crosses(float ax, float az, float bx, float bz,
        float cx, float cz, float nx, float nz, float halfWidth, out float t)
    {
        t = 0;
        float start = (ax - cx) * nx + (az - cz) * nz;
        float end = (bx - cx) * nx + (bz - cz) * nz;
        if (start < 0 || end > 0 || start - end <= 0.00001f) return false;
        t = start / (start - end);
        float x = ax + (bx - ax) * t - cx;
        float z = az + (bz - az) * t - cz;
        return Math.Abs(x * nz - z * nx) <= halfWidth;
    }

    public static float Absorb(ref float energy, float damage)
    {
        damage = Math.Max(0, damage);
        float absorbed = Math.Min(Math.Max(0, energy), damage);
        energy = Math.Max(0, energy - absorbed);
        return damage - absorbed;
    }
}
