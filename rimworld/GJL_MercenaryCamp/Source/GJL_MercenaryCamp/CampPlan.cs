using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MercenaryCamp;

public class PlanSlot : IExposable
{
    public PlanKind kind;
    public IntVec3 offset; // From the depot, so the plan survives a depot rebuilt on the same spot.
    public Rot4 rot = Rot4.North;
    public void ExposeData()
    {
        Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref offset, "offset"); Scribe_Values.Look(ref rot, "rot", Rot4.North);
    }
}

// Free layout: the player plans where facilities and markers go, inside the activity area; the garrison crew builds what is
// planned. Perks still set how many of each the contract supplies.
public partial class MapComponent_Camp
{
    public bool freeLayout;
    public List<PlanSlot> plan = new();
    private CellRect? freeFootprint;
    private bool planDirty;
    private void ExposePlan()
    {
        Scribe_Values.Look(ref freeLayout, "freeLayout"); Scribe_Collections.Look(ref plan, "plan", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) { plan ??= new(); freeFootprint = null; }
    }
    private IntVec3 MarkerCell(PlanKind kind, IntVec3 standardOffset) =>
        center + (freeLayout && plan.FirstOrDefault(p => p.kind == kind) is { } s ? s.offset : standardOffset);
    public IntVec3 RallyCell => MarkerCell(PlanKind.Rally, new IntVec3(0, 0, 2));
    // A post outside the area would send its merc straight back to camp, so only posts the squad may stand on count.
    public IEnumerable<IntVec3> PostCells => freeLayout
        ? plan.Where(p => p.kind == PlanKind.Post).Select(p => center + p.offset).Where(c => Inside(c) || Footprint.Contains(c))
        : Enumerable.Empty<IntVec3>();
    public bool IsPlannedPost(IntVec3 c) => freeLayout && plan.Any(p => p.kind == PlanKind.Post && center + p.offset == c);

    public int PlanLimit(PlanKind kind) => kind switch
    {
        PlanKind.Bed => Capacity, PlanKind.Mortar => S.mortars, PlanKind.MiniTurret => S.miniTurrets, PlanKind.Autocannon => S.autocannons,
        PlanKind.Cover => S.sandbags ? CampRules.PlanLimit(kind) : 0, PlanKind.IED => S.iedCount, _ => CampRules.PlanLimit(kind)
    };
    public int PlanCount(PlanKind kind) => plan.Count(p => p.kind == kind);
    public Dictionary<PlanKind, int> PlanLimits() => Enum.GetValues<PlanKind>().ToDictionary(k => k, PlanLimit);
    // A perk that raises a limit leaves room the player has not planned yet; say what, and where to plan it.
    private void NotifyNewPlanRoom(Dictionary<PlanKind, int> before)
    {
        if (!freeLayout) return;
        var room = Enum.GetValues<PlanKind>().Where(k => PlanLimit(k) > before[k] && PlanLimit(k) > PlanCount(k))
            .Select(k => $"{CampPlan.Label(k)} {PlanLimit(k) - PlanCount(k)}개").ToList();
        if (room.Count > 0) Messages.Message($"자유 설계: 배치 한도가 늘었습니다 ({string.Join(", ", room)} 더). 건축 메뉴의 '용병단' 탭에서 배치하세요.", depot, MessageTypeDefOf.NeutralEvent);
    }
    public bool SetFreeLayout(bool free, out string error)
    {
        error = null;
        if (free == freeLayout) return true;
        if (depot == null || !depot.Spawned) { error = "용병 보급소를 먼저 건설하세요."; return false; }
        if (free && !CampDefOf.GJL_MC_Res_CampPlanning.IsFinished) { error = $"'{CampDefOf.GJL_MC_Res_CampPlanning.LabelCap}' 연구가 필요합니다."; return false; }
        if (free) plan = CampPlan.FromStandard(this); // The camp as it stands becomes the plan, so switching moves nothing.
        else if (CampLayout.ValidateSite(map, center, depot, facilities, true) is { } site) { error = "표준 배치로 돌아가려면 19×15 부지가 비어 있어야 합니다. " + site; return false; }
        freeLayout = free; PlanChanged();
        return true;
    }
    public void AddPlan(PlanKind kind, IntVec3 cell, Rot4 rot)
    {
        if (CampPlan.IsSingle(kind)) plan.RemoveAll(p => p.kind == kind);
        plan.Add(new PlanSlot { kind = kind, offset = cell - center, rot = rot });
        freeFootprint = null; planDirty = true;
    }
    public int RemovePlansAt(IntVec3 cell)
    {
        int removed = plan.RemoveAll(p => CampPlan.RectOf(this, p).Contains(cell));
        if (removed > 0) { freeFootprint = null; planDirty = true; }
        return removed;
    }
    // Retires facilities the plan dropped and roofs rooms it closed; new plans are left for the crew.
    public void PlanChanged()
    {
        planDirty = false; freeFootprint = null; PendingChanged();
        if (Active && depot != null && depot.Spawned) { CampLayout.Build(this); AssignBeds(); }
    }

