using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MercenaryCamp;

public static class CampLayout
{
    public record Slot(string Def, int X, int Z, Rot4 Rotation, bool Medical = false);
    // 19×15 cells (±9, ±7). CellRect.CenteredOn(c, w, h) takes a full width/height, not a radius.
    public static CellRect FootprintAt(IntVec3 center) => new(center.x - 9, center.z - 7, 19, 15);
    // IED spots flank the axis gaps; the second eight (minefield doctrine) sit one row further out.
    private static readonly (int X, int Z)[] IedSpots =
    {
        (-1, 9), (1, 9), (-1, -9), (1, -9), (11, -1), (11, 1), (-11, -1), (-11, 1),
        (-3, 9), (3, 9), (-3, -9), (3, -9), (11, -3), (11, 3), (-11, -3), (-11, 3)
    };
    public static IEnumerable<Slot> Slots(MapComponent_Camp camp)
    {
        if (camp.freeLayout) { foreach (Slot planned in CampPlan.Slots(camp)) yield return planned; yield break; }
        // Two fixed shelters; the central aisle remains open. No free-form building AI.
        for (int x = -8; x <= -2; x++) { yield return new("Wall", x, -6, Rot4.North); yield return new("Wall", x, 6, Rot4.North); }
        for (int z = -5; z <= 5; z++) { yield return new("Wall", -8, z, Rot4.North); yield return new(z == 0 ? "Door" : "Wall", -2, z, Rot4.North); }
        for (int x = 2; x <= 8; x++) { yield return new("Wall", x, -6, Rot4.North); yield return new("Wall", x, 6, Rot4.North); }
        for (int z = -5; z <= 5; z++) { yield return new(z == 0 ? "Door" : "Wall", 2, z, Rot4.North); yield return new("Wall", 8, z, Rot4.North); }
        for (int i = 0; i < camp.Capacity; i++) yield return new("Bed", -7 + i % 3 * 2, -5 + i / 3 * 3, Rot4.North);
        // The table covers x 5..6, z -3..-2; chairs sit flush against it, facing it, so meals count as eaten at a table.
        yield return new("Table2x2c", 5, -3, Rot4.North);
        yield return new("DiningChair", 5, -4, Rot4.North);
        yield return new("DiningChair", 6, -4, Rot4.North);
        yield return new("DiningChair", 5, -1, Rot4.South);
        yield return new("DiningChair", 6, -1, Rot4.South);
        // 2×2 mortars in the open aisle (only the shelters are roofed). Crew stand on the cell south of each gun;
        // the x = 1 column and the rows between guns stay free as walkways to the depot and doors.
        (int X, int Z)[] mortarSpots = { (-1, 6), (-1, -6), (-1, 3), (-1, -3) };
        for (int i = 0; i < camp.MortarCount && i < mortarSpots.Length; i++) yield return new("Turret_Mortar", mortarSpots[i].X, mortarSpots[i].Z, Rot4.North);
        // Engineer works sit just outside the footprint: a ring one cell out with gaps on the axes, turrets on the corners,
        // IEDs flanking the gaps (clear of the pen at z -11 and the mech bay at z +11), autocannons east and west.
        var s = camp.S;
        if (s.sandbags)
        {
            string wall = s.barricades ? "Barricade" : "Sandbags";
            // An axis gap stays an open walkway only where it faces rock or a wall. A gap onto open ground is filled too
            // (sandbags and barricades can still be crossed), so the post behind it is not left bare.
            for (int x = -9; x <= 9; x++)
            {
                if (x != 0 || FacesOpenGround(camp, 0, -8, 0, -1)) yield return new(wall, x, -8, Rot4.North);
                if (x != 0 || FacesOpenGround(camp, 0, 8, 0, 1)) yield return new(wall, x, 8, Rot4.North);
            }
            for (int z = -7; z <= 7; z++)
            {
                if (z != 0 || FacesOpenGround(camp, -10, 0, -1, 0)) yield return new(wall, -10, z, Rot4.North);
                if (z != 0 || FacesOpenGround(camp, 10, 0, 1, 0)) yield return new(wall, 10, z, Rot4.North);
            }
        }
        foreach (var (x, z) in IedSpots.Take(s.iedCount)) yield return new("TrapIED_HighExplosive", x, z, Rot4.North);
        (int X, int Z)[] corners = { (10, 8), (-10, -8), (-10, 8), (10, -8) };
        for (int i = 0; i < s.miniTurrets && i < corners.Length; i++) yield return new("Turret_MiniTurret", corners[i].X, corners[i].Z, Rot4.North);
        (int X, int Z)[] cannons = { (12, 2), (-13, 2) }; // 2×2, north of the east/west IED pairs
        for (int i = 0; i < s.autocannons && i < cannons.Length; i++) yield return new("Turret_Autocannon", cannons[i].X, cannons[i].Z, Rot4.North);
    }
    // True when the three cells beyond a ring gap are walkable, i.e. enemies can come through it.
    private static bool FacesOpenGround(MapComponent_Camp camp, int x, int z, int dx, int dz)
    {
        for (int k = 1; k <= 3; k++)
        {
            IntVec3 c = camp.center + new IntVec3(x + dx * k, 0, z + dz * k);
            if (!c.InBounds(camp.map) || c.Impassable(camp.map)) return false;
        }
        return true;
    }
    // Power conduits (the hidden conduit is fully transparent) run under walls and furniture. No camp facility transmits power,
    // so GenSpawn never wipes them and the player's power line keeps working under the camp.
    public static bool LiesUnderFacilities(Thing t) => t.def.building?.isPowerConduit == true;
    // Outside the validated footprint nothing is cleared except grass: a blocked cell is skipped and retried on the next build.
    private static bool ExtraCellsFree(Map map, CellRect rect, ThingDef def)
    {
        foreach (IntVec3 c in rect)
        {
            if (!c.InBounds(map) || c.CloseToEdge(map, 2)) return false;
            if (def.terrainAffordanceNeeded != null && !c.GetTerrain(map).affordances.Contains(def.terrainAffordanceNeeded)) return false;
            if (c.GetThingList(map).Any(t => !(t is Plant p && !p.def.plant.IsTree) && t is not Filth && !LiesUnderFacilities(t))) return false;
        }
        return true;
    }
    public static ThingDef StuffFor(ThingDef def) => def == null || !def.MadeFromStuff ? null
        : GenStuff.AllowedStuffsFor(def).Contains(ThingDefOf.WoodLog) ? ThingDefOf.WoodLog : GenStuff.DefaultStuffFor(def);
    private static string Key(Slot s) => $"{s.Def}:{s.X}:{s.Z}";
    private static Thing FacilityAt(MapComponent_Camp camp, Slot s) =>
        camp.facilities.FirstOrDefault(t => t != null && t.Spawned && t.Position == camp.center + new IntVec3(s.X, 0, s.Z) && t.def.defName == s.Def);
    private static bool Present(MapComponent_Camp camp, Slot s) => FacilityAt(camp, s) != null;
    public static string Validate(MapComponent_Camp camp, bool initial)
    {
        if (camp.depot == null || !camp.depot.Spawned) return "용병 보급소를 먼저 건설하세요.";
        // Free plans are checked cell by cell as they are placed; a blocked one simply waits for its cells to clear.
        if (camp.freeLayout) return null;
        return ValidateSite(camp.map, camp.depot.Position, camp.depot, camp.facilities, initial);
    }
    // A free layout's living area: the depot plus every bed, table, chair, wall, door and the rally point, with a one-cell walkway.
    public static CellRect FreeFootprint(MapComponent_Camp camp)
    {
        CellRect rect = GenAdj.OccupiedRect(camp.center, Rot4.North, CampDefOf.GJL_MC_ContractDepot.size);
        foreach (PlanSlot p in camp.plan.Where(p => CampPlan.IsLiving(p.kind))) rect = rect.Encapsulate(CampPlan.RectOf(camp, p));
        return rect.ExpandedBy(1).ClipInsideMap(camp.map);
    }
    public static string ValidateSite(Map map, IntVec3 center, Thing ignore, List<Thing> existing, bool wholeSite)
    {
        foreach (IntVec3 c in FootprintAt(center))
            if (CellError(map, c, ignore, existing) is string error) return error;
        return null;
    }
    // The placement ghost marks these in red, so a blocker that is hard to see on the map can still be found.
    public static IEnumerable<IntVec3> BlockedCells(Map map, IntVec3 center, Thing ignore, List<Thing> existing) =>
        FootprintAt(center).Where(c => c.InBounds(map) && CellError(map, c, ignore, existing) != null);
    private static string CellError(Map map, IntVec3 c, Thing ignore, List<Thing> existing)
    {
        if (!c.InBounds(map) || c.CloseToEdge(map, 5)) return "캠프 부지는 맵 경계에서 5칸 이상 떨어져야 합니다.";
        if (!c.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy)) return "19×15칸 전체에 건설 가능한 단단한 지면이 필요합니다.";
        foreach (Thing t in c.GetThingList(map))
        {
            if (t == ignore || existing?.Contains(t) == true || t is Pawn || t.def.category == ThingCategory.Item || t is Filth || LiesUnderFacilities(t)) continue;
            if (t is Plant plant && !plant.def.plant.IsTree) continue;
            if (t is Building || t is Blueprint || t is Frame || t.def.passability == Traversability.Impassable)
                return $"캠프 부지에 {t.LabelNoCount}이(가) 있습니다. 빨간 칸의 건물·바위·설계도를 먼저 치워주세요. 기존 시설은 자동 삭제하지 않습니다.";
        }
        return null;
    }
    // setup: a free layout's never-built plans go up at once (contract start, IED re-arm); otherwise the crew builds them.
    public static void Build(MapComponent_Camp camp, bool restore = false, System.Func<Slot, bool> only = null, bool setup = false)
    {
        var slots = Slots(camp).ToList();
        // Saves from older layouts keep furniture at slots that no longer exist (the old chair spots); retire only company-owned leftovers.
        // A work the plan dropped was retired, not destroyed, so its slot is forgotten and planning it again builds it for free.
        var wanted = slots.Select(s => (s.Def, camp.center + new IntVec3(s.X, 0, s.Z))).ToHashSet();
        if (only == null)
            foreach (Thing stale in camp.facilities.Where(t => t != null && t.Spawned && t.Faction == camp.company && !wanted.Contains((t.def.defName, t.Position))).ToList())
            {
                IntVec3 o = stale.Position - camp.center;
                camp.builtSlots.Remove($"{stale.def.defName}:{o.x}:{o.z}"); camp.facilities.Remove(stale); stale.Destroy();
            }
        foreach (var slot in slots.Where(s => only == null || only(s)))
        {
            if (Present(camp, slot)) continue;
            bool built = camp.builtSlots.Contains(Key(slot));
            if (built ? !restore : camp.freeLayout && !setup) continue;
            TrySpawn(camp, slot);
        }
        if (camp.freeLayout) RoofCampRooms(camp);
        else
        {
            // Six-cell-wide shelters are fully supported by their walls.
            foreach (IntVec3 c in camp.Footprint)
                if ((c.x >= camp.center.x - 8 && c.x <= camp.center.x - 2 || c.x >= camp.center.x + 2 && c.x <= camp.center.x + 8) && c.z >= camp.center.z - 6 && c.z <= camp.center.z + 6)
                    camp.map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
        }
        // A free plan can turn a bed medical or back without moving it.
        foreach (Slot slot in slots.Where(s => s.Def == "Bed"))
            if (FacilityAt(camp, slot) is Building_Bed bed && bed.Medical != slot.Medical) bed.Medical = slot.Medical;
        camp.facilities.RemoveAll(t => t == null || t.Destroyed);
        camp.PendingChanged();
    }
    public static bool CanSpawn(MapComponent_Camp camp, ThingDef def, CellRect rect)
    {
        if (rect.Any(c => !c.InBounds(camp.map) || c.GetEdifice(camp.map) != null)) return false;
        // Inside the validated standard site anything but a building is fine; everywhere else only grass and filth may be in the way.
        return !camp.freeLayout && rect.All(camp.Footprint.Contains) || ExtraCellsFree(camp.map, rect, def);
    }
    private static Thing TrySpawn(MapComponent_Camp camp, Slot slot)
    {
        IntVec3 cell = camp.center + new IntVec3(slot.X, 0, slot.Z);
        ThingDef def = DefDatabase<ThingDef>.GetNamed(slot.Def);
        CellRect rect = GenAdj.OccupiedRect(cell, slot.Rotation, def.size);
        if (!CanSpawn(camp, def, rect)) return null;
        // Only grass is cleared, after validation; never destroy a player structure.
        foreach (var c in rect)
            foreach (Plant p in c.GetThingList(camp.map).OfType<Plant>().Where(p => !p.def.plant.IsTree).ToList()) p.Destroy();
        Thing thing = ThingMaker.MakeThing(def, StuffFor(def));
        thing.SetFaction(camp.company); GenSpawn.Spawn(thing, cell, camp.map, slot.Rotation);
        camp.facilities.Add(thing);
        if (!camp.builtSlots.Contains(Key(slot))) camp.builtSlots.Add(Key(slot));
        if (thing is Building_Bed bed) bed.Medical = slot.Medical;
        return thing;
    }
    // Planned but never built: the crew's construction list. Destroyed works are not here; they wait for a paid repair.
    public static IEnumerable<Slot> PendingSlots(MapComponent_Camp camp) =>
        camp.freeLayout ? Slots(camp).Where(s => !camp.builtSlots.Contains(Key(s)) && !Present(camp, s)) : Enumerable.Empty<Slot>();
    public static Slot PendingSlotAt(MapComponent_Camp camp, IntVec3 cell) =>
        PendingSlots(camp).FirstOrDefault(s => camp.center + new IntVec3(s.X, 0, s.Z) == cell);
    public static CellRect RectOf(MapComponent_Camp camp, Slot s) =>
        GenAdj.OccupiedRect(camp.center + new IntVec3(s.X, 0, s.Z), s.Rotation, DefDatabase<ThingDef>.GetNamed(s.Def).size);
    public static bool Blocked(MapComponent_Camp camp, Slot s) => !CanSpawn(camp, DefDatabase<ThingDef>.GetNamed(s.Def), RectOf(camp, s));
    // Half the vanilla work at most CrewWorkCap, so a wall goes up in seconds and an autocannon in under an hour.
    public static float CrewWork(ThingDef def) =>
        Mathf.Min(CampRules.CrewWorkCap, Mathf.Max(60, def.GetStatValueAbstract(StatDefOf.WorkToBuild, StuffFor(def)) * CampRules.CrewWorkFactor));
    public static Thing CompleteSlot(MapComponent_Camp camp, IntVec3 cell)
    {
        if (PendingSlotAt(camp, cell) is not { } slot || TrySpawn(camp, slot) is not { } thing) return null;
        if (thing is Building_Bed) camp.AssignBeds();
        if (thing.def.passability == Traversability.Impassable || thing is Building_Door || thing is Building_Bed) RoofCampRooms(camp); // May have closed a room.
        camp.PendingChanged();
        return thing;
    }
    // Free layout: a room the camp walled in (its own walls and doors, or natural rock) around a camp bed gets a roof, like a
    // colonist-built room does. A bed planned inside a colony building or courtyard never roofs it.
    public static void RoofCampRooms(MapComponent_Camp camp)
    {
        var done = new HashSet<Room>();
        foreach (Building_Bed bed in camp.facilities.OfType<Building_Bed>().Where(b => b.Spawned).ToList())
        {
            Room room = bed.GetRoom();
            if (room == null || !done.Add(room) || room.TouchesMapEdge || room.IsHuge || room.CellCount > 300) continue;
            if (!room.BorderCells.All(c => c.InBounds(camp.map) && c.GetEdifice(camp.map) is { } e && (camp.facilities.Contains(e) || e.def.building?.isNaturalRock == true))) continue;
            foreach (IntVec3 c in room.Cells)
                if (!c.Roofed(camp.map) && !camp.map.areaManager.NoRoof[c] && RoofCollapseUtility.WithinRangeOfRoofHolder(c, camp.map, true))
                    camp.map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
        }
    }
    // Paid repair covers what was built and then destroyed. Spent IEDs re-arm for free; never-built (blocked) cells are retried free.
    public static int MissingCount(MapComponent_Camp camp) => Slots(camp).Count(s => !s.Def.StartsWith("TrapIED") && camp.builtSlots.Contains(Key(s)) && !Present(camp, s));
    public static bool Repair(MapComponent_Camp camp, out string error)
    {
        error = Validate(camp, false); if (error != null) return false;
        int count = MissingCount(camp); if (count == 0) { error = "파괴된 시설이 없습니다. 손상된 시설은 용병이 수리합니다."; return false; }
        if (!camp.Spend(new() { [ThingDefOf.WoodLog] = count * 25, [ThingDefOf.Steel] = count * 5 })) { error = $"복구에 목재 {count * 25}, 강철 {count * 5}이 필요합니다."; return false; }
        Build(camp, true); camp.AssignBeds(); return true;
    }
}
public class PlaceWorker_Camp : PlaceWorker
{
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
    {
        var camp = map.GetComponent<MapComponent_Camp>();
        if (Find.Maps.Any(m => m != map && (m.GetComponent<MapComponent_Camp>().Active || m.GetComponent<MapComponent_Camp>().depot?.Spawned == true))) return "첫 버전은 한 정착지의 캠프 하나만 지원합니다.";
        if (camp.depot?.Spawned == true && camp.depot != thingToIgnore && camp.depot != thing) return "이 맵에 보급소가 이미 있습니다.";
        if (camp.center.IsValid && loc != camp.center) return "기존 캠프 자리에서만 보급소를 복구할 수 있습니다.";
        if (camp.freeLayout) return AcceptanceReport.WasAccepted; // A free layout keeps its own plan; only the depot cells matter.
        string error = CampLayout.ValidateSite(map, loc, thingToIgnore ?? thing, camp.facilities, true);
        return error == null ? AcceptanceReport.WasAccepted : error;
    }
    public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
    {
        Map map = Find.CurrentMap;
        if (map.GetComponent<MapComponent_Camp>().freeLayout) return;
        GenDraw.DrawFieldEdges(CampLayout.FootprintAt(center).ToList(), Color.cyan);
        var blocked = CampLayout.BlockedCells(map, center, thing, map.GetComponent<MapComponent_Camp>().facilities).ToList();
        if (blocked.Count > 0) GenDraw.DrawFieldEdges(blocked, Color.red);
    }
}
public class Building_ContractDepot : Building_Storage
{
    public MapComponent_Camp Camp => Map.GetComponent<MapComponent_Camp>();
    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad); var camp = map.GetComponent<MapComponent_Camp>();
        camp.depot = this;
        if (!camp.center.IsValid) camp.center = Position;
    }
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var g in base.GetGizmos()) yield return g;
        yield return new Command_Action { defaultLabel = "용병단 관리", defaultDesc = "계약, 활동 구역, 의뢰, 보급과 퍽을 관리합니다.", icon = TexCommand.DesirePower, action = () => Find.WindowStack.Add(new Dialog_Camp(Camp)) };
        if (CampPlan.Designators.Any(d => d.Visible))
            yield return new Command_Action
            {
                defaultLabel = "캠프 설계", defaultDesc = "침상·포대·방어선·경계 초소 등을 직접 배치합니다. 건축 메뉴의 '용병단' 탭과 같습니다.",
                icon = CampPlan.PlanIcon, action = () => Find.WindowStack.Add(new FloatMenu(CampPlan.Designators.Where(d => d.Visible)
                    .Select(d => new FloatMenuOption(d.LabelCap, () => d.ProcessInput(Event.current))).ToList()))
            };
    }
    public override string GetInspectString()
    {
        string text = base.GetInspectString() + $"\n용병 {Camp.Present.Count()}/{Camp.Capacity}명 · 계약 점수 {Camp.points}\n{Camp.reason}\n창고 완성식 {Camp.MealCount}개";
        if (Camp.freeLayout && Camp.PendingCount > 0) text += $"\n자유 설계: 시공 대기 {Camp.PendingCount}개" + (Camp.BlockedCount > 0 ? $" (막힘 {Camp.BlockedCount})" : "");
        return text;
    }
}
