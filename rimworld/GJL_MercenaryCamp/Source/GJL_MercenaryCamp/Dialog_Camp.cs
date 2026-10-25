using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MercenaryCamp;

public class Dialog_Camp : Window
{
    private readonly MapComponent_Camp camp;
    private Vector2 scroll;
    private PerkBranch tab = PerkBranch.Force;
    public override Vector2 InitialSize => new(860, 740);
    public Dialog_Camp(MapComponent_Camp camp) { this.camp = camp; doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true; }
    private void Result(bool ok, string error) { if (!ok) Messages.Message(error, MessageTypeDefOf.RejectInput, false); }
    public override void DoWindowContents(Rect rect)
    {
        if (camp.map == null || camp.depot == null || !camp.depot.Spawned) { Widgets.Label(rect, "보급소가 파괴되었습니다. 원래 자리에 다시 건설하세요."); return; }
        Text.Font = GameFont.Medium; Widgets.Label(new Rect(0,0,rect.width,36), "자율 주둔 용병단"); Text.Font = GameFont.Small;
        var view = new Rect(0,45,rect.width,rect.height-95); var content = new Rect(0,0,view.width-20,1290+camp.members.Count*26);
        Widgets.BeginScrollView(view, ref scroll, content);
        string phase = camp.phase switch { CampPhase.Uncontracted => "미계약", CampPhase.Garrison => camp.Alert ? "경계 태세" : "주둔", CampPhase.Fighting => "교전", CampPhase.Recovering => "후퇴·재정비", _ => "계약 종료·이탈" };
        Widgets.Label(new Rect(0,0,content.width,90), $"{phase} — {camp.reason}\n현원 {camp.Present.Count()} / 정원 {camp.Capacity} · 다운 {camp.Present.Count(p => p.Downed)} · 실종/포로 {camp.members.Count(m => m.missingSince >= 0)} · 계약 점수 {camp.points}\n창고 비축: 완성식 {camp.MealCount}개 · 약(일반 이하) {camp.MedicineCount}개 · 약초 {camp.Count(ThingDefOf.MedicineHerbal)}개 · 은 {camp.Count(ThingDefOf.Silver)}");
        var parts = new System.Collections.Generic.List<string>();
        if (camp.MortarCount > 0) parts.Add($"박격포 {camp.Mortars.Count}/{camp.MortarCount}문 · 계약 포탄 {camp.shellStock:F0}/{camp.ShellCap:F0}발");
        string Squad(string name, System.Collections.Generic.List<BeastSlot> slots, string lost)
        {
            int ready = slots.Count(s => s.pawn == null && s.readyAt <= camp.Now), loose = slots.Count(s => s.pawn != null);
            return $"{name}: 대기 {ready} · 출격 {loose} · {lost} {slots.Count - ready - loose}";
        }
        if (camp.beastSlots.Count > 0) parts.Add(Squad("우리", camp.beastSlots, "재포획 중"));
        if (camp.mechSlots.Count > 0) parts.Add(Squad("정비고", camp.mechSlots, "재생산 중"));
        string support = parts.Count > 0 ? "\n" + string.Join(" · ", parts) : "";
        Widgets.Label(new Rect(0,95,content.width,55), (camp.refillAt < 0 ? "충원 대기 없음" : $"충원까지 {Math.Max(0, camp.refillAt-camp.Now)/(float)CampRules.Day:F1}일 · 결원 1명당 은 100 · 평시·식량 보급 중 도착") + support);
        if (Widgets.ButtonText(new Rect(0,150,370,32), "활동 구역: " + (camp.ValidArea ? camp.area.Label : "미지정/빈 구역")))
        {
            var options = camp.map.areaManager.AllAreas.OfType<Area_Allowed>().Select(a => new FloatMenuOption(a.Label, () => { camp.area = a; camp.reason = "활동 구역 변경. 캠프·보급소와 방어할 지역을 포함하세요."; })).ToList();
            if (options.Count == 0) options.Add(new FloatMenuOption("구역 관리에서 허용 구역을 먼저 만드세요.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        Widgets.Label(new Rect(0,188,content.width,45), "구역 관리에서 선택한 구역을 넓혀주세요. 구역 밖 추격 금지 · 손실 시 캠프 후퇴 · 개별 징집·이동 명령 없음\n물자는 상인처럼 창고·거주 구역에서 바로 가져갑니다. 금지한 물건은 건드리지 않습니다.");
        Quest q = CampQuests.Current(camp);
        Widgets.DrawLineHorizontal(0,240,content.width);
        Widgets.Label(new Rect(0,250,content.width,30), q == null ? "다음 의뢰 대기 중" : q.name + " — " + (q.State == QuestState.NotYetAccepted ? "수락 대기" : q.State == QuestState.Ongoing ? "진행 중" : "종료"));
        if (q != null)
        {
            Widgets.Label(new Rect(0,285,content.width,90), q.description);
            var part = q.GetFirstPartOfType<QuestPart_CampContract>();
            if (part?.mission == ContractMission.Watch) Widgets.Label(new Rect(0,375,content.width,24), $"안전한 주둔: {Math.Max(0,camp.supplyWatchTicks-part.watchStart)/(float)CampRules.Day:F2} / 2일");
            else if (q.State == QuestState.Ongoing && camp.questBlocker != null) Widgets.Label(new Rect(0,375,content.width,24), "자동 완료 대기: " + camp.questBlocker);
            // Accepting settles at once when the colony can already pay; otherwise the camp tick keeps trying.
            if (q.State == QuestState.NotYetAccepted && Widgets.ButtonText(new Rect(0,405,160,32), "의뢰 수락")) { q.Accept(null); CampQuests.Settle(camp); }
        }
        Widgets.DrawLineHorizontal(0,455,content.width);
        DrawLayout(new Rect(0,463,content.width,74));
        Widgets.DrawLineHorizontal(0,545,content.width);
        Widgets.Label(new Rect(0,553,content.width,28), $"퍽 — 노드를 눌러 구매 (창고 물자로 즉시 또는 계약 점수) · 보유 점수 {camp.points} · 의뢰 보상 {CampRules.RewardScale(camp.perks.Count)}배");
        // Branches whose DLC is missing have no tab (their perk defs are not even loaded).
        var branches = Enum.GetValues<PerkBranch>().Where(PerkSummary.Available).ToList();
        if (!branches.Contains(tab)) tab = branches[0];
        TabDrawer.DrawTabs(new Rect(0,623,content.width,490), branches.Select(b => new TabRecord($"{PerkSummary.Label(b)} {camp.Tier(b)}/{CampRules.MaxTier}", () => tab = b, tab == b)).ToList(), 110);
        DrawPerkTree(new Rect(0,631,content.width,482));
        Widgets.Label(new Rect(0,1121,content.width,50), "퍽과 점수는 전멸·해약 후에도 유지됩니다. 납치·실종 대원은 복귀 시 중복 충원을 막기 위해 정원을 유지합니다.\n장비 승급은 평시 보급 점검 시 적용되며 기존 계약 장비는 회수됩니다.");
        GUI.enabled = camp.Active;
        if (Widgets.ButtonText(new Rect(0,1181,245,32), "파괴된 시설 유상 복구")) { bool ok = CampLayout.Repair(camp,out string error); Result(ok,error); }
        if (Widgets.ButtonText(new Rect(265,1181,245,32), "계약 종료")) Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("용병들이 새 출전을 중지하고 맵을 떠납니다. 부상자는 즉시 떠나지 못할 수 있습니다. 계약을 종료할까요?", () => camp.EndContract("고용주가 계약을 종료했습니다.")));
        GUI.enabled = true;
        Widgets.DrawLineHorizontal(0,1226,content.width);
        string intel = camp.S.raidAlert && camp.Threats.Count > 0 ? $" · 적 구성: {camp.ThreatMix}" : "";
        Widgets.Label(new Rect(0,1234,content.width,26), $"대원 명단 (클릭하면 이동) — 구역 안 적 {camp.ThreatsInside} · 구역 밖 적 {camp.ThreatsOutside}{intel}");
        float row = 1264;
        foreach (var r in camp.members.ToList())
        {
            Pawn p = r.pawn; if (p == null) continue;
            var line = new Rect(0,row,content.width,24); row += 26;
            string state = r.missingSince >= 0 ? $"실종 {(camp.Now-r.missingSince)/(float)CampRules.Day:F1}일" : p.Downed ? "다운" : p.InMentalState ? "정신 이상" : p.CurJob?.GetReport(p) ?? "대기";
            Widgets.DrawHighlightIfMouseover(line);
            string role = camp.RoleOf(p) switch { MercRole.Gunner => " (포수)", MercRole.Psycaster => " (사이캐스터)", MercRole.Sniper => " (저격수)", MercRole.Lancer => " (랜서)", _ => "" };
            if (camp.S.leader && camp.members.FirstOrDefault() == r) role += " (분대장)";
            Widgets.Label(line, $"{p.LabelShort}{role} · 체력 {p.health.summaryHealth.SummaryHealthPercent:P0} · {p.equipment?.Primary?.LabelShortCap ?? "무기 없음"} · 사격 {p.skills.GetSkill(SkillDefOf.Shooting).Level} · {state}");
            if (p.Spawned && Widgets.ButtonInvisible(line)) { Close(); CameraJumper.TryJumpAndSelect(p); }
        }
        Widgets.EndScrollView();
    }

    // Standard 19×15 site or the player's own plan; switching to a plan needs the planning research.
    private void DrawLayout(Rect r)
    {
        ResearchProjectDef planning = CampDefOf.GJL_MC_Res_CampPlanning;
        Widgets.Label(new Rect(r.x, r.y + 4, r.width - 250, 26), "캠프 배치 — " + (camp.freeLayout ? "자유 설계" : "표준 19×15"));
        var button = new Rect(r.xMax - 240, r.y, 240, 30);
        if (camp.freeLayout)
        {
            if (Widgets.ButtonText(button, "표준 배치로 되돌리기"))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("표준 19×15 배치로 되돌립니다. 표준 배치에 없는 용병단 시설은 철거되고, 부지 안의 빈 자리는 바로 다시 지어집니다. 계속할까요?",
                    () => Result(camp.SetFreeLayout(false, out string error), error)));
        }
        else
        {
            GUI.enabled = planning.IsFinished;
            if (Widgets.ButtonText(button, "자유 설계로 전환")) Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(CampPlan.SwitchText, () => Result(camp.SetFreeLayout(true, out string error), error)));
            GUI.enabled = true;
            if (!planning.IsFinished) TooltipHandler.TipRegion(button, $"'{planning.LabelCap}' 연구가 필요합니다.");
        }
        Text.Font = GameFont.Tiny;
        string text = camp.freeLayout
            ? string.Join(" · ", Enum.GetValues<PlanKind>().Where(k => !CampPlan.IsSingle(k) && CampPlan.Visible(k) && (camp.PlanLimit(k) > 0 || camp.PlanCount(k) > 0)).Select(k => $"{CampPlan.Label(k)} {camp.PlanCount(k)}/{camp.PlanLimit(k)}"))
                + $"\n시공 대기 {camp.PendingCount}개" + (camp.BlockedCount > 0 ? $" (주황색 {camp.BlockedCount}개는 물건·사람이 비켜야 시공)" : "") + " · 건축 메뉴의 '용병단' 탭이나 보급소의 '캠프 설계'에서 배치"
            : planning.IsFinished ? "자유 설계로 전환하면 지금 배치가 그대로 설계도가 되고, 건축 메뉴의 '용병단' 탭에서 침상·포대·방어선·경계 초소·집결지를 직접 옮기거나 더할 수 있습니다."
            : $"연구 탭 '용병단'의 '{planning.LabelCap}'을(를) 마치면 캠프를 직접 설계할 수 있습니다.";
        Widgets.Label(new Rect(r.x, r.y + 34, r.width, 42), text);
        Text.Font = GameFont.Small;
    }