    // Unbuilt works, cached for drawing, the depot readout and the crew's job search (re-checked when one is finished).
    private List<CampLayout.Slot> pendingCache;
    private HashSet<CampLayout.Slot> blockedCache;
    private float pendingCacheTime;
    public void PendingChanged() => pendingCache = null;
    private void RefreshPending()
    {
        if (pendingCache != null && Time.realtimeSinceStartup < pendingCacheTime + 1f) return;
        pendingCache = CampLayout.PendingSlots(this).ToList();
        blockedCache = pendingCache.Where(s => CampLayout.Blocked(this, s)).ToHashSet();
        pendingCacheTime = Time.realtimeSinceStartup;
    }
    public int PendingCount { get { RefreshPending(); return pendingCache.Count; } }
    public int BlockedCount { get { RefreshPending(); return blockedCache.Count; } }
    public bool IsPending(IntVec3 cell) { RefreshPending(); return pendingCache.Any(s => center + new IntVec3(s.X, 0, s.Z) == cell); }
    // Peacetime crew work: the nearest planned work whose cells are clear, from a cell the squad may stand on.
    public Job ConstructionJob(Pawn p)
    {
        if (!freeLayout || phase != CampPhase.Garrison || Alert) return null;
        RefreshPending();
        foreach (CampLayout.Slot slot in pendingCache.Where(s => !blockedCache.Contains(s)).OrderBy(s => (center + new IntVec3(s.X, 0, s.Z)).DistanceToSquared(p.Position)))
        {
            IntVec3 cell = center + new IntVec3(slot.X, 0, slot.Z);
            if (!p.CanReserve(cell)) continue;
            foreach (IntVec3 stand in CampLayout.RectOf(this, slot).ExpandedBy(1).EdgeCells.OrderBy(c => c.DistanceToSquared(p.Position)))
            {
                if (!stand.InBounds(map) || !stand.Standable(map) || !(Inside(stand) || Footprint.Contains(stand)) || !p.CanReach(stand, PathEndMode.OnCell, Danger.Some)) continue;
                return JobMaker.MakeJob(CampDefOf.GJL_MC_Build, cell, stand);
            }
        }
        return null;
    }

