using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_finaltest 최종 실전 시험(사용자 11-12, decisions 71): 시험 기본 집에서 한 번만 되돌린 뒤 일꾼 2+호위병 1, 채팅 명령을 두 일꾼에게 나눠 한꺼번에 내려
    /// 명령마다 거점 상자 개수로 PASS/FAIL. 되돌리기·상자 채우기 없이 이어 간다. 나무·돌·구리·구리주괴·활·세트·굽기·사냥·밭·가축·상자 정리.
    /// </summary>
    static class FinalTest
    {
        public static void Register() => TestRunner.Register("vh_finaltest", "최종 실전 시험: 이름 없는 실제 말투 명령 한꺼번에(알아들음·해냄 따로, 되돌림 한 번)", Run, 3600f);

        static bool s_dbgTime;
        static float s_dbgValue;

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            bool god = player.InGodMode();
            // 낮으로 고정(시작이 밤이면 귀환이 걸림 — GatherTest와 같은 까닭). End가 되돌림
            s_dbgTime = EnvMan.instance.m_debugTimeOfDay; s_dbgValue = EnvMan.instance.m_debugTime;
            EnvMan.instance.m_debugTimeOfDay = true; EnvMan.instance.m_debugTime = 0.5f;
            player.SetGodMode(true);
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;

            // 1) 시작 때 한 번만 되돌림: 시험 기본 집(te4st41)에 깃발·상자 2개
            var site = new HouseBase.Site();
            yield return HouseBase.Go(site);
            checks.Add($"시험 기본 집 씀 ({site.Why})", site.Ok);
            if (!site.Ok) { End(player, god, checks, onHeard, heard); yield break; }
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f);
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(3f);
            checks.Add($"깃발에 일꾼 2명 나타남 ({WorkerNpc.All.Count}명)", WorkerNpc.All.Count == 2);
            if (WorkerNpc.All.Count != 2) { End(player, god, checks, onHeard, heard); yield break; }
            // 처음부터 일꾼 2 + 호위병 1(사용자 11-12 23:22 — 2명 판 없음)
            yield return TestKit.SpawnGuard();
            checks.Add($"일꾼 2 + 호위병 1 = 3명 ({WorkerNpc.All.Count} + {(WorkerNpc.TheGuard != null ? 1 : 0)})", WorkerNpc.All.Count == 2 && WorkerNpc.TheGuard != null);
            // 남은 명령(밭·굽기·가축·사냥)의 재료는 시작 때 한 번만 거점 상자에 넣는다(decisions 71 — 명령 중엔 상자 채우기 없음)
            StockStart(home);
            // 밭·가축·정리 명령 자리(시작 때 한 번 — 회차 661): 밭은 집 왼쪽 12m 반경 3(FarmTest 집 모드와 같은 자리), 가축은 오른쪽 12m에 "가축" 상자+산딸기+길들인 배고픈 멧돼지
            Vector3 right = Vector3.Cross(Vector3.up, player.transform.forward).normalized;
            Vector3 fieldAt = GatherTest.Ground(home - right * 12f);
            var field = new FarmField(fieldAt.x, fieldAt.z, 3);
            FarmTest.Clean(field);
            Vector3 penAt = GatherTest.Ground(home + right * 12f);
            player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), penAt, player.transform.rotation, doAttack: false);
            Container pen = GatherTest.ChestAt(penAt);
            Tameable pig = null;
            if (pen != null)
            {
                pen.GetComponent<ZNetView>().GetZDO().Set("ChestName", "가축");
                TestKit.PutInChest(pen, "Raspberry", 5);
                var g = Object.Instantiate(ZNetScene.instance.GetPrefab("Boar"), GatherTest.Ground(penAt + right * 4f) + Vector3.up * 0.3f, Quaternion.identity);
                g.GetComponent<MonsterAI>().MakeTame();
                g.GetComponent<MonsterAI>().SetPatrolPoint(); // 우리에 세워 둠(LivestockTest와 같이) — 안 그러면 떠돌다 10m 밖으로(회차 663 FAIL)
                pig = g.GetComponent<Tameable>();
            }
            yield return new WaitForSeconds(1f);
            checks.Add($"가축 상자·길들인 배고픈 멧돼지 놓음", pen != null && pig != null && pig.IsHungry());

            TestRunner.Log($"DIAG 시작 거점 상자: Wood {GatherTest.CountBase(home, "Wood")}, Stone {GatherTest.CountBase(home, "Stone")}, CopperOre {GatherTest.CountBase(home, "CopperOre")}, Copper {GatherTest.CountBase(home, "Copper")}, RawMeat {GatherTest.CountBase(home, "RawMeat")}");
            // 2) 실제 말투(사용자 11-14 다시 짜기 가): 이름 없는 자연스러운 말을 처음에 한꺼번에 — 누가 할지는 일꾼(Dispatch)이, 말은 모드 또는 Claude가 해석.
            //    판정은 둘로 나눔: "알아들음"(해석된 명령이 기대와 같나, Commands.Ran) · "해냄"(거점 상자 개수 등). 구리 광석·생고기는 다른 일이 써서 줄 수 있어 넣은 수·가장 많았던 수로.
            // 실전처럼 쉬는 일꾼 판단을 켬(사용자 11-14 "왜 여전히 한 놈만 일하지"). 시작 검사 뒤에 켬(FishTest와 같은 까닭), End에서 끔.
            Departures.TestIdleChores = true;
            s_ran.Clear();
            Commands.Ran += OnRan;
            var orders = new List<Want>();
            // 밭: 말한 사람 자리가 중심이고 Claude 답이 늦게 오므라 밭 자리에서 말하고 알아들을 때까지(최대 120초) 서 있다 돌아옴(일꾼은 안 옮김 — 순간이동 빼기는 다) 조각)
            Vector3 back = player.transform.position;
            player.TeleportTo(fieldAt + Vector3.up * 0.5f, player.transform.rotation, distantTeleport: false);
            yield return new WaitForSeconds(3f);
            var farm = Say("여기 밭 좀 만들어 줘, 반경 3", CommandKind.Farm, null, 3);
            farm.N = 4; farm.Count = () => FarmTest.Plants(field).Count; farm.Goal = "당근 새싹";
            orders.Add(farm);
            for (float w = 0f; w < 120f && farm.Heard == null; w += 1f) { yield return new WaitForSeconds(1f); Match(orders); }
            player.TeleportTo(back, player.transform.rotation, distantTeleport: false);
            orders.Add(Say("나무 10개 좀 해 와", CommandKind.Item, "Wood", 10, home));
            orders.Add(Say("돌 10개 캐 와", CommandKind.Item, "Stone", 10, home));
            orders.Add(Say("구리 10개", CommandKind.Item, "CopperOre", 10, home)); // 정해진 꼴은 모드가 바로
            orders.Add(Say("활 3개 만들어 줘", CommandKind.Item, "Bow", 3, home, make: true)); // 활은 제작(작업대·재료). 화살은 활이 챙김
            orders.Add(Say("구리주괴 10", CommandKind.Item, "Copper", 10, home, make: true)); // 제련: 거점 제련소·석탄
            orders.Add(SetSay(checks, home));
            orders.Add(Say("고기 4개 구워 줘", CommandKind.Item, "CookedMeat", 4, home, make: true)); // 시작 생고기 4개를 구움
            orders.Add(Say("멧돼지 잡아서 생고기 6개 가져와", CommandKind.Item, "RawMeat", 6, home)); // 사냥: 넣은 수로(굽기가 씀)
            if (pig != null)
                orders.Add(new Want { Text = "(명령 없음 — 쉬는 일꾼이 알아서 가축 먹이, 11-14 !가축 없앰)", Goal = "멧돼지 먹음", N = 1, Count = () => pig != null && !pig.IsHungry() ? 1 : 0 });
            int tidy0 = Tidy.Plan(home).Count, tidyMin = tidy0;
            var tidy = Say("상자 정리 좀 해 줘", CommandKind.Tidy, null, 0);
            tidy.Goal = $"옮길 것 {tidy0} → 0"; tidy.N = 1;
            tidy.Count = () => { int left = Tidy.Plan(home).Count; if (left < tidyMin) { tidyMin = left; TestRunner.Log($"DIAG 정리 남은 것 {left} ({tidy.Who} {(WorkerNpc.All.Find(n => n != null && n.NpcName == tidy.Who)?.Ai.Job?.GetType().Name ?? "일 없음")})"); } return left == 0 ? 1 : 0; };
            orders.Add(tidy);
            orders.RemoveAll(o => o == null);
            float t = 0f;
            while (t < 3000f && orders.Exists(o => o.Done < 0f))
            {
                yield return new WaitForSeconds(5f);
                t += 5f;
                Match(orders);
                foreach (var o in orders) o.Poll(home, t);
            }
            Commands.Ran -= OnRan;
            foreach (var r in s_ran) TestRunner.Log($"DIAG 짝 없는 해석: {r.Key} {Desc(r.Value)}");
            foreach (var o in orders)
            {
                if (o.Kind != null) checks.Add($"알아들음 '{o.Text}' → {(o.Heard == null ? "해석 없음" : o.Who + " " + Desc(o.Heard))}", o.HeardOk);
                checks.Add($"해냄 '{o.Text}' ({o.Who ?? "누구도 안 맡음"}) → {o.Goal ?? o.Item} {o.Best}/{o.N} ({(o.Done >= 0f ? o.Done : t):0}초)", o.Done >= 0f);
            }
            if (Brain.Sets.Get(s_setNo) != null) Brain.Sets.Remove(s_setNo);

            End(player, god, checks, onHeard, heard);
        }

        /// <summary>시작 때 한 번: 밭(경작기·당근 씨앗), 굽기(생고기), 가축(당근 먹이)에 쓸 재료를 거점 상자에 넣는다. 첫 상자가 꽉 차면 다음 상자(회차 657 — 생고기 못 넣음).</summary>
        static void StockStart(Vector3 home)
        {
            var boxes = new List<Container>(Chests.NearBase(home));
            if (boxes.Count == 0) { TestRunner.Log("FAIL: 거점 상자가 없어 시작 재료를 못 넣음"); return; }
            Put(boxes, Crops.Cultivator, 1);
            Put(boxes, "CarrotSeeds", 6);
            Put(boxes, "RawMeat", 4);
            Put(boxes, "Carrot", 10);
        }

        static void Put(List<Container> boxes, string prefab, int n)
        {
            var item = ObjectDB.instance.GetItemPrefab(prefab);
            foreach (var c in boxes)
            {
                bool added = false;
                if (Chests.TryEdit(c, inv => added = inv.AddItem(item, n)) && added) return;
            }
            TestRunner.Log($"시험 상자에 {prefab} {n}개 못 넣음(상자 {boxes.Count}개 모두 꽉 참·사용 중)");
        }

        /// <summary>명령 하나: 한 말(Text), 기대한 해석(Kind·Item·Amount), 실제 해석(Heard·Who), 거점 상자에 n개 더 생긴 때(Done, 초). Item2가 있으면 둘 다(세트).</summary>
        sealed class Want
        {
            public string Text, Goal, Who, Item, Item2;
            public CommandKind? Kind;
            public int Amount, N, Before, Before2, Best;
            public ChatCommand Heard;
            public bool HeardOk;
            public float Done = -1f;
            public System.Func<int> Count; // 상자 개수 대신 셀 것(밭 새싹·가축 먹음·정리 남은 것)

            public void Poll(Vector3 home, float t)
            {
                if (Done >= 0f) return;
                int got = Count != null ? Count() : GatherTest.CountBase(home, Item) - Before;
                if (Count == null && Item2 != null) got = Mathf.Min(got, GatherTest.CountBase(home, Item2) - Before2);
                if (got > Best) Best = got;
                if (Best >= N) Done = t;
            }
        }

        static readonly List<KeyValuePair<string, ChatCommand>> s_ran = new List<KeyValuePair<string, ChatCommand>>();

        static void OnRan(WorkerNpc npc, ChatCommand cmd)
        {
            s_ran.Add(new KeyValuePair<string, ChatCommand>(npc.NpcName, cmd));
            TestRunner.Log($"DIAG 해석: {npc.NpcName} ← {Desc(cmd)}");
        }

        static string Desc(ChatCommand c) => $"{c.Kind}{(c.Item != null ? " " + c.Item : "")}{(c.Amount > 0 ? " " + c.Amount : "")}{(c.Number > 0 ? " #" + c.Number : "")}";

        /// <summary>들어온 해석을 아직 짝 없는 명령에 붙인다: 종류가 같고 물건이 같은 첫 명령. 수량(세트는 번호·밭은 반경도)까지 같아야 "알아들음".</summary>
        static void Match(List<Want> orders)
        {
            for (int i = 0; i < s_ran.Count; i++)
            {
                var cmd = s_ran[i].Value;
                var o = orders.Find(w => w != null && w.Heard == null && w.Kind == cmd.Kind
                    && (cmd.Kind != CommandKind.Item || w.Item == cmd.Item));
                if (o == null) continue;
                o.Heard = cmd; o.Who = s_ran[i].Key;
                o.HeardOk = cmd.Kind == CommandKind.Item ? cmd.Amount == o.Amount
                    : cmd.Kind == CommandKind.SetMake ? cmd.Number == s_setNo && cmd.Amount == o.Amount
                    : cmd.Kind != CommandKind.Farm || cmd.Number == o.Amount;
                s_ran.RemoveAt(i--);
            }
        }

        /// <summary>이름 없이 한 마디를 하고 판정할 Want를 돌려준다(home이 있으면 거점 상자 개수로 판정).
        /// make: 만드는 물건 — 판정은 상자 개수가 처음보다 n개 늘었나. 모아 오는 것은 일꾼들이 상자에 넣은 수(회차 661 — 제작·굽기가 같은 재료를 씀).
        static Want Say(string text, CommandKind kind, string item, int n, Vector3? home = null, bool make = false)
        {
            var w = new Want { Text = text, Kind = kind, Item = item, Amount = n, N = n };
            if (home != null && item != null)
            {
                w.Before = GatherTest.CountBase(home.Value, item);
                // 늦게 같이 캐기(JoinGather)로 동료가 낀 몫도 이 명령의 것이라 모든 일꾼이 넣은 수를 합친다(11-14).
                if (!make) { int p0 = PutByAll(item); w.Count = () => PutByAll(item) - p0; }
            }
            GatherTest.Say(text, null); // 수량 = 새로 만들/가져올 수(사용자 11-14 — 상자 포함 아님)
            return w;
        }

        static int PutByAll(string item)
        {
            int sum = 0;
            foreach (var n in WorkerNpc.All) if (n != null) sum += GatherJob.PutBy(n.NpcName, item);
            return sum;
        }

        static int s_setNo;

        /// <summary>"세트 n번 하나 만들어 줘" — 시험이 세트 메뉴 n번(활·곤봉)을 먼저 저장하고(끝나면 지움), 활·곤봉이 상자에 처음보다 하나씩 더 생기면 PASS(11-14 수량 = 더 만들 수).</summary>
        static Want SetSay(GatherTest.Checks checks, Vector3 home)
        {
            int no = s_setNo = Brain.Sets.NextFree();
            int saved = no == 0 ? 0 : Brain.Sets.Save(no, new[] { "Bow", "Club" }, "시험 세트", "시험");
            checks.Add($"세트 {no}번 저장", saved == no && no != 0);
            if (saved != no || no == 0) return null;
            int bow = GatherTest.CountBase(home, "Bow"), club = GatherTest.CountBase(home, "Club");
            var w = Say($"세트 {no}번 하나 만들어 줘", CommandKind.SetMake, null, 1);
            // 두 물건 모두 처음보다 1개 이상 늘어야 PASS
            w.Goal = "활·곤봉"; w.Item = "Bow"; w.Item2 = "Club"; w.Before = bow; w.Before2 = club;
            return w;
        }

        static void End(Player player, bool god, GatherTest.Checks checks, System.Action<string, string> onHeard, List<string> heard)
        {
            if (EnvMan.instance != null) { EnvMan.instance.m_debugTimeOfDay = s_dbgTime; EnvMan.instance.m_debugTime = s_dbgValue; }
            ChatNet.Heard -= onHeard;
            Commands.Ran -= OnRan;
            Departures.TestIdleChores = false;
            player.SetGodMode(god);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
