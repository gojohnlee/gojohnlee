using HarmonyLib;
using Verse;
using UnityEngine;

namespace ShieldGuard;

public class ShieldGuardMod : Mod
{
    public ShieldGuardMod(ModContentPack content) : base(content)
    {
        new Harmony("gojohnlee.shieldguard").PatchAll();
    }
}

// Intercept during vanilla flight collision checks, including the final flight tick. Only ground projectiles
// using the vanilla flight system are supported; overhead shells deliberately pass through.
[HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
public static class ProjectileShieldPatch
{
    public static bool Prefix(Projectile __instance, Vector3 lastExactPos,
        Vector3 newExactPos, ref bool __result)
    {
        Projectile shot = __instance;
        if (!shot.Spawned || shot.Destroyed || shot.def.projectile.flyOverhead)
            return true;
        var guards = shot.Map.GetComponent<ShieldMapComponent>().Guards;
        if (guards.Count == 0) return true;
        Vector3 start = lastExactPos;
        Vector3 end = newExactPos;
        CompShieldGuard nearest = null;
        float first = float.MaxValue;
        foreach (var shield in guards)
        {
            if (!shield.CanIntercept(shot)) continue;
            Vector3 c = shield.ShieldCenter;
            Vector3 n = shield.Facing;
            if (!ShieldMath.Crosses(start.x, start.z, end.x, end.z, c.x, c.z,
                    n.x, n.z, CompShieldGuard.HalfWidth, out float t) || t >= first) continue;
            // Do not absorb through a wall earlier in the same movement segment.
            IntVec3 hitCell = Vector3.Lerp(start, end, t).ToIntVec3();
            if (!hitCell.InBounds(shot.Map) || !GenSight.LineOfSight(start.ToIntVec3(), hitCell, shot.Map, true)) continue;
            first = t;
            nearest = shield;
        }
        if (nearest == null) return true;
        nearest.Intercept(shot);
        __result = true;
        return false;
    }
}
