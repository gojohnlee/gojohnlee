using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MercenaryCamp;

// Fire support and intel: artillery, engineer turrets, recon, psycasters and the squad leader.
public partial class MapComponent_Camp
{
    public float shellStock;
    public List<int> scoutedRaidTicks = new();
    private bool nextIncendiary;
    private readonly HashSet<int> reportedThreats = new();
    private int lastIntelLetter = -60000;
    private void ExposeSupport()
    {
        Scribe_Values.Look(ref shellStock, "shellStock"); Scribe_Values.Look(ref nextIncendiary, "nextIncendiary");
        Scribe_Collections.Look(ref scoutedRaidTicks, "scoutedRaidTicks", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) scoutedRaidTicks ??= new();
    }
    private void SupportUpkeep()
    {
        shellStock = Math.Min(ShellCap, shellStock + MortarCount * CampMod.Settings.shellsPerMortarDay * S.shellFactor * 600f / CampRules.Day);
        // Contract maintenance: turret barrels topped up, spent IEDs re-armed in peacetime, psycasters' focus restored.
        foreach (var fuel in facilities.Where(t => t.Spawned).Select(t => t.TryGetComp<CompRefuelable>()).Where(f => f != null && !f.IsFull))
            fuel.Refuel(fuel.TargetFuelLevel - fuel.Fuel);
        if (phase == CampPhase.Garrison)
        {
            CampLayout.Build(this); // Retries works skipped because someone stood on the cell; never rebuilds destroyed ones.
            if (S.ieds) CampLayout.Build(this, true, s => s.Def.StartsWith("TrapIED"));
        }
        // Repair-crew doctrine: works mend themselves even mid-battle, 4% of their hit points per upkeep.
        if (S.autoRepair)
            foreach (Thing t in facilities.Where(t => t.Spawned && t.def.useHitPoints && t.HitPoints < t.MaxHitPoints))
                t.HitPoints = Math.Min(t.MaxHitPoints, t.HitPoints + Math.Max(1, t.MaxHitPoints / 25));
        // Roles shift when a member dies, so a newly promoted psycaster is linked here rather than at the next gear check.
        foreach (Pawn p in Present.Where(p => RoleOf(p) == MercRole.Psycaster).ToList()) { SetupPsycaster(p); p.psychicEntropy?.OffsetPsyfocusDirectly(1f); }
        scoutedRaidTicks.RemoveAll(t => t < Now - CampRules.Day);
        if (threats.Count == 0) lastAimed.Clear();
    }

