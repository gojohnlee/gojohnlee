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
        private bool trapsMoved, pixStarted;
        private string pixResult;

        private System.Collections.IEnumerator CountMarkPixels()
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var boxes = RunHud.Drawn.ToArray();
            int red = 0, total = 0;
            foreach (var r in boxes)
                for (int x = (int)r.xMin; x < (int)r.xMax; x++)
                    for (int y = (int)r.yMin; y < (int)r.yMax; y++)
                    {
                        int ty = tex.height - 1 - y;
                        if (x < 0 || ty < 0 || x >= tex.width || ty >= tex.height) continue;
                        total++;
                        var c = tex.GetPixel(x, ty);
                        if (c.r > 0.7f && c.g < 0.35f && c.b < 0.35f) red++;
                    }
            Destroy(tex);
            pixResult = (boxes.Length > 0 && total > 0 && red * 2 >= total ? "ok " : "bad ") + $"{boxes.Length} boxes, {red}/{total} red, screen {Screen.width}x{Screen.height}";
        }

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
                    TestXp(body); TestLockpick(); TestCraft(body); TestTrapsScan(body); TestMining(body); TestImpact(body); TestTemp(body); TestSoundCannon(body); TestSubspace(body);
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
                        for (int i = 0; i < traps.Length; i++)
                        {
                            // 지형 따라 게임이 덫을 지우기도 한다(11-21 시험: 3·8이 gone) → 지워지면 다시 만든다
                            if (traps[i] == null) traps[i] = Utils.Create("beartrap", bp0 + new Vector2(dxs[i], 0.5f), 0f);
                            traps[i].transform.position = bp0 + new Vector2(dxs[i], 0.5f);
                            var rb = traps[i].GetComponent<Rigidbody2D>(); if (rb != null) { rb.velocity = Vector2.zero; rb.simulated = false; }
                        }
                    }
                    if (Since < 1.2f) return;
                    // 표시가 실제 화면 픽셀로 그려졌는지: 프레임 끝에 화면을 찍어 표시 칸 안의 빨간 픽셀을 센다
                    if (!pixStarted) { pixStarted = true; StartCoroutine(CountMarkPixels()); }
                    if (pixResult == null) return;
                    // 덫이 지워졌다 다시 만들어지면 다음 탐색(0.5초)까지 표시가 늦다 → 4초까지 기다린다
                    if ((!pixResult.StartsWith("ok") || !traps.Take(2).All(g => g != null && RunHud.Sensed.Contains(g.transform))) && Since < 4f) { pixStarted = false; pixResult = null; return; }
                    Check("trap mark drawn on screen (red pixels in mark boxes)", pixResult.StartsWith("ok"), pixResult);
                    CheckPanel();
                    Vector2 bp = body.transform.position;
                    Note("traps now at " + string.Join(",", traps.Select(g => g == null ? "gone" : ((Vector2)g.transform.position - bp).ToString("0.0"))) + $", HUD sensed {RunHud.Sensed.Count}, ticks {RunHud.Ticks} scans {RunHud.Scans} r {RunHud.LastRadius} direct {RunHud.Scan(body, Plugin.Perks.TrapRadius).Count} perkR {Plugin.Perks.TrapRadius} t {Time.unscaledTime:0.0}");
                    bool[] seenEach = traps.Select(g => g != null && RunHud.Sensed.Contains(g.transform)).ToArray();
                    Check("trap HUD marks (level 3: 3·8 marked, 30 not)", seenEach[0] && seenEach[1] && !seenEach[2], string.Join(",", seenEach));
                    foreach (var g in traps) if (g != null) Destroy(g);
                    Subspace.Put(body, Utils.Create("bandage", body.transform.position, 0f).GetComponent<Item>());   // 죽을 때 아공간에 붕대 1개
                    Note("kill"); body.brainHealth = 0f; Next(4);
                    break;
                case 4:
                    if (added >= 0 && Since > 2f)
                    {
                        string kept = File.Exists(Subspace.FilePath) ? File.ReadAllText(Subspace.FilePath) : "";
                        Check("subspace kept after death (saved for next run)", Subspace.Bag == null && kept.Contains("bandage"), $"bag {(Subspace.Bag == null ? "gone" : "still")}, file {kept.Length} chars");
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

        private void TestMining(Body body)
        {
            // 몸 아래 단단한 칸을 찾아 아주 작은 피해(0.01)를 준다: 플레이어 공격 중이면 ×1.75, 아니면 그대로
            var w = WorldGeneration.world;
            var bp = w.WorldToBlockPos(body.transform.position);
            Vector2Int pos = bp; bool found = false;
            for (int r = 1; r < 30 && !found; r++)   // 가까운 단단한 칸(아래 먼저, 없으면 둘레) — 세계가 무작위라 바로 아래가 빌 수 있음
                for (int dx = -r; dx <= r && !found; dx++)
                    foreach (var dy in new[] { -r, r }) if (!found) { pos = bp + new Vector2Int(dx, dy); found = w.GetBlock(pos) > 0; }
            if (!found) { Check("mining x1.75 (player attack only)", false, "no block below"); return; }
            float Dmg() { var d = w.GetBlockDamage(pos); return d == null ? 0f : d.damage; }
            float a0 = Dmg();
            PerkPatches.inPlayerAttack = true; w.DamageBlock(pos, 0.01f, false); PerkPatches.inPlayerAttack = false;
            float a1 = Dmg();
            w.DamageBlock(pos, 0.01f, false);
            float a2 = Dmg();
            float on = (a1 - a0) / 0.01f, off = (a2 - a1) / 0.01f;
            Check("mining x1.75 (player attack only)", Mathf.Abs(on - 1.75f) < 0.01f && Mathf.Abs(off - 1f) < 0.01f, $"attack {on:0.00}, other {off:0.00}");
        }

        private void TestImpact(Body body)
        {
            // 근육 피해 = force × 0.33 / 방어(게임 그대로) → 퍽 3단계면 0.4배
            var limb = body.limbs.First(l => !l.isHead && !l.isVital);   // 머리·몸통은 뇌·내출혈 피해가 있어 팔다리로
            float F = body.jumpSpeed * 3f;   // 퍽으로 0.4배여도 문턱(jumpSpeed)을 넘는 크기
            float keepBl = limb.bleedAmount; float keepM = limb.muscleHealth, keepS = limb.skinHealth, keepB = body.brainHealth, keepC = body.consciousness;
            float Drop(PerkState p) { float d = 0; WithPerks(p, () => { limb.muscleHealth = 100f; limb.ImpactDamage(F, Vector2.down); d = 100f - limb.muscleHealth; }); return d; }
            float on = Drop(Plugin.Perks), off = Drop(new PerkState());
            limb.muscleHealth = keepM; limb.skinHealth = keepS; body.brainHealth = keepB; body.consciousness = keepC; limb.pain = 0f; limb.bleedAmount = keepBl;
            Check("impact x0.4 (fall perk 3)", off > 0f && Mathf.Abs(on / off - 0.4f) < 0.01f, $"muscle drop {on:0.00} vs {off:0.00}");
        }

        private void TestTemp(Body body)
        {
            // 37℃에서 멀어지는 쪽(추운 곳에서 식기)만 3단계 단열 2.5배(속도 0.4배), 돌아오는 쪽(더운 몸이 식기)은 그대로
            var w = WorldGeneration.world; float oldT = body.temperature, oldA = w.ambientTemperature;
            float Ratio(float t, float a)
            {
                body.temperature = t; w.ambientTemperature = a;
                float on = body.GetTotalInsulation(), off = 0f;
                WithPerks(new PerkState(), () => off = body.GetTotalInsulation());
                return off / on;
            }
            float away = Ratio(36f, 0f), back = Ratio(39f, 20f);
            body.temperature = oldT; w.ambientTemperature = oldA;
            Check("temperature drift away from 37 x0.4", Mathf.Abs(away - 0.4f) < 0.01f, $"ratio {away:0.00}");
            Check("temperature recovery toward 37 unchanged", Mathf.Abs(back - 1f) < 0.01f, $"ratio {back:0.00}");
        }

        private void TestSoundCannon(Body body)
        {
            // 음파 대포(황무지)·포탑·천장 포탑도 함정 감지에 잡히는지: 몸 옆에 만들어 탐색하고 바로 지운다(쏘기 전에)
            foreach (var id in new[] { "turret", "ceilingturret" })
            {
                var t = (GameObject)Instantiate(Resources.Load(id), body.transform.position + new Vector3(-4f, 1f), Quaternion.identity);
                bool s = RunHud.Scan(body, Plugin.Perks.TrapRadius).Contains(t.transform);
                DestroyImmediate(t);
                Check(id + " sensed by trap perk", s, $"seen {s}");
            }
            var go = (GameObject)Instantiate(Resources.Load("soundcannon"), body.transform.position + new Vector3(4f, 1f), Quaternion.identity);
            bool seen = RunHud.Scan(body, Plugin.Perks.TrapRadius).Contains(go.transform);
            DestroyImmediate(go);
            Check("sound cannon sensed by trap perk", seen, $"seen {seen}");
        }

        /// <summary>물속에서: 아공간 병은 물을 안 받고, 밖에 둔 병은 받는다(게임 WaterContainerItem.Update는 가방 안 병을 건너뜀).</summary>
        private System.Collections.IEnumerator BottleInWater(Body body)
        {
            Note("bottle coroutine running");
            var inBag = Utils.Create("waterbottle", body.transform.position, 0f).GetComponent<WaterContainerItem>();
            var outside = Utils.Create("waterbottle", body.transform.position, 0f).GetComponent<WaterContainerItem>();
            inBag.DrainAll(); outside.DrainAll();
            bool put = Subspace.Put(body, inBag.GetComponent<Item>());
            float s0 = inBag.SpaceLeft, o0 = outside.SpaceLeft;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
            {
                Vector2 p = body.transform.position;
                outside.transform.position = p;
                var orb = outside.GetComponent<Item>().rb; if (orb != null) { orb.simulated = false; orb.velocity = Vector2.zero; }
                // 몸 자리에 물을 두고 바로 게임의 병 Update(자동 채우기)를 부른다 — 물 시뮬이 물을 옮기기 전에
                var upd = HarmonyLib.AccessTools.Method(typeof(WaterContainerItem), "Update");
                FluidManager.main.SetLiquid(p, 1); upd.Invoke(outside, null);
                FluidManager.main.SetLiquid(p, 1); upd.Invoke(inBag, null);
                yield return null;
            }
            FluidManager.main.SetLiquid((Vector2)body.transform.position, 0);
            bool bagDry = Mathf.Approximately(inBag.SpaceLeft, s0), outFilled = outside.SpaceLeft < o0;
            Check("subspace bottle ignores water around (outside bottle fills)", put && bagDry && outFilled,
                $"put {put}, bag space {s0:0}->{inBag.SpaceLeft:0}, outside space {o0:0}->{outside.SpaceLeft:0}");
            Destroy(inBag.gameObject); Destroy(outside.gameObject); Note("bottle test end");
        }

        private void TestSubspace(Body body)
        {
            // 게임 가방 창으로 끌어다 넣는 것과 같은 길(Container.LoadItem → CanHoldItem)로 넣는다
            var results = new List<bool>();
            int cap = Subspace.Capacity;
            float enc0 = body.GetTotalEncumberance();
            for (int n = 0; n <= cap; n++)
            {
                var it = Utils.Create("bandage", body.transform.position, 0f).GetComponent<Item>();
                bool put = Subspace.Put(body, it);
                results.Add(put);
                if (!put) Destroy(it.gameObject);
            }
            bool weightless = Mathf.Abs(body.GetTotalEncumberance() - enc0) < 0.001f;
            bool notWorn = !body.GetAllWearables().Contains(Subspace.Bag.GetComponent<Item>());
            Check($"subspace bag holds {cap}, next refused, not in inventory/wear slots (no weight)",
                cap == 10 && results.Take(cap).All(b => b) && !results[cap] && weightless && notWorn, $"{string.Join(",", results)}, weightless {weightless}, not worn {notWorn}");

            Subspace.SaveToFile();
            foreach (var it in Subspace.Items) DestroyImmediate(it.gameObject);
            int restored = Subspace.RestoreFromFile(body);
            Check("subspace save→restore (save & exit / continue)", restored == Subspace.Capacity && Subspace.Items.All(i => i.id == "bandage"), $"{restored} restored");

            var first = Subspace.Items[0];
            Subspace.Bag.UnloadItem(first, body);   // 가방 창에서 꺼낼 때 게임이 쓰는 함수
            Check("take out (Container.UnloadItem)", Subspace.Items.Count == Subspace.Capacity - 1 && first.transform.parent != Subspace.Bag.transform, $"{Subspace.Items.Count} left");
            Destroy(first.gameObject);

            // 아공간 물건도 가진 물건으로: 자물쇠 키트(FindByIdThorough)·제작 재료와 액체 결과 담기(GetAllItemsThorough)
            foreach (var it in Subspace.Items) DestroyImmediate(it.gameObject);
            var kit = Utils.Create("lockpickingkit", body.transform.position, 0f).GetComponent<Item>();
            bool kitIn = Subspace.Put(body, kit);
            bool kitFound = body.FindByIdThorough("lockpickingkit", out var foundKit) && foundKit == kit;
            var all = body.GetAllItemsThorough();
            bool bagNotListed = !all.Contains(Subspace.Bag.GetComponent<Item>());
            Check("subspace items count as carried (lockpick kit found, crafting list has them, bag itself not)",
                kitIn && kitFound && all.Contains(kit) && bagNotListed && Mathf.Abs(body.GetTotalEncumberance() - enc0) < 0.001f,
                $"put {kitIn}, found {kitFound}, listed {all.Contains(kit)}, bag not listed {bagNotListed}");
            DestroyImmediate(kit.gameObject);

            // 아공간 안은 썩지 않음: 같은 잎을 안·밖에 두고 HandleDecay를 여러 번 부른다
            var leafIn = Utils.Create("foliagemeal", body.transform.position, 0f).GetComponent<Item>();   // 잎·로프는 어디서나 안 썩으니 잎사귀 요리로
            var leafOut = Utils.Create("foliagemeal", body.transform.position + Vector3.up, 0f).GetComponent<Item>();
            bool leafPut = Subspace.Put(body, leafIn);
            float li0 = leafIn.condition, lo0 = leafOut.condition;
            for (int k = 0; k < 3000; k++) { leafIn.HandleDecay(); leafOut.HandleDecay(); }
            Check("no decay inside subspace (outside leaf decays)", leafPut && leafIn.condition == li0 && leafOut.condition < lo0,
                $"put {leafPut}, inside {li0:0.0000}->{leafIn.condition:0.0000}, outside {lo0:0.0000}->{leafOut.condition:0.0000}");
            DestroyImmediate(leafIn.gameObject); DestroyImmediate(leafOut.gameObject);
            foreach (var nid in new[] { "foliage", "rope" })
            {
                var x = Utils.Create(nid, body.transform.position + Vector3.up, 0f).GetComponent<Item>();
                float c0 = x.condition; for (int k = 0; k < 3000; k++) x.HandleDecay();
                Check(nid + " never decays (outside subspace)", x.condition == c0, $"{c0:0.0000}->{x.condition:0.0000}");
                DestroyImmediate(x.gameObject);
            }

            StartCoroutine(BottleInWater(body));
            Subspace.Toggle(body);                 // V: 게임 가방 창 열기 — 다음 단계에서 열려 있는지 본다
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