    private static readonly Color PendingColor = new(.45f, .75f, 1f, .35f), BlockedColor = new(1f, .6f, .2f, .45f), OverLimitColor = new(1f, .25f, .25f, .3f);
    private bool Planning => CampPlan.IsPlanTool(Find.DesignatorManager.SelectedDesignator) || depot != null && depot.Spawned && Find.Selector.IsSelected(depot);
    // Unbuilt works always show, like blueprints (orange: something is in the way); the living area, markers and plans past
    // their limit only while planning or with the depot selected.
    public override void MapComponentUpdate()
    {
        if (!freeLayout || !center.IsValid || Find.CurrentMap != map) return;
        RefreshPending();
        foreach (CampLayout.Slot s in pendingCache)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(s.Def);
            GhostDrawer.DrawGhostThing(center + new IntVec3(s.X, 0, s.Z), s.Rotation, def, null, blockedCache.Contains(s) ? BlockedColor : PendingColor, AltitudeLayer.Blueprint, null, false, CampLayout.StuffFor(def));
        }
        if (!Planning) return;
        GenDraw.DrawFieldEdges(Footprint.Cells.ToList(), new Color(.3f, .85f, 1f, .7f));
        foreach (PlanSlot p in CampPlan.OverLimit(this))
            if (CampPlan.DefFor(p.kind, S) is { } def) GhostDrawer.DrawGhostThing(center + p.offset, p.rot, def, null, OverLimitColor, AltitudeLayer.Blueprint, null, false, CampLayout.StuffFor(def));
        foreach (var marks in plan.Where(p => CampPlan.IsMarker(p.kind)).GroupBy(p => p.kind))
            GenDraw.DrawFieldEdges(marks.Select(p => center + p.offset).Where(c => c.InBounds(map)).ToList(), CampPlan.MarkerColor(marks.Key));
    }
    public override void MapComponentOnGUI()
    {
        if (!freeLayout || !center.IsValid || Find.CurrentMap != map || !Planning) return;
        foreach (PlanSlot p in plan.Where(p => CampPlan.IsMarker(p.kind)))
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(center + p.offset), CampPlan.Label(p.kind), CampPlan.MarkerColor(p.kind));
    }
}

[StaticConstructorOnStartup]
public static class CampPlan
{
    public static readonly Texture2D PlanIcon = ContentFinder<Texture2D>.Get("UI/Designators/PlanOn");
    public static readonly Texture2D RemoveIcon = ContentFinder<Texture2D>.Get("UI/Designators/PlanOff");
    public static IEnumerable<Designator> Designators => DefDatabase<DesignationCategoryDef>.GetNamed("GJL_MC_Camp").AllResolvedDesignators.Where(IsPlanTool);
    public static bool IsPlanTool(Designator d) => d is Designator_CampPlanThing or Designator_CampMarker or Designator_CampPlanRemove;

    public static bool IsMarker(PlanKind k) => k is PlanKind.Post or PlanKind.Rally or PlanKind.Pen or PlanKind.Bay;
    public static bool IsSingle(PlanKind k) => k is PlanKind.Rally or PlanKind.Pen or PlanKind.Bay;
    // Where the squad lives: these set the free layout's footprint (eating, sleeping, cleaning, the stand-to ring).
    public static bool IsLiving(PlanKind k) => k is PlanKind.Bed or PlanKind.MedicalBed or PlanKind.Table or PlanKind.Chair or PlanKind.Wall or PlanKind.Door or PlanKind.Rally;
    // Kinds a perk can raise, so a full count is worth a hint.
    private static bool Grows(PlanKind k) => k is PlanKind.Bed or PlanKind.Mortar or PlanKind.MiniTurret or PlanKind.Autocannon or PlanKind.IED;

    private static readonly Dictionary<string, ThingDef> named = new();
    private static ThingDef Named(string defName) => named.TryGetValue(defName, out ThingDef d) ? d : named[defName] = DefDatabase<ThingDef>.GetNamed(defName);
    public static ThingDef DefFor(PlanKind kind, PerkSummary s) => kind switch
    {
        PlanKind.Bed or PlanKind.MedicalBed => Named("Bed"), PlanKind.Table => Named("Table2x2c"), PlanKind.Chair => Named("DiningChair"),
        PlanKind.Wall => Named("Wall"), PlanKind.Door => Named("Door"), PlanKind.Mortar => CampDefOf.Turret_Mortar,
        PlanKind.MiniTurret => Named("Turret_MiniTurret"), PlanKind.Autocannon => Named("Turret_Autocannon"),
        PlanKind.Cover => Named(s?.barricades == true ? "Barricade" : "Sandbags"), PlanKind.IED => Named("TrapIED_HighExplosive"), _ => null
    };
    private static PlanKind KindOf(CampLayout.Slot s) => s.Def switch
    {
        "Bed" => s.Medical ? PlanKind.MedicalBed : PlanKind.Bed, "Table2x2c" => PlanKind.Table, "DiningChair" => PlanKind.Chair,
        "Wall" => PlanKind.Wall, "Door" => PlanKind.Door, "Turret_Mortar" => PlanKind.Mortar, "Turret_MiniTurret" => PlanKind.MiniTurret,
        "Turret_Autocannon" => PlanKind.Autocannon, "Sandbags" or "Barricade" => PlanKind.Cover, _ => PlanKind.IED
    };
    public static CellRect RectOf(MapComponent_Camp camp, PlanSlot p) =>
        DefFor(p.kind, camp.S) is { } def ? GenAdj.OccupiedRect(camp.center + p.offset, p.rot, def.size) : CellRect.SingleCell(camp.center + p.offset);

