using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_departtest (묶음 1 작업 4): 출발 전 판단·질문·대답·대기열을 질문 자동 대답 없이(TestAutoGo=false).
    /// 재고 질문 → "응", 곧 밤 질문 → "응" → 대기열(다음 아침까지 안 꺼냄) → 아침에 출발, 죽은 곳 질문 → "아니"(보류),
    /// 위험 조건(체력 미드) → 미드가 생기면 스스로 출발, 3분 무응답 보류, 일하는 중 새 명령(급함 아님 → 대기열, 급함 → 끼어듦),
    /// 저장 왕복(대기열·질문이 있는 채로 NPC 인스턴스를 내렸다가 다시 만들어 그대로인지), 밝혀진 곳 아는지(스위치 끔/켬 비교).
    /// 시험 상황(먼 곳의 구리, 위험·죽음 기록)은 지식·장소 기억에 직접 넣고, 끝나면 원래대로 되돌린다.
    /// </summary>
    static class DepartTest
    {
        public static void Register() => TestRunner.Register("vh_departtest", "출발 전 판단·질문·대기열·저장 시험", () => TestKit.Flat(Run()));

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            Departures.TestAutoGo = false;
            Features.OverrideKnowExplored(false); // 상황을 직접 만든다. 이 기능은 맨 끝에서 따로 켜고 끈다
            try
            {
                yield return Body(w, checks);
            }
            finally
            {
                Departures.TestAutoGo = true;
                Features.OverrideKnowExplored(null);
                TestKit.Restore(w);
            }
            yield return Stop();
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks)
        {
            {
                yield return TestKit.Setup(w);
                checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
                if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
                string n1 = w.Ragnar.NpcName, n2 = w.Floki.NpcName;
                ZoneXY hz = Senses.ZoneOf(w.Home);
                // 시험 월드의 같은 자리에 지난 시험(나무·구리 등)이 남긴 상자가 있어 재고가 시험이 넣은 것과 달라진다
                // (2026-10-21: 나무 150·구리 10 → 재고 질문이 먼저 잡힘). 거점 상자에서 이 시험의 물건을 비우고 시작한다.
                TestRunner.Log($"거점 상자 {Chests.NearBase(w.Home).Count}개에서 지난 시험의 나무·구리 {TestKit.ClearBaseStock(w, "Wood", "CopperOre", "MeadHealthMinor", "MeadHealthMedium", "HealthUpgrade_Bonemass")}개 비움");

                // 1) 재고 질문 → "응" → 출발
                TestKit.PutInChest(w.Chest, "Wood", 20);
                yield return Say($"!{n1} 나무 10", 1f);
                checks.Add($"재고 질문 ({Asked(w.Ragnar)})", Departures.AskedAt(w.Ragnar) >= 0 && LastSaid(w, n1).Contains("20"));
                yield return Say("응", 1f);
                checks.Add($"\"응\" → 출발 ({w.Ragnar.Ai.Current})", Departures.AskedAt(w.Ragnar) < 0 && w.Ragnar.Ai.Current == WorkerAI.Mode.Work && w.Ragnar.Ai.Job is GatherJob);
                yield return Stop();
                TestKit.TakeFromChest(w.Chest, "Wood");

                // 먼 곳(3구역 = 192m)의 구리를 아는 상태
                var far = new ZoneXY(hz.X + 3, hz.Y);
                Brain.Get().Saw(new Dictionary<ZoneXY, Dictionary<string, int>> { [far] = new Dictionary<string, int> { ["CopperOre"] = 5 } },
                    new List<ZoneXY> { far }, ZNet.instance.GetTimeSeconds());
                OnlyFar(far, hz);
                string knowFar = Brain.Get().Save();
                var copper = new ChatCommand { Kind = CommandKind.Item, Item = "CopperOre", Amount = 10 };

                // 2) 곧 밤 질문 → "응" → 대기열(아침 조건) → 아직 낮이어도 안 꺼냄 → 아침에 출발
                w.Env.m_debugTime = 0.74f;
                yield return new WaitForSeconds(0.5f);
                OnlyFar(far, hz); // 기다리는 동안 NPC가 둘러보며 거점 근처 구리를 다시 배웠을 수 있다(명령은 같은 프레임에 처리됨)
                Log("곧 밤", Departures.JudgeFor(w.Ragnar, copper));
                yield return Say($"!{n1} 구리 10", 1f);
                checks.Add($"곧 밤 질문 ({Asked(w.Ragnar)})", Departures.AskedAt(w.Ragnar) >= 0);
                yield return Say("응", 1f);
                var q = w.Ragnar.Ai.Queue;
                bool queued = q.Count == 1 && q[0].Work != null && q[0].Work.Ready == ReadyWhen.Morning && q[0].Work.NotBefore > ZNet.instance.GetTimeSeconds();
                checks.Add($"\"응\" → 대기열(다음 아침) {q.Count}칸", queued && w.Ragnar.Ai.Current != WorkerAI.Mode.Work);
                yield return new WaitForSeconds(3f);
                checks.Add("아침 전에는 안 꺼냄(질문 되풀이 없음)", q.Count == 1 && Departures.AskedAt(w.Ragnar) < 0 && w.Ragnar.Ai.Current != WorkerAI.Mode.Work);
                if (queued)
                {
                    ZNet.instance.SetNetTime(q[0].Work.NotBefore + 5.0); // 잠자기처럼 아침으로
                    w.Env.m_debugTime = 0.3f;
                    yield return TestKit.WaitFor(() => w.Ragnar.Ai.Current == WorkerAI.Mode.Work, 8f);
                    checks.Add($"아침 → 대기열에서 꺼내 출발 ({TestKit.Waited:0.0}초)", TestKit.Ok && q.Count == 0);
                }
                w.Env.m_debugTime = 0.5f;
                yield return Stop();

                // 3) 죽은 곳 → 묻지 않고 경고하고 출발(사용자 결정 2026-10-25, 예전엔 질문 → "아니" → 보류)
                Brain.ReplaceForTest(knowFar, "");
                Brain.Places.Record(far, new List<GroupFoe> { new GroupFoe { Kind = "Greydwarf", Count = 1 } }, Outcome.Died, ZNet.instance.GetTimeSeconds(), Guard.Resolve, "Greydwarf");
                Log("죽은 곳", Departures.JudgeFor(w.Ragnar, copper));
                yield return Say($"!{n1} 구리 10", 1f);
                checks.Add($"죽은 곳 → 경고하고 출발 ({w.Ragnar.Ai.Current}, 말 \"{LastSaid(w, n1)}\")",
                    Departures.AskedAt(w.Ragnar) < 0 && w.Ragnar.Ai.Current == WorkerAI.Mode.Work && LastSaid(w, n1).Contains("위험"));
                yield return Stop();

                // 4) 빠듯한 곳(Tight, 예전 체력 미드 조건 질문) → 묻지 않고 경고하고 출발. 미드 하나면 주의가 되는 곳이라 아래 늪 칸에도 쓴다
                bool made = false;
                // 한 종류만 늘리면 비율이 0.65 → 1.30처럼 뛰어 Tight 창(싸움 선~1.0)을 건너뛴다(E030, 회차 167) → 약한 적을 섞어 잘게 맞춘다
                foreach (var kind in new[] { "Greydwarf", "Skeleton", "Draugr", "Greydwarf_Elite" })
                {
                    for (int n = 1; n <= 3 && !made; n++)
                        foreach (var small in new[] { "", "Greyling", "Neck", "Boar", "Deer" })
                        {
                            for (int m = small == "" ? 0 : 1; m <= (small == "" ? 0 : 6) && !made; m++)
                            {
                                Brain.ReplaceForTest(knowFar, "");
                                var foes = new List<GroupFoe> { new GroupFoe { Kind = kind, Count = n } };
                                if (m > 0) foes.Add(new GroupFoe { Kind = small, Count = m });
                                Brain.Places.Record(far, foes, Outcome.Back, ZNet.instance.GetTimeSeconds(), Guard.Resolve);
                                DepartureResult r = Departures.JudgeFor(w.Ragnar, copper);
                                if (r != null && r.Action != DepartAction.Ask && r.Reason.Contains(" Tight,") && WarnsWithMead(w.Ragnar, copper)) { made = true; TestRunner.Log($"위험 상황: {kind} {n}마리 + {small} {m}마리 — {r.Reason}"); }
                                else if (r != null && m <= 1) TestRunner.Log($"조건 아님: {kind} {n}마리 + {small} {m}마리 — {r.Action} {r.Question} | {r.Reason} | 미드 짐 {MeadsIn(w.Ragnar)}");
                            }
                            if (made) break;
                        }
                    if (made) break;
                }
                checks.Add("위험 조건 상황 만들기", made);
                if (made)
                {
                    yield return Say($"!{n1} 구리 10", 1f);
                    checks.Add($"빠듯한 곳 → 묻지 않고 경고하고 출발 ({w.Ragnar.Ai.Current}, 말 \"{LastSaid(w, n1)}\")",
                        Departures.AskedAt(w.Ragnar) < 0 && w.Ragnar.Ai.Current == WorkerAI.Mode.Work);
                    yield return Stop();
                    w.Ragnar.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("MeadHealthMinor"), 1); // 아래 늪 칸은 체력 미드를 가진 상태(→ 주의)에서

                    // 4-2) 같은 곳이 늪인데 독 저항 미드가 없으면: 묻지 않고 투덜대고 바로 출발(사용자 결정 2026-10-22)
                    TestKit.TakeFromChest(w.Chest, "MeadPoisonResist");
                    foreach (var it in new List<ItemDrop.ItemData>(w.Ragnar.GetInventory().GetAllItems())) if (Gear.ResistOf(it) == "Poison") w.Ragnar.GetInventory().RemoveItem(it);
                    Brain.Get().SetBiome(far, (int)Heightmap.Biome.Swamp);
                    OnlyFar(far, hz); // 걸어가는 동안 거점 근처 구리를 배웠을 수 있다
                    DepartureResult rr = Departures.JudgeFor(w.Ragnar, copper);
                    TestRunner.Log($"늪 판단: {rr?.Question} {rr?.Condition} — {rr?.Reason}");
                    int heard0 = w.Heard.Count;
                    yield return Say($"!{n1} 구리 10", 1f);
                    yield return TestKit.WaitFor(() => w.Ragnar.Ai.Current == WorkerAI.Mode.Work, 8f);
                    bool grumbled = w.Heard.FindIndex(heard0, h => h.StartsWith(n1 + ":") && h.Contains("독 저항")) >= 0;
                    checks.Add($"늪·독 저항 미드 없음 → 묻지 않고 투덜대며 출발 ({TestKit.Waited:0.0}초, 투덜 {grumbled})",
                        TestKit.Ok && Departures.AskedAt(w.Ragnar) < 0 && grumbled);
                    yield return Stop();
                }
                Brain.ReplaceForTest("", "");

                // 5) 3분 무응답 → 보류
                TestKit.PutInChest(w.Chest, "Wood", 20);
                yield return Say($"!{n1} 나무 10", 1f);
                bool asked = Departures.AskedAt(w.Ragnar) >= 0;
                Departures.AgeQuestion(w.Ragnar, Replies.Timeout + 1.0);
                int before = w.Heard.Count;
                yield return TestKit.WaitFor(() => Departures.AskedAt(w.Ragnar) < 0, 2f);
                checks.Add("3분 무응답 → 보류하고 말함", asked && TestKit.Ok && w.Heard.Count > before && w.Ragnar.Ai.Current != WorkerAI.Mode.Work);
                TestKit.TakeFromChest(w.Chest, "Wood");

                // 6) 일하는 중 새 명령: 급함 아님 → 대기열, 급함 → 하던 일은 대기열 맨 앞
                yield return Say($"!{n1} 나무 30", 1f);
                bool working = w.Ragnar.Ai.Current == WorkerAI.Mode.Work && w.Ragnar.Ai.Job is GatherJob;
                checks.Add($"나무 30 출발 ({w.Ragnar.Ai.Current})", working);
                yield return Say($"!{n1} 나무 5", 1f);
                q = w.Ragnar.Ai.Queue;
                checks.Add($"급하지 않음 → 대기열 ({q.Count}칸, 하던 일 {w.Ragnar.Ai.Job?.Label})",
                    q.Count == 1 && q[0].Work != null && q[0].Work.Amount == 5 && w.Ragnar.Ai.Job is GatherJob g30 && g30.Target == 30);
                yield return Say($"!{n1} 빨리 나무 7", 1f);
                checks.Add($"급함 → 끼어듦 ({q.Count}칸: {Labels(q)}, 지금 {w.Ragnar.Ai.Job?.Label})",
                    q.Count == 2 && q[0].Resume is GatherJob r30 && r30.Target == 30 && q[0].Work != null && q[0].Work.Resume
                    && w.Ragnar.Ai.Job is GatherJob g7 && g7.Target == 7);

                // 7) 저장 왕복: 라그나는 하던 일+대기열 2칸, 플로키는 질문 대기 → 인스턴스를 내렸다가 다시
                TestKit.PutInChest(w.Chest, "Wood", 20);
                yield return Say($"!{n2} 나무 10", 1f);
                checks.Add($"플로키 재고 질문 ({Asked(w.Floki)})", Departures.AskedAt(w.Floki) >= 0);
                if (Features.SaveWork)
                {
                    w.Ragnar.Ai.Queue.Add(WorkSaves.ToQueued(w.Ragnar, new WorkItem { Kind = WorkKind.Make, Resume = true, Label = "구리 주괴 2", Item = "CopperBar", Amount = 2 }));
                    string sigC = Sig(WorkSaves.Snapshot(w.Ragnar));
                    string pendA = WorkState.SavePending(Departures.Snapshot(w.Floki));
                    WorkSaves.SaveNow(w.Ragnar);
                    WorkSaves.SaveNow(w.Floki);
                    TestRunner.Log($"저장: {n1} [{sigC}], {n2} 질문 [{pendA}]");
                    WorkerNpc oldC = w.Ragnar, oldA = w.Floki;
                    TestKit.Unload(oldC.GetComponent<ZNetView>());
                    TestKit.Unload(oldA.GetComponent<ZNetView>());
                    yield return TestKit.WaitFor(() =>
                    {
                        WorkerNpc c = TestKit.Npc(Personality.Ragnar), a = TestKit.Npc(Personality.Floki);
                        return c != null && a != null && c != oldC && a != oldA;
                    }, 5f);
                    checks.Add($"NPC 다시 생김 ({TestKit.Waited:0.0}초)", TestKit.Ok);
                    w.Ragnar = TestKit.Npc(Personality.Ragnar);
                    w.Floki = TestKit.Npc(Personality.Floki);
                    yield return new WaitForSeconds(2.5f); // WorkSaves가 불러오고(1초마다) 한가하면 첫 칸을 꺼냄
                    if (w.Ragnar != null && w.Floki != null)
                    {
                        string sigC2 = Sig(WorkSaves.Snapshot(w.Ragnar));
                        string pendA2 = WorkState.SavePending(Departures.Snapshot(w.Floki));
                        TestRunner.Log($"불러옴: {n1} [{sigC2}] ({w.Ragnar.Ai.Current}), {n2} 질문 [{pendA2}]");
                        checks.Add("하던 일·대기열 그대로", sigC2 == sigC);
                        checks.Add("질문 대기 그대로", pendA2 == pendA && pendA.Length > 0);
                        yield return Say("아니", 1f);
                        checks.Add("불러온 질문에 대답 받음", Departures.AskedAt(w.Floki) < 0);
                    }
                }
                else TestRunner.Log("저장 왕복 건너뜀: 설정 Features.SaveWork 꺼짐");
                yield return Stop();
                TestKit.TakeFromChest(w.Chest, "Wood");

                // 8) 밝혀진 곳은 안다(작업 3): 스위치 끔 → 먼 밝혀진 구역 모름, 켬 → 앎
                // 지도 밝힘은 캐릭터마다 따로라 새 시험 캐릭터는 거점 둘레도 안 밝혀져 있다 → 바닐라 밝히기(걸어다닐 때와 같음)로 둘레 230m를 밝힌다.
                HarmonyLib.AccessTools.Method(typeof(Minimap), "Explore", new[] { typeof(Vector3), typeof(float) })?.Invoke(Minimap.instance, new object[] { w.Home, 230f });
                var cands = new List<ZoneXY>();
                for (int dx = -3; dx <= 3; dx++)
                    for (int dy = -3; dy <= 3; dy++)
                    {
                        var z = new ZoneXY(hz.X + dx, hz.Y + dy);
                        if (z.Ring(hz) >= 2 && MapSense.Explored(z)) cands.Add(z);
                    }
                TestRunner.Log($"밝혀진 곳 후보(거점에서 2~3구역): {cands.Count}구역");
                Brain.ReplaceForTest("", "");
                yield return new WaitForSeconds(2f);
                checks.Add("끔: 먼 밝혀진 구역은 모름", cands.TrueForAll(z => !Brain.Get().IsLooked(z)));
                Features.OverrideKnowExplored(true);
                MapSense.Reset();
                yield return TestKit.WaitFor(() => cands.Exists(z => Brain.Get().IsLooked(z)), 10f);
                int known = cands.FindAll(z => Brain.Get().IsLooked(z)).Count;
                int withItems = cands.FindAll(z => Brain.Get().Get(z) is ZoneMemory m && m.Items.Count > 0).Count;
                checks.Add($"켬: 밝혀진 구역을 앎 ({known}/{cands.Count}구역, 자원 있는 곳 {withItems}, {TestKit.Waited:0.0}초)", cands.Count > 0 && TestKit.Ok && withItems > 0);
                Features.OverrideKnowExplored(false);
            }
        }

        static IEnumerator Say(string text, float wait)
        {
            GatherTest.Say(text, null);
            yield return new WaitForSeconds(wait);
        }

        static IEnumerator Stop()
        {
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
        }

        /// <summary>
        /// 구리는 먼 곳만 아는 것으로. 지난 구리 시험이 거점 앞 12~24m에 놓은 광맥이 시험 월드에 남아 있으면 NPC가 2초마다 둘러보며
        /// 거점 구역의 구리를 배우고, 판단은 가장 가까운 곳(14m)을 골라 밤·죽은 곳·위험 상황이 안 생긴다(2026-10-21).
        /// 판단·명령 직전(같은 프레임)에 부른다.
        /// </summary>
        static void OnlyFar(ZoneXY far, ZoneXY hz)
        {
            foreach (var m in Brain.Get().ZonesWith("CopperOre", hz))
                if (!m.Zone.Equals(far)) Brain.Get().Forget(m.Zone, "CopperOre");
        }

        static int MeadsIn(WorkerNpc npc)
        {
            int n = 0;
            foreach (var it in npc.GetInventory().GetAllItems()) if (Gear.IsHealthMead(it)) n += it.m_stack;
            return n;
        }

        /// <summary>체력 미드 하나를 쥐여 주면 "주의"(depart.warn)로 내려가는 곳인지 — 아래 늪 칸이 쓰는 상황.</summary>
        static bool WarnsWithMead(WorkerNpc npc, ChatCommand cmd)
        {
            var inv = npc.GetInventory();
            inv.AddItem(ObjectDB.instance.GetItemPrefab("MeadHealthMinor"), 1);
            DepartureResult r = Departures.JudgeFor(npc, cmd);
            foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == "MeadHealthMinor") { inv.RemoveItem(it, 1); break; }
            return r != null && r.Key == "depart.warn";
        }

        static string Asked(WorkerNpc npc) => Departures.AskedAt(npc) >= 0 ? "기다림" : "안 물음";

        static string LastSaid(TestKit.World w, string name)
        {
            for (int i = w.Heard.Count - 1; i >= 0; i--)
                if (w.Heard[i].StartsWith(name + ":") && !w.Bubbles.Contains(w.Heard[i])) return w.Heard[i]; // 잡담 말풍선은 건너뜀
            return "";
        }

        static void Log(string what, DepartureResult r) =>
            TestRunner.Log(r == null ? $"{what}: 판단 없음" : $"{what}: {r.Action} {r.Question} ({r.Reason}) 곳 {(r.Place != null ? r.Place.Zone + " " + r.Place.Distance.ToString("0") + "m" : "없음")}, 재고 {(r.Args.TryGetValue("stock", out string s) ? s : "?")}, {r.Seconds:0}초, 하루 {EnvMan.instance.GetDayFraction():0.000}");

        static string Labels(List<QueuedWork> q)
        {
            var parts = new List<string>();
            foreach (var x in q) parts.Add(x.Label);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>저장 비교용(목표 구역은 이어 할 때 다시 고르므로 뺀다).</summary>
        static string Sig(List<WorkItem> items)
        {
            var parts = new List<string>();
            foreach (var x in items) parts.Add($"{x.Kind}:{x.Item}:{x.Amount}:{(x.Resume ? "R" : "")}:{x.Ready}");
            return string.Join(" ", parts.ToArray());
        }
    }
}
