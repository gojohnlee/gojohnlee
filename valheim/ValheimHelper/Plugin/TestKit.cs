using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 묶음 1 새 시험(vh_departtest·vh_peertest) 공통: 깃발·NPC·상자 준비, 조건 기다리기, 구역 내려감 흉내.
    /// 기존 시험 파일은 건드리지 않으려고 따로 둔다.
    /// </summary>
    static class TestKit
    {
        /// <summary>p 또는 둘레 r m(16곳)·r/2 m(8곳)가 건축 금지 구역(시작 제단 등, Location.m_noBuild)인지. 바닐라는 그 안의 흙 파기를 막는다.</summary>
        public static bool NearNoBuild(Vector3 p, float r)
        {
            if (Location.IsInsideNoBuildLocation(p)) return true;
            for (int k = 0; k < 16; k++)
            {
                Vector3 dir = new Vector3(Mathf.Cos(k * Mathf.PI / 8f), 0f, Mathf.Sin(k * Mathf.PI / 8f));
                if (Location.IsInsideNoBuildLocation(p + dir * r) || (k % 2 == 0 && Location.IsInsideNoBuildLocation(p + dir * r * 0.5f))) return true;
            }
            return false;
        }

        /// <summary>
        /// 자동 시험 묶음 첫 시험 전에 한 번(TestRunner): 시험 캐릭터가 월드 시작 제단(건축 금지)에서 시작하면 제단 밖 마른 초원 평지로 옮긴다.
        /// 회차 72: 새 시험 캐릭터(회차 67)가 제단에서 시작해 거점이 보스석 사이에 놓이고(coppertest 귀환이 BossStone에 막힘) 구덩이가 안 파짐(pittest).
        /// 옮겼으면 true(텔레포트가 끝날 때까지 기다린다).
        /// </summary>
        public static bool LeaveNoBuild(Player player)
        {
            Vector3 from = player.transform.position;
            if (!NearNoBuild(from, 45f)) return false;
            WorldGenerator wg = WorldGenerator.instance;
            float wl = ZoneSystem.instance.m_waterLevel;
            for (float r = 60f; r <= 400f; r += 20f)
                for (int d = 0; d < 16; d++)
                {
                    float a = d * Mathf.PI / 8f;
                    Vector3 p = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    float h = wg.GetHeight(p.x, p.z);
                    if (wg.GetBiome(p) != Heightmap.Biome.Meadows || h < wl + 3f) continue;
                    bool ok = true;
                    for (int k = 0; k < 8 && ok; k++)
                    {
                        float b = k * Mathf.PI / 4f;
                        float hk = wg.GetHeight(p.x + Mathf.Cos(b) * 15f, p.z + Mathf.Sin(b) * 15f);
                        ok = hk > wl + 3f && Mathf.Abs(hk - h) < 2f;
                    }
                    if (!ok || NearNoBuild(p, 45f)) continue;
                    player.TeleportTo(new Vector3(p.x, h + 1f, p.z), player.transform.rotation, true);
                    Plugin.Log.LogInfo($"시험 캐릭터가 건축 금지 구역(시작 제단 등) 옆 → {r:0}m 떨어진 초원 평지로 옮김");
                    return true;
                }
            Plugin.Log.LogInfo("건축 금지 구역 밖 평지를 400m 안에서 못 찾음 → 제자리에서 시험");
            return false;
        }

        public sealed class World
        {
            public Player Player;
            public bool God;
            public EnvMan Env;
            public bool DebugTime;
            public float DebugValue;
            public string DebugEnv;
            public bool DebugWind; public float DebugWindAngle, DebugWindIntensity;
            public Vector3 Home;
            public WorkerNpc Ragnar, Floki;
            public Container Chest;
            public readonly List<string> Heard = new List<string>();
            public System.Action<string, string> OnHeard;
            public string Knowledge0, Places0;
            public bool Shared; // 동시 시험(around): 둘레가 남의 시험이라 끝 정리를 안 함
            public bool CountsCreatures; // 생물을 일부러 부르거나 세는 시험(사냥·싸움 등): 시작 때 남은 생물은 환경 불량, 싸움 중 시간 안 멈춤
        }

        /// <summary>WaitFor 결과(코루틴이라 out을 못 씀).</summary>
        public static bool Ok;
        public static float Waited;

        /// <summary>
        /// 생물을 일부러 부르거나 세는 시험(사냥·싸움 등)이면 true — Setup(World.CountsCreatures)·OldClear(strict)가 정하고 시험 끝(OldEnd)에 끔.
        /// false면 자연 생물이 와도 환경 불량이 아니고, WaitFor는 일꾼이 싸우거나 도망치는 동안 시간을 멈춘다(사용자 결정 2026-10-23 오류 꼼수, E015·E016).
        /// </summary>
        public static bool Strict;
        /// <summary>WaitFor 하나가 싸움·도망으로 멈출 수 있는 최대 초(끝없이 기다리지 않게).</summary>
        const float MaxPause = 120f;

        /// <summary>
        /// cond가 될 때까지 최대 max초. 결과는 Ok·Waited. Strict가 아니면 일꾼이 싸우거나 도망치는 동안은 셈에서 빼고(최대 MaxPause초),
        /// 멈춘 만큼 로그에 남긴다(Waited는 멈춘 시간을 뺀 값).
        /// </summary>
        public static IEnumerator WaitFor(System.Func<bool> cond, float max)
        {
            Waited = 0f;
            float paused = 0f;
            Ok = cond();
            while (!Ok && Waited < max)
            {
                yield return null;
                if (!Strict && paused < MaxPause && AnyEngaged()) paused += Time.deltaTime;
                else Waited += Time.deltaTime;
                Ok = cond();
            }
            if (paused >= 0.5f) TestRunner.Log($"싸움·도망으로 시험 시간 {paused:0}초 멈춤");
        }

        static bool AnyEngaged()
        {
            foreach (var npc in WorkerNpc.All)
                if (npc != null && !npc.IsDead() && npc.Ai?.Guard != null && npc.Ai.Guard.Engaged) return true;
            return false;
        }

        /// <summary>
        /// 낮 고정, 신 모드, NPC를 지우고 깃발을 세워 둘을 부르고, 거점 3m에 상자 하나(지정 안 함). 지식·장소 기억은 비워 둔다
        /// (끝나면 End가 원래대로). 실패하면 null(이유는 로그).
        /// </summary>
        public static IEnumerator Setup(World w, Vector3? around = null)
        {
            w.Player = Player.m_localPlayer;
            w.Shared = around.HasValue;
            Strict = w.CountsCreatures;
            w.God = w.Player.InGodMode();
            w.Player.SetGodMode(true);
            w.Env = EnvMan.instance;
            w.DebugTime = w.Env.m_debugTimeOfDay;
            w.DebugValue = w.Env.m_debugTime;
            w.Env.m_debugTimeOfDay = true;
            w.Env.m_debugTime = 0.5f;
            // 날씨도 맑음으로 고정: 비·바람이면 화로가 젖어 조리대가 안 굽는다(E014, 바닐라 Fireplace.CheckWet) — 시험마다 날씨가 달라 흔들리지 않게
            w.DebugEnv = w.Env.m_debugEnv;
            w.Env.m_debugEnv = "Clear";
            // 바람도 약하게 고정: 맑음이어도 바람이 0.8 넘으면 화로가 젖는다(E014 회차 127 — 맑음 바람 0.79). 바람은 5초에 걸쳐 바뀜
            w.DebugWind = w.Env.m_debugWind; w.DebugWindAngle = w.Env.m_debugWindAngle; w.DebugWindIntensity = w.Env.m_debugWindIntensity;
            w.Env.SetDebugWind(0f, 0.3f);
            w.OnHeard = (n, t) => w.Heard.Add(n + ": " + t);
            ChatNet.Heard += w.OnHeard;
            w.Knowledge0 = Brain.Get().Save();
            w.Places0 = Brain.Places.Save();

            // around가 있으면 그 자리 둘레(동시 시험용)만 정리하고 거기에 세운다. 없으면 예전처럼 전부 정리·플레이어 앞.
            if (around.HasValue) Workers.DespawnNear(around, 60f); else Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = (around ?? w.Player.transform.position) + w.Player.transform.forward * 4f;
            if (ZoneSystem.instance.GetGroundHeight(home, out float gh)) home.y = gh;
            w.Home = home;
            // 지난 시험 상자(사용 중 표시가 남으면 비우지도 못해 재고가 틀어짐, 회차 60)를 치운다. 동시 시험(around)은 남의 상자라 안 건드림.
            if (!around.HasValue && GatherTest.ClearOldChests(home) > 0) yield return new WaitForSeconds(0.5f);
            w.Player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, w.Player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(3f);
            w.Ragnar = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Ragnar);
            w.Floki = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Floki);
            if (w.Ragnar == null || w.Floki == null) yield break;
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                foreach (var n in new[] { "AxeFlint", "PickaxeAntler", "ArmorLeatherChest", "ArmorLeatherLegs" })
                    npc.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                Prep.WearBest(npc);
                var food = new Inventory("vh_test_food", null, 4, 2);
                foreach (var n in new[] { "CookedMeat", "Honey", "Blueberries" }) food.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                npc.EatFrom(food);
            }
            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            Vector3 spot = GatherTest.Ground(home + right * 3f);
            w.Player.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), spot, w.Player.transform.rotation, doAttack: false);
            yield return new WaitForSeconds(1f);
            w.Chest = GatherTest.ChestAt(spot);
            Brain.ReplaceForTest("", "");
            if (w.Chest != null) yield return CheckStart(w, !around.HasValue);
        }

        /// <summary>시작 조건이 안 맞음 — TestRunner.Safe가 "판정: 환경 불량"으로 적고 본시험은 안 한다(PASS 아님).</summary>
        public sealed class EnvBadException : System.Exception
        {
            public EnvBadException(string why) : base(why) { }
        }

        static World s_cur;
        static float s_t0;
        static readonly Queue<string> s_flow = new Queue<string>();
        const int FlowMax = 40;
        const float StartRadius = 60f;

        /// <summary>
        /// 시작 조건(사용자 결정 2026-10-22 시험 환경): 둘레 생물(적·동료 해골 등)은 먼저 치우고, 그래도 남거나 플레이어·일꾼·상자가
        /// 시험 시작 상태가 아니면 EnvBadException. 맞으면 시작 상태를 적고 상태 흐름 기록을 시작한다.
        /// </summary>
        static IEnumerator CheckStart(World w, bool clear)
        {
            if (clear)
            {
                int n = ClearCreatures(w.Home);
                if (n > 0)
                {
                    TestRunner.Log($"시작 정리: 거점 {StartRadius:0}m 안 생물 {n}마리 치움");
                    yield return new WaitForSeconds(0.5f);
                }
            }
            var bad = new List<string>();
            if (w.Player == null || w.Player.IsDead()) bad.Add("플레이어 없음·죽음");
            else if (!w.Player.InGodMode()) bad.Add("플레이어 무적 아님");
            int left = Creatures(w.Home).Count;
            if (left > 0 && Strict) bad.Add($"거점 {StartRadius:0}m 안 생물 {left}");
            else if (left > 0) TestRunner.Log($"시작: 거점 {StartRadius:0}m 안 생물 {left} 남음 — 오면 일꾼이 싸움(환경 불량 아님)");
            foreach (var npc in new[] { w.Ragnar, w.Floki })
            {
                if (npc == null || npc.IsDead()) { bad.Add("일꾼 없음·죽음"); continue; }
                if (npc.Ai.Job != null) bad.Add($"{npc.NpcName} 일 중({npc.Ai.Job.Label})");
                if (npc.Ai.Queue.Count > 0) bad.Add($"{npc.NpcName} 대기열 {npc.Ai.Queue.Count}");
            }
            if (Items(w.Chest.GetInventory()) > 0) bad.Add("새 상자가 비어 있지 않음");
            s_cur = w;
            s_t0 = Time.time;
            s_flow.Clear();
            TestRunner.Log("시작 상태: " + Snapshot(w));
            if (bad.Count > 0)
            {
                s_cur = null;
                throw new EnvBadException(string.Join(", ", bad.ToArray()));
            }
            Plugin.Instance.StartCoroutine(Track(w));
        }

        /// <summary>거점 둘레의 플레이어·일꾼이 아닌 생물(적·짐승·동료 해골).</summary>
        static List<Character> Creatures(Vector3 home)
        {
            var list = new List<Character>();
            foreach (Character c in Character.GetAllCharacters())
                if (c != null && !c.IsDead() && !(c is Player) && !(c is WorkerNpc)
                    && Vector3.Distance(c.transform.position, home) < StartRadius) list.Add(c);
            return list;
        }

        static int ClearCreatures(Vector3 home)
        {
            int n = 0;
            foreach (var c in Creatures(home))
            {
                var view = c.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(c.gameObject);
                n++;
            }
            return n;
        }

        /// <summary>거점 둘레 땅에 떨어진 물건.</summary>
        static List<ZNetView> Drops(Vector3 home)
        {
            var list = new List<ZNetView>();
            foreach (var zdo in Senses.Nearby(home, StartRadius))
            {
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                if (v != null && v.IsValid() && v.GetComponent<ItemDrop>() != null) list.Add(v);
            }
            return list;
        }

        /// <summary>
        /// 끝 정리(사용자 결정 2026-10-22 시험 환경): 시험이 남긴 둘레 생물·땅의 물건을 치우고, 0.5초 뒤에도 남으면
        /// 정리 실패로 남은 자동 시험을 멈춘다(TestRunner.Halt — 앞 시험이 뒤 시험을 망치지 않게).
        /// </summary>
        static void Cleanup(Vector3 home)
        {
            int creatures = ClearCreatures(home), drops = 0, logs = 0;
            foreach (var v in Drops(home))
            {
                v.ClaimOwnership();
                ZNetScene.instance.Destroy(v.gameObject);
                drops++;
            }
            // 시험이 남긴 통나무(쓰러뜨린 나무·쪼갠 반토막)도 치운다: 다음 시험 일꾼 곁에서 밀리거나 구르면 친다(E020 — 회차 95 묶음 stonetest가 Tree에 죽음)
            foreach (var zdo in Senses.Nearby(home, StartRadius))
            {
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                if (v == null || !v.IsValid() || v.GetComponent<TreeLog>() == null) continue;
                v.ClaimOwnership();
                ZNetScene.instance.Destroy(v.gameObject);
                logs++;
            }
            if (creatures + drops + logs > 0) TestRunner.Log($"끝 정리: 생물 {creatures}·땅의 물건 {drops}·통나무 {logs} 치움");
            Plugin.Instance.StartCoroutine(VerifyCleanup(home));
        }

        static Vector3? s_oldHome;

        /// <summary>
        /// 옛 시험(Setup을 안 쓰는 것)의 시작 정리: 깃발을 세우기 전에 거점 둘레 생물을 치우고 거점을 기억한다
        /// (끝 정리는 TestRunner.Finish가 OldEnd로). 치운 수를 돌려준다(0보다 크면 0.5초 기다린 뒤 OldCheck).
        /// </summary>
        public static int OldClear(Vector3 home, bool strict = false)
        {
            s_oldHome = home;
            Strict = strict;
            int n = ClearCreatures(home);
            if (n > 0) TestRunner.Log($"시작 정리: 거점 {StartRadius:0}m 안 생물 {n}마리 치움");
            return n;
        }

        /// <summary>그래도 생물이 남으면 신 모드를 되돌리고 EnvBadException(본시험 안 함, PASS 아님).</summary>
        public static void OldCheck(Vector3 home, Player player, bool god)
        {
            var left = Creatures(home);
            if (left.Count == 0) return;
            if (!Strict)
            {
                TestRunner.Log($"시작: 거점 {StartRadius:0}m 안 생물 {left.Count} 남음 — 오면 일꾼이 싸움(환경 불량 아님)");
                return;
            }
            s_oldHome = null;
            player.SetGodMode(god);
            var names = new List<string>();
            foreach (var c in left) names.Add($"{Utils.GetPrefabName(c.gameObject)} {Vector3.Distance(c.transform.position, home):0}m{(c.GetComponent<ZNetView>()?.IsValid() == true ? "" : " 무효")}");
            throw new EnvBadException($"거점 {StartRadius:0}m 안 생물 {left.Count} ({string.Join(", ", names.ToArray())})");
        }

        /// <summary>옛 시험 끝 정리(TestRunner.Finish가 부름, OldClear를 안 부른 시험은 아무것도 안 함).</summary>
        public static void OldEnd()
        {
            Strict = false;
            if (s_oldHome == null) return;
            Vector3 home = s_oldHome.Value;
            s_oldHome = null;
            try { Cleanup(home); }
            catch (System.Exception e) { TestRunner.Halt("끝 정리 오류: " + e.Message); }
        }

        static IEnumerator VerifyCleanup(Vector3 home)
        {
            yield return new WaitForSeconds(0.5f); // 없앤 것은 프레임 끝에 사라짐
            int c = Creatures(home).Count, d = Drops(home).Count;
            if (c > 0)
            {
                // 그 사이 저절로 생긴 짐승일 수 있음(회차 96: portaltest 끝 0.5초 뒤 1마리) → 이름 남기고 한 번 더 치움
                var names = new List<string>();
                foreach (var cr in Creatures(home)) names.Add($"{Utils.GetPrefabName(cr.gameObject)} {Vector3.Distance(cr.transform.position, home):0}m");
                TestRunner.Log($"끝 정리 뒤 생물 {c} ({string.Join(", ", names.ToArray())}) → 다시 치움");
                ClearCreatures(home);
                yield return new WaitForSeconds(0.5f);
                c = Creatures(home).Count;
                d = Drops(home).Count;
            }
            // 남은 생물은 다음 시험을 막지 않음(자연 생물 허용, 사용자 결정 2026-10-23 — 생물을 세는 시험은 시작 때 치우고 못 치우면 환경 불량)
            if (c > 0) TestRunner.Log($"끝 정리 뒤에도 생물 {c} 남음 — 다음 시험은 그대로");
            if (d > 0) TestRunner.Halt($"앞 시험 끝 정리 실패(거점 {StartRadius:0}m 안 물건 {d} 남음)");
        }

        static int Items(Inventory inv)
        {
            int n = 0;
            if (inv != null) foreach (var it in inv.GetAllItems()) n += it.m_stack;
            return n;
        }

        /// <summary>한 줄 상태: 일꾼(체력·거점까지·일/단계·짐·대기열)·상자(개수·사용 중)·둘레 생물.</summary>
        public static string Snapshot(World w)
        {
            string N(WorkerNpc n) => n == null ? "-" :
                $"{n.NpcName} hp{n.GetHealth():0}/{n.GetMaxHealth():0} {Vector3.Distance(n.transform.position, w.Home):0}m " +
                $"{(n.Ai.Job == null ? "Idle" : n.Ai.Job.GetType().Name + "/" + n.Ai.Job.StepName)} 짐{Items(n.GetInventory())} 대기{n.Ai.Queue.Count}";
            string chest = w.Chest == null ? "-" : $"{Items(w.Chest.GetInventory())}{(w.Chest.IsInUse() ? " 사용중" : "")}";
            return $"[{Time.time - s_t0:0}s] {N(w.Ragnar)} | {N(w.Floki)} | 상자 {chest} | 생물 {Creatures(w.Home).Count}";
        }

        /// <summary>시험 동안 3초마다 상태를 링 버퍼에(FAIL이면 End가 diag에 쏟음).</summary>
        static IEnumerator Track(World w)
        {
            while (s_cur == w && TestRunner.Running)
            {
                try
                {
                    s_flow.Enqueue(Snapshot(w));
                    while (s_flow.Count > FlowMax) s_flow.Dequeue();
                }
                catch (System.Exception e) { s_flow.Enqueue("기록 오류: " + e.Message); }
                yield return new WaitForSeconds(3f);
            }
        }

        /// <summary>검사 하나가 FAIL인 순간의 상태(Checks.Add가 부름).</summary>
        public static void NoteFail()
        {
            if (s_cur == null) return;
            try { TestRunner.Log("  상태(FAIL 순간): " + Snapshot(s_cur)); }
            catch (System.Exception e) { TestRunner.Log("  상태 기록 오류: " + e.Message); }
        }

        /// <summary>
        /// 중첩 코루틴(yield return IEnumerator)을 한 줄로 편다. Unity에 넘기면 안쪽 예외를 TestRunner.Safe가 못 잡아
        /// 시험이 FAIL 없이 멈추므로, 새 시험은 등록할 때 이걸 거친다.
        /// </summary>
        public static IEnumerator Flat(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator top = stack.Peek();
                    bool moved;
                    try { moved = top.MoveNext(); }
                    catch
                    {
                        // 바깥 코루틴들의 finally(Restore·정리)가 돌게 닫고 다시 던진다(TestRunner.Safe가 FAIL·환경 불량으로 적음).
                        while (stack.Count > 0) (stack.Pop() as System.IDisposable)?.Dispose();
                        throw;
                    }
                    if (!moved) { stack.Pop(); continue; }
                    if (top.Current is IEnumerator inner) { stack.Push(inner); continue; }
                    yield return top.Current;
                }
            }
            finally
            {
                // 한도로 끊겨 Dispose되면(TestRunner.Safe) 안쪽 finally(Restore·정리)도 돈다
                while (stack.Count > 0) (stack.Pop() as System.IDisposable)?.Dispose();
            }
        }

        /// <summary>시험이 바꾼 것을 되돌린다(예외로 끝나도 finally에서 부름, 여러 번 불러도 됨).</summary>
        public static void Restore(World w)
        {
            if (s_cur == w) s_cur = null; // 상태 흐름 기록 멈춤(링 버퍼는 End가 쏟을 때까지 남김)
            if (w.OnHeard != null) { ChatNet.Heard -= w.OnHeard; w.OnHeard = null; }
            if (w.Player != null) w.Player.SetGodMode(w.God);
            if (w.Env != null)
            {
                w.Env.m_debugTimeOfDay = w.DebugTime;
                w.Env.m_debugTime = w.DebugValue;
                w.Env.m_debugEnv = w.DebugEnv ?? "";
                w.Env.m_debugWind = w.DebugWind; w.Env.m_debugWindAngle = w.DebugWindAngle; w.Env.m_debugWindIntensity = w.DebugWindIntensity;
            }
            if (w.Knowledge0 != null)
            {
                Brain.ReplaceForTest(w.Knowledge0, w.Places0); // 시험이 만든 지식·장소 기억을 지우고 원래대로
                Brain.Save();
                w.Knowledge0 = null;
            }
        }

        public static void End(World w, GatherTest.Checks checks)
        {
            bool clean = true;
            foreach (var h in w.Heard) if (h.Contains("{")) clean = false;
            checks.Add("대사에 자리표시자 안 남음", clean);
            if (w.Ragnar != null)
            {
                try { TestRunner.Log("끝 상태: " + Snapshot(w)); }
                catch (System.Exception e) { TestRunner.Log("끝 상태 기록 오류: " + e.Message); }
            }
            if (checks.Passed != checks.Total && s_flow.Count > 0)
            {
                TestRunner.Log("--- 상태 흐름(3초마다, 최근 " + s_flow.Count + "개) ---");
                foreach (var f in s_flow) TestRunner.Log(f);
            }
            if (!w.Shared) Cleanup(w.Home);
            Restore(w);
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in w.Heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }

        public static void PutInChest(Container c, string prefab, int n) =>
            Chests.TryEdit(c, inv => inv.AddItem(ObjectDB.instance.GetItemPrefab(prefab), n));

        public static void TakeFromChest(Container c, string prefab)
        {
            string name = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            Chests.TryEdit(c, inv => inv.RemoveItem(name, inv.CountItems(name)));
        }

        /// <summary>거점 상자 전부(지난 시험이 남긴 것 포함)에서 그 물건들을 비운다. 비운 개수.</summary>
        public static int ClearBaseStock(World w, params string[] prefabs)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(w.Home))
                foreach (var p in prefabs)
                {
                    int had = GatherTest.Count(c, p);
                    if (had > 0) { TakeFromChest(c, p); n += had - GatherTest.Count(c, p); }
                }
            return n;
        }

        static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");

        /// <summary>
        /// 구역이 내려간 것처럼 인스턴스만 없앤다(ZDO는 남김) — 바닐라 ZNetScene.RemoveObjects와 같은 순서.
        /// 플레이어 근처면 ZNetScene이 다음 프레임에 ZDO에서 다시 만든다(재접속·구역 다시 불러오기 흉내).
        /// </summary>
        public static void Unload(ZNetView view)
        {
            if (view == null || !view.IsValid()) return;
            ZDO zdo = view.GetZDO();
            view.ResetZDO();
            Object.Destroy(view.gameObject);
            Instances(ZNetScene.instance).Remove(zdo);
        }

        /// <summary>NPC 인스턴스(이름으로, 다시 만들어진 것도).</summary>
        public static WorkerNpc Npc(Core.Npc.Personality p) => WorkerNpc.All.Find(n => n != null && n.Personality == p);

        public static string Name(WorkerNpc npc) => npc != null ? npc.NpcName : "?";
    }
}
