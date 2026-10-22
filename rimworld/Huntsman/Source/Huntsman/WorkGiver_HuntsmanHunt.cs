using RimWorld;
using Verse;
using Verse.AI;

namespace Huntsman;

// Use the game's reservation, hunting position, finishing and corpse hauling logic.
// The vanilla hunter ShouldSkip assumes an apparel tracker, which mechanoids lack.
public class WorkGiver_HuntsmanHunt : WorkGiver_HunterHunt
{
    private static bool IsHuntsman(Pawn pawn) => pawn.def.defName == "GJL_HM_Huntsman";

    public override bool ShouldSkip(Pawn pawn, bool forced = false)
    {
        return !IsHuntsman(pawn) || pawn.Drafted || pawn.equipment?.Primary == null ||
            !HasHuntingWeapon(pawn) ||
            !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Hunt);
    }

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        // Never turn this into a second work giver for colonists or other mech types.
        // Only designated animals; no wild humans, owned livestock or forbidden targets.
        if (ShouldSkip(pawn, forced) || t is not Pawn animal || !animal.RaceProps.Animal ||
            animal.Dead || animal.Faction == pawn.Faction || animal.IsForbidden(pawn) ||
            !pawn.CanReserveAndReach(animal, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced))
            return false;
        return base.HasJobOnThing(pawn, t, forced);
    }
}