    // Perk tree: the five-tier ladder on the left, doctrine choices branching off it on the right, research chips on top.
    private const float NodeW = 384, NodeH = 78, RowH = 88;
    private static readonly Color Line = new(.35f, .35f, .38f), OwnedLine = new(.45f, .8f, .5f), LockedText = new(1f, .6f, .45f);
    private bool Owned(CampPerkDef p) => camp.perks.Contains(p.defName);
    private void DrawPerkTree(Rect area)
    {
        var perks = DefDatabase<CampPerkDef>.AllDefs.Where(p => p.branch == tab).ToList();
        var ladder = perks.Where(p => !p.doctrine).OrderBy(p => p.tier).ToList();
        var doctrines = perks.Where(p => p.doctrine).OrderBy(p => p.tier).ThenBy(p => p.index).ToList();
        float x = area.x + 64;
        Text.Font = GameFont.Tiny;
        Widgets.Label(new Rect(area.x, area.y + 5, 64, 20), "필요 연구");
        foreach (ResearchProjectDef r in ladder.Concat(doctrines).SelectMany(p => p.researchPrerequisites).Where(r => r != null).Distinct())
        {
            string label = r.IsFinished ? r.LabelCap.ToString() : $"{r.LabelCap} {r.ProgressPercent:P0}";
            var chip = new Rect(x, area.y + 2, Text.CalcSize(label).x + 16, 22);
            if (chip.xMax > area.xMax) break;
            Widgets.DrawBoxSolid(chip, r.IsFinished ? new Color(.18f, .32f, .2f) : new Color(.34f, .2f, .16f));
            Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(chip, label); Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawHighlightIfMouseover(chip);
            TooltipHandler.TipRegion(chip, $"{r.LabelCap}" + (r.IsFinished ? " (완료)" : $" — 진행 {r.ProgressPercent:P0}") + $"\n\n{r.description}\n\n클릭하면 연구 탭에서 엽니다.");
            if (Widgets.ButtonInvisible(chip)) OpenResearch(r);
            x += chip.width + 6;
        }
        if (x == area.x + 64) Widgets.Label(new Rect(x, area.y + 5, 200, 20), "없음");
        Text.Font = GameFont.Small;
        float top = area.y + 32;
        Rect Ladder(int tier) => new(area.x, top + (tier - 1) * RowH, NodeW, NodeH);
        // Connectors first, so the nodes cover their ends.
        for (int i = 1; i < ladder.Count; i++)
        {
            Rect a = Ladder(ladder[i - 1].tier), b = Ladder(ladder[i].tier);
            Widgets.DrawLine(new Vector2(a.x + 36, a.yMax), new Vector2(b.x + 36, b.y), Owned(ladder[i]) ? OwnedLine : Line, 2);
        }
        var placed = new List<(CampPerkDef perk, Rect rect)>();
        foreach (var group in doctrines.GroupBy(d => d.tier))
        {
            Rect from = Ladder(group.Key);
            int i = 0;
            foreach (CampPerkDef d in group)
            {
                var r = new Rect(area.x + NodeW + 36, top + (group.Key - 1 + i++) * RowH, NodeW, NodeH);
                float midX = from.xMax + 18;
                Color c = Owned(d) ? OwnedLine : Line;
                Widgets.DrawLine(new Vector2(from.xMax, from.center.y), new Vector2(midX, from.center.y), c, 2);
                Widgets.DrawLine(new Vector2(midX, from.center.y), new Vector2(midX, r.center.y), c, 2);
                Widgets.DrawLine(new Vector2(midX, r.center.y), new Vector2(r.x, r.center.y), c, 2);
                placed.Add((d, r));
            }
            if (group.Key > 1)
            {
                var hint = new Rect(area.x + NodeW + 36, top + (group.Key - 2) * RowH + 8, NodeW, NodeH - 16);
                Text.Font = GameFont.Tiny; GUI.color = new Color(.8f, .8f, .7f);
                Widgets.Label(hint, $"교리 — {PerkSummary.Label(tab)} {group.Key}단계부터 아래 교리를 모두 살 수 있습니다. 단계 퍽보다 비쌉니다.");
                GUI.color = Color.white; Text.Font = GameFont.Small;
            }
        }
        foreach (CampPerkDef p in ladder) DrawNode(Ladder(p.tier), p);
        foreach (var (d, r) in placed) DrawNode(r, d);
    }
    private void DrawNode(Rect r, CampPerkDef p)
    {
        bool owned = Owned(p);
        string locked = owned ? null : camp.LockReason(p);
        bool supplies = locked == null && camp.CanAfford(p), points = locked == null && camp.points >= p.cost;
        Widgets.DrawBoxSolid(r, owned ? new Color(.13f, .22f, .15f) : locked == null ? new Color(.16f, .19f, .24f) : new Color(.12f, .12f, .13f));
        GUI.color = owned ? OwnedLine : supplies || points ? new Color(.55f, .75f, 1f) : Line;
        Widgets.DrawBox(r); GUI.color = Color.white;
        var icon = new Rect(r.x + 8, r.y + (r.height - 56) / 2, 56, 56);
        DrawPerkIcon(icon, p, owned || locked == null ? 1f : .4f);
        float tx = icon.xMax + 8, tw = r.xMax - tx - 6;
        Widgets.Label(new Rect(tx, r.y + 3, tw, 24), ((p.doctrine ? "교리 · " : $"{p.tier}단계 · ") + p.LabelCap).Truncate(tw));
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(.85f, .85f, .85f);
        Widgets.Label(new Rect(tx, r.y + 25, tw, 32), Short(p, tw));
        string status = owned ? "적용됨" : locked ?? $"물자 {(supplies ? "충분" : "부족")} · 계약 점수 {p.cost}점 (보유 {camp.points})";
        GUI.color = owned ? OwnedLine : locked != null ? LockedText : supplies || points ? new Color(.6f, .85f, 1f) : new Color(.8f, .8f, .6f);
        Widgets.Label(new Rect(tx, r.yMax - 19, tw, 18), status.Truncate(tw));
        GUI.color = Color.white; Text.Font = GameFont.Small;
        Widgets.DrawHighlightIfMouseover(r);
        TooltipHandler.TipRegion(r, () => NodeTip(p, locked), 0x6A3C0 + p.index);
        if (!owned && Widgets.ButtonInvisible(r)) BuyMenu(p, locked, supplies, points);
    }
    private static void DrawPerkIcon(Rect r, CampPerkDef p, float alpha)
    {
        Def def = (Def)p.iconThing ?? p.iconKind ?? p.weapon ?? p.mechs.LastOrDefault() ?? (Def)p.beasts.LastOrDefault();
        AbilityDef ability = p.iconAbility ?? p.psycasts.LastOrDefault();
        if (def != null) { Widgets.DefIcon(r, def, def is ThingDef t ? CampLayout.StuffFor(t) : null, alpha: alpha); return; }
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(r, ability?.uiIcon ?? TexCommand.DesirePower);
        GUI.color = Color.white;
    }
    // Two Tiny lines on the node; the full text is in the tooltip.
    private readonly Dictionary<CampPerkDef, string> shortDesc = new();
    private string Short(CampPerkDef p, float width)
    {
        if (shortDesc.TryGetValue(p, out string s)) return s;
        s = p.description;
        if (Text.CalcHeight(s, width) > 32)
        {
            int n = s.Length;
            while (n > 0 && Text.CalcHeight(s.Substring(0, n) + "...", width) > 32) n--;
            s = s.Substring(0, n).TrimEnd() + "...";
        }
        return shortDesc[p] = s;
    }
    private string NodeTip(CampPerkDef p, string locked)
    {
        var lines = new List<string> { p.LabelCap, "", p.description, "" };
        lines.Add("물자: " + string.Join(" · ", p.price.Select(c => $"{c.thingDef.label} {c.count} (보유 {camp.Count(c.thingDef)})")));
        lines.Add($"또는 계약 점수 {p.cost}점 (보유 {camp.points}점)");
        if (p.researchPrerequisites.Count > 0) lines.Add("연구: " + string.Join(", ", p.researchPrerequisites.Select(r => r.LabelCap + (r.IsFinished ? " (완료)" : " (미완료)"))));
        if (p.doctrine) lines.Add($"교리: {PerkSummary.Label(p.branch)} {p.tier}단계부터 살 수 있고, 같은 분기의 다른 교리와 함께 적용됩니다.");
        if (locked != null && !Owned(p)) { lines.Add(""); lines.Add(locked); }
        return string.Join("\n", lines);
    }
    private void BuyMenu(CampPerkDef p, string locked, bool supplies, bool points)
    {
        if (locked != null) { Messages.Message(locked, MessageTypeDefOf.RejectInput, false); return; }
        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
        {
            new(supplies ? $"물자로 즉시 구매: {MapComponent_Camp.PriceText(p)}" : $"물자 부족: {MapComponent_Camp.PriceText(p)}", supplies ? () => Result(camp.Buy(p, true, out string error), error) : null),
            new(points ? $"계약 점수로 구매: {p.cost}점" : $"계약 점수 부족: {p.cost}점 필요 (보유 {camp.points}점)", points ? () => Result(camp.Buy(p, false, out string error), error) : null)
        }));
    }
    private void OpenResearch(ResearchProjectDef r)
    {
        Close();
        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Research);
        (MainButtonDefOf.Research.TabWindow as MainTabWindow_Research)?.Select(r);
    }
}
