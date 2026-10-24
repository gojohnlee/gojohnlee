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

        /// <summary>
        /// 늪 회귀(사용자 결정 2026-10-23 "늪 회귀로 오류 정리"): autotest.txt "biome=swamp"(스크립트 $env:VH_BIOME). None이면 예전처럼 제자리.
        /// </summary>
        public static Heightmap.Biome RunBiome = Heightmap.Biome.None;
        /// <summary>시험이 스스로 자리를 찾을 때 바라는 바이옴(보통 초원, 늪 회귀면 그 바이옴).</summary>
        public static Heightmap.Biome SiteBiome => RunBiome == Heightmap.Biome.None ? Heightmap.Biome.Meadows : RunBiome;
        /// <summary>시험 자리 찾기의 마른 땅 여유(물 위 m, 생성 높이). 늪은 물보다 1m 높은 땅도 드물어 0.5m(회차 138).</summary>
        public static float DryMin => RunBiome == Heightmap.Biome.Swamp ? 0.5f : 2f;
        /// <summary>
        /// 늪 회귀에서도 원래 자리(제단 옆 초원)에서 도는 시험: 밭 작물은 늪에서 안 자람(밭·아침 정산),
        /// 구덩이 시험은 물 높이 아래로 판 구덩이가 물에 잠겨 마른 구덩이 탈출을 못 봄(자리 찾기도 초원 고정).
        /// </summary>
        static readonly HashSet<string> s_stayHome = new HashSet<string> { "vh_farmtest", "vh_settletest", "vh_pittest" };
        static Vector3? s_origin, s_biomeSpot;

        public static void SetRunBiome(string name)
        {
            try { RunBiome = (Heightmap.Biome)System.Enum.Parse(typeof(Heightmap.Biome), name, true); }
            catch (System.ArgumentException) { TestRunner.Halt($"모르는 바이옴 {name}"); }
        }

        /// <summary>
        /// 자동 시험마다 시작 전에(TestRunner): RunBiome이 있으면 그 바이옴 마른 평지(처음 한 번 찾음)로, 원래 자리 시험(s_stayHome)이면 처음 자리로 옮긴다.
        /// 옮겼으면 true(텔레포트가 끝날 때까지 기다린다). 못 찾으면 남은 시험은 환경 불량.
        /// </summary>
        public static bool GoSite(Player player, string test)
        {
            if (RunBiome == Heightmap.Biome.None) return false;
            if (s_origin == null)
            {
                s_origin = player.transform.position;
                s_biomeSpot = FindBiomeSpot(s_origin.Value, RunBiome);
                if (s_biomeSpot == null && RunBiome == Heightmap.Biome.Swamp)
                {
                    // 기본안(회차 138, decisions "늪 회귀로 오류 정리"의 "더 험한 끝낸 바이옴"): 늪에 마른 땅이 없으면 검은숲(언덕·바위·나무·회색난쟁이)
                    Plugin.Log.LogWarning("늪에 마른 땅이 없어 검은숲에서 회귀");
                    RunBiome = Heightmap.Biome.BlackForest;
                    s_biomeSpot = FindBiomeSpot(s_origin.Value, RunBiome);
                }
                if (s_biomeSpot == null) { TestRunner.Halt($"{RunBiome} 마른 평지를 못 찾음"); return false; }
                Plugin.Log.LogInfo($"{RunBiome} 회귀: 시험 자리 {s_biomeSpot.Value} (처음 자리에서 {Utils.DistanceXZ(s_origin.Value, s_biomeSpot.Value):0}m)");
            }
            if (s_biomeSpot == null) return false;
            Vector3 want = s_stayHome.Contains(test) ? s_origin.Value : s_biomeSpot.Value;
            if (Utils.DistanceXZ(player.transform.position, want) < 30f) return false;
            player.TeleportTo(want + Vector3.up, player.transform.rotation, true);
            Plugin.Log.LogInfo($"{test}: {(want == s_origin.Value ? "원래 자리(바이옴 자원·물 문제)" : RunBiome + " 시험 자리")}로 옮김");
            return true;
        }

        /// <summary>
        /// from에서 100m~5km를 바깥으로 돌며 가운데가 그 바이옴이고 15m 둘레가 평평한(3m 안) 자리 중, 가운데·둘레 가장 낮은 곳이 물보다 1m 넘는 첫 자리
        /// — 없으면 0.5m 넘는 것 중 가장 높은 자리(회차 138: 시험 월드 늪은 안쪽 표본 8829곳 중 가장 높아도 물 위 3.4m, 평평한 곳은 0m 이하). 생성 높이라 안 불러온 곳도 본다.
        /// </summary>
        static Vector3? FindBiomeSpot(Vector3 from, Heightmap.Biome biome)
        {
            WorldGenerator wg = WorldGenerator.instance;
            float wl = ZoneSystem.instance.m_waterLevel;
            Vector3? best = null;
            float bestLow = 0.5f;
            int seen = 0;
            float maxH = float.MinValue, maxLow = float.MinValue; // 못 찾을 때 볼 통계: 표본 가운데 최고·평평한 자리 최저의 최고(물 위 m)
            for (float r = 100f; r <= 5000f; r += 25f)
            {
                int n = Mathf.Max(32, Mathf.CeilToInt(2f * Mathf.PI * r / 25f)); // 25m 간격(회차 138: 50m·32방향은 늪 표본 131곳뿐)
                for (int d = 0; d < n; d++)
                {
                    float a = d * 2f * Mathf.PI / n;
                    Vector3 p = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (wg.GetBiome(p) != biome) continue;
                    seen++;
                    float h = wg.GetHeight(p.x, p.z), low = h - wl;
                    maxH = Mathf.Max(maxH, low);
                    bool ok = true;
                    for (int k = 0; k < 8 && ok; k++)
                    {
                        Vector3 dir = new Vector3(Mathf.Cos(k * Mathf.PI / 4f), 0f, Mathf.Sin(k * Mathf.PI / 4f));
                        Vector3 q = p + dir * 15f;
                        float hk = wg.GetHeight(q.x, q.z);
                        low = Mathf.Min(low, hk - wl);
                        ok = Mathf.Abs(hk - h) < 3f; // 회차 138: 늪 안쪽은 물보다 1m 높은 평지가 없어 가장자리(둘레는 다른 바이옴이어도 됨)·3m 안까지 봄
                    }
                    if (!ok) continue;
                    maxLow = Mathf.Max(maxLow, low);
                    if (low <= bestLow || NearNoBuild(p, 45f)) continue;
                    best = new Vector3(p.x, h, p.z);
                    bestLow = low;
                    if (low >= 1f) { r = float.MaxValue; break; } // 충분히 마른 첫 자리면 그만 찾음
                }
            }
            Plugin.Log.LogInfo($"{biome} 자리 찾기: 그 바이옴 표본 {seen}곳(가운데 최고 {maxH:0.0}m·평평한 곳 최저의 최고 {maxLow:0.0}m), 고른 자리 물 위 최저 {(best == null ? "-" : bestLow.ToString("0.0"))}m");
            return best;
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
            public System.Action<string, string> OnHeard, OnBubbled;
            /// <summary>Heard 중 잡담 말풍선 줄(질문·대답을 볼 때 건너뛰려고 — departtest 회차 128: 질문 직후 말풍선이 끼어 "마지막 말"이 바뀜).</summary>
            public readonly HashSet<string> Bubbles = new HashSet<string>();
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
            w.OnBubbled = (n, t) => w.Bubbles.Add(n + ": " + t);
            ChatNet.Bubbled += w.OnBubbled;
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
            if (!around.HasValue && SweepGround(home) > 0) yield return new WaitForSeconds(0.5f); // 앞 시험이 남긴 땅 물건·몸·통나무(개선 셋 가)
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
            if (RunBiome != Heightmap.Biome.None) LogSpawners(w.Home);
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
            StartStalls();
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

        /// <summary>진단(바이옴 회귀): 거점 둘레의 생물 생성기(둥지 등 SpawnArea — 바닐라 SpawnSystem과 달리 로그를 안 남김).</summary>
        static void LogSpawners(Vector3 home)
        {
            var list = new List<string>();
            foreach (var s in Object.FindObjectsByType<SpawnArea>(FindObjectsSortMode.None))
            {
                float d = Utils.DistanceXZ(s.transform.position, home);
                if (d <= 200f) list.Add($"{Utils.GetPrefabName(s.gameObject)} {d:0}m(발동 {s.m_triggerDistance:0}m·{s.m_spawnIntervalSec:0}초·곁 {s.m_maxNear})");
            }
            TestRunner.Log($"둘레 생성기(200m): {(list.Count == 0 ? "없음" : string.Join(", ", list.ToArray()))}");
        }

        public static int ClearCreatures(Vector3 home)
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

        /// <summary>ClearWhileHeld가 치운 생물 수.</summary>
        public static int KeptClear;

        /// <summary>
        /// 시험이 일꾼 발을 묶은 동안(held()가 null 아님) 1초마다 그 일꾼·거점 둘레 생물을 치운다. 묶인 일꾼은 도망도 싸움도 못 하고
        /// 죽어도 같은 몸으로 거점에서 일어나 묶인 채라(WorkerNpc.OnDeath), 자연 생물이 오면 거점에서 되풀이해 죽는다
        /// (회차 141 검은숲 vh_hoptest: 회색 난쟁이에 60번 넘게 죽어 뒷칸 FAIL). running()이 false가 되면 끝.
        /// </summary>
        public static IEnumerator ClearWhileHeld(World w, System.Func<Character> held, System.Func<bool> running)
        {
            KeptClear = 0;
            while (running())
            {
                Character c = held();
                if (c != null && !Strict) KeptClear += ClearCreatures(c.transform.position) + ClearCreatures(w.Home);
                yield return new WaitForSeconds(1f);
            }
        }

        /// <summary>
        /// 바이옴 회귀(RunBiome) 채집 시험 동안 1초마다 거점 둘레(StartRadius) 생물을 치운다. 시험 일꾼은 뿔 곡괭이만 들고 갑옷이 없어 검은숲 회색난쟁이
        /// 한 마리에도 도망치고(잃을 체력 비율 5), 도망은 설계상 그 목표를 버린다(구상서 8장 a) — 회차 142 검은숲 vh_coppertest: 거점 12m 앞 시험 광맥을
        /// 버리고 480m 밖 광맥으로 감. 시험 자리만 지키고 먼 구역은 그대로. running()이 false이거나 다음 시험이 시작되면 끝. 치운 수는 KeptClear.
        /// </summary>
        public static IEnumerator KeepSiteClear(Vector3 home, System.Func<bool> running)
        {
            KeptClear = 0;
            int gen = TestRunner.Gen;
            while (running() && TestRunner.Running && TestRunner.Gen == gen)
            {
                if (!Strict) KeptClear += ClearCreatures(home);
                yield return new WaitForSeconds(1f);
            }
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
        /// 시작 땅 정리(개선 셋 가, 사용자 10-25): 앞 시험이 남긴 거점 둘레 땅 물건·쓰러진 몸(Ragdoll — 몇 초 뒤 전리품을 떨굼)·통나무 수를
        /// 이름과 함께 diag에 늘 적고(0이어도 — E025·E027·E033 대조용), 있으면 치운다. 지금 일꾼 것이 아닌 묘비도(이름·거리). 치운 수를 돌려준다(0보다 크면 0.5초 기다림).
        /// </summary>
        static int SweepGround(Vector3 home)
        {
            var names = new Dictionary<string, int>();
            int drops = 0, bodies = 0, logs = 0;
            foreach (var v in Drops(home))
            {
                var it = v.GetComponent<ItemDrop>();
                string n = Utils.GetPrefabName(v.gameObject);
                names.TryGetValue(n, out int c);
                names[n] = c + (it != null && it.m_itemData != null ? it.m_itemData.m_stack : 1);
                v.ClaimOwnership();
                ZNetScene.instance.Destroy(v.gameObject);
                drops++;
            }
            foreach (var r in Object.FindObjectsByType<Ragdoll>(FindObjectsSortMode.None))
            {
                var rv = r.GetComponent<ZNetView>();
                if (rv == null || !rv.IsValid() || Vector3.Distance(r.transform.position, home) > StartRadius) continue;
                rv.ClaimOwnership();
                ZNetScene.instance.Destroy(r.gameObject);
                bodies++;
            }
            foreach (var zdo in Senses.Nearby(home, StartRadius))
            {
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                if (v == null || !v.IsValid() || v.GetComponent<TreeLog>() == null) continue;
                v.ClaimOwnership();
                ZNetScene.instance.Destroy(v.gameObject);
                logs++;
            }
            // 옛 묘비(사용자 요청 10-25): 원본 월드에 옛 이름 "Claude" 묘비가 있음 — 지금 일꾼(Name1·Name2) 것이 아니면 이름·거리를 적고 치운다
            var graves = new List<string>();
            foreach (var zdo in Senses.Nearby(home, StartRadius))
            {
                ZNetView v = ZNetScene.instance.FindInstance(zdo);
                if (v == null || !v.IsValid() || v.GetComponent<TombStone>() == null) continue;
                string owner = zdo.GetString(ZDOVars.s_ownerName);
                if (owner == Workers.Name1 || owner == Workers.Name2) continue;
                graves.Add($"{owner} {Vector3.Distance(zdo.GetPosition(), home):0}m");
                v.ClaimOwnership();
                ZNetScene.instance.Destroy(v.gameObject);
            }
            var parts = new List<string>();
            foreach (var kv in names) parts.Add($"{kv.Key} {kv.Value}");
            TestRunner.Log($"시작 땅: 거점 {StartRadius:0}m 안 물건 {drops}{(parts.Count > 0 ? " (" + string.Join(", ", parts.ToArray()) + ")" : "")}·쓰러진 몸 {bodies}·통나무 {logs}·옛 묘비 {graves.Count}{(graves.Count > 0 ? " (" + string.Join(", ", graves.ToArray()) + ")" : "")}"
                + (drops + bodies + logs + graves.Count > 0 ? " → 치우고 시작" : ""));
            return drops + bodies + logs + graves.Count;
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
            return n + SweepGround(home); // 앞 시험이 남긴 땅 물건·몸·통나무도(개선 셋 가)
        }

        /// <summary>그래도 생물이 남으면 신 모드를 되돌리고 EnvBadException(본시험 안 함, PASS 아님).</summary>
        public static void OldCheck(Vector3 home, Player player, bool god)
        {
            StartStalls();
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
            try { LogStalls(); }
            catch (System.Exception e) { TestRunner.Log("멈춤 기록 오류: " + e.Message); }
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
            // 죽은 적의 쓰러진 몸(Ragdoll)은 몇 초 뒤 사라지며 전리품을 떨군다(E027: raidtest 뒤 0.5초에 물건 1) → 몸을 치우고 물건을 이름과 함께 남긴 뒤 다시 치움(최대 3번)
            for (int i = 0; i < 3 && d > 0; i++)
            {
                var names = new List<string>();
                foreach (var v in Drops(home)) names.Add($"{Utils.GetPrefabName(v.gameObject)} {Vector3.Distance(v.transform.position, home):0}m");
                int bodies = 0;
                foreach (var r in Object.FindObjectsByType<Ragdoll>(FindObjectsSortMode.None))
                {
                    var rv = r.GetComponent<ZNetView>();
                    if (rv == null || !rv.IsValid() || Vector3.Distance(r.transform.position, home) > StartRadius + 20f) continue;
                    rv.ClaimOwnership();
                    ZNetScene.instance.Destroy(r.gameObject);
                    bodies++;
                }
                TestRunner.Log($"끝 정리 뒤 물건 {d} ({string.Join(", ", names.ToArray())}), 쓰러진 몸 {bodies} → 다시 치움");
                foreach (var v in Drops(home)) { v.ClaimOwnership(); ZNetScene.instance.Destroy(v.gameObject); }
                yield return new WaitForSeconds(1f);
                d = Drops(home).Count;
            }
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


        // ---- 멈춤 재기(사용자 요청 10-25 "멍하니 서 있기"): 일이 있는데 5초 넘게 1m 안에서 안 움직이고 안 휘두르면 한 번 ----
        const float StallMin = 5f, StallMove = 1f, StallTick = 0.25f;
        sealed class StallState { public Vector3 Anchor; public float Since; public string Label; public string Why; }
        static readonly Dictionary<WorkerNpc, StallState> s_stall = new Dictionary<WorkerNpc, StallState>();
        // 끝난 멈춤(까닭·단계, 초): 까닭 없는 것 / 일부러 선 것(WorkerAI.Hold·질문 답 기다림)
        static readonly List<KeyValuePair<string, float>> s_stalls = new List<KeyValuePair<string, float>>();
        static readonly List<KeyValuePair<string, float>> s_holds = new List<KeyValuePair<string, float>>();

        static object s_stallRun;
        static int s_samples;       // 일 중 1m 안에 서 있던 표본 수(재기가 도는지 보려고)
        static float s_maxStill;    // 그중 가장 오래 선 초(5초 안 되는 것까지)
        static readonly Dictionary<string, float> s_holdTime = new Dictionary<string, float>(); // Hold 까닭별 합(5초 안 되는 것까지)
        static int s_miss0;         // 시작 때 GatherJob.MissSwings

        /// <summary>멈춤 재기 시작(새 시험 CheckStart·옛 시험 OldCheck). 끝은 LogStalls(End·OldEnd).</summary>
        public static void StartStalls()
        {
            var run = s_stallRun = new object();
            Plugin.Instance.StartCoroutine(Stalls(run));
        }

        static IEnumerator Stalls(object run)
        {
            s_stall.Clear(); s_stalls.Clear(); s_holds.Clear();
            s_samples = 0; s_maxStill = 0f;
            s_holdTime.Clear(); s_miss0 = GatherJob.MissSwings;
            while (s_stallRun == run && TestRunner.Running)
            {
                try
                {
                    foreach (var npc in WorkerNpc.All)
                    {
                        if (npc == null || npc.IsDead() || npc.Ai == null) continue;
                        var ai = npc.Ai;
                        Vector3 p = npc.transform.position;
                        if (ai.Job != null && Time.time - ai.HoldAt < StallTick + 0.1f && ai.HoldWhy != null)
                        {
                            s_holdTime.TryGetValue(ai.HoldWhy, out float ht);
                            s_holdTime[ai.HoldWhy] = ht + StallTick;
                        }
                        if (!s_stall.TryGetValue(npc, out StallState st)) s_stall[npc] = st = new StallState { Anchor = p, Since = Time.time };
                        bool idle = ai.Job == null || (ai.Guard != null && ai.Guard.Engaged) || npc.InAttack();
                        if (idle || Vector3.Distance(p, st.Anchor) > StallMove)
                        {
                            CloseStall(npc, st);
                            st.Anchor = p; st.Since = Time.time; st.Label = null; st.Why = null;
                            continue;
                        }
                        if (st.Label == null) st.Label = ai.Job.GetType().Name + "/" + ai.Job.StepName;
                        s_samples++;
                        s_maxStill = Mathf.Max(s_maxStill, Time.time - st.Since);
                        if (st.Why == null)
                        {
                            if (Time.time - ai.HoldAt < 0.6f) st.Why = ai.HoldWhy;
                            else if (Departures.Awaiting) st.Why = "질문 답 기다림";
                        }
                    }
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("멈춤 재기 오류: " + e.Message); }
                yield return new WaitForSeconds(StallTick);
            }
        }

        static void CloseStall(WorkerNpc npc, StallState st)
        {
            float t = Time.time - st.Since;
            if (st.Label == null || t < StallMin) return;
            string what = st.Why != null ? $"{st.Why} {st.Label}" : st.Label;
            (st.Why != null ? s_holds : s_stalls).Add(new KeyValuePair<string, float>(what, t));
            Plugin.Log.LogInfo($"멈춤 {t:0.0}초: {npc.NpcName} {what}{(st.Why != null ? " (일부러)" : "")} / {npc.MoveDebug}");
        }

        /// <summary>시험 끝 diag 한 줄: 까닭 없는 멈춤과 일부러 선 것을 따로.</summary>
        static void LogStalls()
        {
            if (s_stallRun == null) return;
            s_stallRun = null;
            foreach (var kv in s_stall) if (kv.Key != null) CloseStall(kv.Key, kv.Value);
            s_stall.Clear();
            TestRunner.Log($"멈춤 {Sum(s_stalls)} · 일부러 선 것 {Sum(s_holds)} (선 표본 {s_samples}, 가장 오래 {s_maxStill:0.0}초) · 헛휘두름 {GatherJob.MissSwings - s_miss0}번 · 기다림 합 {HoldTimes()}");
        }

        static string HoldTimes()
        {
            if (s_holdTime.Count == 0) return "없음";
            var parts = new List<string>();
            foreach (var kv in s_holdTime) parts.Add($"{kv.Key} {kv.Value:0}초");
            return string.Join(", ", parts.ToArray());
        }

        static string Sum(List<KeyValuePair<string, float>> list)
        {
            if (list.Count == 0) return "0번";
            float total = 0f;
            var longest = list[0];
            var by = new Dictionary<string, float>();
            foreach (var kv in list)
            {
                total += kv.Value;
                if (kv.Value > longest.Value) longest = kv;
                by.TryGetValue(kv.Key, out float s);
                by[kv.Key] = s + kv.Value;
            }
            var top = new List<KeyValuePair<string, float>>(by);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < top.Count && i < 3; i++) parts.Add($"{top[i].Key} {top[i].Value:0}초");
            return $"{list.Count}번, 합 {total:0}초, 가장 긴 {longest.Value:0}초({longest.Key}) [많은 것: {string.Join(", ", parts.ToArray())}]";
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
            if (w.OnBubbled != null) { ChatNet.Bubbled -= w.OnBubbled; w.OnBubbled = null; }
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
            try { LogStalls(); }
            catch (System.Exception e) { TestRunner.Log("멈춤 기록 오류: " + e.Message); }
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
