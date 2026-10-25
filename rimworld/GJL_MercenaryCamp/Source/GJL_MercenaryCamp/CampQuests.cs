using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using RimWorld.Planet;
using Verse;

namespace MercenaryCamp;

public enum ContractMission { Contact, Supply, Watch }
public class QuestPart_CampContract : QuestPart
{
    public Map map;
    public ContractMission mission;
    public int serial, watchStart, scale = 1;
    public bool paid;
    public override void Notify_QuestSignalReceived(Signal signal)
    {
        if (signal.tag == quest.InitiateSignal && map != null) watchStart = map.GetComponent<MapComponent_Camp>().supplyWatchTicks;
    }
    public override string DescriptionPart => map == null ? "주둔 맵이 사라졌습니다." : "조건이 갖춰지는 즉시 창고에서 물자를 가져가 자동으로 완료됩니다. 진행 상황은 용병 보급소의 관리 화면에서 확인하세요.";
    public override IEnumerable<GlobalTargetInfo> QuestLookTargets { get { if (map?.GetComponent<MapComponent_Camp>().depot is Thing t) yield return new GlobalTargetInfo(t); } }
    public override void ExposeData()
    {
        base.ExposeData(); Scribe_References.Look(ref map, "map"); Scribe_Values.Look(ref mission, "mission");
        Scribe_Values.Look(ref serial, "serial"); Scribe_Values.Look(ref paid, "paid"); Scribe_Values.Look(ref watchStart, "watchStart");
        Scribe_Values.Look(ref scale, "scale", 1);
    }
}
// Quests are offered by the depot; this root is deliberately not a random storyteller quest.
public class QuestNode_Contract : QuestNode
{
    protected override bool TestRunInt(Slate slate) => false;
    protected override void RunInt() { }
}
public static class CampQuests
{
    public static Quest Current(MapComponent_Camp c) => Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == c.currentQuestId);
    public static void Tick(MapComponent_Camp c)
    {
        if (c.depot == null || !c.depot.Spawned) return;
        var current = Current(c);
        if (current is { State: QuestState.Ongoing }) { Settle(c); return; }
        if (current != null && !current.Historical) return;
        if (c.phase == CampPhase.Uncontracted || c.Active && c.Now >= c.nextQuestAt) Offer(c);
    }
    // An accepted quest settles itself the moment the colony can pay, the way a trader takes goods straight from storage.
    public static void Settle(MapComponent_Camp c) => c.questBlocker = Complete(c, out string error) ? null : error;
    public static void Offer(MapComponent_Camp c)
    {
        if (Current(c) is Quest previous && !previous.Historical) return;
        ContractMission kind = !c.Active ? ContractMission.Contact : c.questSequence % 2 == 0 ? ContractMission.Supply : ContractMission.Watch;
        int scale = CampRules.RewardScale(c.perks.Count);
        Quest q = Quest.MakeRaw(); q.root = CampDefOf.GJL_MC_ContractQuest;
        q.name = kind switch { ContractMission.Contact => "용병단: 첫 주둔 계약", ContractMission.Supply => "용병단: 보급 계약", _ => "용병단: 주둔 보호 협약" };
        q.description = kind switch
        {
            ContractMission.Contact => "캠프 부지 19×15칸을 확보하고 창고에 목재 400, 강철 200, 은 200을 준비하세요. 수락하면 물자가 갖춰지는 즉시 창고에서 가져가고 독립 용병 4명이 캠프에 주둔합니다. 이후 식량은 창고의 완성식을 직접 가져가 먹습니다.",
            ContractMission.Supply => $"완성식 {20 * scale}개와 약초 {4 * scale}개를 납품하세요. 보상: 계약 점수 {scale}. 수락하면 창고에 물자가 갖춰지는 즉시 자동으로 가져갑니다.",
            _ => $"식량 보급을 유지하며 안전한 주둔 시간을 합계 2일 채우세요. 전투·후퇴·식량 고갈 중에는 진행되지 않습니다. 보상: 계약 점수 {2 * scale}. 2일을 채우면 자동으로 완료됩니다."
        };
        q.AddPart(new QuestPart_CampContract { map = c.map, mission = kind, serial = c.contractSerial, watchStart = c.supplyWatchTicks, scale = scale });
        Find.QuestManager.Add(q); c.currentQuestId = q.id;
        Messages.Message(q.name + " 의뢰가 도착했습니다. 퀘스트 탭 또는 보급소에서 수락하세요.", c.depot, MessageTypeDefOf.PositiveEvent);
    }
    public static bool Complete(MapComponent_Camp c, out string error)
    {
        error = null; Quest q = Current(c); var part = q?.GetFirstPartOfType<QuestPart_CampContract>();
        if (q == null || part == null || q.State != QuestState.Ongoing || part.paid || c.rewardedQuests.Contains(q.id)) { error = "수락한 미완료 의뢰가 없습니다."; return false; }
        if (part.mission != ContractMission.Contact && (!c.Active || part.serial != c.contractSerial)) { error = "이전 계약의 의뢰는 완료할 수 없습니다."; return false; }
        switch (part.mission)
        {
            case ContractMission.Contact:
                if (!c.StartContract(out error)) return false;
                Messages.Message("첫 주둔 계약: 창고에서 목재 400, 강철 200, 은 200을 가져갔습니다.", c.depot, MessageTypeDefOf.PositiveEvent); break;
            case ContractMission.Supply:
                int meals = 20 * part.scale, herbs = 4 * part.scale;
                int haveMeals = c.MealCount, haveHerbs = c.Count(ThingDefOf.MedicineHerbal);
                if (haveMeals < meals || haveHerbs < herbs) { error = $"창고에 완성식 {meals}개와 약초 {herbs}개가 필요합니다. (현재 {haveMeals}, {haveHerbs})"; return false; }
                c.Spend(new() { [ThingDefOf.MedicineHerbal] = herbs }); c.SpendMeals(meals); c.points += part.scale;
                Messages.Message($"보급 계약: 창고에서 완성식 {meals}개와 약초 {herbs}개를 가져갔습니다.", c.depot, MessageTypeDefOf.PositiveEvent); break;
            case ContractMission.Watch:
                if (c.supplyWatchTicks - part.watchStart < 2 * CampRules.Day) { error = "보급이 유지되는 안전한 주둔 시간이 2일 필요합니다."; return false; }
                c.points += 2 * part.scale; break;
        }
        // Persist the receipt before ending the quest; a reload cannot pay it again.
        part.paid = true; c.rewardedQuests.Add(q.id); c.questSequence++; c.nextQuestAt = c.Now + 3 * CampRules.Day;
        q.End(QuestEndOutcome.Success); return true;
    }
    public static void Cancel(MapComponent_Camp c)
    { var q = Current(c); if (q != null && !q.Historical) q.End(QuestEndOutcome.Fail, sendLetter: false); c.currentQuestId = -1; }
}
