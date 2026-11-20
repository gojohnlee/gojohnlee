using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Carryover.Core;
using UnityEngine;

namespace CasualtiesCarryover
{
    /// <summary>
    /// CU_CARRYOVER_TEST=perk일 때만(소리 끔). 시험용 상태 파일만 쓴다.
    /// 메뉴: 퍽 5개를 다 산 상태로 퍽 창을 열어 화면을 찍고(CU_CARRYOVER_SHOT) 회차 시작.
    /// 회차 안: 경험치·자물쇠·제작·함정 감지·아공간(넣기·가득·저장/되살리기·꺼내기)을 실제 게임 코드로 확인하고, 죽여서 아공간이 비는지·포인트를 본다.
    /// </summary>
    internal class PerkTest : MonoBehaviour
    {
        private static PerkTest inst;
        private int phase;   // 0 메뉴, 1 회차 시작 대기, 2 검사, 3 함정 표시 확인, 4 죽음 대기, 5 끝
        private float t0, phaseT;
        private readonly StringBuilder log = new StringBuilder(), checks = new StringBuilder();
        private PerkState maxed;
        private bool shot, started, ok = true;
        private int added = -1;
        private GameObject[] traps;
        private bool trapsMoved;

        private void Awake()
        {
            inst = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            t0 = Time.realtimeSinceStartup;
            Plugin.State = new CarryoverState(); Plugin.SaveState();
            maxed = new PerkState { Points = 999 };
            for (int i = 0; i < PerkState.Perks.Length; i++) while (maxed.Buy(i)) { }
            maxed.Points = 5;
            Plugin.Perks = maxed; Plugin.SavePerks();
            Note("perktest begin: " + maxed.Serialize().Replace('\n', ' '));
        }

        internal static void OnRunStarted() { if (inst != null && inst.phase == 1) inst.started = true; }
        internal static void OnRunEnded(int add) { if (inst != null) { inst.added = add; inst.Note($"run end, perk points +{add}, total {Plugin.Perks.Points}"); } }

        private void Next(int p) { phase = p; phaseT = Time.realtimeSinceStartup; }
        private float Since => Time.realtimeSinceStartup - phaseT;
        private void Note(string s) { log.Append($"[{Time.realtimeSinceStartup - t0:0.0}s] {s}\n"); Plugin.Log.LogInfo("[perktest] " + s); }
        private void Check(string name, bool pass, string val) { checks.Append($"{(pass ? "ok  " : "BAD ")} {name}: {val}\n"); ok &= pass; }

        private void Update()
        {
            AudioListener.volume = 0f;
            if (phase < 5 && Time.realtimeSinceStartup - t0 > 300f) { Finish("TIMEOUT phase " + phase); return; }
            try { Step(); } catch (Exception e) { Finish("EXCEPTION " + e); }
        }

