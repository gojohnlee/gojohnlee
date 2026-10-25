using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MercenaryCamp;

public class CampSettings : ModSettings
{
    public float refillDays = 3, graceDays = 3, retreatLoss = .4f, shellsPerMortarDay = 8;
    public bool debug;
    public override void ExposeData()
    {
        Scribe_Values.Look(ref refillDays, "refillDays", 3f);
        Scribe_Values.Look(ref graceDays, "graceDays", 3f);
        Scribe_Values.Look(ref retreatLoss, "retreatLoss", .4f);
        Scribe_Values.Look(ref shellsPerMortarDay, "shellsPerMortarDay", 8f);
        Scribe_Values.Look(ref debug, "debug");
    }
}
public class CampMod : Mod
{
    public static CampSettings Settings;
    public CampMod(ModContentPack pack) : base(pack)
    {
        Settings = GetSettings<CampSettings>();
        new Harmony("gojohnlee.mercenarycamp").PatchAll();
    }
    public override string SettingsCategory() => "자율 주둔 용병단";
    public override void DoSettingsWindowContents(Rect rect)
    {
        var list = new Listing_Standard(); list.Begin(rect);
        list.Label($"기본 충원 주기: {Settings.refillDays:F1}일 (포병·병력 퍽으로 단축)");
        Settings.refillDays = list.Slider(Settings.refillDays, 1, 10);
        list.Label($"보급 고갈 유예: {Settings.graceDays:F1}일");
        Settings.graceDays = list.Slider(Settings.graceDays, 1, 10);
        list.Label($"후퇴 기준: 전투 시작 병력의 {Settings.retreatLoss:P0} 손실·다운·이탈");
        Settings.retreatLoss = list.Slider(Settings.retreatLoss, .25f, .75f);
        list.Label($"계약 포탄: 박격포 1문당 하루 {Settings.shellsPerMortarDay:F0}발 (최대 2일치 비축)");
        Settings.shellsPerMortarDay = Mathf.Round(list.Slider(Settings.shellsPerMortarDay, 2, 20));
        list.CheckboxLabeled("상태 전환 디버그 로그", ref Settings.debug); list.End();
    }
}
public class CampPerkDef : Def
{
    public PerkBranch branch;
    public int tier, cost, capacity, shooting, mortars, lancers, snipers, psycasters, psylink, miniTurrets, autocannons;
    public float refillDays, refillFactor = 1, shellFactor = 1, recaptureDays, raidWarningHours, ambushRange, raidPointsFactor = 1;
    public bool leader, freeRefill, incendiary, artilleryAnywhere, sandbags, barricades, ieds, raidAlert;
    public ThingDef weapon, armor, helmet;
    // Anomaly / Mech branches: the full roster at this tier, one entry per pawn.
    public List<PawnKindDef> beasts = new(), mechs = new();
    public List<AbilityDef> psycasts = new();
    // Paid from colony stock for an instant purchase, instead of contract points.
    public List<ThingDefCountClass> price = new();
    // The colony must have finished these (mod tab or vanilla) before the perk can be bought.
    public List<ResearchProjectDef> researchPrerequisites = new();
    // Doctrines sit beside the ladder, not on it: available once the branch reaches `tier`, and a branch may take all of them.
    public bool doctrine;
    // memberHediff goes on mercenaries (only `roles`, if set; only while on a post, if atPostOnly); squadHediff on released beasts and mechs.
    public HediffDef memberHediff, squadHediff;
    public List<MercRole> roles;
    public bool atPostOnly;
    // Doctrine bonuses stack on top of the ladder values.
    public int capacityBonus, iedBonus;
    public float shellMultiplier = 1, raidWarningBonus, raidPointsMultiplier = 1, recaptureFactor = 1, rebuildFactor = 1;
    public bool empShells, autoRepair, markTargets;
    // Perk tree icon; without one the tree falls back to the perk's weapon, squad or psycast.
    public ThingDef iconThing;
    public PawnKindDef iconKind;
    public AbilityDef iconAbility;
    public Dictionary<ThingDef, int> PriceTable => price.ToDictionary(c => c.thingDef, c => c.count);
    public IEnumerable<ResearchProjectDef> MissingResearch => researchPrerequisites.Where(r => r != null && !r.IsFinished);
}
// Everything the selected perks add up to, computed once per change instead of on every tick query.
public class PerkSummary
{
    public readonly Dictionary<PerkBranch, int> tiers = new();
    public int capacity = 4, mortars, lancers, snipers, psycasters, psylink, miniTurrets, autocannons, iedCount;
    public float refillDays = float.MaxValue, refillFactor = 1, shellFactor = 1, recaptureDays = CampRules.BeastRecaptureDays, raidWarningHours, ambushRange, raidPointsFactor = 1;
    public float mechRebuildDays = CampRules.MechRebuildDays;
    public bool leader, freeRefill, incendiary, artilleryAnywhere, sandbags, barricades, ieds, raidAlert, empShells, autoRepair, markTargets;
    public CampPerkDef elite;
    public List<PawnKindDef> beasts = new(), mechs = new();
    public List<AbilityDef> psycasts = new();
    // Perks that put a hediff on mercenaries or on released beasts and mechs.
    public readonly List<CampPerkDef> memberHediffs = new(), squadHediffs = new();
    private float ladderRaidPoints = 1; // Ladder intel takes the best tier; doctrine intel multiplies on top.
    public PerkSummary(IEnumerable<CampPerkDef> perks)
    {
        int capacityBonus = 0, iedBonus = 0;
        float shellMultiplier = 1, raidWarningBonus = 0, recaptureFactor = 1;
        foreach (var p in perks)
        {
            if (p.memberHediff != null) memberHediffs.Add(p);
            if (p.squadHediff != null) squadHediffs.Add(p);
            capacityBonus += p.capacityBonus; iedBonus += p.iedBonus; shellMultiplier *= p.shellMultiplier; raidWarningBonus += p.raidWarningBonus;
            raidPointsFactor *= p.raidPointsMultiplier; recaptureFactor *= p.recaptureFactor; mechRebuildDays *= p.rebuildFactor;
            empShells |= p.empShells; autoRepair |= p.autoRepair; markTargets |= p.markTargets;
            if (p.doctrine) continue; // Doctrines never raise the branch tier.
            tiers[p.branch] = Math.Max(tiers.TryGetValue(p.branch, out int t) ? t : 0, p.tier);
            capacity = Math.Max(capacity, p.capacity); mortars = Math.Max(mortars, p.mortars); lancers = Math.Max(lancers, p.lancers);
            snipers = Math.Max(snipers, p.snipers); psycasters = Math.Max(psycasters, p.psycasters); psylink = Math.Max(psylink, p.psylink);
            miniTurrets = Math.Max(miniTurrets, p.miniTurrets); autocannons = Math.Max(autocannons, p.autocannons);
            if (p.refillDays > 0) refillDays = Math.Min(refillDays, p.refillDays);
            refillFactor = Math.Min(refillFactor, p.refillFactor); shellFactor = Math.Max(shellFactor, p.shellFactor);
            if (p.recaptureDays > 0) recaptureDays = Math.Min(recaptureDays, p.recaptureDays);
            raidWarningHours = Math.Max(raidWarningHours, p.raidWarningHours); ambushRange = Math.Max(ambushRange, p.ambushRange);
            ladderRaidPoints = Math.Min(ladderRaidPoints, p.raidPointsFactor);
            leader |= p.leader; freeRefill |= p.freeRefill; incendiary |= p.incendiary; artilleryAnywhere |= p.artilleryAnywhere;
            sandbags |= p.sandbags; barricades |= p.barricades; ieds |= p.ieds; raidAlert |= p.raidAlert;
            if (p.branch == PerkBranch.Elite && (elite == null || p.tier > elite.tier)) elite = p;
            if (p.branch == PerkBranch.Anomaly && p.tier >= Tier(PerkBranch.Anomaly)) beasts = p.beasts;
            if (p.branch == PerkBranch.Mech && p.tier >= Tier(PerkBranch.Mech)) mechs = p.mechs;
            foreach (var a in p.psycasts) if (!psycasts.Contains(a)) psycasts.Add(a);
        }
        capacity += capacityBonus; shellFactor *= shellMultiplier; recaptureDays *= recaptureFactor; raidPointsFactor *= ladderRaidPoints;
        if (raidWarningHours > 0) raidWarningHours += raidWarningBonus;
        iedCount = ieds ? CampRules.BaseIeds + iedBonus : 0;
        if (!ModsConfig.AnomalyActive) beasts = new();
        if (!ModsConfig.BiotechActive) mechs = new();
        if (!ModsConfig.RoyaltyActive) { psycasters = 0; psycasts = new(); }
    }
    public int Tier(PerkBranch b) => tiers.TryGetValue(b, out int t) ? t : 0;
    public static bool Available(PerkBranch b) => b switch
    {
        PerkBranch.Anomaly => ModsConfig.AnomalyActive, PerkBranch.Mech => ModsConfig.BiotechActive, PerkBranch.Psychic => ModsConfig.RoyaltyActive, _ => true
    };
    public static string Label(PerkBranch b) => b switch
    {
        PerkBranch.Force => "병력", PerkBranch.Elite => "정예", PerkBranch.Support => "포병", PerkBranch.Anomaly => "변칙",
        PerkBranch.Engineer => "공병", PerkBranch.Mech => "메카", PerkBranch.Psychic => "사이킥", _ => "정찰"
    };
}
[DefOf]
public static class CampDefOf
{
    public static ThingDef GJL_MC_ContractDepot, Turret_Mortar, Shell_HighExplosive, Shell_Incendiary, Shell_EMP, Gun_BoltActionRifle, Gun_SniperRifle, Gun_ChargeLance;
    public static FactionDef GJL_MC_Company;
    public static PawnKindDef GJL_MC_Rifleman;
    public static JobDef GJL_MC_Relax, GJL_MC_Rescue, GJL_MC_Clean, GJL_MC_Train, GJL_MC_Build;
    public static DutyDef GJL_MC_MechSquad;
    public static HediffDef GJL_MC_Leadership, GJL_MC_Marked;
    public static ResearchProjectDef GJL_MC_Res_CampPlanning;
    public static QuestScriptDef GJL_MC_ContractQuest;
    public static ThinkTreeDef GJL_MC_CampBehavior;
    static CampDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(CampDefOf));
}