    // Plans past a kind's limit (beds beyond the roster, guns beyond the perk count) stay on the plan but are not built.
    private static IEnumerable<IGrouping<PlanKind, PlanSlot>> Facilities(MapComponent_Camp camp) => camp.plan.Where(p => !IsMarker(p.kind)).GroupBy(p => p.kind);
    public static IEnumerable<CampLayout.Slot> Slots(MapComponent_Camp camp) => Facilities(camp).SelectMany(g => g.Take(camp.PlanLimit(g.Key)))
        .Select(p => new CampLayout.Slot(DefFor(p.kind, camp.S).defName, p.offset.x, p.offset.z, p.rot, p.kind == PlanKind.MedicalBed));
    public static IEnumerable<PlanSlot> OverLimit(MapComponent_Camp camp) => Facilities(camp).SelectMany(g => g.Skip(camp.PlanLimit(g.Key)));
    // Must run while the camp is still on the standard layout.
    public static List<PlanSlot> FromStandard(MapComponent_Camp camp)
    {
        var result = CampLayout.Slots(camp).Select(s => new PlanSlot { kind = KindOf(s), offset = new IntVec3(s.X, 0, s.Z), rot = s.Rotation }).ToList();
        result.Add(new PlanSlot { kind = PlanKind.Rally, offset = new IntVec3(0, 0, 2) });
        if (ModsConfig.AnomalyActive) result.Add(new PlanSlot { kind = PlanKind.Pen, offset = new IntVec3(0, 0, -11) });
        if (ModsConfig.BiotechActive) result.Add(new PlanSlot { kind = PlanKind.Bay, offset = new IntVec3(0, 0, 11) });
        return result;
    }

