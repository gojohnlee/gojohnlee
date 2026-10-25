using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MercenaryCamp;

public class MemberRecord : IExposable
{
    public Pawn pawn;
    public int missingSince = -1, gearTier = -1;
    public void ExposeData()
    {
        Scribe_References.Look(ref pawn, "pawn"); Scribe_Values.Look(ref missingSince, "missingSince", -1);
        Scribe_Values.Look(ref gearTier, "gearTier", -1);
    }
}
// One pen or bay place. A stored beast or mech is not a pawn at all; it is generated on release and vanishes on return.
public class BeastSlot : IExposable
{
    public PawnKindDef kind;
    public Pawn pawn;
    public int readyAt;
    public void ExposeData() { Scribe_Defs.Look(ref kind, "kind"); Scribe_References.Look(ref pawn, "pawn"); Scribe_Values.Look(ref readyAt, "readyAt"); }
}

public partial class MapComponent_Camp : MapComponent
{
    public Building_ContractDepot depot;
    public IntVec3 center = IntVec3.Invalid;
    public Faction company;
    public Area area;
    public CampPhase phase;
    public List<MemberRecord> members = new();
    public List<Thing> facilities = new();
    public List<string> perks = new();
    public List<string> builtSlots = new();
    public List<int> rewardedQuests = new();
    public int points, baseline, quietSince = -1, refillAt = -1, shortageSince = -1, nextQuestAt;
    public int currentQuestId = -1, questSequence, contractSerial, supplyWatchTicks, layoutVersion = CurrentLayoutVersion;
    // 2: footprint fixed from 9×7 to the intended 19×15. 3: supplies come from colony storage, so the depot stops taking hauls.
    public const int CurrentLayoutVersion = 3;
    public bool everContracted;
    public string reason;
    public string questBlocker; // Why the accepted quest has not settled yet; recomputed on every attempt, so never saved.
    private readonly List<Pawn> threats = new();
    private readonly Dictionary<int, int> nextPathAttempt = new();
    private int lastWarning = -60000;
    private PerkSummary summary;
    public MapComponent_Camp(Map map) : base(map) { reason = "보급소를 건설하고 첫 접촉 의뢰를 수락하세요."; }
    public int Now => Find.TickManager.TicksGame;
    public bool Active => phase is CampPhase.Garrison or CampPhase.Fighting or CampPhase.Recovering;
    public IEnumerable<CampPerkDef> SelectedPerks => perks.Select(n => DefDatabase<CampPerkDef>.GetNamedSilentFail(n)).Where(p => p != null);
    public PerkSummary S => summary ??= new PerkSummary(SelectedPerks);
    public int Tier(PerkBranch b) => S.Tier(b);
    public int Capacity => S.capacity;
    public float RefillDays => Math.Min(CampMod.Settings.refillDays, S.refillDays) * S.refillFactor;
    public IReadOnlyList<Pawn> Threats => threats;
    public IEnumerable<Pawn> Present => members.Where(r => IsPresent(r.pawn)).Select(r => r.pawn);
    public IEnumerable<Pawn> Ready => Present.Where(p => !p.Downed && !p.InMentalState && p.health.summaryHealth.SummaryHealthPercent > .35f);
    public bool ValidArea => area != null && map.areaManager.AllAreas.Contains(area) && area.ActiveCells.Any();
    public bool Inside(IntVec3 c) => ValidArea && c.InBounds(map) && area[c];
    // Standard camps live in the fixed 19×15 site; a free layout lives wherever its beds, tables, walls and rally point were planned.
    public CellRect Footprint => freeLayout ? (freeFootprint ??= CampLayout.FreeFootprint(this)) : CampLayout.FootprintAt(center);
    public bool IsPresent(Pawn p) => p != null && !p.Dead && p.Spawned && p.Map == map && p.Faction == company && !p.IsPrisoner;
    public bool IsMember(Pawn p) => p != null && p.Faction == company && !p.IsPrisoner && members.Any(m => m.pawn == p);
    public bool IsFacility(Thing t) => Active && facilities.Contains(t);
    public bool CanLiveAt(IntVec3 c) => Footprint.Contains(c) && (Inside(c) || !ValidArea);
    public bool NearCamp(IntVec3 c) => Footprint.ExpandedBy(CampRules.CampGuardRadius).Contains(c);
    // The area, plus the recon ambush belt around it. Shooting into the belt is allowed; walking into it is not.
    public bool InEngagementZone(IntVec3 c) => Inside(c) || S.ambushRange > 0 && ValidArea
        && GenRadial.RadialCellsAround(c, S.ambushRange, true).Any(x => x.InBounds(map) && area[x]);
    // Only threats in the engagement zone or at the camp belong to the battle; a siege or hive elsewhere must not freeze the camp.
    public bool Engages(Pawn enemy) => InEngagementZone(enemy.Position) || NearCamp(enemy.Position);
    // Stand-to: any active threat anywhere on the map. Recomputed from the threat scan every 120 ticks, so never saved.
    public bool Alert { get; private set; }
    public bool AtCamp(IntVec3 c) => Footprint.ExpandedBy(1).Contains(c);
    // Low cover a merc can shoot over (sandbags, barricades and the like), the player's or the camp's. Walls and rock block
    // the shot itself, and furniture under half fill hardly covers.
    private bool IsCover(Thing t) => t is Building b && b.def.fillPercent >= .5f && b.def.fillPercent < 1f && (b.Faction == Faction.OfPlayer || b.Faction == company);
    private bool CoverNear(IntVec3 c)
    {
        foreach (IntVec3 d in GenAdj.AdjacentCells) { IntVec3 n = c + d; if (n.InBounds(map) && n.GetEdifice(map) is { } e && IsCover(e)) return true; }
        return false;
    }
    // A defensive post: the camp and its ring, or a cell beside low cover inside the zone. Defenders on a post may fire at any
    // threat in weapon range, but never leave the posts to chase.
    public bool AtPost(IntVec3 c) => AtCamp(c) || IsPlannedPost(c) || Inside(c) && CoverNear(c);
    public int ThreatsInside => threats.Count(p => Inside(p.Position));
    public int ThreatsOutside => threats.Count(p => !Inside(p.Position));
    // Roles fill the roster front to back: gunners, psycasters, snipers, lancers; the rest are riflemen.
    public MercRole RoleOf(Pawn p)
    {
        int i = members.FindIndex(m => m.pawn == p);
        if (i < 0) return MercRole.Rifleman;
        if ((i -= MortarCount) < 0) return MercRole.Gunner;
        if ((i -= S.psycasters) < 0) return MercRole.Psycaster;
        if ((i -= S.snipers) < 0) return MercRole.Sniper;
        if ((i -= S.lancers) < 0) return MercRole.Lancer;
        return MercRole.Rifleman;
    }
    public bool IsGunner(Pawn p) => RoleOf(p) == MercRole.Gunner;