// Camp jobs are saved against the pawn's Humanlike tree, which can't see insertTag subtrees, so every load warned
// "Could not find think node"; fall back to the camp subtree, whose keys are stable per defName.
[HarmonyPatch(typeof(ThinkTreeDef), nameof(ThinkTreeDef.TryGetThinkNodeWithSaveKey))]
public static class CampJobGiverOnLoad
{
    public static void Postfix(int key, ref ThinkNode outNode, ref bool __result)
    {
        if (__result || key == -1) return;
        outNode = CampDefOf.GJL_MC_CampBehavior.thinkRoot.ThisAndChildrenRecursive.FirstOrDefault(n => n.UniqueSaveKey == key);
        __result = outNode != null;
    }
}

// Contract discipline: an insult between mercs (or with a colonist) stays words; a squad that brawls every day loses its beds to injuries.
[HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.SocialFightChance))]
public static class CampNoSocialFights
{
    public static void Postfix(Pawn ___pawn, Pawn initiator, ref float __result)
    { if (__result > 0 && (CampMortar.IsCompany(___pawn) || CampMortar.IsCompany(initiator))) __result = 0; }
}

// Camp-owned facilities must not become a source of free furniture.
[HarmonyPatch(typeof(Designator_Claim), nameof(Designator_Claim.CanDesignateThing))]
public static class ProtectCampClaim
{
    public static void Postfix(Thing t, ref AcceptanceReport __result)
    { if (t.Map?.GetComponent<MapComponent_Camp>().IsFacility(t) == true) __result = "용병 전용 시설입니다. 보급소에서 계약을 관리하세요."; }
}
[HarmonyPatch(typeof(Designator_Deconstruct), nameof(Designator_Deconstruct.CanDesignateThing))]
public static class ProtectCampDeconstruct
{
    public static void Postfix(Thing t, ref AcceptanceReport __result)
    { if (t.Map?.GetComponent<MapComponent_Camp>().IsFacility(t) == true) __result = "용병 전용 시설입니다. 보급소에서 계약을 관리하세요."; }
}