    public static AcceptanceReport CanPlan(MapComponent_Camp camp, PlanKind kind, IntVec3 cell, Rot4 rot)
    {
        if (camp?.depot == null || !camp.depot.Spawned || !camp.center.IsValid) return "용병 보급소를 먼저 건설하세요.";
        if (!camp.freeLayout) return "자유 설계 모드가 아닙니다.";
        Map map = camp.map;
        if (!cell.InBounds(map)) return false;
        int limit = camp.PlanLimit(kind);
        if (limit <= 0) return UnlockHint(kind);
        if (!IsSingle(kind) && camp.PlanCount(kind) >= limit) return $"{Label(kind)} 한도 {limit}개를 모두 배치했습니다." + (Grows(kind) ? " 퍽으로 늘릴 수 있습니다." : "");
        if (!cell.InHorDistOf(camp.center, CampRules.PlanRadius)) return $"보급소에서 {CampRules.PlanRadius:F0}칸 안에만 설계할 수 있습니다.";
        ThingDef def = DefFor(kind, camp.S);
        CellRect rect = def != null ? GenAdj.OccupiedRect(cell, rot, def.size) : CellRect.SingleCell(cell);
        CellRect depotRect = camp.depot.OccupiedRect();
        // A single marker (rally, pen, bay) moves, so its old spot does not count as taken.
        var taken = camp.plan.Where(p => !(IsSingle(kind) && p.kind == kind)).Select(p => RectOf(camp, p)).ToList();
        foreach (IntVec3 c in rect)
        {
            if (!c.InBounds(map) || c.CloseToEdge(map, 3)) return "맵 가장자리에 너무 가깝습니다.";
            if (c.Fogged(map)) return "보이지 않는 곳에는 설계할 수 없습니다.";
            if (camp.ValidArea && !camp.area[c]) return "용병 활동 구역 안에만 설계할 수 있습니다.";
            if (depotRect.Contains(c)) return "보급소 자리입니다.";
            if (taken.Any(r => r.Contains(c))) return "이미 다른 설계가 있습니다.";
            if (def?.terrainAffordanceNeeded != null && !c.GetTerrain(map).affordances.Contains(def.terrainAffordanceNeeded)) return "이 지면에는 지을 수 없습니다.";
            if (def == null && !c.Standable(map)) return "대원이 설 수 있는 칸이어야 합니다.";
            foreach (Thing t in c.GetThingList(map))
            {
                if (camp.facilities.Contains(t) || t is Pawn || t is Filth || t.def.category == ThingCategory.Item || CampLayout.LiesUnderFacilities(t)) continue;
                if (t is Plant plant && !plant.def.plant.IsTree) continue;
                if (t is Building || t is Blueprint || t is Frame || t is Plant || t.def.passability == Traversability.Impassable)
                    return $"{t.LabelNoCount}이(가) 있습니다. 먼저 치워주세요.";
            }
        }
        // Gun crews need their cell free, and no new work may stand on someone else's.
        if (def != null && def.hasInteractionCell)
        {
            IntVec3 crew = ThingUtility.InteractionCellWhenAt(def, cell, rot, map);
            if (!crew.InBounds(map) || crew.Impassable(map) || rect.Contains(crew) || depotRect.Contains(crew) || taken.Any(r => r.Contains(crew))) return "포수가 설 칸이 막혀 있습니다.";
        }
        foreach (PlanSlot p in camp.plan)
            if (DefFor(p.kind, camp.S) is { hasInteractionCell: true } other && rect.Contains(ThingUtility.InteractionCellWhenAt(other, camp.center + p.offset, p.rot, map)))
                return $"{Label(p.kind)}의 포수 자리를 막습니다.";
        return true;
    }
    public static string UnlockHint(PlanKind kind) => kind switch
    {
        PlanKind.Mortar => "포병 1단계(박격포 분대)가 필요합니다.", PlanKind.MiniTurret => "공병 3단계(자동 포탑)가 필요합니다.",
        PlanKind.Autocannon => "공병 5단계(중화기 포대)가 필요합니다.", PlanKind.Cover => "공병 1단계(모래주머니 방어선)가 필요합니다.",
        PlanKind.IED => "공병 2단계(IED 매설)가 필요합니다.", _ => "지금은 배치할 수 없습니다."
    };
    public static bool Visible(PlanKind kind) => (DebugSettings.godMode || CampDefOf.GJL_MC_Res_CampPlanning.IsFinished) && kind switch
    {
        PlanKind.Pen => ModsConfig.AnomalyActive, PlanKind.Bay => ModsConfig.BiotechActive, _ => true
    };
    // A plan tool picked while the camp is still on the standard layout offers the switch first, then picks itself.
    public static bool ReadyToPlan(MapComponent_Camp camp, Designator tool)
    {
        if (camp?.depot == null || !camp.depot.Spawned) { Messages.Message("용병 보급소를 먼저 건설하세요.", MessageTypeDefOf.RejectInput, false); return false; }
        if (camp.freeLayout) return true;
        if (tool is Designator_CampPlanRemove) { Messages.Message("자유 설계 중에만 쓸 수 있습니다.", MessageTypeDefOf.RejectInput, false); return false; }
        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(SwitchText, () =>
        {
            if (camp.SetFreeLayout(true, out string error)) Find.DesignatorManager.Select(tool);
            else Messages.Message(error, MessageTypeDefOf.RejectInput, false);
        }));
        return false;
    }
    public const string SwitchText = "캠프를 자유 설계로 전환할까요?\n\n지금 캠프 배치가 그대로 설계도가 되므로 철거되는 시설은 없습니다. 이후 새로 배치한 시설은 평시에 용병들이 직접 시공하고, 설계에서 지운 시설은 용병단이 철거합니다. 시설 수 한도는 퍽이 정합니다.";

    public static string Label(PlanKind kind) => kind switch
    {
        PlanKind.Bed => "용병 침상", PlanKind.MedicalBed => "의료 침상", PlanKind.Table => "식탁", PlanKind.Chair => "의자",
        PlanKind.Wall => "막사 벽", PlanKind.Door => "막사 문", PlanKind.Mortar => "박격포 진지", PlanKind.MiniTurret => "미니 포탑",
        PlanKind.Autocannon => "오토캐논 포대", PlanKind.Cover => "방어선", PlanKind.IED => "IED 함정", PlanKind.Post => "경계 초소",
        PlanKind.Rally => "집결지", PlanKind.Pen => "괴물 우리", _ => "메카 정비고"
    };
    public static string CountLabel(MapComponent_Camp camp, PlanKind kind) =>
        camp == null || !camp.freeLayout || IsSingle(kind) ? Label(kind) : $"{Label(kind)} {camp.PlanCount(kind)}/{camp.PlanLimit(kind)}";
    public static string Description(PlanKind kind) => kind switch
    {
        PlanKind.Bed => "용병이 잠자고 치료받는 침상. 한도는 정원과 같습니다.",
        PlanKind.MedicalBed => "다운되거나 치료가 필요한 대원이 먼저 쓰는 의료 침상. 최대 2개.",
        PlanKind.Table => "식사용 2×2 식탁. 의자를 식탁에 붙여, 식탁을 보게 놓으세요. 최대 2개.",
        PlanKind.Chair => "식탁 의자. 최대 8개.",
        PlanKind.Wall => "용병이 짓는 막사 벽. 용병 침상이 있는 방을 캠프 벽·문(또는 자연 암벽)으로 완전히 두르면 지붕을 자동으로 덮습니다. 최대 120칸.",
        PlanKind.Door => "막사 문. 최대 8개.",
        PlanKind.Mortar => "포병 퍽만큼 배치합니다. 포수가 서는 칸을 비워 두고, 지붕 아래는 피하세요.",
        PlanKind.MiniTurret => "공병 3단계부터. 계약 발전기로 전력이 필요 없고 고장·정비도 무료. 구역 안의 적만 사격합니다.",
        PlanKind.Autocannon => "공병 5단계부터 2기. 구역 안의 적만 사격합니다.",
        PlanKind.Cover => "공병 1단계부터. 모래주머니(공병 4단계부터 바리케이드)를 끌어서 줄로 배치합니다. 옆 칸은 경계 위치로 쓰입니다. 최대 80칸.",
        PlanKind.IED => "공병 2단계부터. 터진 함정은 평시에 무료로 다시 설치하고, 아군은 밟아도 터지지 않습니다.",
        PlanKind.Post => "적이 나타나면 대원이 먼저 자리 잡는 사격 위치. 초소의 대원은 사거리 안의 적을 쏘되 추격하지 않습니다. 활동 구역 안이어야 합니다. 최대 16곳.",
        PlanKind.Rally => "평시 대기·복귀 지점이자 충원 병력이 도착하는 곳. 생활 구역에 포함됩니다.",
        PlanKind.Pen => "변칙체가 풀려나고, 싸움이 끝나면 돌아오는 우리 위치.",
        _ => "메카가 출격하고 귀환하는 정비고 위치."
    };
    public static Color MarkerColor(PlanKind kind) => kind switch
    {
        PlanKind.Post => new Color(1f, .85f, .3f), PlanKind.Rally => new Color(.4f, 1f, .5f), PlanKind.Pen => new Color(.95f, .45f, .6f), _ => new Color(.6f, .7f, 1f)
    };
    public static Texture2D MarkerIcon(PlanKind kind) => kind switch
    {
        PlanKind.Post => TexCommand.Attack, PlanKind.Rally => TexCommand.GatherSpotActive,
        PlanKind.Pen => DefDatabase<ThingDef>.GetNamedSilentFail("PenMarker")?.uiIcon ?? TexCommand.ReleaseAnimals,
        _ => DefDatabase<ThingDef>.GetNamedSilentFail("BasicRecharger")?.uiIcon ?? TexCommand.Install
    };
    public static string DisabledReason(MapComponent_Camp camp, PlanKind kind) => camp != null && camp.freeLayout && camp.PlanLimit(kind) <= 0 ? UnlockHint(kind) : null;
}

