using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>제작 계획 세우기·말하기·이어 하기(설계 C, 실행은 PlanJob).</summary>
    public static partial class Departures
    {
        // ---- 제작 계획 (설계 C, 계획을 말한 뒤 PlanJob으로 실행) ----

        /// <summary>시험용(vh_restartcraft): 이번 실행에서 저장한 계획으로 이어 간 수, 계획을 다시 세운 수.</summary>
        internal static int ResumedPlans, ReplannedPlans;

        /// <summary>
        /// 저장에서 불러온 제작 칸을 다시 시작. 저장한 계획(w.Plan)이 있으면 그 단계·상태·진행으로 이어 간다(외부 검토 + 사용자 승인 10-26):
        /// 설비에 넣은 것·나오는 중인 것은 이 작업의 남은 기다림·꺼내기로 남아 재고로 다시 세거나 다른 작업과 나눠 세지 않는다.
        /// 없거나(옛 저장) 깨졌으면 현재 재고로 새로 세운다.
        /// </summary>
        public static void ResumeMake(WorkerNpc npc, WorkItem w)
        {
            PlanRunner saved = Features.RunPlans ? PlanSave.Load(w.Plan, s => MachineStep.AlreadyCooking(npc.Home, s)) : null;
            if (saved != null)
            {
                ResumedPlans++;
                Plugin.Log.LogInfo($"{npc.NpcName} 계획 이어 함(저장): {w.Label} — 끝 {saved.Count(StepState.Done)}/{saved.Plan.Steps.Count}, 돌던 기다림 {saved.Count(StepState.Running)}");
                npc.Ai.StartJob(new PlanJob(npc, saved, w.Label) { Goal = w });
                return;
            }
            ReplannedPlans++;
            if (!string.IsNullOrEmpty(w.Plan)) Plugin.Log.LogWarning($"{npc.NpcName} 저장된 계획을 못 읽음 — {w.Label} 다시 세움");
            else Plugin.Log.LogInfo($"{npc.NpcName} 저장된 계획 없음 — {w.Label} 다시 세움");
            if (w.SetNo > 0) MakeSet(npc, w.SetNo, w.Amount, w.Base);
            else Craft(npc, new ChatCommand { Kind = CommandKind.Item, Item = w.Item, Amount = w.Amount }, w.Base);
        }

        /// <summary>
        /// 제작 수량 = 새로 만들 수(사용자 11-14 "나무 20은 20개 해 오라는 것, 상자 포함 20이 아님"). 계획(CraftPlanner)은 "상자에 n개 되게"라
        /// 명령 때 상자에 있던 수(bases)를 더해 목표로 준다. fresh = 채팅 명령(지금 상자 수를 잼), 아니면 저장된 bases(없으면 옛 저장 — 목표 = 수량).
        /// </summary>
        static string BaseOf(Vector3 home, IEnumerable<string> items)
        {
            var stock = BaseStock(home);
            var parts = new List<string>();
            foreach (string it in items) { stock.TryGetValue(it, out int n); parts.Add(it + ":" + n); }
            return string.Join(",", parts.ToArray());
        }

        static int BaseFor(string bases, string item)
        {
            if (string.IsNullOrEmpty(bases)) return 0;
            foreach (string p in bases.Split(','))
            {
                int c = p.LastIndexOf(':');
                if (c > 0 && p.Substring(0, c) == item && int.TryParse(p.Substring(c + 1), out int n)) return n;
            }
            return 0;
        }

        static void Craft(WorkerNpc npc, ChatCommand cmd, string bases = null, bool fresh = false)
        {
            RecipeBook book = Recipes.Book();
            if (book.RecipesFor(cmd.Item).Count == 0 && book.ProcessesFor(cmd.Item).Count == 0) { npc.Say("notYet"); return; }
            if (fresh) bases = BaseOf(npc.Home, new[] { cmd.Item });
            Plan plan = PlanFor(npc, new List<Ingredient> { new Ingredient(cmd.Item, BaseFor(bases, cmd.Item) + cmd.Amount) });
            var args = new Dictionary<string, string>
            {
                ["item"] = GatherJob.ItemLabel(cmd.Item), ["n"] = cmd.Amount.ToString(), ["minutes"] = Departure.Minutes5(plan.Seconds).ToString(),
            };
            if (FarmFor(npc, plan, args)) return;
            SayPlan(npc, plan, args);
            StartPlan(npc, plan, args["item"], new WorkItem { Kind = WorkKind.Make, Resume = true, Label = args["item"], Item = cmd.Item, Amount = cmd.Amount, Base = bases });
        }

        /// <summary>
        /// 계획이 끝까지 할 수 있거나 모자란 게 설비뿐이면(StationsOnly — 재료는 모아 두고 끝에 지어 달라고 함, 10-26) 실제로 시작(6번 실행기).
        /// 범위 밖·모름이 끼면 시작하지 않음. 일하는 중이면 시작하지 않고 대기열에(꺼낼 때 다시 세움, 회차 660).
        /// </summary>
        static void StartPlan(WorkerNpc npc, Plan plan, string label, WorkItem goal)
        {
            if (!Features.RunPlans || plan.Steps.Count == 0 || (plan.Partial && !plan.StationsOnly) || plan.TooLong) return;
            var mode = npc.Ai.Current;
            // Work인데 하던 일이 없으면 대기열에서 꺼내는 중(앞 일이 막 끝남, WorkerAI.NextQueued) — 쉬는 것과 같다(회차 128: 죽은 뒤 이어 할 계획이 여기서 버려짐)
            bool busy = mode == WorkerAI.Mode.Work ? npc.Ai.Job != null && !(npc.Ai.Job is TidyJob) : mode != WorkerAI.Mode.Idle && mode != WorkerAI.Mode.Home;
            if (busy)
            {
                // 일하는 중이면 대기열에(회차 660 — 최종 실전 시험에서 일하는 중 받은 활·구리주괴·세트·굽기 명령이 버려짐). 꺼낼 때 ResumeMake가 지금 재고로 다시 세움
                if (mode == WorkerAI.Mode.Work && goal != null && goal.Kind == WorkKind.Make)
                {
                    npc.Ai.Queue.Add(WorkSaves.ToQueued(npc, goal));
                    Plugin.Log.LogInfo($"{npc.NpcName} 계획 {label}: 하던 일 → 대기열");
                    return;
                }
                Plugin.Log.LogInfo($"{npc.NpcName} 계획 {label}: 지금 {mode}라 시작 안 함");
                return;
            }
            npc.Ai.StartJob(new PlanJob(npc, plan, label) { Goal = goal });
        }

        /// <summary>!세트 n 수량: 세트 안의 물건마다 수량만큼(6-4). 범위 밖 물건은 빼고 계획.</summary>
        public static void MakeSet(WorkerNpc npc, int number, int amount, string bases = null, bool fresh = false)
        {
            var targets = Brain.Sets.Targets(number, amount);
            if (fresh) { var names = new List<string>(); foreach (var t in targets) names.Add(t.Item); bases = BaseOf(npc.Home, names); }
            for (int k = 0; k < targets.Count; k++) targets[k] = new Ingredient(targets[k].Item, BaseFor(bases, targets[k].Item) + targets[k].Amount); // 세트도 "수량만큼 더"(11-14)
            var args = new Dictionary<string, string> { ["no"] = number.ToString(), ["item"] = "세트 " + number + "번", ["n"] = amount.ToString() };
            if (targets.Count == 0) { npc.Say("setNone", args); return; }
            Plan plan = PlanFor(npc, targets);
            if (plan.OutOfScope.Count > 0)
            {
                targets.RemoveAll(t => plan.OutOfScope.Contains(t.Item));
                if (targets.Count == 0) { npc.Say("depart.outOfScope", args); return; }
                plan = PlanFor(npc, targets);
            }
            args["minutes"] = Departure.Minutes5(plan.Seconds).ToString();
            if (FarmFor(npc, plan, args)) return;
            SayPlan(npc, plan, args);
            StartPlan(npc, plan, args["item"], new WorkItem { Kind = WorkKind.Make, Resume = true, Label = args["item"], SetNo = number, Amount = amount, Base = bases });
        }

        static Plan PlanFor(WorkerNpc npc, List<Ingredient> targets)
        {
            RecipeBook book = Recipes.Book();
            Vector3 home = npc.Home;
            var stock = BaseStock(home);
            ZoneXY homeZone = Senses.ZoneOf(home);
            return PlanWith(npc, book, home, homeZone, targets, stock);
        }

        /// <summary>거점 재료 상자의 물건 수(계획이 "있는 것"으로 보는 수 — 제작 수량 기준 BaseOf도 같은 것을 씀).</summary>
        static Dictionary<string, int> BaseStock(Vector3 home)
        {
            var stock = new Dictionary<string, int>();
            foreach (var c in Chests.NearBase(home, ChestRole.Ingredients))
                foreach (var it in c.GetInventory().GetAllItems())
                {
                    // 부서진 연장·장비(내구도 1 이하, WornOut)는 있는 것으로 안 셈: 일에는 못 쓰는데(BestTool·출발 판단은 뺌) "상자에 있어"로 제작을 안 해
                    // 연장 부러짐 뒤 다시 건 일이 연장 없음으로 거절되고 대기열이 비었음(vh_toolbreaktest — 거점 제작대로 못 고친 부러진 곡괭이는 짐 정리가 상자에 넣음)
                    if (it.m_dropPrefab == null || WorkerNpc.WornOut(it)) continue;
                    string key = Utils.GetPrefabName(it.m_dropPrefab);
                    stock.TryGetValue(key, out int n);
                    stock[key] = n + it.m_stack;
                }
            return stock;
        }

        static Plan PlanWith(WorkerNpc npc, RecipeBook book, Vector3 home, ZoneXY homeZone, List<Ingredient> targets, Dictionary<string, int> stock)
        {
            var planner = new CraftPlanner(book, Recipes.At(home), (item, n) =>
            {
                var zones = Brain.Get().ZonesWith(item, homeZone);
                if (zones.Count == 0) return float.NaN; // 어디 있는지 모름: 찾기 상한으로 셈
                float dist = Utils.DistanceXZ(home, Senses.ZoneCenter(zones[0].Zone));
                return dist * 2f / Departure.WalkSpeed + n * 4f;
            });
            Plan plan = planner.Expand(targets, stock);
            Plugin.Log.LogInfo($"{npc.NpcName} 제작 계획 {targets[0].Item} {targets[0].Amount}(목표 {targets.Count}개): 단계 {plan.Steps.Count}, {plan.Seconds:0}초, 모을 것 [" +
                string.Join(", ", new List<string>(Keys(plan.Gathers)).ToArray()) + $"], 없는 설비 [{string.Join(", ", plan.MissingStations.ToArray())}], " +
                $"범위 밖 [{string.Join(", ", plan.OutOfScope.ToArray())}], 모름 [{string.Join(", ", plan.Unknown.ToArray())}]");
            return plan;
        }

        static void SayPlan(WorkerNpc npc, Plan plan, Dictionary<string, string> args)
        {
            if (plan.OutOfScope.Count > 0) { npc.Say("depart.outOfScope", args); return; }
            if (plan.Steps.Count == 0 && !plan.Partial) { npc.Say("craft.inStock", args); return; }
            if (plan.MissingStations.Count > 0)
            {
                args["station"] = Recipes.StationName(plan.MissingStations[0]);
                // 설비만 없고 모을 재료가 있으면 모아 둔다(StartPlan이 시작) — 끝나면 PlanJob이 craft.missingDone
                npc.Say(plan.StationsOnly && plan.Steps.Count > 0 && !plan.TooLong && Features.RunPlans ? "craft.missingGather" : "craft.missing", args);
                return;
            }
            if (plan.Unknown.Count > 0) { args["what"] = GatherJob.ItemLabel(plan.Unknown[0]); npc.Say("craft.unknown", args); return; }
            if (plan.TooLong) { npc.Say("craft.tooLong", args); return; }
            var shortNames = new List<string>();
            foreach (var s in plan.Short) shortNames.Add(GatherJob.ItemLabel(s));
            args["short"] = shortNames.Count == 2 ? shortNames[0] + Josa.For(shortNames[0], "이랑") + " " + shortNames[1] : shortNames.Count == 1 ? shortNames[0] : args["item"];
            npc.Say("craft.plan", args);
        }

        static IEnumerable<string> Keys(Dictionary<string, int> d)
        {
            foreach (var kv in d) yield return kv.Key + " " + kv.Value;
        }
    }
}