// Camp turrets: the camp picks targets (engagement zone only, mortars also keep clear of friendlies), contract shells and
// barrels are virtual, and the contract supplies power and maintenance, so the camp never draws on colony stock or grid.
public static class CampMortar
{
    public static MapComponent_Camp CampOf(Thing b) => b?.Map?.GetComponent<MapComponent_Camp>() is { } c && c.IsFacility(b) ? c : null;
    // Only camp facilities belong to the hidden company faction, so this is a cheap filter for hot paths.
    public static bool IsCompany(Thing t) => t?.Faction is { } f && f.def == CampDefOf.GJL_MC_Company;
}
[HarmonyPatch(typeof(Building_TurretGun), nameof(Building_TurretGun.TryFindNewTarget))]
public static class CampMortarTargeting
{
    public static void Postfix(Building_TurretGun __instance, ref LocalTargetInfo __result)
    {
        if (CampMortar.CampOf(__instance) is not { } c) return;
        __result = __instance.def == CampDefOf.Turret_Mortar ? c.MortarTarget(__instance) : c.TurretTarget(__instance, __result);
    }
}
[HarmonyPatch(typeof(JobDriver_ManTurret), "GunNeedsLoading")]
public static class CampMortarLoading
{
    public static bool Prefix(Building b, ref bool __result)
    {
        if (b is not Building_TurretGun gun || CampMortar.CampOf(b) is not { } c) return true;
        var shell = gun.gun.TryGetComp<CompChangeableProjectile>();
        if (shell != null && !shell.Loaded && c.TakeShell()) shell.LoadShell(c.NextShellDef(), 1);
        __result = shell != null && !shell.Loaded; // Still empty: the driver looks for ammo, finds none and ends the job.
        return false;
    }
}
[HarmonyPatch(typeof(JobDriver_ManTurret), "GunNeedsRefueling")]
public static class CampMortarBarrel
{
    public static bool Prefix(Building b, ref bool __result)
    {
        if (CampMortar.CampOf(b) == null) return true;
        var barrel = b.TryGetComp<CompRefuelable>();
        if (barrel != null && !barrel.IsFull) barrel.Refuel(barrel.TargetFuelLevel - barrel.Fuel);
        __result = false;
        return false;
    }
}
[HarmonyPatch(typeof(JobDriver_ManTurret), nameof(JobDriver_ManTurret.FindAmmoForTurret))]
public static class CampMortarNoHauledAmmo
{
    public static bool Prefix(Building_TurretGun gun, ref Thing __result) { if (CampMortar.CampOf(gun) == null) return true; __result = null; return false; }
}
[HarmonyPatch(typeof(JobDriver_ManTurret), nameof(JobDriver_ManTurret.FindFuelForTurret))]
public static class CampMortarNoHauledFuel
{
    public static bool Prefix(Building_TurretGun gun, ref Thing __result) { if (CampMortar.CampOf(gun) == null) return true; __result = null; return false; }
}
[HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn), MethodType.Getter)]
public static class CampTurretPower
{
    public static void Postfix(CompPowerTrader __instance, ref bool __result) { if (!__result && CampMortar.IsCompany(__instance.parent)) __result = true; }
}
[HarmonyPatch(typeof(CompBreakdownable), nameof(CompBreakdownable.DoBreakdown))]
public static class CampTurretNoBreakdown
{
    public static bool Prefix(CompBreakdownable __instance) => !CampMortar.IsCompany(__instance.parent);
}

