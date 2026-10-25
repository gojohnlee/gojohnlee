using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MercenaryCamp;

public class ThinkNode_ConditionalMercenary : ThinkNode_Conditional
{
    // Any company pawn on the camp map is ours to steer, so a surplus returnee leaves instead of idling forever.
    protected override bool Satisfied(Pawn pawn) => pawn.Map?.GetComponent<MapComponent_Camp>() is { company: not null } c && pawn.Faction == c.company && !pawn.IsPrisoner;
}

// Pen beasts (inserted into the fleshbeast tree): hunt every enemy on the map, then walk back to the pen when recalled.
public class ThinkNode_ConditionalCampBeast : ThinkNode_Conditional
{
    protected override bool Satisfied(Pawn pawn) => pawn.Map?.GetComponent<MapComponent_Camp>()?.IsBeast(pawn) == true;
}
public class ThinkNode_ConditionalCampBeastRecalled : ThinkNode_Conditional
{
    protected override bool Satisfied(Pawn pawn) => pawn.Map.GetComponent<MapComponent_Camp>().BeastsRecalled;
}
public class JobGiver_CampBeastReturn : ThinkNode_JobGiver
{
    // The camp tick removes beasts and mechs that reach the pen or bay; this only walks them there.
    protected override Job TryGiveJob(Pawn pawn)
    {
        IntVec3 pen = pawn.Map.GetComponent<MapComponent_Camp>().HomeOf(pawn);
        if (pawn.Position.InHorDistOf(pen, 3)) { Job wait = JobMaker.MakeJob(JobDefOf.Wait, 120); return wait; }
        Job go = JobMaker.MakeJob(JobDefOf.Goto, pen); go.locomotionUrgency = LocomotionUrgency.Jog; go.expiryInterval = 600; return go;
    }
}

