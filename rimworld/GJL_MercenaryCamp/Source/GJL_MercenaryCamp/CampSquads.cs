using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MercenaryCamp;

// Released squads: Anomaly pen beasts hunt the whole map; Biotech bay mechs fight inside the engagement zone.
// Both come out when a fight starts and go back (vanish) when it is over; losses come back free after a delay.
public partial class MapComponent_Camp
{
    public List<BeastSlot> beastSlots = new(), mechSlots = new();
    public int beastsReleasedAt = -1;
    private void ExposeSquads()
    {
        Scribe_Values.Look(ref beastsReleasedAt, "beastsReleasedAt", -1);
        Scribe_Collections.Look(ref beastSlots, "beastSlots", LookMode.Deep); Scribe_Collections.Look(ref mechSlots, "mechSlots", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            beastSlots ??= new(); mechSlots ??= new();
            beastSlots.RemoveAll(s => s.kind == null); mechSlots.RemoveAll(s => s.kind == null); // DLC removed since the save.
        }
    }
    // Every company animal-like pawn is ours, including the halves a dividing fleshbeast leaves behind.
    public bool IsBeast(Pawn p) => company != null && p.Faction == company && !p.RaceProps.Humanlike && !p.RaceProps.IsMechanoid;
    public bool IsCampMech(Pawn p) => company != null && p.Faction == company && p.RaceProps.IsMechanoid;
    public bool BeastsRecalled => !Active || threats.Count == 0 || beastsReleasedAt >= 0 && Now - beastsReleasedAt > CampRules.BeastMaxOutTicks;
    public bool MechsRecalled => !Active || !threats.Any(t => InEngagementZone(t.Position));
    public IntVec3 PenCell => MarkerCell(PlanKind.Pen, new IntVec3(0, 0, -11));
    public IntVec3 BayCell => MarkerCell(PlanKind.Bay, new IntVec3(0, 0, 11));
    public IntVec3 HomeOf(Pawn p) => p.RaceProps.IsMechanoid ? BayCell : PenCell;

    public void SyncSquadSlots() { Sync(beastSlots, S.beasts); Sync(mechSlots, S.mechs); }
    private void Sync(List<BeastSlot> slots, List<PawnKindDef> roster)
    {
        var want = roster.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kind in slots.Select(s => s.kind).Distinct().ToList())
        {
            int keep = want.TryGetValue(kind, out int n) ? n : 0;
            while (slots.Count(s => s.kind == kind) > keep && slots.FirstOrDefault(s => s.kind == kind && s.pawn == null) is { } idle) slots.Remove(idle);
        }
        foreach (var kv in want)
            for (int have = slots.Count(s => s.kind == kv.Key); have < kv.Value; have++) slots.Add(new BeastSlot { kind = kv.Key, readyAt = Now });
    }
    private List<Pawn> Release(List<BeastSlot> slots, IntVec3 home)
    {
        var released = new List<Pawn>();
        foreach (var s in slots.Where(s => s.pawn == null && s.readyAt <= Now))
        {
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(s.kind, company, forceGenerateNewPawn: true));
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(home, map, 3), map);
            p.TryGetComp<CompCanBeDormant>()?.WakeUp(); // Fingerspikes and cluster mechs generate asleep.
            s.pawn = p; released.Add(p);
        }
        return released;
    }
    private void ReleaseSquads()
    {
        SyncSquadSlots();
        var beasts = Release(beastSlots, PenCell);
        if (beasts.Count > 0)
        {
            beastsReleasedAt = Now;
            Messages.Message($"용병단이 우리를 열었습니다. 변칙체 {beasts.Count}마리가 맵의 적을 사냥합니다.", new TargetInfo(PenCell, map), MessageTypeDefOf.NeutralEvent);
        }
        var mechs = Release(mechSlots, BayCell);
        if (mechs.Count > 0)
        {
            LordMaker.MakeNewLord(company, new LordJob_CampMechs(), map, mechs);
            Messages.Message($"정비고에서 메카 {mechs.Count}기가 출격합니다.", new TargetInfo(BayCell, map), MessageTypeDefOf.NeutralEvent);
        }
    }
    private void UpdateSquads()
    {
        Settle(beastSlots, S.recaptureDays);
        Settle(mechSlots, S.mechRebuildDays);
        if (company == null) return;
        foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(company).Where(p => !p.RaceProps.Humanlike).ToList())
        {
            bool recalled = p.RaceProps.IsMechanoid ? MechsRecalled : BeastsRecalled;
            IntVec3 home = HomeOf(p);
            if (recalled && (p.Downed || p.Position.InHorDistOf(home, 3) || !p.CanReach(home, PathEndMode.OnCell, Danger.Deadly))) Store(p);
        }
    }
    private void Settle(List<BeastSlot> slots, float days)
    {
        foreach (var s in slots.Where(s => s.pawn != null))
            if (s.pawn.Dead || s.pawn.Destroyed || !s.pawn.Spawned || s.pawn.Map != map || s.pawn.Faction != company)
            { s.pawn = null; s.readyAt = Now + (int)(days * CampRules.Day); }
    }
    // Back in the pen or bay: survivors are ready for the next fight; division leftovers simply go away.
    private void Store(Pawn p)
    {
        if (beastSlots.Concat(mechSlots).FirstOrDefault(s => s.pawn == p) is { } slot) { slot.pawn = null; slot.readyAt = Now; }
        Lord lord = p.GetLord();
        p.Destroy();
        if (lord != null && lord.ownedPawns.Count == 0) map.lordManager.RemoveLord(lord);
    }
}

public class LordJob_CampMechs : LordJob
{
    public override StateGraph CreateGraph() { var graph = new StateGraph(); graph.AddToil(new LordToil_CampMechs()); return graph; }
}
public class LordToil_CampMechs : LordToil
{
    public override void UpdateAllDuties() { foreach (Pawn p in lord.ownedPawns) p.mindState.duty = new PawnDuty(CampDefOf.GJL_MC_MechSquad); }
}
// Bay mechs follow the mercenaries' rule: fight inside the engagement zone, never chase past it.
public class JobGiver_CampMechFight : ThinkNode_JobGiver
{
    protected override Job TryGiveJob(Pawn pawn)
    {
        var c = pawn.Map.GetComponent<MapComponent_Camp>();
        if (c.MechsRecalled) return null;
        Pawn enemy = c.Threats.Where(t => c.ActiveThreat(t) && c.InEngagementZone(t.Position)).OrderBy(t => t.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
        if (enemy == null) return null;
        Verb verb = pawn.TryGetAttackVerb(enemy, false);
        if (verb == null) return null;
        if (verb.IsMeleeAttack) { Job melee = JobMaker.MakeJob(JobDefOf.AttackMelee, enemy); melee.expiryInterval = 300; melee.checkOverrideOnExpire = true; return melee; }
        if (verb.CanHitTarget(enemy)) { Job shot = JobMaker.MakeJob(JobDefOf.AttackStatic, enemy); shot.maxNumStaticAttacks = 3; shot.expiryInterval = 360; return shot; }
        if (CastPositionFinder.TryFindCastPosition(new CastPositionRequest { caster = pawn, target = enemy, verb = verb,
            maxRangeFromTarget = verb.EffectiveRange, wantCoverFromTarget = true, validator = cell => c.Inside(cell) }, out IntVec3 dest))
        { Job move = JobMaker.MakeJob(JobDefOf.Goto, dest); move.expiryInterval = 300; return move; }
        return null;
    }
}