// Architect tools for the 용병단 tab. Thing tools reuse the vanilla placement ghost, rotation keys and drag lines.
public abstract class Designator_CampPlanThing : Designator_Place
{
    protected abstract PlanKind Kind { get; }
    private static MapComponent_Camp Camp => Find.CurrentMap?.GetComponent<MapComponent_Camp>();
    private ThingDef Def => CampPlan.DefFor(Kind, Camp?.S);
    protected Designator_CampPlanThing() { defaultDesc = CampPlan.Description(Kind); Order = 10 + (int)Kind; }
    public override BuildableDef PlacingDef => Def;
    public override ThingStyleDef ThingStyleDefForPreview => null;
    public override ThingDef StuffDef => CampLayout.StuffFor(Def);
    public override bool DragDrawMeasurements => Def.drawStyleCategory != null;
    public override bool Visible => CampPlan.Visible(Kind);
    public override string Label => CampPlan.CountLabel(Camp, Kind);
    public override bool Disabled { get { disabledReason = CampPlan.DisabledReason(Camp, Kind); return disabledReason != null; } }
    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms) { icon = Def.GetUIIconForStuff(StuffDef); return base.GizmoOnGUI(topLeft, maxWidth, parms); }
    public override void Selected() { icon = Def.GetUIIconForStuff(StuffDef); base.Selected(); }
    public override void ProcessInput(Event ev) { if (CampPlan.ReadyToPlan(Camp, this)) base.ProcessInput(ev); }
    public override AcceptanceReport CanDesignateCell(IntVec3 c) => CampPlan.CanPlan(Camp, Kind, c, placingRot);
    public override void DesignateSingleCell(IntVec3 c) => Camp.AddPlan(Kind, c, placingRot);
    protected override void FinalizeDesignationSucceeded() { base.FinalizeDesignationSucceeded(); Camp?.PlanChanged(); }
}
public class Designator_CampBed : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.Bed; }
public class Designator_CampMedicalBed : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.MedicalBed; }
public class Designator_CampTable : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.Table; }
public class Designator_CampChair : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.Chair; }
public class Designator_CampWall : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.Wall; }
public class Designator_CampDoor : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.Door; }
public class Designator_CampMortar : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.Mortar; }
public class Designator_CampMiniTurret : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.MiniTurret; }
public class Designator_CampAutocannon : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.Autocannon; }
public class Designator_CampCover : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.Cover; }
public class Designator_CampIED : Designator_CampPlanThing { protected override PlanKind Kind => PlanKind.IED; }