        private void Step()
        {
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            switch (phase)
            {
                case 0:
                    var pre = FindObjectOfType<PreRunScript>();
                    if (pre == null) { phaseT = Time.realtimeSinceStartup; return; }
                    if (Since > 3f && PerkMenu.Inst != null) PerkMenu.Inst.Open = true;
                    if (Since > 5f && !shot)
                    {
                        shot = true;
                        var path = Environment.GetEnvironmentVariable("CU_CARRYOVER_SHOT");
                        if (!string.IsNullOrEmpty(path)) { ScreenCapture.CaptureScreenshot(path); Note("screenshot " + path); }
                    }
                    if (Since > 7f) { PerkMenu.Inst.Open = false; Note("StartRun"); pre.StartRun(); Next(1); }
                    break;
                case 1:
                    if (started && body != null) Next(2);
                    break;
                case 2:
                    if (Since < 4f) return;
                    TestXp(body); TestLockpick(); TestCraft(body); TestTrapsScan(body); TestSubspace(body);
                    Next(3);
                    break;
                case 3:
                    // 시험용 덫은 스스로 아래(땅)로 옮겨 가므로, 확인 전까지 매 프레임 몸 옆에 붙잡아 둔다: 3·8은 표시, 30은 반경(15) 밖
                    if (!trapsMoved)
                    {
                        // 앞에서 만든 덫은 회차 시작 직후 월드 정리로 지워질 수 있어 여기서 새로 만든다
                        trapsMoved = true; phaseT = Time.realtimeSinceStartup;
                        foreach (var g in traps) if (g != null) Destroy(g);
                        traps = new GameObject[3];
                        for (int i = 0; i < 3; i++) traps[i] = Utils.Create("beartrap", body.transform.position, 0f);
                    }
                    {
                        Vector2 bp0 = body.transform.position;
                        var dxs = new[] { 3f, 8f, 30f };
                        for (int i = 0; i < traps.Length; i++) if (traps[i] != null)
                        {
                            traps[i].transform.position = bp0 + new Vector2(dxs[i], 0.5f);
                            var rb = traps[i].GetComponent<Rigidbody2D>(); if (rb != null) { rb.velocity = Vector2.zero; rb.simulated = false; }
                        }
                    }
                    if (Since < 1.2f) return;
                    CheckPanel();
                    Vector2 bp = body.transform.position;
                    Note("traps now at " + string.Join(",", traps.Select(g => g == null ? "gone" : ((Vector2)g.transform.position - bp).ToString("0.0"))) + $", HUD sensed {RunHud.Sensed.Count}");
                    bool[] seenEach = traps.Select(g => g != null && RunHud.Sensed.Contains(g.transform)).ToArray();
                    Check("trap HUD marks (level 3: 3·8 marked, 30 not)", seenEach[0] && seenEach[1] && !seenEach[2], string.Join(",", seenEach));
                    foreach (var g in traps) if (g != null) Destroy(g);
                    Note("kill"); body.brainHealth = 0f; Next(4);
                    break;
                case 4:
                    if (added >= 0 && Since > 2f)
                    {
                        Check("subspace emptied on death", Subspace.Items.Count == 0 && !File.Exists(Subspace.FilePath), $"{Subspace.Items.Count} items, file {File.Exists(Subspace.FilePath)}");
                        Check("short run +0 points", added == 0 && Plugin.Perks.Points == 5, $"+{added}, total {Plugin.Perks.Points}");
                        Finish(null);
                    }
                    break;
            }
        }

        private void WithPerks(PerkState p, Action a) { var keep = Plugin.Perks; Plugin.Perks = p; try { a(); } finally { Plugin.Perks = keep; } }

        private void TestXp(Body body)
        {
            var sk = body.skills; float before = sk.expRES;
            sk.AddExp(1, 10f);
            float r = (sk.expRES - before) / (10f * Skills.xpGainMult * sk.RESGainMult);
            Check("xp x1.3", Math.Abs(r - 1.3f) < 0.01f, r.ToString("0.000"));
        }

        private void TestLockpick()
        {
            float on = new LockpingMinigame(null, 1f).anglePrecision, off = 0f;
            WithPerks(new PerkState(), () => off = new LockpingMinigame(null, 1f).anglePrecision);
            Check("lockpick precision x1.75", Math.Abs(on - 1.75f) < 0.001f && Math.Abs(off - 1f) < 0.001f, $"on {on:0.000} off {off:0.000}");
        }

        private void TestCraft(Body body)
        {
            var r = Recipes.recipes.FirstOrDefault(x => x.result != null && !x.result.isLiquid && x.INT >= 3 && Resources.Load(x.result.id) != null);
            if (r == null) { Check("craft recipe found", false, "none"); return; }
            var sk = body.skills; int keepInt = sk.INT;
            sk.INT = r.INT - 2;   // 요구 지능보다 2 낮음: 원래는 50%로 실패
            // Item.allItems는 Start에서 채워지므로 같은 프레임엔 FindObjectsOfType로 센다
            Item[] Now() => FindObjectsOfType<Item>().Where(i => i.id == r.result.id).ToArray();
            int Count() => Now().Length;
            var existing = Now();
            int c0 = Count();
            for (int i = 0; i < 10; i++) r.result.SpawnResult(r.INT);
            int withPerk = Count() - c0;
            var madeOn = Now().Except(existing).ToArray();
            bool fullQuality = madeOn.All(i => Math.Abs(i.condition - r.result.resultCondition) < 0.001f);
            int c1 = Count();
            WithPerks(new PerkState(), () => { for (int i = 0; i < 10; i++) r.result.SpawnResult(r.INT); });
            int without = Count() - c1;
            sk.INT = keepInt;
            var made = Now().Except(existing).ToArray();
            foreach (var it in made) Destroy(it.gameObject);
            Check($"craft '{r.result.id}' (INT {r.INT}, mine {r.INT - 2})", withPerk == 10 * r.result.amount && without < withPerk && fullQuality,
                $"with perk {withPerk}/{10 * r.result.amount} (full quality {fullQuality}), without {without}");
        }