public class JobGiver_Camp : ThinkNode_JobGiver
{
    private readonly JobGiver_ExitMapBest exit = new();
    protected override Job TryGiveJob(Pawn pawn)
    {
        var c = pawn.Map.GetComponent<MapComponent_Camp>();
        if (c.phase == CampPhase.Leaving || !c.IsMember(pawn)) return exit.TryIssueJobPackage(pawn, default).Job ?? Idle(pawn);
        if (pawn.Downed) return null; // The game's downed/mental-state branches precede this subtree.
        if (c.CanAttemptPath(pawn))
        {
            if (c.phase == CampPhase.Recovering || !c.ValidArea || !c.Inside(pawn.Position))
            {
                Job defence = Combat(pawn, c, false); if (defence != null) return defence;
                if (!c.Footprint.Contains(pawn.Position)) return ReturnToCamp(pawn, c);
            }
            if (c.phase == CampPhase.Fighting && c.Ready.Contains(pawn))
            {
                Job man = c.MortarJob(pawn); if (man != null) return man; // Gunners only leave the rifle line while their gun has a safe target.
                Job cast = c.PsycastJob(pawn); if (cast != null) return cast;
                Job fight = Combat(pawn, c, true); if (fight != null) return fight;
            }
            // Stand-to: fire from a perimeter post facing the threat. Only a merc about to collapse or starve steps away.
            if (c.Alert && c.Ready.Contains(pawn) && !(pawn.needs.rest?.CurLevelPercentage < .1f) && !(pawn.needs.food?.CurLevelPercentage < .2f))
            {
                Job fire = Combat(pawn, c, false); if (fire != null) return fire;
                Job post = c.GuardJob(pawn); if (post != null) return post;
            }
            // Meals are fetched only in camp, so they are eaten at the camp table rather than out in the field.
            if (pawn.needs.food?.CurLevelPercentage < .45f && c.CanLiveAt(pawn.Position) && c.Deliver(pawn, c.Meals) is { } food)
            { Job eat = JobMaker.MakeJob(JobDefOf.Ingest, food); eat.count = 1; return eat; }
            if (c.phase != CampPhase.Fighting)
            {
                Job medical = Care(pawn, c); if (medical != null) return medical;
            }
            if (pawn.needs.rest?.CurLevelPercentage < .3f || pawn.health.HasHediffsNeedingTend() || pawn.health.summaryHealth.SummaryHealthPercent < .8f)
            {
                Building_Bed bed = BedFor(pawn, pawn, c);
                if (bed != null) return JobMaker.MakeJob(JobDefOf.LayDown, bed);
                if (CampMod.Settings.debug) c.DebugNoBed(pawn);
            }
            // Before the walk home: works planned out at the guns or the cover line are built in one trip, not one per return.
            Job build = c.ConstructionJob(pawn); if (build != null) return build;
            if (!c.Footprint.Contains(pawn.Position)) return ReturnToCamp(pawn, c);
            if (c.phase != CampPhase.Fighting)
            {
                var damaged = c.facilities.OfType<Building>().FirstOrDefault(b => b.Spawned && b.HitPoints < b.MaxHitPoints && c.CanLiveAt(b.Position) && pawn.CanReserveAndReach(b, PathEndMode.Touch, Danger.Some));
                if (damaged != null) return JobMaker.MakeJob(JobDefOf.Repair, damaged);
                var filth = c.Footprint.Cells.SelectMany(cell => cell.GetThingList(pawn.Map)).OfType<Filth>().FirstOrDefault(f => c.CanLiveAt(f.Position) && pawn.CanReserveAndReach(f, PathEndMode.Touch, Danger.Some));
                if (filth != null) return JobMaker.MakeJob(CampDefOf.GJL_MC_Clean, filth);
            }
        }
        // Spare garrison time is rifle drill; they relax instead when recreation runs low, so drill never costs them mood.
        if (c.phase == CampPhase.Garrison && c.Footprint.Contains(pawn.Position) && !(pawn.needs.joy?.CurLevelPercentage < .3f))
            return JobMaker.MakeJob(CampDefOf.GJL_MC_Train);
        return Idle(pawn);
    }
    private static Job Idle(Pawn p) { Job j = JobMaker.MakeJob(CampDefOf.GJL_MC_Relax); j.expiryInterval = 240; return j; }
    private static Job ReturnToCamp(Pawn p, MapComponent_Camp c)
    {
        IntVec3 spot = c.RallyCell;
        if (spot.Standable(p.Map) && p.CanReach(spot, PathEndMode.OnCell, Danger.Deadly))
        { Job j = JobMaker.MakeJob(JobDefOf.Goto, spot); j.expiryInterval = 600; j.locomotionUrgency = LocomotionUrgency.Jog; return j; }
        c.PathBlocked(p); return Idle(p);
    }
    // Uses the same validity rule LayDown re-checks every tick, so a chosen bed is never dropped on arrival.
    // Owned bed first, then any unowned camp bed (LayDown claims it); medical beds only when the game would allow medical rest.
    public static Building_Bed BedFor(Pawn actor, Pawn patient, MapComponent_Camp c)
    {
        bool medical = patient.Downed || HealthAIUtility.ShouldSeekMedicalRest(patient);
        Building_Bed med = null, own = null, free = null;
        foreach (var b in c.facilities.OfType<Building_Bed>())
        {
            if (!b.Spawned || !c.CanLiveAt(b.Position) || b.Medical && !medical || b.CurOccupants.Any(p => p != patient)) continue;
            if (!RestUtility.IsValidBedFor(b, patient, actor, checkSocialProperness: false, allowMedBedEvenIfSetToNoCare: true)) continue;
            if (b.Medical) med ??= b;
            else if (b.IsOwner(patient)) own = b;
            else if (!b.OwnersForReading.Any()) free ??= b;
        }
        return (patient.health.HasHediffsNeedingTend() || patient.Downed ? med : null) ?? own ?? free ?? med;
    }
    private static Job Care(Pawn p, MapComponent_Camp c)
    {
        if (p.WorkTagIsDisabled(WorkTags.Caring)) return null;
        foreach (Pawn patient in c.Present.OrderByDescending(x => x.Downed).ThenBy(x => x.health.summaryHealth.SummaryHealthPercent))
        {
            if (!c.Inside(patient.Position) && !c.Footprint.Contains(patient.Position)) continue;
            if (patient != p && patient.Downed && !patient.InBed() && p.CanReserveAndReach(patient, PathEndMode.OnCell, Danger.Some))
            {
                var bed = BedFor(p, patient, c);
                if (bed != null) { Job rescue = JobMaker.MakeJob(CampDefOf.GJL_MC_Rescue, patient, bed); rescue.count = 1; return rescue; }
            }
            if (patient.health.HasHediffsNeedingTend() && (patient == p || patient.InBed() || patient.Downed)
                && p.CanReserveAndReach(patient, PathEndMode.ClosestTouch, Danger.Some))
            {
                return JobMaker.MakeJob(JobDefOf.TendPatient, patient, c.Deliver(p, c.Medicines)); // No medicine in stock: tended without.
            }
            if (patient != p && patient.Downed && patient.needs.food?.CurLevelPercentage < .4f && p.CanReserve(patient) && c.Deliver(p, c.Meals) is { } food)
            { Job feed = JobMaker.MakeJob(JobDefOf.FeedPatient, food, patient); feed.count = 1; return feed; }
        }
        return null;
    }
    private static Job Combat(Pawn p, MapComponent_Camp c, bool mayMove)
    {
        var enemies = p.Map.mapPawns.AllPawnsSpawned.Where(t => c.AllowsTarget(p, t)).OrderBy(t => t.Position.DistanceToSquared(p.Position)).ToList();
        if (enemies.Count == 0) return null;
        Verb verb = p.TryGetAttackVerb(enemies[0], false);
        if (verb == null || verb.IsMeleeAttack) return null;
        // The nearest enemy may be out of range or behind a wall while another is a clear shot; a few line checks are cheap.
        Pawn enemy = enemies.Take(5).FirstOrDefault(t => verb.CanHitTarget(t));
        if (enemy != null)
        {
            p.mindState.enemyTarget = enemy;
            Job shot = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("GJL_MC_Attack"), enemy);
            shot.verbToUse = verb; shot.maxNumStaticAttacks = 3; shot.expiryInterval = 360; return shot;
        }
        // Defenders at the camp may shoot past the zone, but only an enemy inside it is worth moving for.
        if (!mayMove || (enemy = enemies.FirstOrDefault(t => c.InEngagementZone(t.Position))) == null) return null;
        p.mindState.enemyTarget = enemy;
        if (CastPositionFinder.TryFindCastPosition(new CastPositionRequest { caster = p, target = enemy, verb = verb,
            maxRangeFromTarget = verb.EffectiveRange, wantCoverFromTarget = true, validator = cell => c.Inside(cell) }, out IntVec3 dest))
        {
            Job move = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("GJL_MC_CombatMove"), dest, enemy);
            move.expiryInterval = 300; move.locomotionUrgency = LocomotionUrgency.Jog; return move;
        }
        return null;
    }
}