public abstract class Designator_CampMarker : Designator
{
    protected abstract PlanKind Kind { get; }
    private static MapComponent_Camp Camp => Find.CurrentMap?.GetComponent<MapComponent_Camp>();
    protected Designator_CampMarker()
    {
        useMouseIcon = true; soundSucceeded = SoundDefOf.Designate_PlaceBuilding; defaultDesc = CampPlan.Description(Kind); Order = 10 + (int)Kind;
    }
    public override bool Visible => CampPlan.Visible(Kind);
    public override string Label => CampPlan.CountLabel(Camp, Kind);
    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms) { icon = CampPlan.MarkerIcon(Kind); return base.GizmoOnGUI(topLeft, maxWidth, parms); }
    public override void Selected() { icon = CampPlan.MarkerIcon(Kind); base.Selected(); }
    public override void ProcessInput(Event ev) { if (CampPlan.ReadyToPlan(Camp, this)) base.ProcessInput(ev); }
    public override AcceptanceReport CanDesignateCell(IntVec3 c) => CampPlan.CanPlan(Camp, Kind, c, Rot4.North);
    public override void DesignateSingleCell(IntVec3 c) => Camp.AddPlan(Kind, c, Rot4.North);
    protected override void FinalizeDesignationSucceeded() { base.FinalizeDesignationSucceeded(); Camp?.PlanChanged(); }
    public override void SelectedUpdate()
    {
        IntVec3 c = UI.MouseCell();
        if (c.InBounds(Map)) GenDraw.DrawFieldEdges(new List<IntVec3> { c }, CanDesignateCell(c).Accepted ? CampPlan.MarkerColor(Kind) : Color.red);
    }
}
public class Designator_CampPost : Designator_CampMarker { protected override PlanKind Kind => PlanKind.Post; }
public class Designator_CampRally : Designator_CampMarker { protected override PlanKind Kind => PlanKind.Rally; }
public class Designator_CampPen : Designator_CampMarker { protected override PlanKind Kind => PlanKind.Pen; }
public class Designator_CampBay : Designator_CampMarker { protected override PlanKind Kind => PlanKind.Bay; }