    // --- Squad leader (Force 4) and doctrine hediffs ---
    // Kept in step with the perks, the roster roles and, for post-only doctrines, where each merc is standing.
    public void ApplyPerkHediffs()
    {
        Pawn leader = members.FirstOrDefault()?.pawn;
        bool lead = S.leader && IsPresent(leader) && !leader.Downed;
        foreach (Pawn p in Present.ToList())
        {
            SetHediff(p, CampDefOf.GJL_MC_Leadership, lead);
            foreach (CampPerkDef perk in S.memberHediffs)
                SetHediff(p, perk.memberHediff, (perk.roles == null || perk.roles.Contains(RoleOf(p))) && (!perk.atPostOnly || AtPost(p.Position)));
        }
        if (S.squadHediffs.Count > 0 && company != null)
            foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(company).Where(p => !p.RaceProps.Humanlike).ToList())
                foreach (CampPerkDef perk in S.squadHediffs)
                    SetHediff(p, perk.squadHediff, perk.branch == (p.RaceProps.IsMechanoid ? PerkBranch.Mech : PerkBranch.Anomaly));
        if (S.markTargets && phase == CampPhase.Fighting) MarkTargets();
    }
    private static void SetHediff(Pawn p, HediffDef def, bool on)
    {
        Hediff h = p.health.hediffSet.GetFirstHediffOfDef(def);
        if (on && h == null) p.health.AddHediff(def);
        else if (!on && h != null) p.health.RemoveHediff(h);
    }
    // Target-marking doctrine: every enemy in the engagement zone takes extra damage until it has been out of it for a while.
    private void MarkTargets()
    {
        foreach (Pawn t in threats.Where(t => ActiveThreat(t) && InEngagementZone(t.Position)))
        {
            Hediff mark = t.health.hediffSet.GetFirstHediffOfDef(CampDefOf.GJL_MC_Marked) ?? t.health.AddHediff(CampDefOf.GJL_MC_Marked);
            if (mark.TryGetComp<HediffComp_Disappears>() is { } timer) timer.ticksToDisappear = timer.Props.disappearsAfterTicks.max;
        }
    }

    // --- Artillery ---
    public int MortarCount => S.mortars;
    public List<Building_TurretGun> Mortars => facilities.OfType<Building_TurretGun>().Where(m => m.Spawned && m.def == CampDefOf.Turret_Mortar).ToList();
    public float ShellCap => CampRules.ShellCap(MortarCount, CampMod.Settings.shellsPerMortarDay * S.shellFactor);
    public bool TakeShell() { if (shellStock < 1) return false; shellStock -= 1; return true; }
    public ThingDef NextShellDef()
    {
        // EMP doctrine: against a mostly mechanoid wave, every shell is EMP.
        if (S.empShells && threats.Count > 0 && threats.Count(t => t.RaceProps.IsMechanoid) * 2 >= threats.Count) return CampDefOf.Shell_EMP;
        if (!S.incendiary) return CampDefOf.Shell_HighExplosive;
        nextIncendiary = !nextIncendiary; // Incendiary perk: every other shell.
        return nextIncendiary ? CampDefOf.Shell_Incendiary : CampDefOf.Shell_HighExplosive;
    }
    // Splash safety: any non-hostile pawn near the impact point (colonists, mercs, beasts, mechs, visitors, prisoners) blocks it.
    public bool FriendlyNear(IntVec3 c, float radius = CampRules.FriendlyFireRadius, Pawn except = null) => GenRadial.RadialDistinctThingsAround(c, map, radius, true).OfType<Pawn>()
        .Any(p => p != except && !p.Dead && !p.HostileTo(Faction.OfPlayer) && (p.Faction != null || p.RaceProps.Humanlike));
    private int Cluster(Pawn enemy) => threats.Count(o => o.Position.InHorDistOf(enemy.Position, 5));
    public LocalTargetInfo MortarTarget(Building_TurretGun mortar)
    {
        if (phase != CampPhase.Fighting || mortar.AttackVerb is not { } verb) return LocalTargetInfo.Invalid;
        Pawn best = null; int bestCluster = -1;
        foreach (Pawn enemy in threats)
        {
            // The observation perk lets the guns reach the whole map; everything else stays inside the engagement zone.
            if (!ActiveThreat(enemy) || !S.artilleryAnywhere && !InEngagementZone(enemy.Position)) continue;
            float min = verb.verbProps.EffectiveMinRange(enemy, mortar), d2 = enemy.Position.DistanceToSquared(mortar.Position);
            if (d2 < min * min || d2 > verb.verbProps.range * verb.verbProps.range) continue;
            if (enemy.Position.GetRoof(map) is { isThickRoof: true } || FriendlyNear(enemy.Position)) continue;
            int cluster = Cluster(enemy);
            if (cluster > bestCluster) { best = enemy; bestCluster = cluster; }
        }
        return best != null ? new LocalTargetInfo(best) : LocalTargetInfo.Invalid;
    }
    public Job MortarJob(Pawn p)
    {
        int i = members.FindIndex(m => m.pawn == p);
        var mortars = Mortars;
        if (i < 0 || i >= Math.Min(MortarCount, mortars.Count)) return null;
        var mortar = mortars[i];
        if (mortar.gun.TryGetComp<CompChangeableProjectile>() is not { } shell || !shell.Loaded && shellStock < 1) return null;
        if (!MortarTarget(mortar).IsValid || !p.CanReserveAndReach(mortar, PathEndMode.InteractionCell, Danger.Deadly)) return null;
        Job man = JobMaker.MakeJob(JobDefOf.ManTurret, mortar); man.expiryInterval = 1200; man.checkOverrideOnExpire = true;
        return man;
    }
    // Engineer turrets keep vanilla's pick when it is in the engagement zone, else take the nearest enemy there they can hit.
    public LocalTargetInfo TurretTarget(Building_TurretGun turret, LocalTargetInfo vanilla)
    {
        if (vanilla.Thing is Pawn v && ActiveThreat(v) && InEngagementZone(v.Position)) return vanilla;
        if (turret.AttackVerb is not { } verb) return LocalTargetInfo.Invalid;
        Pawn best = threats.Where(t => ActiveThreat(t) && InEngagementZone(t.Position) && verb.CanHitTarget(t))
            .OrderBy(t => t.Position.DistanceToSquared(turret.Position)).FirstOrDefault();
        return best != null ? new LocalTargetInfo(best) : LocalTargetInfo.Invalid;
    }

    // --- Recon ---
    // Scouting report: one letter per new wave, with its make-up and where it is.
    private void ReportNewThreats()
    {
        if (threats.Count == 0) { reportedThreats.Clear(); return; }
        if (!S.raidAlert) return;
        var fresh = threats.Where(t => !reportedThreats.Contains(t.thingIDNumber)).ToList();
        if (fresh.Count == 0 || Now - lastIntelLetter < CampRules.Hour) return;
        foreach (var t in fresh) reportedThreats.Add(t.thingIDNumber);
        lastIntelLetter = Now;
        string mix = string.Join(", ", fresh.GroupBy(t => t.KindLabel).Select(g => $"{g.Key} {g.Count()}"));
        string where = fresh.Any(t => InEngagementZone(t.Position)) ? "이미 활동 구역 안에 있습니다." : "아직 활동 구역 밖입니다.";
        Find.LetterStack.ReceiveLetter("정찰 보고", $"적 {fresh.Count}명 확인: {mix}. {where}", LetterDefOf.ThreatSmall, new LookTargets(fresh));
    }
    public string ThreatMix => string.Join(", ", threats.GroupBy(t => t.KindLabel).Select(g => $"{g.Key} {g.Count()}"));
    // A delayed raid comes back through the same worker; let it through once instead of delaying it again.
    public bool ConsumeScoutedRaid(int now)
    {
        int i = scoutedRaidTicks.FindIndex(t => t <= now && t >= now - CampRules.Day);
        if (i < 0) return false;
        scoutedRaidTicks.RemoveAt(i);
        return true;
    }

    // --- Psycasters ---
    private void SetupPsycaster(Pawn p)
    {
        if (!ModsConfig.RoyaltyActive || S.psylink <= 0) return;
        int level = p.GetPsylinkLevel();
        if (level < S.psylink) p.ChangePsylinkLevel(S.psylink - level, false);
        foreach (AbilityDef a in S.psycasts) if (p.abilities.GetAbility(a) == null) p.abilities.GainAbility(a);
    }
    private static readonly string[] PsycastPriority = { "MassChaosSkip", "BerserkPulse", "VertigoPulse", "Stun", "Burden" };
    // A target already stunned or carrying the effect this cast would give is skipped, so one enemy is not re-burdened forever.
    private static bool AlreadyAffected(Ability ability, Pawn t) => t.stances.stunner.Stunned
        || ability.EffectComps.Any(e => e.props is CompProperties_AbilityGiveHediff g && g.hediffDef != null && t.health.hediffSet.HasHediff(g.hediffDef));
    // When each psycast was last aimed at each enemy. An effect that did not stick (a resisted cast, a target too dull to hold
    // it) would otherwise draw the same cast every time the heat allows, faster still with the heat-venting doctrine.
    // Not saved: a load costs one repeat at most. Cleared once the map is quiet.
    private readonly Dictionary<(AbilityDef, int), int> lastAimed = new();
    private bool RecentlyAimed(AbilityDef def, Pawn t) => lastAimed.TryGetValue((def, t.thingIDNumber), out int at)
        && Now - at < Math.Max(600, (int)(def.GetStatValueAbstract(StatDefOf.Ability_Duration) * 60));
    // Psycasts do nothing to a psychically deaf target.
    private static bool Reachable(AbilityDef def, Pawn t) => !def.IsPsycast || t.GetStatValue(StatDefOf.PsychicSensitivity) > .01f;
    // Strongest first. Area casts need a cluster and no friendly within the pulse radius (+1.5); self-centred ones count from
    // the caster. Single-target casts pick the nearest unaffected enemy.
    public Job PsycastJob(Pawn p)
    {
        if (RoleOf(p) != MercRole.Psycaster || p.abilities == null) return null;
        foreach (AbilityDef def in S.psycasts.OrderBy(a => Array.IndexOf(PsycastPriority, a.defName) is int i and >= 0 ? i : 99))
        {
            Ability ability = p.abilities.GetAbility(def);
            if (ability == null || ability.OnCooldown || !ability.CanCast || p.psychicEntropy != null && p.psychicEntropy.WouldOverflowEntropy(def.EntropyGain)) continue;
            float radius = def.EffectRadius, range = ability.verb.verbProps.range;
            var near = threats.Where(t => ActiveThreat(t) && InEngagementZone(t.Position)).ToList();
            if (range <= 0)
            {
                if (radius > 0 && near.Count(t => t.Position.InHorDistOf(p.Position, radius)) >= 2 && !FriendlyNear(p.Position, radius + 1.5f, p))
                    return ability.GetJob(p, p);
                continue;
            }
            var candidates = near.Where(t => t.Position.InHorDistOf(p.Position, range) && !RecentlyAimed(def, t) && ability.CanApplyOn(new LocalTargetInfo(t)) && ability.verb.CanHitTarget(t));
            Pawn target = radius > 0
                ? candidates.Where(t => near.Count(o => o.Position.InHorDistOf(t.Position, radius)) >= 2 && !FriendlyNear(t.Position, radius + 1.5f))
                    .OrderByDescending(t => near.Count(o => o.Position.InHorDistOf(t.Position, radius))).FirstOrDefault()
                : candidates.Where(t => Reachable(def, t) && !AlreadyAffected(ability, t)).OrderBy(t => t.Position.DistanceToSquared(p.Position)).FirstOrDefault();
            if (target != null) { lastAimed[(def, target.thingIDNumber)] = Now; return ability.GetJob(target, target); }
        }
        return null;
    }
}
