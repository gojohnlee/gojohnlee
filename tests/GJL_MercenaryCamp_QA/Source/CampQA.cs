using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MercenaryCamp;
using RimWorld;
using Verse;
using Verse.AI;

// The test wipes the area around the map centre, so it only runs when the game is launched with -campqa.
public class CampQA : Mod
{
    public static bool Enabled => GenCommandLine.CommandLineArgPassed("campqa");
    // -campqaload=<save>: load a save made by an older build and check the layout migration instead of the full run.
    public static string MigrationSave => GenCommandLine.TryGetCommandLineArg("campqaload", out string name) ? name : null;
    public static string LogPath => Path.Combine(GenFilePaths.SaveDataFolderPath,"Camp-QA.log");
    public static Dictionary<string,int> Jobs = new();
    public static int Passed, Failed, MortarShots, TurretShots;
    public static float RaidPoints = -1;
    public static Dictionary<string,int> Casts = new();
    public static void Write(string s) { File.AppendAllText(LogPath,DateTime.UtcNow.ToString("O")+" "+s+Environment.NewLine); }
    public CampQA(ModContentPack p):base(p) { if(!Enabled)return; new Harmony("gojohnlee.mercenarycamp.qa").PatchAll(); Write("BOOT"); }
}
[HarmonyPatch(typeof(Log),nameof(Log.Error),new[]{typeof(string)})]
public static class ErrorCapture { public static void Prefix(string text) => CampQA.Write("ERROR "+text); }
[HarmonyPatch(typeof(Log),nameof(Log.Message),new[]{typeof(string)})]
public static class MessageCapture { public static void Prefix(string text) { if(text!=null&&text.StartsWith("[MercenaryCamp]"))CampQA.Write("DEBUG "+text); } }
[HarmonyPatch(typeof(MapComponent_Camp),nameof(MapComponent_Camp.ChangePhase))]
public static class PhaseCapture
{
    public static void Prefix(MapComponent_Camp __instance,CampPhase next,string why)
    {
        if(__instance.phase==next)return;
        var threats=__instance.map.mapPawns.AllPawnsSpawned.Where(__instance.ActiveThreat).Select(p=>$"{p.LabelShort}@{p.Position}{(__instance.Inside(p.Position)?"(in)":"")}");
        CampQA.Write($"PHASE {__instance.phase}->{next} t={Find.TickManager.TicksGame} {why} threats=[{string.Join(", ",threats)}]");
    }
}
[HarmonyPatch(typeof(Building_TurretGun),"BeginBurst")]
public static class MortarShotCapture
{
    public static void Prefix(Building_TurretGun __instance)
    { if(__instance.def==ThingDefOf.Turret_Mortar)CampQA.MortarShots++; else if(MercenaryCamp.CampMortar.IsCompany(__instance))CampQA.TurretShots++; }
}
[HarmonyPatch(typeof(Ability),nameof(Ability.Activate),new[]{typeof(LocalTargetInfo),typeof(LocalTargetInfo)})]
public static class PsycastCapture
{
    public static void Postfix(Ability __instance,bool __result)
    {
        if(__instance.pawn?.kindDef?.defName!="GJL_MC_Rifleman")return;
        string k=(__result?"OK ":"NO ")+__instance.def.defName; CampQA.Casts[k]=CampQA.Casts.GetValueOrDefault(k)+1;
    }
}
// Runs after the camp's own prefix, so it sees the raid points as they reach the vanilla worker.
[HarmonyPatch(typeof(IncidentWorker_RaidEnemy),"TryExecuteWorker")]
public static class RaidPointsCapture
{
    [HarmonyPriority(Priority.Last)]
    public static void Prefix(IncidentParms parms) => CampQA.RaidPoints=parms.points;
}
[HarmonyPatch(typeof(Pawn_JobTracker),nameof(Pawn_JobTracker.StartJob))]
public static class JobCapture
{
    public static void Prefix(Pawn ___pawn,Job newJob)
    { if (___pawn.kindDef?.defName=="GJL_MC_Rifleman" && newJob!=null) { string n=newJob.def.defName; CampQA.Jobs[n]=CampQA.Jobs.GetValueOrDefault(n)+1; } }
}
[StaticConstructorOnStartup]
public static class DefinitionCheck
{
    static DefinitionCheck()
    {
        if(!CampQA.Enabled)return;
        CampMod.Settings.debug=true;
        foreach(var m in Log.Messages) if(m.type==LogMessageType.Error) CampQA.Write("LOAD_ERROR "+m.text);
        foreach(var d in LoadedModManager.GetMod<CampMod>().Content.AllDefs)
            foreach(string e in d.ConfigErrors())CampQA.Write("CONFIG_ERROR "+d.defName+" "+e);
        CampQA.Write($"DEFS perks={DefDatabase<CampPerkDef>.DefCount} doctrines={DefDatabase<CampPerkDef>.AllDefs.Count(p=>p.doctrine)} research={DefDatabase<ResearchProjectDef>.AllDefs.Count(r=>r.tab?.defName=="GJL_MC_ResearchTab")} planTools={CampPlan.Designators.Count()}");
        CampQA.Write("DEFS_READY");
        if(CampQA.MigrationSave!=null) LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame(CampQA.MigrationSave));
    }
}
public class CampTest : GameComponent
{
    private Map map; private MapComponent_Camp c; private int stage,tick; private Pawn enemy; private float foodBefore;
    private Pawn eater,sleeper; private int expectedPoints,expectedMembers,expectedReceipts; private bool loaded; private List<Thing> conduits;
    public CampTest(Game g){}
    public void Check(string name,bool ok) { if(ok)CampQA.Passed++;else CampQA.Failed++; CampQA.Write((ok?"PASS ":"FAIL ")+name); }
    public override void ExposeData() { Scribe_Values.Look(ref expectedPoints,"expectedPoints");Scribe_Values.Look(ref expectedMembers,"expectedMembers");Scribe_Values.Look(ref expectedReceipts,"expectedReceipts"); }
    public override void LoadedGame()
    {
        if(!CampQA.Enabled)return;
        c=Find.Maps.Select(m=>m.GetComponent<MapComponent_Camp>()).FirstOrDefault(x=>x.everContracted);
        if(c==null){CampQA.Write("LOAD_SKIPPED no contracted camp");stage=99;return;}
        map=c.map;
        if(CampQA.MigrationSave!=null)
        {
            CampQA.Write($"MIGRATION phase={c.phase} company={c.company?.Name ?? "null"} members={c.members.Count} present={c.Present.Count()} memberFactions=[{string.Join(", ",c.members.Select(m=>m.pawn==null?"null":$"{m.pawn.LabelShort}:{m.pawn.Faction?.Name}:{(m.pawn.Spawned?"spawned":"not")}:{(m.pawn.Dead?"dead":"alive")}"))}]");
            if(c.Present.FirstOrDefault() is Pawn probe)c.DebugNoBed(probe);
            Check("migration: layout version updated",c.layoutVersion==MapComponent_Camp.CurrentLayoutVersion);
            Check("migration: whole footprint inside area",c.Footprint.Cells.All(c.Inside));
            Check("migration: chairs moved to the table",!c.Active||ChairsAtTable());
            CampQA.Write($"MIGRATION perks=[{string.Join(", ",c.perks)}] mortars={c.Mortars.Count}/{c.MortarCount} medicalBeds={c.facilities.OfType<Building_Bed>().Count(b=>b.Medical)}");
            Check("migration: old medic perk now fields mortars",!c.Active||c.Mortars.Count==c.MortarCount&&!c.facilities.OfType<Building_Bed>().Any(b=>b.Medical));
            if(c.Present.Any())Check("migration: beds usable by members",c.Present.Any(p=>JobGiver_Camp.BedFor(p,p,c)!=null));
            else CampQA.Write("SKIP migration bed check: save has no living members");
            loaded=true;stage=85;tick=Find.TickManager.TicksGame;return;
        }
        loaded=true; stage=90;
        enemy=map.mapPawns.AllPawnsSpawned.FirstOrDefault(c.ActiveThreat);
        Check("save/load member references",c.members.Count==expectedMembers&&c.Present.Count()==expectedMembers);
        Check("save/load points and receipts",c.points==expectedPoints&&c.rewardedQuests.Count==expectedReceipts);
        Check("save/load area and faction",c.ValidArea&&c.company!=Faction.OfPlayer&&c.company!=null);
        Check("save/load retreat latch",c.phase==CampPhase.Recovering);
        tick=Find.TickManager.TicksGame; CampQA.Write("LOAD_READY");
    }
    private float nextBeat;
    public override void GameComponentUpdate()
    {
        if(!CampQA.Enabled||stage>=99)return;
        if(Find.CurrentMap!=null) Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
        // Heartbeat: shows whether a stalled run is paused behind a window or never got a map.
        if(UnityEngine.Time.realtimeSinceStartup<nextBeat)return;
        nextBeat=UnityEngine.Time.realtimeSinceStartup+15;
        CampQA.Write($"BEAT stage={stage} map={Find.CurrentMap!=null} ticks={Find.TickManager.TicksGame} paused={Find.TickManager.Paused} windows=[{string.Join(", ",Find.WindowStack.Windows.Select(w=>w.GetType().Name))}]");
    }
    // Vanilla stack limits apply (no stack-size mods in the QA profile), so large deliveries are split.
    private void Supply(ThingDef d,int n)
    {
        while(n>0)
        {
            var t=ThingMaker.MakeThing(d);t.stackCount=Math.Min(n,d.stackLimit);n-=t.stackCount;
            var cell=c.depot.OccupiedRect().Cells.FirstOrDefault(x=>x.GetThingList(map).Count(y=>y.def.category==ThingCategory.Item)<3);
            if(!cell.IsValid||!c.depot.OccupiedRect().Contains(cell))throw new Exception("test supply depot full");
            GenSpawn.Spawn(t,cell,map);
        }
    }
    private void MakeEnemy(IntVec3 pos)
    {
        // Quicktest worlds do not always contain pirates; any humanlike enemy faction will do.
        var faction=Faction.OfPirates??Find.FactionManager.RandomEnemyFaction(false,false,false);
        enemy=PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate,faction); GenSpawn.Spawn(enemy,pos,map);
        enemy.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait,99999),JobCondition.InterruptForced);
        CampQA.Write($"ENEMY {enemy.LabelShort} faction={enemy.Faction?.Name} hostile={enemy.HostileTo(Faction.OfPlayer)} at {pos} inside={c.Inside(pos)}");
    }
    private int shotsBefore,turretShotsBefore,silverBefore,lettersBefore; private readonly List<Pawn> squad=new(); private IntVec3 newTurretCell; private int unbuilt;
    private void MakeSquad(IntVec3 pos,int n)
    {
        for(int i=0;i<n;i++){MakeEnemy(pos+new IntVec3(i*2-n,0,0));squad.Add(enemy);}
    }
    private void ClearSquad(){foreach(var p in squad.Where(p=>p.Spawned).ToList())p.Destroy();squad.Clear();}
    // A downed enemy is no longer a threat, so reuse only a healthy one.
    private void PlaceEnemy(IntVec3 pos)
    {
        if(enemy!=null&&!enemy.Dead&&!enemy.Downed&&enemy.Spawned){enemy.Position=pos;enemy.Notify_Teleported();return;}
        if(enemy!=null&&enemy.Spawned)enemy.Destroy();
        MakeEnemy(pos);
    }
    private bool ChairsAtTable()
    {
        var table=c.facilities.FirstOrDefault(t=>t.def.defName=="Table2x2c"); if(table==null)return false;
        var chairs=c.facilities.Where(t=>t.def.defName=="DiningChair").ToList();
        return chairs.Count==4&&chairs.All(ch=>table.OccupiedRect().Contains(ch.Position+ch.Rotation.FacingCell));
    }
    private void Finish(string why)
    {
        CampQA.Write($"SUMMARY {CampQA.Passed} passed, {CampQA.Failed} failed ({why})");
        CampQA.Write("JOBS "+string.Join(", ",CampQA.Jobs));
        CampQA.Write("TESTS_FINISHED"); stage=99; Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
        Root.Shutdown();
    }
    public override void GameComponentTick()
    {
        if(!CampQA.Enabled||stage>=98&&!loaded||stage==99)return;
        try
        {
            int now=Find.TickManager.TicksGame;
            if(stage==0)
            {
                map=Find.CurrentMap;if(map==null)return;c=map.GetComponent<MapComponent_Camp>();var center=map.Center;
                foreach(var cell in CellRect.CenteredOn(center,30).ClipInsideMap(map))
                {
                    // Steam geysers and other indestructible map features are lifted off instead, or a random map can block the site.
                    foreach(var t in cell.GetThingList(map).ToArray())if(t is not Pawn){if(t.def.destroyable)t.Destroy();else if(t.Spawned)t.DeSpawn();}
                    map.roofGrid.SetRoof(cell,null);map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
                }
                // Quicktest maps can carry wildlife or raiders; anything hostile left on the map would muddy the phase checks.
                foreach(var p in map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)).ToList()) { CampQA.Write($"CLEARED hostile {p.LabelShort}@{p.Position}"); p.Destroy(); }
                // A player's hidden power line crossing the site (three cells under the east shelter's north wall) must not block it.
                var pw=CampDefOf.GJL_MC_ContractDepot.PlaceWorkers.OfType<PlaceWorker_Camp>().Single();
                var blocker=GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.WoodLog),center+new IntVec3(-9,0,-7),map);blocker.SetFaction(Faction.OfPlayer);
                Check("site: a player wall blocks placement and is marked red",!pw.AllowsPlacing(CampDefOf.GJL_MC_ContractDepot,center,Rot4.North,map).Accepted&&CampLayout.BlockedCells(map,center,null,c.facilities).SequenceEqual(new[]{blocker.Position}));
                blocker.Destroy();
                conduits=new[]{(5,6),(6,6),(7,6),(5,7)}.Select(o=>{var t=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HiddenConduit"));t.SetFaction(Faction.OfPlayer);return GenSpawn.Spawn(t,center+new IntVec3(o.Item1,0,o.Item2),map);}).ToList();
                Check("site: hidden conduits do not block placement",pw.AllowsPlacing(CampDefOf.GJL_MC_ContractDepot,center,Rot4.North,map).Accepted&&!CampLayout.BlockedCells(map,center,null,c.facilities).Any());
                var depot=(Building_ContractDepot)ThingMaker.MakeThing(CampDefOf.GJL_MC_ContractDepot,ThingDefOf.WoodLog);depot.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(depot,center,map);
                Supply(ThingDefOf.WoodLog,400);Supply(ThingDefOf.Steel,200);Supply(ThingDefOf.Silver,200);Supply(ThingDefOf.MealSimple,40);Supply(ThingDefOf.MedicineHerbal,25);
                CampQuests.Offer(c);var q=CampQuests.Current(c);q.Accept(null);
                Check("contact quest completes",CampQuests.Complete(c,out var err));if(err!=null)CampQA.Write(err);
                Check("4 independent non-player pawns",c.Present.Count()==4&&c.Present.All(p=>!p.IsColonist&&!p.IsPlayerControlled));
                Check("fixed camp and 4 beds",c.facilities.OfType<Building_Bed>().Count()==4&&c.ValidArea);
                Check("chairs face the table",ChairsAtTable());
                Check("site: hidden conduits kept under camp walls",conduits.All(t=>t.Spawned)&&conduits.Take(3).All(t=>t.Position.GetEdifice(map)?.def==ThingDefOf.Wall));
                Check("receipt blocks duplicate contact",!CampQuests.Complete(c,out _));
                eater=c.Present.First();sleeper=c.Present.Skip(1).First();eater.needs.food.CurLevelPercentage=.12f;foodBefore=eater.needs.food.CurLevelPercentage;
                sleeper.needs.rest.CurLevelPercentage=.1f;
                foreach(var p in c.Present)p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                tick=now;stage=1;CampQA.Write("MAP_READY");
            }
            else if(stage==1&&now-tick>1800)
            {
                Check("autonomous eating",eater.needs.food.CurLevelPercentage>foodBefore+.2f);
                bool slept=CampQA.Jobs.GetValueOrDefault("LayDown")>0&&sleeper.needs.rest.CurLevelPercentage>.1f;
                Check("autonomous sleeping",slept);
                if(!slept){ CampQA.Write($"sleeper rest={sleeper.needs.rest.CurLevelPercentage:F2} job={sleeper.CurJobDef?.defName} phase={c.phase}"); c.DebugNoBed(sleeper); }
                Check("camp stays in garrison without threats",c.phase==CampPhase.Garrison);
                // 4 pawns over 1800 ticks; instantly-ending jobs (the old Clean loop) blow far past this.
                Check("no job restart spam",CampQA.Jobs.Values.Sum()<300);
                CampQA.Write("JOBS "+string.Join(", ",CampQA.Jobs));
                c.points=20;
                // Research gate: the mortar perk needs the vanilla Mortars project, and the free layout its own project.
                var mortarPerk=DefDatabase<CampPerkDef>.GetNamed("GJL_MC_Support1");
                Check("research gates perks",c.LockReason(mortarPerk)?.Contains("연구")==true&&!c.Buy(mortarPerk,false,out _)&&c.points==20);
                Check("research gates the free layout",!c.SetFreeLayout(true,out _)&&!c.freeLayout&&!CampPlan.Designators.Any(d=>d.Visible));
                foreach(var r in DefDatabase<ResearchProjectDef>.AllDefsListForReading)if(!r.IsFinished)Find.ResearchManager.FinishProject(r,false,null,false);
                Check("finished research opens the plan tools",CampPlan.Designators.Count(d=>d.Visible)>=12);
                foreach(var name in new[]{"GJL_MC_Force1","GJL_MC_Force2","GJL_MC_Support1","GJL_MC_Elite1"}) Check("perk "+name,c.Buy(DefDatabase<CampPerkDef>.GetNamed(name),false,out _));
                var guns=c.Mortars;
                Check("capacity and mortar emplacement",c.Capacity==8&&guns.Count==2&&!c.facilities.OfType<Building_Bed>().Any(b=>b.Medical));
                Check("mortars open to the sky with a free crew cell",guns.All(g=>!g.OccupiedRect().Cells.Any(x=>x.Roofed(map))&&g.InteractionCell.Standable(map)));
                var wall=c.facilities.First(t=>t.def==ThingDefOf.Wall);wall.Destroy();int missing=CampLayout.MissingCount(c);
                Check("buy next perk",c.Buy(DefDatabase<CampPerkDef>.GetNamed("GJL_MC_Elite2"),false,out _));
                Check("perk does not repair destroyed walls for free",CampLayout.MissingCount(c)==missing);
                Supply(ThingDefOf.WoodLog,100);Supply(ThingDefOf.Steel,50);Check("paid facility repair",CampLayout.Repair(c,out _));
                Check("no duplicate facility creation",CampLayout.MissingCount(c)==0);
                Supply(ThingDefOf.Silver,1000);c.refillAt=now;tick=now;stage=2;
            }
            else if(stage==2&&now-tick>660)
            {
                Check("delayed reinforcement fills capacity",c.Present.Count()==8);
                if(c.Present.Count()!=8)CampQA.Write($"refill state phase={c.phase} refillAt={c.refillAt} now={now} shortage={c.shortageSince} silver={c.Count(ThingDefOf.Silver)} members={c.members.Count}/{c.Capacity}");
                c.UpgradeEquipment();Check("elite equipment",c.Present.All(p=>p.equipment.Primary.def.defName=="Gun_AssaultRifle"));
                foreach(var p in c.Present){p.needs.rest.CurLevelPercentage=1;p.needs.food.CurLevelPercentage=1;p.jobs.EndCurrentJob(JobCondition.InterruptForced);}
                foreach(var cell in CellRect.CenteredOn(c.center,20).ClipInsideMap(map))c.area[cell]=true;
                MakeEnemy(c.center+new IntVec3(0,0,17));tick=now;stage=3;
            }
            else if(stage==3&&now-tick>300)
            {
                Check("automatic threat response",c.phase==CampPhase.Fighting||c.phase==CampPhase.Recovering);
                Check("combat jobs issued",CampQA.Jobs.GetValueOrDefault("GJL_MC_Attack")+CampQA.Jobs.GetValueOrDefault("GJL_MC_CombatMove")>0);
                PlaceEnemy(c.center+new IntVec3(0,0,25));
                // A merc at camp or on a post may fire at anything in range (no chasing); one out in the zone may not target past it.
                var scout=c.Present.First();scout.Position=c.center+new IntVec3(0,0,12);scout.Notify_Teleported();
                Check("no target outside area",!c.AtPost(scout.Position)&&!c.AllowsTarget(scout,enemy));
                Check("a merc at camp may fire past the area",c.AtPost(c.center)&&c.Present.Where(p=>c.AtCamp(p.Position)).All(p=>c.AllowsTarget(p,enemy)));
                tick=now;stage=4;
            }
            else if(stage==4&&now-tick>240)
            {
                // Posts may keep firing at the enemy past the boundary; nobody may walk out after it.
                Check("boundary pursuit cancelled",c.Present.All(p=>p.CurJobDef?.defName!="GJL_MC_CombatMove"&&(c.Inside(p.Position)||c.Footprint.Contains(p.Position))));
                // A fresh enemy: the one past the boundary may already be shot up by the posts and go down before the next scan.
                c.ChangePhase(CampPhase.Garrison,"QA resume");if(enemy!=null&&enemy.Spawned)enemy.Destroy();MakeEnemy(c.center+new IntVec3(0,0,17));tick=now;stage=5;
            }
            else if(stage==5&&now-tick>240)
            {
                c.baseline=8;foreach(var p in c.Present.Take(4).ToList())p.Kill(null);tick=now;stage=6;
            }
            else if(stage==6&&now-tick>240)
            {
                Check("loss retreat latch",c.phase==CampPhase.Recovering);
                // Out of every rifle's reach from the camp, so the waiting enemy survives the save/load and recovery checks.
                PlaceEnemy(c.center+new IntVec3(0,0,50));
                expectedPoints=c.points;expectedMembers=c.members.Count;expectedReceipts=c.rewardedQuests.Count;
                GameDataSaveLoader.SaveGame("MercenaryCamp-QA");stage=7;tick=now;
            }
            else if(stage==7&&now-tick>60)
            { stage=98; LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("MercenaryCamp-QA"),"LoadingLongEvent",false,null); }
            else if(loaded&&stage==90&&now-tick>500)
            {
                Check("no duplication after loaded ticks",c.members.Count==expectedMembers&&c.points==expectedPoints);
                Check("no reengagement while enemy remains",c.phase==CampPhase.Recovering);
                // Fast-forward the quiet timer: an enemy parked outside the area must not hold the camp in retreat forever.
                c.quietSince=now-3600;tick=now;stage=91;
            }
            else if(loaded&&stage==85&&now-tick>600)
            {
                Check("migration: loaded camp keeps ticking",c.facilities.Count(t=>t.def.defName=="DiningChair")==4&&c.Footprint.Cells.All(c.Inside));
                Finish("migration");
            }
            else if(loaded&&stage==91&&now-tick>240)
            {
                Check("recovery ends while enemy waits outside area",c.phase==CampPhase.Garrison&&enemy!=null&&enemy.Spawned&&!c.Inside(enemy.Position));
                if(c.phase!=CampPhase.Garrison)CampQA.Write($"RECOVERY phase={c.phase} quietSince={c.quietSince} now={now} threats=[{string.Join(", ",c.Threats.Select(t=>$"{t.LabelShort}:{t.kindDef.defName}@{t.Position} engages={c.Engages(t)}"))}]");
                // Artillery: a squad 40 cells out, inside a widened area, beyond mortar minimum range and rifle-line splash.
                if(enemy.Spawned)enemy.Destroy();
                foreach(var cell in CellRect.CenteredOn(c.center,45).ClipInsideMap(map))c.area[cell]=true;
                c.shellStock=10;shotsBefore=CampQA.MortarShots;
                // Quicktest colonists wander; one standing near the squad would (correctly) veto every shot.
                foreach(var col in map.mapPawns.FreeColonistsSpawned.ToList()){col.Position=c.center+new IntVec3(0,0,-28);col.Notify_Teleported();col.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait,6000),JobCondition.InterruptForced);}
                MakeSquad(c.center+new IntVec3(0,0,40),4);tick=now;stage=92;
            }
            else if(loaded&&stage==92&&now-tick>1500)
            {
                CampQA.Write($"ARTILLERY shots={CampQA.MortarShots-shotsBefore} shellsLeft={c.shellStock:F1} manJobs={CampQA.Jobs.GetValueOrDefault("ManTurret")}");
                if(CampQA.MortarShots==shotsBefore)
                    foreach(var g in c.Mortars)CampQA.Write($"  mortar {g.Position} target={c.MortarTarget(g)} gunners=[{string.Join(", ",c.Present.Where(c.IsGunner).Select(p=>$"{p.LabelShort} ready={c.Ready.Contains(p)} job={p.CurJobDef?.defName}"))}] friendlies=[{string.Join(", ",map.mapPawns.AllPawnsSpawned.Where(p=>!p.HostileTo(Faction.OfPlayer)&&p.Position.InHorDistOf(c.center+new IntVec3(0,0,40),12)).Select(p=>p.LabelShort))}]");
                Check("mortar crew fires contract shells",CampQA.MortarShots>shotsBefore&&c.shellStock<10);
                Check("mortar crew hauled no shells",map.listerThings.ThingsOfDef(ThingDefOf.Shell_HighExplosive).Count==0);
                ClearSquad();c.ChangePhase(CampPhase.Garrison,"QA beasts");
                if(!ModsConfig.AnomalyActive){CampQA.Write("SKIP beast checks: Anomaly inactive");tick=now;stage=100;return;}
                Check("perk GJL_MC_Anomaly1",c.Buy(DefDatabase<CampPerkDef>.GetNamed("GJL_MC_Anomaly1"),false,out _));
                MakeSquad(c.center+new IntVec3(0,0,40),2);tick=now;stage=93;
            }
            else if(loaded&&stage==93&&now-tick>300)
            {
                var beasts=c.beastSlots.Where(s=>s.pawn!=null&&s.pawn.Spawned).Select(s=>s.pawn).ToList();
                CampQA.Write($"BEASTS out={beasts.Count} kinds=[{string.Join(", ",beasts.Select(b=>b.kindDef.defName))}] jobs=[{string.Join(", ",beasts.Select(b=>b.CurJobDef?.defName))}]");
                Check("pen beasts released on engagement",beasts.Count==4);
                Check("beasts fight for the camp",beasts.All(b=>b.Faction==c.company&&!b.HostileTo(Faction.OfPlayer)&&!c.ActiveThreat(b)));
                ClearSquad();tick=now;stage=94;
            }
            else if(loaded&&stage==94&&now-tick>3000)
            {
                Check("beasts return to the pen when the map is clear",!map.mapPawns.SpawnedPawnsInFaction(c.company).Any(c.IsBeast));
                if(map.mapPawns.SpawnedPawnsInFaction(c.company).Any(c.IsBeast))CampQA.Write($"BEASTS_OUT recalled={c.BeastsRecalled} threats=[{string.Join(", ",c.Threats.Select(t=>$"{t.LabelShort}:{t.kindDef.defName}@{t.Position}"))}] beasts=[{string.Join(", ",map.mapPawns.SpawnedPawnsInFaction(c.company).Where(c.IsBeast).Select(b=>$"{b.kindDef.defName}@{b.Position} {b.CurJobDef?.defName} downed={b.Downed}"))}]");
                Check("survivors ready for the next fight",c.beastSlots.Count==4&&c.beastSlots.All(s=>s.pawn==null));
                tick=now;stage=100;
            }
            // --- Every branch to tier 5 ---
            else if(loaded&&stage==100&&now-tick>60)
            {
                c.ChangePhase(CampPhase.Garrison,"QA full tree");c.points=300;
                var order=Enum.GetValues<PerkBranch>().Where(PerkSummary.Available).SelectMany(b=>DefDatabase<CampPerkDef>.AllDefs.Where(p=>p.branch==b&&!p.doctrine).OrderBy(p=>p.tier)).Where(p=>!c.perks.Contains(p.defName)).ToList();
                var failed=order.Where(p=>!c.Buy(p,false,out _)).Select(p=>p.defName).ToList();
                Check("every available branch reaches tier 5",failed.Count==0&&Enum.GetValues<PerkBranch>().Where(PerkSummary.Available).All(b=>c.Tier(b)==5));
                if(failed.Count>0)CampQA.Write("BUY_FAILED "+string.Join(", ",failed));
                // Doctrines: every one of them, both per branch, stacking.
                var doctrines=DefDatabase<CampPerkDef>.AllDefs.Where(p=>p.doctrine&&PerkSummary.Available(p.branch)).ToList();
                var docFailed=doctrines.Where(p=>!c.Buy(p,false,out _)).Select(p=>p.defName).ToList();
                Check("every doctrine can be taken, two per branch",docFailed.Count==0&&doctrines.Count==2*Enum.GetValues<PerkBranch>().Count(PerkSummary.Available));
                if(docFailed.Count>0)CampQA.Write("DOCTRINE_FAILED "+string.Join(", ",docFailed));
                Check("doctrines cost more than a tier-2 perk",doctrines.All(p=>p.cost==3));
                Check("doctrines never raise a tier",Enum.GetValues<PerkBranch>().Where(PerkSummary.Available).All(b=>c.Tier(b)==5));
                Check("ammo stock doctrine multiplies the shell cap",Math.Abs(c.ShellCap-c.MortarCount*CampMod.Settings.shellsPerMortarDay*2*2*1.5f)<.01f);
                CampQA.Write($"TREE capacity={c.Capacity} mortars={c.Mortars.Count} beasts={c.beastSlots.Count} mechs={c.mechSlots.Count} psycasts=[{string.Join(", ",c.S.psycasts.Select(a=>a.defName))}]");
                Supply(ThingDefOf.MealSimple,30);
                silverBefore=c.Count(ThingDefOf.Silver);c.refillAt=now;tick=now;stage=101;
            }
            else if(loaded&&stage==101&&now-tick>700)
            {
                int Count(string def)=>c.facilities.Count(t=>t.Spawned&&t.def.defName==def);
                CampQA.Write($"ENGINEER barricades={Count("Barricade")} sandbags={Count("Sandbags")} ieds={Count("TrapIED_HighExplosive")} mini={Count("Turret_MiniTurret")} autocannon={Count("Turret_Autocannon")}");
                Check("engineer works built (ring, 16 IEDs with the minefield, turrets)",Count("Barricade")>=50&&Count("Sandbags")==0&&Count("TrapIED_HighExplosive")==16&&Count("Turret_MiniTurret")==4&&Count("Turret_Autocannon")==2);
                foreach(var s in CampLayout.Slots(c).Where(s=>!c.facilities.Any(t=>t.Spawned&&t.def.defName==s.Def&&t.Position==c.center+new IntVec3(s.X,0,s.Z))))
                    CampQA.Write($"UNBUILT {s.Def}@({s.X},{s.Z}) built={c.builtSlots.Contains($"{s.Def}:{s.X}:{s.Z}")} on=[{string.Join(", ",(c.center+new IntVec3(s.X,0,s.Z)).GetThingList(map).Select(t=>t.def.defName))}]");
                Check("camp turrets powered by contract",c.facilities.OfType<Building_TurretGun>().Where(t=>t.def!=ThingDefOf.Turret_Mortar).All(t=>t.TryGetComp<CompPowerTrader>()?.PowerOn!=false));
                Check("standing army refills free to 12",c.Present.Count()==12&&c.Count(ThingDefOf.Silver)==silverBefore);
                c.UpgradeEquipment();
                var byRole=c.Present.GroupBy(c.RoleOf).ToDictionary(g=>g.Key,g=>g.ToList());
                CampQA.Write("ROLES "+string.Join(", ",byRole.Select(kv=>$"{kv.Key}={kv.Value.Count}")));
                Check("lancers carry charge lances",byRole.GetValueOrDefault(MercRole.Lancer)?.Count==2&&byRole[MercRole.Lancer].All(p=>p.equipment.Primary?.def.defName=="Gun_ChargeLance"));
                Check("snipers carry sniper rifles",byRole.GetValueOrDefault(MercRole.Sniper)?.Count==2&&byRole[MercRole.Sniper].All(p=>p.equipment.Primary?.def.defName=="Gun_SniperRifle"));
                Check("riflemen get charge rifles and marine helmets",byRole.GetValueOrDefault(MercRole.Rifleman,new()).All(p=>p.equipment.Primary?.def.defName=="Gun_ChargeRifle"&&p.apparel.WornApparel.Any(a=>a.def.defName=="Apparel_PowerArmorHelmet")));
                Check("squad leader buff on everyone",c.Present.All(p=>p.health.hediffSet.HasHediff(CampDefOf.GJL_MC_Leadership)));
                var rapid=DefDatabase<HediffDef>.GetNamed("GJL_MC_Doc_Rapid");
                Check("force doctrine hediff on everyone",c.Present.All(p=>p.health.hediffSet.HasHediff(rapid)));
                if(ModsConfig.RoyaltyActive){var amp=DefDatabase<HediffDef>.GetNamed("GJL_MC_Doc_Amp");Check("psychic doctrine only on psycasters",c.Present.All(p=>p.health.hediffSet.HasHediff(amp)==(c.RoleOf(p)==MercRole.Psycaster)));}
                // Both force doctrines at once; the trench one only while the merc stands on a post.
                var trench=DefDatabase<HediffDef>.GetNamed("GJL_MC_Doc_Trench"); c.ApplyPerkHediffs(); // Same tick as the positions it is checked against.
                Check("trench doctrine follows the post",c.Present.All(p=>p.health.hediffSet.HasHediff(trench)==c.AtPost(p.Position)));
                if(ModsConfig.RoyaltyActive)Check("psycasters linked with all psycasts",byRole.GetValueOrDefault(MercRole.Psycaster)?.Count==2&&byRole[MercRole.Psycaster].All(p=>p.GetPsylinkLevel()>=6&&c.S.psycasts.All(a=>p.abilities.GetAbility(a)!=null)));
                Check("ambush belt reaches 10 cells past the area",c.InEngagementZone(c.center+new IntVec3(0,0,50))&&!c.InEngagementZone(c.center+new IntVec3(0,0,60)));
                // Full engagement: a far squad for the guns plus a near one for turrets, psycasters and mechs.
                foreach(var p in c.Present){p.needs.rest.CurLevelPercentage=1;p.needs.food.CurLevelPercentage=1;}
                shotsBefore=CampQA.MortarShots;turretShotsBefore=CampQA.TurretShots;c.shellStock=c.ShellCap;lettersBefore=Find.LetterStack.LettersListForReading.Count(l=>l.Label=="정찰 보고");
                MakeSquad(c.center+new IntVec3(0,0,40),4);MakeSquad(c.center+new IntVec3(0,0,16),4);tick=now;stage=102;
            }
            else if(loaded&&stage==102&&now-tick>900)
            {
                var mechs=map.mapPawns.SpawnedPawnsInFaction(c.company).Where(c.IsCampMech).ToList();
                int casts=CampQA.Jobs.Where(kv=>kv.Key.StartsWith("CastAbility")).Sum(kv=>kv.Value);
                CampQA.Write($"BATTLE phase={c.phase} mechs={mechs.Count} [{string.Join(", ",mechs.Select(m=>m.kindDef.defName+":"+m.CurJobDef?.defName))}] casts={casts} mortarShots={CampQA.MortarShots-shotsBefore} turretShots={CampQA.TurretShots-turretShotsBefore} enemiesLeft={squad.Count(p=>p.Spawned&&!p.Dead&&!p.Downed)}");
                if(ModsConfig.BiotechActive)Check("bay mechs deploy under the camp duty",mechs.Count>0&&mechs.All(m=>m.mindState.duty?.def==CampDefOf.GJL_MC_MechSquad&&!m.HostileTo(Faction.OfPlayer)));
                CampQA.Write($"PSYCASTS activations=[{string.Join(", ",CampQA.Casts.Select(kv=>$"{kv.Key} {kv.Value}"))}] heat=[{string.Join(", ",c.Present.Where(p=>c.RoleOf(p)==MercRole.Psycaster).Select(p=>$"{p.psychicEntropy.EntropyValue:F0}/{p.psychicEntropy.MaxEntropy:F0} focus {p.psychicEntropy.CurrentPsyfocus:P0}"))}]");
                if(ModsConfig.RoyaltyActive)Check("psycasters cast in combat",CampQA.Casts.Where(kv=>kv.Key.StartsWith("OK")).Sum(kv=>kv.Value)>0);
                if(ModsConfig.RoyaltyActive)Check("psycast jobs are not a retry loop",casts<=2*CampQA.Casts.Sum(kv=>kv.Value)+4);
                // 8 enemies: a lasting debuff should never be stacked on the same few targets over and over.
                if(ModsConfig.RoyaltyActive)Check("lasting psycasts not re-cast on affected enemies",CampQA.Casts.GetValueOrDefault("OK Burden")<=8&&CampQA.Casts.GetValueOrDefault("OK VertigoPulse")<=8);
                Check("engineer turrets fire",CampQA.TurretShots>turretShotsBefore);
                Check("scouting report letter",Find.LetterStack.LettersListForReading.Count(l=>l.Label=="정찰 보고")>lettersBefore);
                ClearSquad();foreach(var p in map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)).ToList())p.Destroy();
                tick=now;stage=103;
            }
            else if(loaded&&stage==103&&now-tick>3000)
            {
                Check("mechs and beasts go home after the fight",!map.mapPawns.SpawnedPawnsInFaction(c.company).Any(p=>!p.RaceProps.Humanlike));
                // Recon: the raid is turned into a warning, then arrives on schedule, shrunk by intel.
                c.ChangePhase(CampPhase.Garrison,"QA raid");
                var parms=new IncidentParms{target=map,points=600,faction=Find.FactionManager.RandomEnemyFaction(false,false,false),raidStrategy=RaidStrategyDefOf.ImmediateAttack,raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn,forced=true};
                bool ok=IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                Check("raid delayed by scouts",ok&&c.scoutedRaidTicks.Count==1&&!map.mapPawns.AllPawnsSpawned.Any(c.ActiveThreat)&&Find.Storyteller.incidentQueue.Count>0);
                CampQA.RaidPoints=-1;tick=now;stage=104;
            }
            else if(loaded&&stage==104&&now-tick>(int)(c.S.raidWarningHours*CampRules.Hour)+600)
            {
                CampQA.Write($"RAID arrived={map.mapPawns.AllPawnsSpawned.Count(c.ActiveThreat)} points={CampQA.RaidPoints:F0}");
                Check("scouted raid arrives on schedule",map.mapPawns.AllPawnsSpawned.Any(c.ActiveThreat)&&c.scoutedRaidTicks.Count==0);
                // Recon 5 (-15%) and the intel-network doctrine (-5%) multiply: 600 × 0.85 × 0.95.
                Check("intel shrinks the raid by 15% then 5%",Math.Abs(CampQA.RaidPoints-484.5f)<1);
                Check("intel network adds 4 warning hours",Math.Abs(c.S.raidWarningHours-8)<.01f);
                foreach(var p in map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)).ToList())p.Destroy();
                tick=now;stage=105;
            }
            // --- Free layout: convert, retire a work, plan a new one, let the crew build it, convert back ---
            else if(loaded&&stage==105&&now-tick>300)
            {
                c.ChangePhase(CampPhase.Garrison,"QA free layout");
                int before=c.facilities.Count(t=>t.Spawned); var siteBefore=c.Footprint;
                // Standard works never built (a corpse or dropped gun on the cell) become crew work in the free layout.
                unbuilt=CampLayout.Slots(c).Count(s=>!c.builtSlots.Contains($"{s.Def}:{s.X}:{s.Z}")&&!c.facilities.Any(t=>t.Spawned&&t.def.defName==s.Def&&t.Position==c.center+new IntVec3(s.X,0,s.Z)));
                Check("switch to free layout",c.SetFreeLayout(true,out var err)&&c.freeLayout); if(err!=null)CampQA.Write(err);
                Check("free layout keeps every facility",c.facilities.Count(t=>t.Spawned)==before&&c.PendingCount==unbuilt);
                CampQA.Write($"FREE before={before} unbuilt={unbuilt} pending={c.PendingCount} blocked={c.BlockedCount} plan={c.plan.Count}");
                Check("converted footprint matches the standard site",c.Footprint.Equals(siteBefore));
                var turret=c.facilities.First(t=>t.Spawned&&t.def.defName=="Turret_MiniTurret");
                IntVec3 old=turret.Position; c.RemovePlansAt(old); c.PlanChanged();
                Check("removing a plan retires its facility",!turret.Spawned&&c.facilities.Count(t=>t.Spawned)==before-1);
                newTurretCell=old+new IntVec3(Math.Sign(old.x-c.center.x)*3,0,Math.Sign(old.z-c.center.z)*3);
                var report=CampPlan.CanPlan(c,PlanKind.MiniTurret,newTurretCell,Rot4.North);
                Check("plan a turret on clear ground",report.Accepted); if(!report.Accepted)CampQA.Write("PLAN_REJECTED "+report.Reason);
                c.AddPlan(PlanKind.MiniTurret,newTurretCell,Rot4.North); c.PlanChanged();
                Check("overlapping plan rejected",!CampPlan.CanPlan(c,PlanKind.Wall,newTurretCell,Rot4.North).Accepted);
                Check("limit reached rejects one more turret",!CampPlan.CanPlan(c,PlanKind.MiniTurret,c.center+new IntVec3(0,0,-16),Rot4.North).Accepted);
                Check("new plan waits for the crew",c.PendingCount==unbuilt+1&&c.IsPending(newTurretCell)&&!c.facilities.Any(t=>t.Spawned&&t.Position==newTurretCell));
                IntVec3 post=c.center+new IntVec3(0,0,14);
                Check("plan a guard post",CampPlan.CanPlan(c,PlanKind.Post,post,Rot4.North).Accepted);
                c.AddPlan(PlanKind.Post,post,Rot4.North); c.AddPlan(PlanKind.Rally,c.center+new IntVec3(0,0,3),Rot4.North); c.PlanChanged();
                Check("guard post and moved rally point",c.AtPost(post)&&c.PostCells.Contains(post)&&c.PlanCount(PlanKind.Rally)==1&&c.RallyCell==c.center+new IntVec3(0,0,3));
                foreach(var p in c.Present){p.needs.rest.CurLevelPercentage=1;p.needs.food.CurLevelPercentage=1;p.jobs.EndCurrentJob(JobCondition.InterruptForced);}
                tick=now;stage=106;
            }
            else if(loaded&&stage==106&&now-tick>2500)
            {
                var built=c.facilities.FirstOrDefault(t=>t.Spawned&&t.Position==newTurretCell&&t.def.defName=="Turret_MiniTurret");
                CampQA.Write($"CONSTRUCTION built={built!=null} pending={c.PendingCount} blocked={c.BlockedCount} buildJobs={CampQA.Jobs.GetValueOrDefault("GJL_MC_Build")} phase={c.phase} alert={c.Alert}");
                Check("crew builds the planned turret",built!=null&&built.Faction==c.company&&!c.IsPending(newTurretCell));
                Check("crew construction used the build job",CampQA.Jobs.GetValueOrDefault("GJL_MC_Build")>0);
                Check("back to the standard layout",c.SetFreeLayout(false,out var err)&&!c.freeLayout); if(err!=null)CampQA.Write(err);
                Check("standard layout restored",!c.facilities.Any(t=>t.Spawned&&t.Position==newTurretCell)&&c.facilities.Count(t=>t.Spawned&&t.def.defName=="Turret_MiniTurret")==4);
                Finish("all stages");
            }
        }
        catch(Exception e){CampQA.Write("EXCEPTION "+e);Finish("exception");}
    }
}