        private void TestTrapsScan(Body body)
        {
            Vector2 p = body.transform.position;
            traps = new[] { 3f, 8f, 13f }.Select(dx => Utils.Create("beartrap", p + new Vector2(dx, 0.5f), 0f)).ToArray();
            int t = PerkState.IndexOf("traps");
            var counts = new int[4];
            var probe = new PerkState();
            for (int lv = 0; lv <= 3; lv++)
            {
                probe.Levels[t] = lv;
                var found = RunHud.Scan(body, probe.TrapRadius);
                counts[lv] = traps.Count(g => g != null && found.Contains(g.transform));
            }
            Check("trap scan by level (0,1,2,3 → 0,1,2,3 of ours at 3·8·13)", counts.SequenceEqual(new[] { 0, 1, 2, 3 }), string.Join(",", counts));
        }

        private void TestSubspace(Body body)
        {
            // 게임 가방 창으로 끌어다 넣는 것과 같은 길(Container.LoadItem → CanHoldItem)로 넣는다
            var results = new List<bool>();
            for (int n = 0; n < 4; n++)
            {
                var it = Utils.Create("bandage", body.transform.position, 0f).GetComponent<Item>();
                bool put = Subspace.Put(body, it);
                results.Add(put);
                if (!put) Destroy(it.gameObject);
            }
            var inv = body.GetAllItemsThorough();
            bool weightless = !inv.Any(i => i == Subspace.Bag.GetComponent<Item>() || Subspace.Items.Contains(i));
            bool notWorn = !body.GetAllWearables().Contains(Subspace.Bag.GetComponent<Item>());
            Check("subspace bag holds 3, 4th refused, not in inventory/wear slots (no weight)",
                string.Join(",", results) == "True,True,True,False" && weightless && notWorn, $"{string.Join(",", results)}, weightless {weightless}, not worn {notWorn}");

            Subspace.SaveToFile();
            foreach (var it in Subspace.Items) DestroyImmediate(it.gameObject);
            int restored = Subspace.RestoreFromFile(body);
            Check("subspace save→restore (save & exit / continue)", restored == 3 && Subspace.Items.All(i => i.id == "bandage"), $"{restored} restored");

            var first = Subspace.Items[0];
            Subspace.Bag.UnloadItem(first, body);   // 가방 창에서 꺼낼 때 게임이 쓰는 함수
            Check("take out (Container.UnloadItem)", Subspace.Items.Count == 2 && first.transform.parent != Subspace.Bag.transform, $"{Subspace.Items.Count} left");
            Destroy(first.gameObject);

            Subspace.Toggle(body);                  // V: 게임 가방 창 열기 — 다음 단계에서 열려 있는지 본다
        }

        private void CheckPanel()
        {
            var pc = PlayerCamera.main;
            bool open = pc.currentContainer == Subspace.Bag && pc.containerMenu.activeSelf;
            Check("V opens game bag panel with subspace bag", open, $"current {(pc.currentContainer == null ? "none" : pc.currentContainer.name)}, menu {pc.containerMenu.activeSelf}");
            var path = Environment.GetEnvironmentVariable("CU_CARRYOVER_SHOT");
            if (!string.IsNullOrEmpty(path)) ScreenCapture.CaptureScreenshot(path.Replace(".png", "-bag.png"));
        }

        private void Finish(string error)
        {
            phase = 5;
            if (error != null) ok = false;
            var r = (ok ? "PASS\n" : "FAIL " + (error ?? "") + "\n") + checks + log;
            var path = Environment.GetEnvironmentVariable("CU_CARRYOVER_RESULT");
            if (!string.IsNullOrEmpty(path)) { try { File.WriteAllText(path, r); } catch (Exception e) { Plugin.Log.LogError(e.Message); } }
            Plugin.Log.LogInfo("[perktest] " + (ok ? "PASS" : "FAIL"));
            Application.Quit();
        }
    }
}
