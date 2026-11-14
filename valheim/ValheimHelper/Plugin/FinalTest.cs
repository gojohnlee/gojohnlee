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
        public static void Register() => TestRunner.Register("vh_finaltest", "최종 실전 시험: 이름 없는 실제 말투 명령 한꺼번에(알아들음·해냄 따로, 되돌림 한 번)", Run, 4500f); // 안 기다림 4200초보다 길게(E103: 3600에 끊겨 남은 칸을 못 적음)

        static bool s_autoGo;

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
            // 실플레이 조건(다시 짜기 다): 시간 고정 없음(밤도 옴), 질문 자동 대답 없음 — End가 되돌림
            s_autoGo = Departures.TestAutoGo;
            Departures.TestAutoGo = false;
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
            WorkerNpc.TestPicked = Busy.Picked;
            var orders = new List<Want>();
            // 밭: 말한 사람 자리가 중심 → 플레이어는 시작 때 한 번 밭 자리에 서서 모든 명령을 거기서 말하고 시험 내내 서 있음
            //    (다시 짜기 다: 명령 사이 순간이동·알아들을 때까지 기다림 없음 — 실플레이처럼 한꺼번에)
            player.TeleportTo(fieldAt + Vector3.up * 0.5f, player.transform.rotation, distantTeleport: false);
            yield return new WaitForSeconds(3f);
            TestRunner.Log($"DIAG 시작 시각: {(EnvMan.IsNight() ? "밤" : "낮")} (하루 {EnvMan.instance.GetDayFraction():F2})");
            var farm = Say("여기 밭 좀 만들어 줘, 반경 3", CommandKind.Farm, null, 3);
            farm.N = 4; farm.Count = () => FarmTest.Plants(field).Count; farm.Goal = "당근 새싹";
            orders.Add(farm);
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
            tidy.Count = () => { var npcT = WorkerNpc.All.Find(n => n != null && n.NpcName == tidy.Who); HashSet<string> sk = null; if (npcT != null) Commands.TidySkip.TryGetValue(npcT, out sk); int left = 0; foreach (var m in Tidy.Plan(home)) if (sk == null || !sk.Contains(m.Name)) left++; /* 자리 없어 건너뛴 물건은 뺌(E103) */ if (left < tidyMin) { tidyMin = left; TestRunner.Log($"DIAG 정리 남은 것 {left} ({tidy.Who} {(WorkerNpc.All.Find(n => n != null && n.NpcName == tidy.Who)?.Ai.Job?.GetType().Name ?? "일 없음")})"); } return left == 0 ? 1 : 0; };
            orders.Add(tidy);
            orders.RemoveAll(o => o == null);
            float t = 0f;
            int nights = 0; bool wasNight = EnvMan.IsNight();
            while (t < 4200f && orders.Exists(o => o.Done < 0f)) // 밤이 끼면 더 걸림(3000 → 4200초)
            {
                yield return new WaitForSeconds(5f);
                t += 5f;
                bool night = EnvMan.IsNight();
                if (night && !wasNight) { nights++; TestRunner.Log($"DIAG 밤 시작 {t:F0}초 ({nights}번째)"); }
                if (!night && wasNight) TestRunner.Log($"DIAG 낮 시작 {t:F0}초");
                wasNight = night;
                // 급한 명령(나) 셋째 조각, decisions 147): 모두 바쁠 때쯤 "급해 … 먼저" — 하던 일을 멈추고 바로 시작, 멈춘 일은 대기열 맨 앞
                if (t == 120f) { var u = Say("급해, 나무 5개 먼저 해 와", CommandKind.Item, "Wood", 5, home); u.Urgent = true; orders.Add(u); }
                Match(orders);
                foreach (var o in orders) o.Poll(home, t);
                Busy.Sample(t);
            }
            Commands.Ran -= OnRan;
            WorkerNpc.TestPicked = null;
            Busy.Report(checks);
            if (pig != null && pig.IsHungry()) // E102: 가축 못 먹임 — 우리 상자 먹이·멧돼지 자리
                TestRunner.Log($"DIAG 가축 못 먹임: 멧돼지 우리에서 {Vector3.Distance(pig.transform.position, penAt):0}m, 우리 상자 Raspberry {(pen != null ? pen.GetInventory().CountItems("$item_raspberries") : -1)}");
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
            public bool HeardOk, Urgent;
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

        /// <summary>우선순위 판정 나) 첫 조각(11-14 다시 짜기): 5초마다 일꾼마다 일 종류(없으면 "쉼"), 호위병은 동행·싸움 여부를 센다.
        /// 판정은 "일꾼마다 일함"만(사용자 11-12 "모든 일꾼 늘 같이"), 쉬는 비율·일 종류는 살펴볼 것 기록(DIAG).</summary>
        static class Busy
        {
            static readonly Dictionary<string, Dictionary<string, int>> s_jobs = new Dictionary<string, Dictionary<string, int>>();

            // 호위병 부름 칸(나) 둘째 조각): 일꾼이 위험한 일(Risky)을 하는 동안 동행(호위병·동료)이 붙었나. 30초 안에 안 붙으면
            // 그때 호위병이 쉬고 있었으면 "안 옴"(FAIL), 바빴으면(싸움·다른 동행·순찰) "바빠 기다림"(살펴볼 것).
            static readonly Dictionary<string, float> s_riskFrom = new Dictionary<string, float>();
            static readonly HashSet<string> s_riskDone = new HashSet<string>();
            static int s_with, s_wait, s_none;
            static readonly List<string> s_riskLog = new List<string>();

            // 급한 명령 칸(나) 셋째 조각): 받은 일꾼이 30초 안에 나무 일을 시작하나, 하던 일이 있었으면 그 일이 대기열 맨 앞에 있나
            static WorkerNpc s_uNpc;
            static IWorkerJob s_uOld;
            static float s_uAt;
            static string s_uResult;
            static bool s_uOk, s_uResumed, s_uYields;

            public static void UrgentRan(WorkerNpc npc)
            {
                if (s_uNpc != null) return;
                s_uNpc = npc; s_uOld = npc.Ai.Job; s_uAt = Time.time; s_uYields = npc.Ai.Yields; // 쉬는 일꾼 집안일(Yields)은 명령에 양보해 버려짐(decisions 308)
                TestRunner.Log($"DIAG 급한 명령: {npc.NpcName} 하던 일 {(s_uOld != null ? s_uOld.Label : "없음")}, 대기열 {npc.Ai.Queue.Count}");
            }

            static void Urgent(float t)
            {
                if (s_uNpc == null || s_uNpc.Ai == null) return;
                var ai = s_uNpc.Ai;
                if (s_uResult == null)
                {
                    if (ai.Job is GatherJob gj && gj.Item == "Wood" && ai.Job != s_uOld)
                    {
                        bool front = s_uOld == null || s_uYields || ai.Queue.Count > 0 && ai.Queue[0].Resume == s_uOld;
                        s_uOk = front;
                        s_uResult = $"{s_uNpc.NpcName} {Time.time - s_uAt:0}초 만에 시작, " + (s_uOld == null ? "쉬던 일꾼" : s_uYields ? $"하던 집안일 '{s_uOld.Label}' 양보"
                            : front ? $"하던 '{s_uOld.Label}' 대기열 맨 앞" : $"하던 '{s_uOld.Label}' 대기열 맨 앞 아님({(ai.Queue.Count > 0 ? ai.Queue[0].Label : "빈 대기열")})");
                    }
                    else if (Time.time - s_uAt > 30f) s_uResult = $"{s_uNpc.NpcName} 30초 안에 안 시작 (지금 {ai.Job?.Label ?? "쉼"})";
                }
                else if (!s_uResumed && s_uOld != null && ai.Job == s_uOld) { s_uResumed = true; TestRunner.Log($"DIAG 급한 명령 뒤 하던 일 다시 이음: {s_uNpc.NpcName} '{s_uOld.Label}' {t:0}초"); }
            }

            // 대기열 순서 칸: 대기열에서 꺼내 시작한 일이 앞 칸(지금 할 수 있는 것)을 건너뛰었나. 아닌 일을 먼저 하면 DIAG만
            static readonly Dictionary<string, List<QueuedWork>> s_q = new Dictionary<string, List<QueuedWork>>();
            static readonly Dictionary<string, IWorkerJob> s_job = new Dictionary<string, IWorkerJob>();
            static int s_take, s_skip;

            static void Order(WorkerNpc n, float t)
            {
                string who = n.NpcName;
                var job = n.Ai.Job;
                s_q.TryGetValue(who, out var prev); s_job.TryGetValue(who, out var was);
                if (prev != null && job != null && job != was)
                {
                    int i = prev.FindIndex(q => q.Resume == job);
                    if (i < 0) i = prev.FindIndex(q => q.Resume == null && !n.Ai.Queue.Contains(q));
                    if (i >= 0)
                    {
                        s_take++;
                        for (int k = 0; k < i; k++)
                            if (n.Ai.Queue.Contains(prev[k]) && (prev[k].Ready == null || prev[k].Ready()))
                            { s_skip++; TestRunner.Log($"DIAG 대기열 건너뜀: {who} {t:0}초 '{prev[i].Label}'을 '{prev[k].Label}'보다 먼저"); break; }
                    }
                    else if (n.Ai.Queue.Count > 0) TestRunner.Log($"DIAG 대기열 두고 다른 일: {who} {t:0}초 {job.Label} (대기열 {n.Ai.Queue.Count}, 첫 칸 '{n.Ai.Queue[0].Label}')");
                }
                s_q[who] = new List<QueuedWork>(n.Ai.Queue); s_job[who] = job;
            }

            // 부딪힘 칸(나) 넷째 조각): 표본 때 두 일꾼이 1.2m 안에서 둘 다 걸으려는데 못 움직이면 부딪힘(DIAG), 같은 쌍이 표본 4번 내리(15초 넘게) 그러면 서로 막힘(FAIL)
            static readonly Dictionary<string, int> s_bumpRun = new Dictionary<string, int>();
            static int s_bumps, s_locks;

            static void Bumps(float t)
            {
                var all = new List<WorkerNpc>();
                foreach (var n in WorkerNpc.All) if (n != null && !n.IsDead() && n.Ai != null) all.Add(n);
                var now = new HashSet<string>();
                for (int i = 0; i < all.Count; i++)
                    for (int k = i + 1; k < all.Count; k++)
                    {
                        WorkerNpc a = all[i], b = all[k];
                        if (!a.PushingStill || !b.PushingStill || Utils.DistanceXZ(a.transform.position, b.transform.position) > 1.2f) continue;
                        string key = a.NpcName + "·" + b.NpcName;
                        now.Add(key);
                        s_bumpRun.TryGetValue(key, out int run);
                        s_bumpRun[key] = ++run;
                        if (run == 1) { s_bumps++; TestRunner.Log($"DIAG 부딪힘: {key} {t:0}초 ({a.Ai.Job?.Label ?? "쉼"} / {b.Ai.Job?.Label ?? "쉼"})"); }
                        if (run == 4) { s_locks++; TestRunner.Log($"DIAG 서로 막힘 15초: {key} {t:0}초 / {a.MoveDebug}"); }
                    }
                foreach (var key in new List<string>(s_bumpRun.Keys)) if (!now.Contains(key)) s_bumpRun.Remove(key);
            }

            // 남의 물건 가져감 칸(나) 다섯째 조각): 일꾼이 주운 떨굼이 다른 일꾼이 줍기 단계에서 가러 가던 것이면 셈(DIAG).
            // 한 일꾼이 5번 넘게 빼앗기면 헛걸음 되풀이로 FAIL(기준 11-12: FAIL은 헛걸음 되풀이 등)
            static readonly Dictionary<string, int> s_lost = new Dictionary<string, int>();
            static int s_steals;

            public static void Picked(WorkerNpc who, ZDOID id)
            {
                if (who == null || id.IsNone()) return;
                foreach (var n in WorkerNpc.All)
                {
                    if (n == null || n == who || !(n.Ai?.Job is GatherJob gj) || gj.CollectDrop != id) continue;
                    s_steals++;
                    s_lost.TryGetValue(n.NpcName, out int c); s_lost[n.NpcName] = c + 1;
                    TestRunner.Log($"DIAG 남의 물건 가져감: {who.NpcName}이 {n.NpcName}이 줍던 {gj.Item}을 주움 ({n.Ai.Job.Label})");
                }
            }

            public static void Sample(float t)
            {
                var g = WorkerNpc.TheGuard;
                string escorted = null;
                Urgent(t);
                Bumps(t);
                foreach (var n in WorkerNpc.All)
                {
                    if (n == null || n.Ai == null) continue;
                    Order(n, t);
                    Add(n.NpcName, n.Ai.Job?.GetType().Name ?? (n.Ai.Resting ? "쉼(집안일 멈춤)" : "쉼"));
                    if (g != null && n.Ai.EscortName == g.NpcName) escorted = n.NpcName;
                    Risk(n, g, t);
                }
                // 호위병 쪽은 EscortName이 비어 있음(따라가는 쪽은 Follow) — 일꾼 쪽 EscortName으로 셈(회차 668 "동행 0"은 이 탓)
                if (g != null && g.Ai != null) Add(g.NpcName, escorted != null ? "동행 " + escorted : g.Ai.GuardBusy ? "싸움" : g.Ai.Job?.GetType().Name ?? "쉼");
            }

            static void Risk(WorkerNpc n, WorkerNpc g, float t)
            {
                string who = n.NpcName;
                if (!n.Ai.Risky || n.Ai.Job == null) { s_riskFrom.Remove(who); s_riskDone.Remove(who); return; }
                if (!s_riskFrom.TryGetValue(who, out float from)) s_riskFrom[who] = from = t;
                if (s_riskDone.Contains(who)) return;
                string job = n.Ai.Job.GetType().Name;
                if (n.Ai.EscortName != null) { s_with++; s_riskDone.Add(who); s_riskLog.Add($"{who} {job} {t:0}초 동행 {n.Ai.EscortName} ({t - from:0}초 만)"); }
                else if (t - from >= 30f)
                {
                    bool free = Departures.GuardFree(g);
                    if (free) s_none++; else s_wait++;
                    s_riskDone.Add(who);
                    s_riskLog.Add($"{who} {job} {t:0}초 동행 없음 30초 — 호위병 {(g == null ? "없음" : free ? "쉬는데 안 옴" : "바쁨 " + g.Ai.Current + "/" + (g.Ai.Job?.GetType().Name ?? "-"))}");
                }
            }

            static void Add(string who, string what)
            {
                if (!s_jobs.TryGetValue(who, out var d)) s_jobs[who] = d = new Dictionary<string, int>();
                d.TryGetValue(what, out int c); d[what] = c + 1;
            }

            public static void Report(GatherTest.Checks checks)
            {
                foreach (var n in WorkerNpc.All)
                {
                    if (n == null) continue;
                    s_jobs.TryGetValue(n.NpcName, out var d);
                    int all = 0, rest = 0;
                    if (d != null) foreach (var kv in d) { all += kv.Value; if (kv.Key.StartsWith("쉼")) rest += kv.Value; }
                    checks.Add($"일꾼 {n.NpcName} 일함 (표본 {all - rest}/{all}, 쉼 {(all > 0 ? rest * 100 / all : 0)}%)", all - rest > 0);
                }
                foreach (var kv in s_jobs)
                {
                    var parts = new List<string>();
                    foreach (var j in kv.Value) parts.Add(j.Key + " " + j.Value);
                    TestRunner.Log($"DIAG 일 비율 {kv.Key}: {string.Join(", ", parts)}");
                }
                s_jobs.Clear();
                foreach (var l in s_riskLog) TestRunner.Log("DIAG 위험한 일 동행: " + l);
                checks.Add($"위험한 일에 동행 붙음(호위병 부름): 동행 {s_with}번, 호위병 바빠 기다림 {s_wait}번, 호위병 쉬는데 안 옴 {s_none}번", s_none == 0);
                s_riskFrom.Clear(); s_riskDone.Clear(); s_riskLog.Clear(); s_with = s_wait = s_none = 0;
                checks.Add($"급한 명령 먼저: {s_uResult ?? (s_uNpc == null ? "급한 해석 없음" : "판정 전 끝")}", s_uResult != null && s_uOk);
                if (s_uOld != null && !s_uResumed) TestRunner.Log($"DIAG 급한 명령 뒤 하던 일 안 이음: '{s_uOld.Label}'");
                checks.Add($"대기열 순서대로 꺼냄: 꺼냄 {s_take}번, 건너뜀 {s_skip}번", s_skip == 0);
                checks.Add($"일꾼끼리 서로 막힘 없음: 부딪힘 {s_bumps}번, 15초 넘게 막힘 {s_locks}번", s_locks == 0);
                s_bumpRun.Clear(); s_bumps = s_locks = 0;
                int most = 0; string mostWho = "-";
                foreach (var kv in s_lost) if (kv.Value > most) { most = kv.Value; mostWho = kv.Key; }
                checks.Add($"남의 물건 가져감 되풀이 없음: {s_steals}번, 가장 많이 빼앗긴 {mostWho} {most}번", most <= 5);
                s_lost.Clear(); s_steals = 0;
                s_uNpc = null; s_uOld = null; s_uResult = null; s_uOk = s_uResumed = false; s_q.Clear(); s_job.Clear(); s_take = s_skip = 0;
            }
        }

        static readonly List<KeyValuePair<string, ChatCommand>> s_ran = new List<KeyValuePair<string, ChatCommand>>();

        static void OnRan(WorkerNpc npc, ChatCommand cmd)
        {
            s_ran.Add(new KeyValuePair<string, ChatCommand>(npc.NpcName, cmd));
            if (cmd.Urgent) Busy.UrgentRan(npc); // Ran은 일을 시작하기 전 — 하던 일을 여기서 잡아 둠
            TestRunner.Log($"DIAG 해석: {npc.NpcName} ← {Desc(cmd)}");
        }

        static string Desc(ChatCommand c) => $"{c.Kind}{(c.Item != null ? " " + c.Item : "")}{(c.Amount > 0 ? " " + c.Amount : "")}{(c.Number > 0 ? " #" + c.Number : "")}";

        /// <summary>들어온 해석을 아직 짝 없는 명령에 붙인다: 종류가 같고 물건이 같은 첫 명령. 수량(세트는 번호·밭은 반경도)까지 같아야 "알아들음".</summary>
        static void Match(List<Want> orders)
        {
            for (int i = 0; i < s_ran.Count; i++)
            {
                var cmd = s_ran[i].Value;
                System.Predicate<Want> same = w => w != null && w.Heard == null && w.Kind == cmd.Kind
                    && (cmd.Kind != CommandKind.Item || w.Item == cmd.Item);
                var o = orders.Find(w => same(w) && w.Urgent == cmd.Urgent) ?? orders.Find(same); // 급함까지 같은 것 먼저(나무 10 · 급한 나무 5)
                if (o == null) continue;
                o.Heard = cmd; o.Who = s_ran[i].Key;
                o.HeardOk = cmd.Urgent != o.Urgent ? false
                    : cmd.Kind == CommandKind.Item ? cmd.Amount == o.Amount
                    : cmd.Kind == CommandKind.SetMake ? cmd.Number == s_setNo && cmd.Amount == o.Amount
                    : cmd.Kind != CommandKind.Farm || cmd.Number == o.Amount;
                s_ran.RemoveAt(i--);
            }
        }

        /// <summary>이름 없이 한 마디를 하고 판정할 Want를 돌려준다(home이 있으면 거점 상자 개수로 판정).
        /// make: 만드는 물건 — 판정은 상자 개수가 처음보다 n개 늘었나. 모아 오는 것은 일꾼들이 상자에 넣은 수(회차 661 — 제작·굽기가 같은 재료를 씀).</summary>
        static Want Say(string text, CommandKind kind, string item, int n, Vector3? home = null, bool make = false)
        {
            var w = new Want { Text = text, Kind = kind, Item = item, Amount = n, N = n };
            if (home != null && item != null)
            {
                w.Before = GatherTest.CountBase(home.Value, item);
                // 늦게 같이 캐기(JoinGather)로 동료가 낀 몫도 이 명령의 것이라 모든 일꾼이 넣은 수를 합친다(11-14).
                if (!make) { int p0 = PutByAll(item); w.Count = () => PutByAll(item) - p0; }
                // 만든 음식은 상자만 셈 — 명령 몫은 들고 갈 음식에서 뒤로라 넣자마자 사라지면 안 됨(회차 671 구운 고기 4 → 3개 들고 사냥, 회차 673 고침)
                else { Vector3 h = home.Value; int b0 = w.Before; w.Count = () => GatherTest.CountBase(h, item) - b0; }
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
            Departures.TestAutoGo = s_autoGo;
            ChatNet.Heard -= onHeard;
            Commands.Ran -= OnRan;
            WorkerNpc.TestPicked = null;
            Departures.TestIdleChores = false;
            player.SetGodMode(god);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
