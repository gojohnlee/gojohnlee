using System;

namespace MercenaryCamp;

public enum CampPhase { Uncontracted, Garrison, Fighting, Recovering, Leaving }
// Support is shown as 포병 (artillery); the name stays so existing saves keep their perks.
// Anomaly, Mech and Psychic need their DLC (Anomaly, Biotech, Royalty).
public enum PerkBranch { Force, Elite, Support, Anomaly, Engineer, Mech, Psychic, Recon }
public enum MercRole { Rifleman, Gunner, Psycaster, Sniper, Lancer }
// Free-layout plan entries. Post, Rally, Pen and Bay are markers only; the rest are facilities the crew builds.
public enum PlanKind { Bed, MedicalBed, Table, Chair, Wall, Door, Mortar, MiniTurret, Autocannon, Cover, IED, Post, Rally, Pen, Bay }

// No game dependency: the retreat latch and progression rules can be regression tested.
public static class CampRules
{
    public const int Day = 60000, Hour = 2500;
    public const int MaxTier = 5;
    public const int MissingSlotDays = 15;
    // Enemies this close to the camp footprint count as part of the camp's battle even outside the area.
    public const int CampGuardRadius = 8;
    // Mortar shells land up to 9 cells off target (vanilla forced miss), plus blast radius. Also used for area psycasts.
    public const float FriendlyFireRadius = 10;
    public const float BeastRecaptureDays = 3, MechRebuildDays = 3;
    // Released beasts are called back once the map is clear, or after this long regardless.
    public const int BeastMaxOutTicks = Day;
    // Free layout: everything within this many cells of the depot, so the camp stays one place the crew can walk.
    public const float PlanRadius = 30;
    // Crew construction runs at vanilla speed on this share of the facility's work, and never longer than the cap.
    public const float CrewWorkFactor = .5f, CrewWorkCap = 3000;
    public const int BaseIeds = 8;
    public static int PlanLimit(PlanKind kind) => kind switch
    {
        PlanKind.MedicalBed => 2, PlanKind.Table => 2, PlanKind.Chair => 8, PlanKind.Wall => 120, PlanKind.Door => 8,
        PlanKind.Cover => 80, PlanKind.Post => 16, _ => 1
    };
    public static float ShellCap(int mortars, float perDay) => mortars * perDay * 2;
    public static bool Retreat(int baseline, int ready, float lossFraction) =>
        baseline > 0 && baseline - ready >= Math.Max(1, (int)Math.Ceiling(baseline * lossFraction));
    // Every branch can go all the way, one tier at a time; the price (points or supplies) is the only limit.
    public static bool IsNextTier(int currentTier, int wantedTier) => wantedTier == currentTier + 1 && wantedTier <= MaxTier;
    public static bool CanBuy(int currentTier, int wantedTier, int points, int cost) => IsNextTier(currentTier, wantedTier) && points >= cost;
    // A doctrine sits beside the ladder: open once the branch reaches its tier; a branch may take all of its doctrines.
    public static bool CanTakeDoctrine(int branchTier, int requiredTier) => branchTier >= requiredTier;
    // Quests grow with the company: every 10 perks raise both the delivery and the reward by one step.
    public static int RewardScale(int perkCount) => 1 + perkCount / 10;
    public static int RefillDelay(float days, bool wipedOut) => (int)(Math.Max(.25f, days) * Day * (wipedOut ? 2 : 1));
    public static bool RecoveryComplete(bool anyActiveThreat, int now, int quietSince, int quietTicks) =>
        !anyActiveThreat && quietSince >= 0 && now - quietSince >= quietTicks;
}