// Recon: scouts turn a hostile raid into a warning plus the same raid a few hours later, and intel shrinks it on arrival.
[HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
public static class CampRaidScouting
{
    public static bool Prefix(IncidentWorker_RaidEnemy __instance, IncidentParms parms, ref bool __result)
    {
        if (parms.target is not Map map || map.GetComponent<MapComponent_Camp>() is not { Active: true } c) return true;
        int now = Find.TickManager.TicksGame;
        if (c.S.raidWarningHours > 0 && parms.quest == null && !c.ConsumeScoutedRaid(now))
        {
            int fireTick = now + (int)(c.S.raidWarningHours * CampRules.Hour);
            Find.Storyteller.incidentQueue.Add(__instance.def, fireTick, parms);
            c.scoutedRaidTicks.Add(fireTick);
            string who = parms.faction != null ? parms.faction.Name + " 세력의 " : "";
            Find.LetterStack.ReceiveLetter("정찰 경보", $"용병단 정찰병이 {who}습격 준비를 포착했습니다. 약 {c.S.raidWarningHours:F0}시간 뒤 도착합니다.", LetterDefOf.ThreatBig, new LookTargets(c.depot));
            __result = true;
            return false;
        }
        if (c.S.raidPointsFactor < 1) parms.points *= c.S.raidPointsFactor;
        return true;
    }
}