public class JobDriver_CampRelax : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
    protected override IEnumerable<Toil> MakeNewToils()
    {
        var relax = ToilMaker.MakeToil(); relax.defaultCompleteMode = ToilCompleteMode.Delay; relax.defaultDuration = 240;
        relax.tickIntervalAction = delta => pawn.needs.joy?.GainJoy(.0001f * delta, JoyKindDefOf.Meditative);
        yield return relax;
    }
}
// Shooting practice with no target or ammo: one skill gain per tick interval, so it costs no more than relaxing.
public class JobDriver_CampTrain : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
    protected override IEnumerable<Toil> MakeNewToils()
    {
        var drill = ToilMaker.MakeToil(); drill.defaultCompleteMode = ToilCompleteMode.Delay; drill.defaultDuration = 600;
        drill.tickIntervalAction = delta => pawn.skills?.Learn(SkillDefOf.Shooting, .1f * delta);
        yield return drill;
    }
}
// Vanilla Clean skips filth outside the player's home area and expects a target queue; the camp may sit outside home.
public class JobDriver_CampClean : JobDriver
{
    private float workDone;
    private Filth Filth => (Filth)job.targetA.Thing;
    public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        Toil clean = ToilMaker.MakeToil();
        clean.tickIntervalAction = delta =>
        {
            workDone += pawn.GetStatValue(StatDefOf.CleaningSpeed) * delta;
            if (workDone < Filth.def.filth.cleaningWorkToReduceThickness) return;
            workDone = 0; Filth.ThinFilth();
            if (Filth.Destroyed) ReadyForNextToil();
        };
        clean.defaultCompleteMode = ToilCompleteMode.Never;
        clean.WithEffect(EffecterDefOf.Clean, TargetIndex.A);
        clean.PlaySustainerOrSound(() => SoundDefOf.Interact_CleanFilth);
        yield return clean;
    }
    public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref workDone, "workDone"); }
}
public class JobDriver_CampAttack : JobDriver_AttackStatic
{
    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOn(() => !pawn.Map.GetComponent<MapComponent_Camp>().AllowsTarget(pawn, job.targetA.Thing));
        foreach (var t in base.MakeNewToils()) yield return t;
    }
}
public class JobDriver_CampCombatMove : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOn(() => !pawn.Map.GetComponent<MapComponent_Camp>().AllowsTarget(pawn, job.targetB.Thing)
            || !pawn.Map.GetComponent<MapComponent_Camp>().Inside(job.targetA.Cell));
        yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
    }
}
public class JobDriver_CampRescue : JobDriver
{
    // Bed slots match LayDown's reservation, so a patient already lying in a double bed doesn't block the second slot.
    public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
        && pawn.Reserve(job.targetB, job, ((Building_Bed)job.targetB.Thing).SleepingSlotsCount, 0, null, errorOnFailed);
    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A); this.FailOnDestroyedOrNull(TargetIndex.B);
        AddFinishAction(condition => { if (pawn.carryTracker.CarriedThing != null) pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _); });
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOn(() => !job.targetA.Pawn.Downed);
        yield return Toils_Haul.StartCarryThing(TargetIndex.A);
        yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.OnCell);
        // The patient's LayDown must reserve the bed itself; holding it here fails that job and the rescue repeats forever.
        yield return Toils_Reserve.Release(TargetIndex.B);
        yield return Toils_General.Do(() =>
        {
            Pawn patient = job.targetA.Pawn; var bed = (Building_Bed)job.targetB.Thing;
            if (!pawn.carryTracker.TryDropCarriedThing(bed.Position, ThingPlaceMode.Direct, out _)) return;
            if (!bed.Medical) patient.ownership.ClaimBedIfNonMedical(bed);
            patient.jobs.Notify_TuckedIntoBed(bed); // Same hand-off vanilla rescue uses: snaps to the slot and keeps the patient lying down.
        });
    }
}
