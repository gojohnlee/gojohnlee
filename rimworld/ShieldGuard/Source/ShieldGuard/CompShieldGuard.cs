using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShieldGuard;

public class CompProperties_ShieldGuard : CompProperties
{
    public CompProperties_ShieldGuard() { compClass = typeof(CompShieldGuard); }
}

public class ShieldMapComponent : MapComponent
{
    public readonly List<CompShieldGuard> Guards = new();
    public ShieldMapComponent(Map map) : base(map) { }
}

public class CompShieldGuard : ThingComp
{
    public const float Capacity = 120f, HalfWidth = 1.25f;
    private float energy = Capacity;
    private int lastHit = -10000;
    private int disabledUntil;
    private bool enabled = true;
    private bool locked;
    private Rot4 lockedFacing = Rot4.South;
    private Pawn Pawn => (Pawn)parent;
    public Vector3 Facing => (locked ? lockedFacing : Pawn.Rotation).FacingCell.ToVector3();
    public Vector3 ShieldCenter => Pawn.DrawPos + Facing * 0.55f;
    private bool Operational => Pawn.Spawned && !Pawn.Dead && !Pawn.Downed &&
        !Pawn.stances.stunner.Stunned && Pawn.Awake() && enabled;
    private bool Active => Operational && energy > 0 && Find.TickManager.TicksGame >= disabledUntil;

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        var guards = parent.Map.GetComponent<ShieldMapComponent>().Guards;
        if (!guards.Contains(this)) guards.Add(this);
    }
    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        map.GetComponent<ShieldMapComponent>().Guards.Remove(this);
    }
    public override void PostExposeData()
    {
        Scribe_Values.Look(ref energy, "shieldEnergy", Capacity);
        Scribe_Values.Look(ref lastHit, "shieldLastHit", -10000);
        Scribe_Values.Look(ref disabledUntil, "shieldDisabledUntil");
        Scribe_Values.Look(ref enabled, "shieldEnabled", true);
        Scribe_Values.Look(ref locked, "shieldFacingLocked");
        Scribe_Values.Look(ref lockedFacing, "shieldLockedFacing", Rot4.South);
    }
    public override void CompTickInterval(int delta) => Recharge(delta);
    private void Recharge(int delta)
    {
        int now = Find.TickManager.TicksGame;
        if (!Operational || now < disabledUntil) return;
        int eligible = Mathf.Clamp(now - lastHit - 300, 0, delta);
        energy = Mathf.Min(Capacity, energy + eligible * 0.2f);
    }
    public bool CanIntercept(Projectile shot)
    {
        if (!Active || shot.Launcher == parent) return false;
        // Friendly outgoing fire is allowed, including allied factions.
        return shot.Launcher == null || shot.Launcher.Faction == null ||
            parent.Faction == null || shot.Launcher.HostileTo(parent);
    }
    public void Intercept(Projectile shot)
    {
        lastHit = Find.TickManager.TicksGame;
        var def = shot.DamageDef;
        float overflow = ShieldMath.Absorb(ref energy, shot.DamageAmount);
        if (def == DamageDefOf.EMP)
        {
            energy = 0;
            disabledUntil = lastHit + 600;
        }
        // Copy all needed values before destroying the projectile. The intercepted shot no
        // longer reaches the rear line; overload is transferred to this mech with normal armor.
        var damage = new DamageInfo(def, overflow, shot.ArmorPenetration,
            -1f, shot.Launcher, null, shot.EquipmentDef);
        shot.Destroy(DestroyMode.Vanish);
        if (overflow > 0) parent.TakeDamage(damage);
    }
    public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
    {
        absorbed = false;
        // EMP explosions also disable the barrier. Ordinary explosions/melee bypass it.
        if (dinfo.Def != DamageDefOf.EMP) return;
        energy = 0;
        lastHit = Find.TickManager.TicksGame;
        disabledUntil = lastHit + 600;
    }
    public override string CompInspectStringExtra() =>
        "GJL_SG_Energy".Translate(energy.ToString("F0"), Capacity.ToString("F0")).ToString() +
        (Find.TickManager.TicksGame < disabledUntil ? "\n" + "GJL_SG_EMP".Translate().ToString() : "");

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        if (parent.Faction != Faction.OfPlayer) yield break;
        yield return new Command_Toggle {
            defaultLabel = "GJL_SG_Toggle".Translate(), defaultDesc = "GJL_SG_ToggleDesc".Translate(),
            icon = TexCommand.Draft, isActive = () => enabled, toggleAction = () => enabled = !enabled
        };
        yield return new Command_Toggle {
            defaultLabel = "GJL_SG_Lock".Translate(), defaultDesc = "GJL_SG_LockDesc".Translate(),
            icon = TexCommand.Draft, isActive = () => locked,
            toggleAction = () => { locked = !locked; if (locked) lockedFacing = Pawn.Rotation; }
        };
        yield return new Command_Action {
            defaultLabel = "GJL_SG_Rotate".Translate(), defaultDesc = "GJL_SG_RotateDesc".Translate(),
            icon = TexCommand.Draft, action = () => {
                lockedFacing = new Rot4(((locked ? lockedFacing : Pawn.Rotation).AsInt + 1) % 4);
                locked = true;
            }
        };
    }

    // Native mesh artwork: large dark slab, metal rim, viewport and blue charge strip.
    // Materials are initialized lazily on the rendering thread, never during XML loading.
    private static Material rim, plate, window, glow, empty;
    public override void PostDraw()
    {
        if (!Pawn.Spawned || Pawn.Dead) return;
        rim ??= SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.35f, 0.4f, 0.42f));
        plate ??= SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.11f, 0.15f, 0.16f));
        window ??= SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.02f, 0.04f, 0.05f));
        glow ??= SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.15f, 0.85f, 1f));
        empty ??= SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.4f, 0.12f, 0.08f));
        Vector3 c = ShieldCenter;
        c.y = Pawn.DrawPos.y + 0.05f;
        float angle = (locked ? lockedFacing : Pawn.Rotation).AsAngle;
        Quaternion q = Quaternion.AngleAxis(angle, Vector3.up);
        DrawPanel(c, q, new Vector3(1.0f, 1, 1.15f), rim);
        c.y += 0.003f;
        DrawPanel(c, q, new Vector3(0.88f, 1, 1.03f), plate);
        Vector3 slit = c + q * new Vector3(0, 0.003f, 0.3f);
        DrawPanel(slit, q, new Vector3(0.48f, 1, 0.14f), window);
        Vector3 strip = c + q * new Vector3(0, 0.006f, -0.3f);
        DrawPanel(strip, q, new Vector3(0.65f, 1, 0.065f), Active ? glow : empty);
        if (Active && Find.Selector.IsSelected(parent))
        {
            Vector3 line = c + Facing * 0.08f; line.y += 0.01f;
            DrawPanel(line, q, new Vector3(HalfWidth * 2, 1, 0.035f), glow);
        }
    }
    private static void DrawPanel(Vector3 at, Quaternion q, Vector3 size, Material material) =>
        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, q, size), material, 0);
}