    public override void ExposeData()
    {
        Scribe_References.Look(ref depot, "depot"); Scribe_References.Look(ref company, "company"); Scribe_References.Look(ref area, "area");
        Scribe_Values.Look(ref center, "center", IntVec3.Invalid); Scribe_Values.Look(ref phase, "phase");
        Scribe_Collections.Look(ref members, "members", LookMode.Deep); Scribe_Collections.Look(ref facilities, "facilities", LookMode.Reference);
        Scribe_Collections.Look(ref perks, "perks", LookMode.Value); Scribe_Collections.Look(ref rewardedQuests, "rewardedQuests", LookMode.Value);
        Scribe_Collections.Look(ref builtSlots, "builtSlots", LookMode.Value);
        Scribe_Values.Look(ref points, "points"); Scribe_Values.Look(ref baseline, "baseline"); Scribe_Values.Look(ref quietSince, "quietSince", -1);
        Scribe_Values.Look(ref refillAt, "refillAt", -1); Scribe_Values.Look(ref shortageSince, "shortageSince", -1);
        Scribe_Values.Look(ref nextQuestAt, "nextQuestAt"); Scribe_Values.Look(ref currentQuestId, "currentQuestId", -1);
        Scribe_Values.Look(ref questSequence, "questSequence"); Scribe_Values.Look(ref contractSerial, "contractSerial");
        Scribe_Values.Look(ref supplyWatchTicks, "supplyWatchTicks"); Scribe_Values.Look(ref everContracted, "everContracted"); Scribe_Values.Look(ref reason, "reason");
        Scribe_Values.Look(ref layoutVersion, "layoutVersion", 1);
        ExposeSupport(); ExposeSquads(); ExposePlan();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        { members ??= new(); facilities ??= new(); perks ??= new(); rewardedQuests ??= new(); builtSlots ??= new(); summary = null; }
    }
    // Applies layout changes from mod updates to existing camps. Never-built slots only, so destroyed facilities stay a paid repair.
    public override void FinalizeInit()
    {
        base.FinalizeInit();
        if (layoutVersion < 2 && ValidArea && center.IsValid)
        {
            // Older builds created the area from a 9×7 rect, leaving the beds outside it; add the real footprint once.
            foreach (var c in Footprint) if (c.InBounds(map)) area[c] = true;
        }
        // Once only, so a player who re-enables it as an ordinary shelf keeps that choice. What it holds still counts as stock.
        if (layoutVersion < 3) depot?.settings.filter.SetDisallowAll();
        layoutVersion = CurrentLayoutVersion;
        if (Active && depot != null && depot.Spawned) { CampLayout.Build(this); AssignBeds(); SyncSquadSlots(); }
    }
    public override void MapComponentTick()
    {
        if (Now % 120 != 0) return;
        UpdateSquads(); // Before the phase gates, so beasts and mechs still come home after the contract ends.
        if (phase == CampPhase.Uncontracted) { CampQuests.Tick(this); return; }
        if (phase == CampPhase.Leaving)
        {
            if (!members.Any(m => m.pawn != null && !m.pawn.Dead && m.pawn.Spawned && m.pawn.Map == map))
            { phase = CampPhase.Uncontracted; reason = "계약 종료. 남은 시설은 다음 계약에 재사용합니다."; members.Clear(); }
            return;
        }
        UpdateMembers(); ScanThreats(); UpdateBattle(); ApplyPerkHediffs();
        if (planDirty) PlanChanged();
        if (Now % 600 == 0)
        {
            UpdateSupplies(); Refill(); CampQuests.Tick(this);
            if (phase == CampPhase.Garrison && shortageSince < 0) supplyWatchTicks += 600;
            SupportUpkeep();
        }
        // Must stay a multiple of the 120-tick gate above, otherwise it only fires every 15000 ticks.
        if (Now % 2400 == 0) { SyncRelations(); UpgradeEquipment(); }
        // Revoke a moving target the moment it leaves the zone. Never overwrite tending, sleep or mental-state jobs.
        foreach (Pawn p in Present.ToList())
        {
            var enemy = p.mindState.enemyTarget;
            if (enemy != null && !AllowsTarget(p, enemy))
            {
                p.mindState.enemyTarget = null;
                if (IsCombatJob(p.CurJobDef)) p.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
    }
    public static bool IsCombatJob(JobDef d) => d == JobDefOf.AttackMelee || d == JobDefOf.AttackStatic || d == JobDefOf.Goto || d == JobDefOf.Wait_Combat || d?.defName == "GJL_MC_Attack" || d?.defName == "GJL_MC_CombatMove";
    private void UpdateMembers()
    {
        foreach (var r in members.ToList())
        {
            if (r.pawn == null || r.pawn.Dead || r.pawn.Faction != company) { members.Remove(r); continue; }
            if (IsPresent(r.pawn)) r.missingSince = -1;
            else if (r.missingSince < 0) r.missingSince = Now;
            else if (Now - r.missingSince >= CampRules.MissingSlotDays * CampRules.Day)
            {
                members.Remove(r);
                Messages.Message($"용병 {r.pawn.LabelShort}이(가) {CampRules.MissingSlotDays}일째 돌아오지 않아 자리를 충원합니다.", depot, MessageTypeDefOf.NegativeEvent);
            }
        }
        // A long-missing member who returns after their slot was refilled rejoins only if there is room; otherwise they leave.
        foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(company).ToList())
            if (p.RaceProps.Humanlike && !p.Dead && !p.IsPrisoner && members.Count < Capacity && !members.Any(m => m.pawn == p))
                members.Add(new MemberRecord { pawn = p });
        // A live missing/captured member keeps their slot; no duplicate replacement on return.
        if (members.Count < Capacity && refillAt < 0)
            refillAt = Now + CampRules.RefillDelay(RefillDays, !members.Any(r => r.pawn != null && !r.pawn.Dead));
    }
    // A member in a mental break is the colony's problem, never a target for their own squad.
    public bool ActiveThreat(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer)
        && (p.TryGetComp<CompCanBeDormant>()?.Awake ?? true) && !p.IsPsychologicallyInvisible() && (company == null || p.Faction != company);
    private void ScanThreats()
    {
        threats.Clear(); threats.AddRange(map.mapPawns.AllPawnsSpawned.Where(ActiveThreat));
        if (threats.Any(p => !InEngagementZone(p.Position))) Warn("활동 구역 밖에 적이 있습니다. 용병은 구역 밖으로 출격하지 않습니다.");
        ReportNewThreats();
    }
    public bool AllowsTarget(Pawn merc, Thing target)
    {
        if (!Active || target is not Pawn enemy || !ActiveThreat(enemy)) return false;
        if (AtPost(merc.Position)) return true; // Weapon range is the limit; holding a post is not chasing.
        if (phase == CampPhase.Fighting && Inside(merc.Position)) return InEngagementZone(enemy.Position);
        // During retreat, invalid areas and travel, self-defence is local and never permits chasing.
        return merc.Position.DistanceToSquared(enemy.Position) <= 64 && (Footprint.Contains(merc.Position) || !Inside(merc.Position));
    }
    private void UpdateBattle()
    {
        // Set before interrupting, because the interrupted mercs pick their next job straight away.
        bool alert = threats.Count > 0, rising = alert && !Alert;
        Alert = alert;
        if (rising) StandTo();
        bool insideThreat = threats.Any(p => InEngagementZone(p.Position));
        if (!ValidArea && phase == CampPhase.Fighting) ChangePhase(CampPhase.Recovering, "활동 구역이 삭제되거나 비었습니다. 캠프로 복귀합니다.");
        if (!ValidArea) Warn("용병 활동 구역이 없거나 비었습니다. 보급소에서 구역을 지정하세요.");
        else if (!Inside(center)) Warn("캠프와 보급소가 활동 구역 밖입니다. 생활 시설도 구역에 포함하세요.");
        if (phase == CampPhase.Garrison && insideThreat)
        {
            baseline = Ready.Count(); quietSince = -1;
            if (baseline > 0) ChangePhase(CampPhase.Fighting, "지정 구역 안의 적에 대응 중");
        }
        if (phase == CampPhase.Fighting)
        {
            if (CampRules.Retreat(baseline, Ready.Count(), CampMod.Settings.retreatLoss))
                ChangePhase(CampPhase.Recovering, "사망·다운·부상·이탈이 누적되어 캠프로 후퇴합니다. 같은 전투에는 재출전하지 않습니다.");
            else if (!insideThreat) ChangePhase(CampPhase.Recovering, "구역 내 위협 해소. 캠프로 복귀·재정비 중");
        }
        if (phase == CampPhase.Recovering)
        {
            bool engaged = threats.Any(Engages);
            if (engaged) quietSince = -1;
            else if (quietSince < 0) quietSince = Now;
            if (CampRules.RecoveryComplete(engaged, Now, quietSince, Tier(PerkBranch.Support) >= 2 ? 1800 : 3600))
                ChangePhase(CampPhase.Garrison, "위협 해소 후 재정비 완료");
        }
    }
    private int lastStandTo = -60000;
    // Wakes the squad and sends it to the perimeter the moment a threat appears on the map.
    private void StandTo()
    {
        if (Now - lastStandTo >= CampRules.Hour)
            Messages.Message("용병단: 적 출현. 경계 태세로 적 방향 외곽 엄폐 위치에 배치합니다.", threats.OrderBy(t => t.Position.DistanceToSquared(center)).First(), MessageTypeDefOf.ThreatSmall);
        lastStandTo = Now;
        foreach (Pawn p in Ready.ToList())
            if (p.CurJobDef != JobDefOf.TendPatient && p.CurJobDef != CampDefOf.GJL_MC_Rescue && p.CurJobDef != JobDefOf.ManTurret && !IsCombatJob(p.CurJobDef))
                p.jobs.EndCurrentJob(JobCondition.InterruptForced);
    }
    // Stand-to post nearest the threat, preferring cover toward it: the footprint edge (the corridor behind the sandbag ring),
    // plus any cell beside low cover the player built inside the zone, except while retreating, when the squad holds the camp.
    // A merc already on a comparable post stays put, so the line doesn't shuffle as the enemy moves.
    public Job GuardJob(Pawn p)
    {
        Pawn threat = threats.OrderBy(t => t.Position.DistanceToSquared(center)).FirstOrDefault();
        if (threat == null) return null;
        IntVec3 at = threat.Position, post = IntVec3.Invalid;
        float Score(IntVec3 c) => c.DistanceTo(at) - 15 * CoverUtility.CalculateOverallBlockChance(c, at, map);
        // Planned posts always count; a free layout's own cover lines are not ringed by the footprint edge, so their sides count too.
        IEnumerable<IntVec3> cells = Footprint.EdgeCells.Concat(PostCells);
        if (phase != CampPhase.Recovering && ValidArea)
        {
            var cover = map.listerBuildings.allBuildingsColonist.Where(b => IsCover(b) && Inside(b.Position));
            if (freeLayout) cover = cover.Concat(facilities.OfType<Building>().Where(b => b.Spawned && IsCover(b) && Inside(b.Position)));
            cells = cells.Concat(cover.SelectMany(b => GenAdj.CellsAdjacent8Way(b)).Where(Inside));
        }
        foreach (IntVec3 c in cells.Where(c => c.InBounds(map) && c.Standable(map)).Distinct().OrderBy(Score))
        {
            bool taken = c != p.Position && (c.GetFirstPawn(map) != null || !map.pawnDestinationReservationManager.CanReserve(c, p, false));
            if (!taken && p.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) { post = c; break; }
        }
        if (AtPost(p.Position) && (!post.IsValid || Score(p.Position) <= Score(post) + 4)) post = p.Position;
        if (!post.IsValid) return null;
        if (p.Position == post) { Job hold = JobMaker.MakeJob(JobDefOf.Wait_Combat); hold.expiryInterval = 120; hold.checkOverrideOnExpire = true; return hold; }
        p.mindState.enemyTarget = null; // Running between posts is not a pursuit; a stale target would get the run revoked.
        Job go = JobMaker.MakeJob(JobDefOf.Goto, post); go.locomotionUrgency = LocomotionUrgency.Sprint; go.expiryInterval = 300; return go;
    }
    public void ChangePhase(CampPhase next, string why)
    {
        if (phase == next) return;
        bool engaging = phase == CampPhase.Garrison && next == CampPhase.Fighting;
        phase = next; reason = why;
        if (CampMod.Settings.debug) Log.Message($"[MercenaryCamp] {next}: {why}");
        if (engaging) ReleaseSquads();
        Messages.Message("용병단: " + why, depot ?? (Thing)Present.FirstOrDefault(), MessageTypeDefOf.NeutralEvent);
        foreach (var p in Present.Where(p => !p.Downed && !p.InMentalState).ToList())
            if (p.CurJobDef != JobDefOf.TendPatient && p.CurJobDef != CampDefOf.GJL_MC_Rescue)
            { p.mindState.enemyTarget = null; p.jobs.EndCurrentJob(JobCondition.InterruptForced); }
    }
    private int lastBedDebug = -60000;
    public void DebugNoBed(Pawn p)
    {
        if (Now - lastBedDebug < 2500) return;
        lastBedDebug = Now;
        var lines = facilities.OfType<Building_Bed>().Where(b => b.Spawned).Select(b => $"  {b.Position} med={b.Medical} live={CanLiveAt(b.Position)} owners=[{string.Join("/", b.OwnersForReading.Select(o => o.LabelShort))}]"
            + $" use={RestUtility.CanUseBedNow(b, p, false, true)} reach={p.CanReach(b, PathEndMode.OnCell, Danger.Some)} reserve={p.CanReserve(b, b.SleepingSlotsCount, 0)}");
        Log.Message($"[MercenaryCamp] {p.LabelShort}: no usable bed (owned {p.ownership.OwnedBed?.Position.ToString() ?? "none"})\n" + string.Join("\n", lines));
    }
    public void Warn(string text)
    {
        if (Now - lastWarning < 15000) return;
        lastWarning = Now; Messages.Message(text, depot, MessageTypeDefOf.CautionInput);
    }
    // The company draws on colony stock the way a visiting trader does: anything in storage or lying in the home area, plus
    // anything left in the camp. Fogged and forbidden things are off limits, so the player holds stock back by forbidding it.
    public bool InSupply(Thing t) => t.Spawned && !t.IsForbidden(Faction.OfPlayer) && !t.Position.Fogged(map)
        && (t.IsInAnyStorage() || map.areaManager.Home[t.Position] || CanLiveAt(t.Position));
    private IEnumerable<Thing> Stock(ThingRequestGroup group, Func<Thing, bool> kind) => map.listerThings.ThingsInGroup(group).Where(t => kind(t) && InSupply(t));
    public static bool IsMeal(Thing t) => t.def.ingestible?.foodType.HasFlag(FoodTypeFlags.Meal) == true && t.IngestibleNow;
    // Cheapest first, then whatever spoils soonest.
    public IEnumerable<Thing> Meals => Stock(ThingRequestGroup.FoodSourceNotPlantOrTree, IsMeal)
        .OrderBy(t => t.def.ingestible.preferability).ThenBy(t => t.TryGetComp<CompRottable>()?.TicksUntilRotAtCurrentTemp ?? int.MaxValue);
    // Herbal and industrial grade only: the contract never burns the colony's glitterworld medicine.
    private static float Potency(Thing t) => t.def.GetStatValueAbstract(StatDefOf.MedicalPotency);
    public IEnumerable<Thing> Medicines => Stock(ThingRequestGroup.Medicine, t => Potency(t) <= 1f).OrderBy(Potency);
    public int MealCount => Meals.Sum(t => t.stackCount);
    public int MedicineCount => Medicines.Sum(t => t.stackCount);
    public int Count(ThingDef def) => map.listerThings.ThingsOfDef(def).Where(InSupply).Sum(t => t.stackCount);
    private static void Take(List<Thing> from, int amount)
    {
        foreach (var t in from) { if (amount == 0) break; int take = Math.Min(amount, t.stackCount); t.SplitOff(take).Destroy(); amount -= take; }
    }
    public bool Spend(Dictionary<ThingDef, int> costs)
    {
        if (costs.Any(kv => Count(kv.Key) < kv.Value)) return false;
        foreach (var kv in costs) Take(map.listerThings.ThingsOfDef(kv.Key).Where(InSupply).ToList(), kv.Value);
        return true;
    }
    public bool SpendMeals(int amount)
    {
        if (MealCount < amount) return false;
        Take(Meals.ToList(), amount);
        return true;
    }
    // Hands one unit to a merc: something already lying in the camp first (an interrupted delivery), else one taken from colony
    // stock and set down at their feet. Nothing waits in the depot, so food no longer rots there.
    public Thing Deliver(Pawn to, IEnumerable<Thing> candidates)
    {
        Thing stock = null;
        foreach (Thing t in candidates)
        {
            if (CanLiveAt(t.Position)) { if (to.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some)) return t; }
            else if (stock == null && !map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer)) stock = t;
        }
        return stock != null && GenPlace.TryPlaceThing(stock.SplitOff(1), to.Position, map, ThingPlaceMode.Near, out Thing placed) ? placed : null;
    }
    private void UpdateSupplies()
    {
        if (MealCount > 0)
        {
            if (shortageSince >= 0) Messages.Message("용병단 식량 보급이 재개되었습니다.", depot, MessageTypeDefOf.PositiveEvent);
            shortageSince = -1;
        }
        else
        {
            if (shortageSince < 0) { shortageSince = Now; Messages.Message($"용병단 식량이 없습니다. {CampMod.Settings.graceDays:F1}일 안에 창고에 완성식을 비축하세요. 금지한 식사는 가져가지 않습니다.", depot, MessageTypeDefOf.NegativeEvent); }
            if (Now - shortageSince >= CampMod.Settings.graceDays * CampRules.Day) EndContract("식량 보급 유예기간이 끝나 계약을 종료합니다.");
        }
    }
    public void EndContract(string why)
    {
        CampQuests.Cancel(this); refillAt = -1; ChangePhase(CampPhase.Leaving, why);
    }
    public void EnsureFaction()
    {
        if (company == null) { company = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(CampDefOf.GJL_MC_Company)); Find.FactionManager.Add(company); }
        SyncRelations();
    }
    public void SyncRelations()
    {
        if (company == null) return;
        if (Active && company.HostileTo(Faction.OfPlayer)) { EndContract("플레이어와의 적대 관계로 계약이 종료되었습니다."); return; }
        foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
        {
            if (f == company) continue;
            FactionRelationKind kind = f == Faction.OfPlayer ? FactionRelationKind.Ally : f.HostileTo(Faction.OfPlayer) ? FactionRelationKind.Hostile : FactionRelationKind.Neutral;
            company.SetRelation(new FactionRelation { other = f, kind = kind, baseGoodwill = kind == FactionRelationKind.Ally ? 100 : kind == FactionRelationKind.Hostile ? -100 : 0 });
            f.SetRelation(new FactionRelation { other = company, kind = kind, baseGoodwill = kind == FactionRelationKind.Ally ? 100 : kind == FactionRelationKind.Hostile ? -100 : 0 });
        }
    }
    public bool StartContract(out string error)
    {
        error = CampLayout.Validate(this, true);
        if (error != null) return false;
        if (!Spend(new() { [ThingDefOf.WoodLog] = 400, [ThingDefOf.Steel] = 200, [ThingDefOf.Silver] = 200 })) { error = $"창고에 목재 400, 강철 200, 은 200이 필요합니다. (현재 {Count(ThingDefOf.WoodLog)}, {Count(ThingDefOf.Steel)}, {Count(ThingDefOf.Silver)})"; return false; }
        EnsureFaction(); center = depot.Position; freeFootprint = null; contractSerial++; everContracted = true;
        CampLayout.Build(this, true, setup: true);
        if (!ValidArea && map.areaManager.TryMakeNewAllowed(out Area_Allowed created))
        {
            area = created; created.SetLabel("용병 활동 구역"); foreach (var c in Footprint) area[c] = true;
            foreach (IntVec3 c in plan.SelectMany(p => CampPlan.RectOf(this, p))) if (freeLayout && c.InBounds(map)) area[c] = true;
        }
        phase = CampPhase.Garrison; reason = "캠프 주둔 중. 활동 구역을 방어할 범위로 넓혀주세요.";
        shortageSince = -1; supplyWatchTicks = 0; nextQuestAt = Now + CampRules.Day * 3;
        while (members.Count < Capacity) SpawnMember();
        return true;
    }
    public void SpawnMember()
    {
        var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(CampDefOf.GJL_MC_Rifleman, company, forceGenerateNewPawn: true,
            mustBeCapableOfViolence: true, allowFood: false, allowAddictions: false, fixedBiologicalAge: 28, forceNoIdeo: true,
            validatorPreGear: p => !p.WorkTagIsDisabled(WorkTags.Caring) && !p.WorkTagIsDisabled(WorkTags.ManualDumb) && !p.WorkTagIsDisabled(WorkTags.ManualSkilled)));
        p.inventory?.innerContainer.ClearAndDestroyContents();
        var record = new MemberRecord { pawn = p }; members.Add(record);
        IntVec3 spawn = CellFinder.RandomClosewalkCellNear(RallyCell, map, 2);
        GenSpawn.Spawn(p, spawn, map); p.needs.food.CurLevelPercentage = 1; p.needs.rest.CurLevelPercentage = 1;
        p.skills.GetSkill(SkillDefOf.Construction).Level = 6;
        UpgradeMember(record); AssignBeds();
    }
    private void Refill()
    {
        if (!Active || phase != CampPhase.Garrison || refillAt < 0 || Now < refillAt || shortageSince >= 0 || depot == null || !depot.Spawned) return;
        int missing = Capacity - members.Count;
        if (missing <= 0) { refillAt = -1; return; }
        // Replacements are not a free equipment farm: food plus a small recruitment payment, waived by the standing-army perk.
        int silver = S.freeRefill ? 0 : missing * 100;
        if (silver > 0 && !Spend(new() { [ThingDefOf.Silver] = silver })) { Warn($"충원 대기: 창고에 은 {silver}과 완성식이 필요합니다."); return; }
        for (int i = 0; i < missing; i++) SpawnMember();
        refillAt = -1; Messages.Message($"용병 {missing}명이 보충되었습니다." + (silver > 0 ? $" 창고에서 은 {silver}을 가져갔습니다." : ""), depot, MessageTypeDefOf.PositiveEvent);
    }
    public void UpgradeEquipment() { foreach (var r in members.Where(r => IsPresent(r.pawn))) UpgradeMember(r); }
    private void UpgradeMember(MemberRecord r)
    {
        Pawn p = r.pawn; MercRole role = RoleOf(p); CampPerkDef elite = S.elite;
        p.skills.GetSkill(SkillDefOf.Medicine).Level = Math.Max(p.skills.GetSkill(SkillDefOf.Medicine).Level, 5); // Everyone can patch up a squadmate.
        if (role == MercRole.Psycaster) SetupPsycaster(p);
        if (p.Downed || phase == CampPhase.Fighting) return;
        ThingDef weapon = role switch { MercRole.Sniper => CampDefOf.Gun_SniperRifle, MercRole.Lancer => CampDefOf.Gun_ChargeLance, _ => elite?.weapon ?? CampDefOf.Gun_BoltActionRifle };
        if (p.equipment.Primary?.def != weapon)
        {
            p.equipment.DestroyAllEquipment(); // Contract-issued old gear is returned, never dropped as a repeatable reward.
            p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(weapon));
        }
        foreach (ThingDef gear in new[] { elite?.armor, elite?.helmet })
        {
            if (gear == null || p.apparel.WornApparel.Any(a => a.def == gear)) continue;
            foreach (var old in p.apparel.WornApparel.ToList()) if (!ApparelUtility.CanWearTogether(old.def, gear, p.RaceProps.body)) { p.apparel.Remove(old); old.Destroy(); }
            p.apparel.Wear((Apparel)ThingMaker.MakeThing(gear, gear.MadeFromStuff ? GenStuff.DefaultStuffFor(gear) : null));
        }
        if (r.gearTier < 0) p.skills.GetSkill(SkillDefOf.Shooting).Level = elite?.shooting ?? 8;
        r.gearTier = elite?.tier ?? 0;
    }
    public void AssignBeds()
    {
        var beds = facilities.OfType<Building_Bed>().Where(b => b.Spawned && !b.Medical).ToList();
        for (int i = 0; i < members.Count && i < beds.Count; i++)
            if (IsPresent(members[i].pawn)) members[i].pawn.ownership.ClaimBedIfNonMedical(beds[i]);
    }
    public bool CanAfford(CampPerkDef perk) => perk.price.Count > 0 && perk.price.All(c => Count(c.thingDef) >= c.count);
    // Why a perk cannot be taken right now, cost aside; null when it can.
    public string LockReason(CampPerkDef perk)
    {
        if (perks.Contains(perk.defName)) return "이미 적용됨";
        if (!PerkSummary.Available(perk.branch)) return "필요한 DLC가 없습니다.";
        if (!Active) return "계약 중에만 구매할 수 있습니다.";
        if (perk.doctrine)
        {
            if (!CampRules.CanTakeDoctrine(Tier(perk.branch), perk.tier)) return $"{PerkSummary.Label(perk.branch)} {perk.tier}단계가 필요합니다.";
        }
        else if (!CampRules.IsNextTier(Tier(perk.branch), perk.tier)) return "선행 단계를 먼저 선택하세요.";
        var missing = perk.MissingResearch.ToList();
        return missing.Count > 0 ? "연구 필요: " + string.Join(", ", missing.Select(r => r.LabelCap)) : null;
    }
    // Paid either with contract points or, instantly, with the perk's price taken straight from colony stock.
    public bool Buy(CampPerkDef perk, bool withSupplies, out string error)
    {
        error = LockReason(perk); if (error != null) return false;
        if (withSupplies ? !CanAfford(perk) : points < perk.cost) { error = withSupplies ? "창고 물자가 부족합니다: " + PriceText(perk) : "계약 점수가 부족합니다."; return false; }
        error = CampLayout.Validate(this, false); if (error != null) return false;
        var limitsBefore = PlanLimits();
        if (withSupplies) { Spend(perk.PriceTable); Messages.Message($"{perk.LabelCap}: 창고에서 {PriceText(perk)}을(를) 가져갔습니다.", depot, MessageTypeDefOf.PositiveEvent); }
        else points -= perk.cost;
        perks.Add(perk.defName); summary = null;
        CampLayout.Build(this); AssignBeds(); SyncSquadSlots(); UpgradeEquipment(); ApplyPerkHediffs();
        if (members.Count < Capacity && refillAt < 0) refillAt = Now + CampRules.RefillDelay(RefillDays, false);
        NotifyNewPlanRoom(limitsBefore);
        return true;
    }
    public static string PriceText(CampPerkDef perk) => string.Join(" · ", perk.price.Select(c => $"{c.thingDef.label} {c.count}"));
    public bool CanAttemptPath(Pawn p) => !nextPathAttempt.TryGetValue(p.thingIDNumber, out int t) || Now >= t;
    public void PathBlocked(Pawn p) { nextPathAttempt[p.thingIDNumber] = Now + 1800; Warn("용병의 캠프 경로가 막혔습니다. 통로를 확인하세요. 잠시 후 다시 시도합니다."); }
}
