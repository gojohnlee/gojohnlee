using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_finaltest 최종 실전 시험(사용자 11-12, decisions 71): 시험 기본 집에서 한 번만 되돌린 뒤 일꾼 2+호위병 1, 채팅 명령을 두 일꾼에게 나눠 한꺼번에 내려
    /// 명령마다 거점 상자 개수로 PASS/FAIL. 되돌리기·상자 채우기 없이 이어 간다. 나무·돌·구리·구리주괴·활·세트·굽기·사냥·밭·가축·상자 정리.
    /// </summary>
    static class FinalTest
    {
        public static void Register() => TestRunner.Register("vh_finaltest", "최종 실전 시험 ① 나무·돌·구리·구리주괴·활·세트 채팅 명령(되돌림 한 번)", Run, 3600f);

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
                pig = g.GetComponent<Tameable>();
            }
            yield return new WaitForSeconds(1f);
            checks.Add($"가축 상자·길들인 배고픈 멧돼지 놓음", pen != null && pig != null && pig.IsHungry());

            TestRunner.Log($"DIAG 시작 거점 상자: Wood {GatherTest.CountBase(home, "Wood")}, Stone {GatherTest.CountBase(home, "Stone")}, CopperOre {GatherTest.CountBase(home, "CopperOre")}, Copper {GatherTest.CountBase(home, "Copper")}, RawMeat {GatherTest.CountBase(home, "RawMeat")}");
            // 2) 명령을 두 일꾼에게 나눠 처음에 한꺼번에 내림(바쁘면 일꾼 대기열) → 명령마다 거점 상자 개수로 따로 판정(사용자 11-14 바로잡기 — 하나씩 끝을 기다리지 않음)
            //    구리 광석은 제련이, 생고기는 굽기가 써서 줄어들 수 있어 판정은 지금까지 가장 많았던 수로.
            string a = Workers.Name1, b = Workers.Name2;
            // 실전처럼 쉬는 일꾼 판단을 켬(사용자 11-14 "왜 여전히 한 놈만 일하지": 시험 중엔 ChoresOn이 꺼져 JoinGather·JoinHelp가 안 돌아
            // 플로키가 돌·활·굽기를 금방 끝내고 라그나 혼자 구리·제련·사냥을 다 함). 시작 검사 뒤에 켬(FishTest와 같은 까닭), End에서 끔.
            Departures.TestIdleChores = true;
            var orders = new List<Want>
            {
                Ask(home, a, "나무", "Wood"),
                Ask(home, b, "돌", "Stone"),
                Ask(home, a, "구리", "CopperOre"),
                Ask(home, b, "활", "Bow", make: true, n: 3), // 활은 제작(작업대·재료). 시험은 3자루만(화살은 활이 챙김)
                Ask(home, a, "구리주괴", "Copper", make: true), // 제련: 거점 제련소·석탄 연료가 있어야 함(없으면 FAIL로 드러남)
                SetAsk(checks, home, b),
                Ask(home, b, "CookedMeat", "CookedMeat", make: true, n: 4), // 굽기(만드는 물건이라 상자 수 — 회차 662: 넣은 수로 세어 4개 구워 넣고도 0): 시작 생고기 4개를 구움. 한글 "구운 고기"는 물건 이름에 없어 프리팹 이름(회차 657)
                Ask(home, a, "RawMeat", "RawMeat", n: 6), // 사냥: 집 근처 멧돼지가 드물어 6개(회차 660 — 고기는 다 모을 때까지 들고 다님). 회차 661: 6개 넣었는데 상자엔 3(굽기가 씀) → 넣은 수로 판정
            };
            // 밭: "!밭 3"은 말한 사람 자리가 중심이라 플레이어가 밭 자리에 가서 말하고 돌아옴(일꾼은 안 옮김)
            Vector3 back = player.transform.position;
            player.TeleportTo(fieldAt + Vector3.up * 0.5f, player.transform.rotation, distantTeleport: false);
            yield return new WaitForSeconds(3f);
            GatherTest.Say($"!{b} 밭 3", null);
            yield return new WaitForSeconds(1f);
            player.TeleportTo(back, player.transform.rotation, distantTeleport: false);
            orders.Add(new Want { Who = b, Word = "밭 3 → 당근 새싹", N = 4, Count = () => FarmTest.Plants(field).Count });
            if (pig != null)
            {
                GatherTest.Say($"!{a} 가축", null);
                orders.Add(new Want { Who = a, Word = "가축 → 멧돼지 먹음", N = 1, Count = () => pig != null && !pig.IsHungry() ? 1 : 0 });
            }
            int tidy0 = Tidy.Plan(home).Count, tidyMin = tidy0;
            GatherTest.Say($"!{b} 정리", null);
            orders.Add(new Want { Who = b, Word = $"정리(처음 옮길 것 {tidy0}) → 옮길 것 0", N = 1, Count = () => { int left = Tidy.Plan(home).Count; if (left < tidyMin) { tidyMin = left; TestRunner.Log($"DIAG 정리 남은 것 {left} ({b} {(WorkerNpc.All.Find(n => n != null && n.NpcName == b)?.Ai.Job?.GetType().Name ?? "일 없음")})"); } return left == 0 ? 1 : 0; } });
            orders.RemoveAll(o => o == null);
            float t = 0f;
            while (t < 3000f && orders.Exists(o => o.Done < 0f))
            {
                yield return new WaitForSeconds(5f);
                t += 5f;
                foreach (var o in orders) o.Poll(home, t);
            }
            foreach (var o in orders)
                checks.Add($"{o.Who} {o.Word} {o.N} 명령 → 거점 상자 {o.Best}개 ({(o.Done >= 0f ? o.Done : t):0}초)", o.Done >= 0f);
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

        /// <summary>명령 하나: 누구에게 무엇을 시켰고, 거점 상자에 n개 더 생긴 때(Done, 초). Item2가 있으면 둘 다(세트).</summary>
        sealed class Want
        {
            public string Who, Word, Item, Item2;
            public int N, Before, Before2, Best;
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

        /// <summary>"!일꾼 말 10"을 내리고 판정할 Want를 돌려준다.
        /// make: 만드는 물건(제작 계획)은 수량이 "상자에 n개 되게"라(PlanFor가 재고를 빼고 셈) 처음 개수 + n을 시킨다 — 집 상자에 원래 구리주괴 14개가 있어 "구리주괴 10"은 "이미 상자에 있어"였음.</summary>
        static Want Ask(Vector3 home, string who, string word, string item, bool make = false, int n = 10)
        {
            int before = GatherTest.CountBase(home, item);
            GatherTest.Say($"!{who} {word} {(make ? before + n : n)}", null);
            var w = new Want { Who = who, Word = word, Item = item, N = n, Before = before };
            // 모아 오는 명령은 그 일꾼이 상자에 넣은 수로(회차 661 — 같은 때 활·세트 제작이 나무를, 쉬는 일꾼 굽기가 생고기를 써서 상자 순증가가 0·3이었음)
            // 늦게 같이 캐기(JoinGather)로 동료가 낀 몫도 이 명령의 것이라 모든 일꾼이 넣은 수를 합친다(11-14).
            if (!make) { int p0 = PutByAll(item); w.Count = () => PutByAll(item) - p0; }
            return w;
        }

        static int PutByAll(string item)
        {
            int sum = 0;
            foreach (var n in WorkerNpc.All) if (n != null) sum += GatherJob.PutBy(n.NpcName, item);
            return sum;
        }

        static int s_setNo;

        /// <summary>"!일꾼 세트 n amount" — 시험이 세트 메뉴 n번(활·곤봉)을 먼저 저장하고(끝나면 지움), 활·곤봉이 상자에 amount개 이상 되면 PASS.
        /// amount = 두 물건 중 많은 쪽 + 1(PlanFor가 재고를 빼고 셈이라 "상자에 amount개 되게"에 맞춤 — 활 명령과 같은 까닭).
        /// 활 명령이 앞서 활을 늘려도 곤봉이 함께 amount개가 돼야 PASS라 세트가 돈 것을 본다.</summary>
        static Want SetAsk(GatherTest.Checks checks, Vector3 home, string who)
        {
            int no = s_setNo = Brain.Sets.NextFree();
            int saved = no == 0 ? 0 : Brain.Sets.Save(no, new[] { "Bow", "Club" }, "시험 세트", "시험");
            checks.Add($"세트 {no}번 저장", saved == no && no != 0);
            if (saved != no || no == 0) return null;
            int bow = GatherTest.CountBase(home, "Bow"), club = GatherTest.CountBase(home, "Club");
            int amount = Mathf.Max(bow, club) + 1;
            GatherTest.Say($"!{who} 세트 {no} {amount}", null);
            // 판정 꼴을 Want에 맞춤: 두 물건 모두 amount 이상 = 각각 (amount - 처음) 이상 늘어남 — 적게 늘어난 쪽 기준이라 Before를 amount - N으로 둠
            return new Want { Who = who, Word = $"세트 {no}번", Item = "Bow", Item2 = "Club", N = 1, Before = amount - 1, Before2 = amount - 1 };
        }

        static void End(Player player, bool god, GatherTest.Checks checks, System.Action<string, string> onHeard, List<string> heard)
        {
            if (EnvMan.instance != null) { EnvMan.instance.m_debugTimeOfDay = s_dbgTime; EnvMan.instance.m_debugTime = s_dbgValue; }
            ChatNet.Heard -= onHeard;
            Departures.TestIdleChores = false;
            player.SetGodMode(god);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