public class Designator_CampPlanRemove : Designator
{
    private static MapComponent_Camp Camp => Find.CurrentMap?.GetComponent<MapComponent_Camp>();
    public Designator_CampPlanRemove()
    {
        defaultLabel = "설계 제거"; defaultDesc = "끌어서 고른 칸의 용병단 설계를 지웁니다. 이미 지은 시설은 용병단이 철거합니다(환불 없음).";
        useMouseIcon = true; soundDragSustain = SoundDefOf.Designate_DragStandard; soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
        soundSucceeded = SoundDefOf.Designate_ZoneDelete; Order = 100;
    }
    public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;
    public override bool DragDrawMeasurements => true;
    public override bool Visible => CampPlan.Visible(PlanKind.Post);
    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms) { icon = CampPlan.RemoveIcon; return base.GizmoOnGUI(topLeft, maxWidth, parms); }
    public override void Selected() { icon = CampPlan.RemoveIcon; base.Selected(); }
    public override void ProcessInput(Event ev) { if (CampPlan.ReadyToPlan(Camp, this)) base.ProcessInput(ev); }
    public override AcceptanceReport CanDesignateCell(IntVec3 c) => Camp is { freeLayout: true } camp && camp.center.IsValid && camp.plan.Any(p => CampPlan.RectOf(camp, p).Contains(c));
    public override void DesignateSingleCell(IntVec3 c) => Camp.RemovePlansAt(c);
    protected override void FinalizeDesignationSucceeded() { base.FinalizeDesignationSucceeded(); Camp?.PlanChanged(); }
}

// Crew construction: walk beside the work, build it with a progress bar at the merc's construction speed, then it appears.
public class JobDriver_CampBuild : JobDriver
{
    private float workDone, workTotal;
    private MapComponent_Camp Camp => Map.GetComponent<MapComponent_Camp>();
    public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
    protected override IEnumerable<Toil> MakeNewToils()
    {
        IntVec3 cell = job.targetA.Cell;
        this.FailOn(() => Camp.phase != CampPhase.Garrison || Camp.Alert || !Camp.IsPending(cell));
        ThingDef def = CampLayout.PendingSlotAt(Camp, cell) is { } slot ? DefDatabase<ThingDef>.GetNamed(slot.Def) : null;
        yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
        Toil build = ToilMaker.MakeToil();
        build.initAction = () => { if (workTotal <= 0) workTotal = def != null ? CampLayout.CrewWork(def) : 1; };
        build.tickIntervalAction = delta =>
        {
            pawn.rotationTracker.FaceCell(cell);
            workDone += pawn.GetStatValue(StatDefOf.ConstructionSpeed) * 1.7f * delta;
            if (workDone < workTotal) return;
            if (CampLayout.CompleteSlot(Camp, cell) != null) SoundDefOf.Building_Complete.PlayOneShot(new TargetInfo(cell, Map));
            ReadyForNextToil();
        };
        build.defaultCompleteMode = ToilCompleteMode.Never;
        build.WithProgressBar(TargetIndex.A, () => workTotal > 0 ? workDone / workTotal : 0f);
        if (def != null) build.WithEffect(CampLayout.StuffFor(def)?.stuffProps?.constructEffect ?? def.constructEffect ?? EffecterDefOf.ConstructMetal, TargetIndex.A);
        yield return build;
    }
    public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref workDone, "workDone"); Scribe_Values.Look(ref workTotal, "workTotal"); }
}
