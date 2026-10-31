using System.Collections.Generic;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Storage;
using ValheimHelper.Core.Work;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 내 개인 상자 준비(상자 기능 ③, 사용자 결정 2026-10-23): "!준비 미드 5"(한 번) · "!채워 개인 미드 5"(유지) · "!준비 끔 [물건]".
    /// 내 개인 상자 = 이름에 개인·보물(ChestTags.Private) + 내가 지은 상자(Piece 만든 이 = 내 플레이어 ID), 일꾼 거점 125m 안·불러와진 것.
    /// 일꾼은 개인 상자에 넣기만 한다. 꺼내는 곳은 개인 아닌 거점 상자(열린 것 빼고). 쉬는 일꾼이 CheckEvery초마다(명령 직후엔 바로) —
    /// 플레이어가 모두 64m 밖이면 걷지 않고 바로, 아니면 TidyJob 걸음(꺼내 들고 → 개인 상자에 넣기, 그만두면 든 것 되돌림).
    /// 나-1: 거점 상자에 있는 것을 옮김. 나-2: 거점 상자에도 모자란 몫은 자동 보충이 채집·제작으로 구해 거점 상자에 넣고(Shortages),
    /// 다음 확인 때 옮김. 구할 수 없거나 출발 판단이 막으면(10분 쉼) 한 번 준비는 "모자라" 말하고 지움, 유지는 기다림.
    /// </summary>
    public static class PersonalChests
    {
        internal static float CheckEvery = 60f;
        /// <summary>시험 중엔 꺼 둔다 — vh_mychesttest만 켬.</summary>
        internal static bool TestAllow;
        static float s_timer = 60f;

        /// <summary>말한 사람의 플레이어 ID(캐릭터 ZDO의 playerID — Piece 만든 이와 같은 값). 모르면 0.</summary>
        internal static long OwnerOf(Speaker who)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(who.CharacterId) : null;
            return zdo != null ? zdo.GetLong(ZDOVars.s_playerID, 0L) : 0L;
        }

        static string NameOf(long owner)
        {
            foreach (var p in Player.GetAllPlayers())
                if (p != null && p.GetPlayerID() == owner) return p.GetPlayerName();
            return "주인";
        }

        /// <summary>그 사람의 개인 상자들(가까운 순).</summary>
        internal static List<Container> Of(long owner, Vector3 home)
        {
            var list = new List<Container>();
            if (owner == 0) return list;
            var provider = Chests.TagProvider;
            foreach (var zdo in Senses.Nearby(home, Chests.BaseRange, Chests.IsBaseChest))
            {
                if (provider == null || (provider(zdo) & ChestTags.Private) == 0 || zdo.GetLong(ZDOVars.s_creator, 0L) != owner) continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                Container c = view != null ? view.GetComponent<Container>() : null;
                if (c != null) list.Add(c);
            }
            list.Sort((a, b) => Utils.DistanceXZ(a.transform.position, home).CompareTo(Utils.DistanceXZ(b.transform.position, home)));
            return list;
        }

        /// <summary>!계속채워 목록의 개인 몫: 그 사람 것만 "물건 수/목표 막대" 줄로.</summary>
        internal static List<string> StatusLines(long owner, Vector3 home)
        {
            var lines = new List<string>();
            if (owner == 0) return lines;
            foreach (var e in Brain.Personal.Entries)
                if (e.Owner == owner) lines.Add(RefillBook.Line(GatherJob.ItemLabel(e.Item), Stock(owner, e.Item, home), e.Amount, e.Once));
            return lines;
        }

        static int Stock(long owner, string item, Vector3 home)
        {
            int n = 0;
            foreach (var c in Of(owner, home)) n += Tidy.Count(c, item);
            return n;
        }

        /// <summary>꺼낼 수 있는 상자: 개인·보물 아닌 거점 상자, 누가 연 것 빼고, 그 물건이 많은 순.</summary>
        static List<Container> Sources(Vector3 home, string item)
        {
            var list = new List<Container>();
            var provider = Chests.TagProvider;
            foreach (var zdo in Senses.Nearby(home, Chests.BaseRange, Chests.IsBaseChest))
            {
                if (provider != null && (provider(zdo) & ChestTags.Private) != 0) continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                Container c = view != null ? view.GetComponent<Container>() : null;
                if (c != null && !Chests.InUse(c) && Tidy.Count(c, item) > 0) list.Add(c);
            }
            list.Sort((a, b) => Tidy.Count(b, item).CompareTo(Tidy.Count(a, item)));
            return list;
        }

        /// <summary>개인·보물 아닌 거점 상자에 든 수(열린 상자 포함 — 누가 잠깐 연 사이에 또 구하러 가지 않게).</summary>
        static int BaseCount(Vector3 home, string item)
        {
            int n = 0;
            var provider = Chests.TagProvider;
            foreach (var zdo in Senses.Nearby(home, Chests.BaseRange, Chests.IsBaseChest))
            {
                if (provider != null && (provider(zdo) & ChestTags.Private) != 0) continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                Container c = view != null ? view.GetComponent<Container>() : null;
                if (c != null) n += Tidy.Count(c, item);
            }
            return n;
        }

        /// <summary>
        /// 나-2: 거점 상자로도 못 채우는 몫 — 자동 보충(Departures.RefillDue)이 채집·제작 일로 구해 거점 상자에 넣는다.
        /// 개인 상자로 옮기는 건 그 뒤 Tick. 거점 상자에 개인 상자가 없는 사람 것도 셈(명령 때 확인함 — 상자를 부쉈으면 구해만 둠).
        /// </summary>
        internal static List<Ingredient> Shortages(Vector3 home)
        {
            if (Brain.Personal.Count == 0) return new List<Ingredient>();
            return Brain.Personal.Short((o, it) => Stock(o, it, home), it => BaseCount(home, it));
        }

        /// <summary>옮길 것(모자란 물건마다 가장 많이 든 상자 하나 → 자리 있는 첫 개인 상자, 모자란 수만큼). TidyJob이 다녀올 때마다 다시 부른다.</summary>
        internal static List<Tidy.Move> Plan(Vector3 home)
        {
            var moves = new List<Tidy.Move>();
            foreach (var need in Brain.Personal.Due((o, it) => Stock(o, it, home)))
            {
                List<Container> src = Sources(home, need.Entry.Item);
                if (src.Count == 0) continue;
                ItemDrop.ItemData sample = Sample(src[0], need.Entry.Item);
                Container to = null;
                foreach (var c in Of(need.Entry.Owner, home)) if (sample != null && c.GetInventory().CanAddItem(sample, 1)) { to = c; break; }
                if (to == null) continue;
                moves.Add(new Tidy.Move { From = src[0], To = to, Name = need.Entry.Item, Count = need.Missing });
            }
            return moves;
        }

        static ItemDrop.ItemData Sample(Container c, string item)
        {
            foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == item) return it;
            return null;
        }

        // ---- 명령 ----

        internal static void Command(Speaker who, ChatCommand cmd, WorkerNpc npc)
        {
            long owner = OwnerOf(who);
            var args = new Dictionary<string, string> { ["owner"] = who.Name, ["item"] = cmd.Item != null ? GatherJob.ItemLabel(cmd.Item) : "전부", ["n"] = cmd.Amount.ToString() };
            if (owner == 0) { Plugin.Log.LogWarning($"개인 상자 준비: {who.Name} 플레이어 ID 모름"); npc.Say("notYet"); return; }
            if (cmd.Kind == CommandKind.PrepOff)
            {
                int n = Brain.Personal.Off(owner, cmd.Item);
                Plugin.Log.LogInfo($"개인 상자 준비 끔: {who.Name} {cmd.Item ?? "전부"} ({n}개 지움)");
                npc.Say("prepOff", args);
                return;
            }
            if (Of(owner, npc.Home).Count == 0) { npc.Say("prepNoChest", args); return; }
            bool once = cmd.Kind == CommandKind.Prep;
            Brain.Personal.Set(owner, cmd.Item, cmd.Amount, once);
            Plugin.Log.LogInfo($"개인 상자 준비: {who.Name}({owner}) {cmd.Item} {cmd.Amount} {(once ? "한 번" : "유지")}, 지금 {Stock(owner, cmd.Item, npc.Home)}");
            npc.Say(once ? "prepOnce" : "prepKeep", args);
            s_timer = Mathf.Min(s_timer, 1f); // 바로(말이 먼저 나가게 1초 뒤)
        }

        // ---- 쉴 때 채우기 ----

        /// <summary>
        /// CheckEvery초마다(호스트만): 모자란 것이 있으면 거점 30m 안에서 쉬는 일꾼 하나가 채운다(Tidy.Tick과 같은 조건, 정리 걸음 중이면 안 함).
        /// 거점 상자에 아예 없는 한 번 준비는 "모자라"(지금 개인 상자 수)를 말하고 지운다.
        /// </summary>
        public static void Tick(float dt)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (TestRunner.Running && !TestAllow) return;
            s_timer -= dt;
            if (s_timer > 0f) return;
            s_timer = CheckEvery;
            if (Brain.Personal.Count == 0) return;
            WorkerNpc npc = null;
            foreach (var n in WorkerNpc.All)
            {
                if (n == null || n.Ai == null) continue;
                if (n.Ai.Job is TidyJob) return;
                if (npc == null && n.Ai.Current == WorkerAI.Mode.Idle && n.Ai.Queue.Count == 0 && Departures.AskedAt(n) < 0 && !n.Ai.HasChores
                    && !n.Ai.GuardBusy && !n.InInterior() && Utils.DistanceXZ(n.transform.position, n.Home) <= 30f) npc = n;
            }
            if (npc == null) return;
            Vector3 home = npc.Home;
            foreach (var need in Brain.Personal.Due((o, it) => Stock(o, it, home)))
            {
                if (!need.Entry.Once || Sources(home, need.Entry.Item).Count > 0) continue;
                // 나-2: 구할 수 있으면(채집·제작·밭, 출발 판단이 막지 않음) 자동 보충이 구해 올 때까지 둔다. 누가 구하는 중이어도 둠.
                if (Departures.RefillBusy(need.Entry.Item) || (Departures.Obtainable(need.Entry.Item) && !Departures.RefillSkipped(need.Entry.Item))) continue;
                int have = need.Entry.Amount - need.Missing;
                Plugin.Log.LogInfo($"개인 상자 준비: {need.Entry.Item} 거점 상자에 없고 구할 수 없음 → 한 번 준비 끝(개인 상자 {have}/{need.Entry.Amount})");
                npc.Say("prepShort", new Dictionary<string, string> { ["owner"] = NameOf(need.Entry.Owner), ["item"] = GatherJob.ItemLabel(need.Entry.Item), ["n"] = have.ToString() });
                Brain.Personal.Done(need.Entry);
            }
            List<Tidy.Move> plan = Plan(home);
            if (plan.Count == 0) return;
            if (GatherJob.FarFromPlayers(home))
            {
                int moved = 0;
                foreach (var m in plan) moved += MoveNow(m);
                if (moved > 0) BaseStock.Changed();
                Plugin.Log.LogInfo($"{npc.NpcName} 개인 상자 채움(플레이어 멀어 바로): {moved}개");
                return;
            }
            Plugin.Log.LogInfo($"{npc.NpcName} 개인 상자 채우러(걸어서): 옮길 것 {plan.Count}");
            npc.Ai.StartJob(new TidyJob(npc, Plan, "state.prep"));
        }

        /// <summary>바로 옮김(두 상자 TryEdit 겹쳐, 최대 Count개). 옮긴 수. 합이 바뀌면 오류 로그.</summary>
        static int MoveNow(Tidy.Move m)
        {
            int sum = Tidy.Count(m.From, m.Name) + Tidy.Count(m.To, m.Name), moved = 0;
            Chests.TryEdit(m.To, tinv => Chests.TryEdit(m.From, finv => { moved = Tidy.TakeSome(finv, tinv, m.Name, m.Count); }));
            int after = Tidy.Count(m.From, m.Name) + Tidy.Count(m.To, m.Name);
            if (after != sum) Plugin.Log.LogError($"개인 상자 준비: {m.Name} 합이 바뀜 {sum} → {after}");
            return moved;
        }

        /// <summary>시험용: 다음 Tick에 바로 확인.</summary>
        internal static void TestNow() => s_timer = 0f;
    }
}
