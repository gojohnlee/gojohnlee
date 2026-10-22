using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Chat;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// vh_farmtest (⑨ 심기, FarmJob): 경작기 없이 시키면 거절 → 상자에 경작기 1·당근 씨앗 6을 넣고 거점 옆 반경 3m 밭에
    /// 당근 4(Farm.Plan으로 계산)를 시키면 갈고 새싹 4개를 심고, 씨앗 2·경작기를 상자에 도로 넣는지(씨앗 수 보존).
    /// 끝에 그 새싹들의 자라는 시간을 1초로 줄여 4개가 다 자라는지(= 갈기·자리·바이옴이 바닐라 판정을 통과) 본다.
    /// 이어서 거두기만 시켜(FarmJob harvest) 다 자란 당근을 다 거둬 떨굴 양만큼 상자에 넣는지 본다.
    /// </summary>
    static class FarmTest
    {
        public static void Register() => TestRunner.Register("vh_farmtest", "밭 심기 시험", () => TestKit.Flat(Run()));

        const string Seed = "CarrotSeeds", Produce = "Carrot";

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
            FarmField field = null;
            try { yield return Body(w, checks, f => field = f); }
            finally
            {
                if (field != null) { Clean(field); Brain.Farms.Remove(field.X, field.Z); }
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, System.Action<FarmField> made)
        {
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC·상자", w.Claude != null && w.Chest != null);
            if (w.Claude == null || w.Chest == null) yield break;
            TestRunner.Log("DIAG " + Crops.Describe() + $" / ZNetScene에 갈기 조각 {Crops.CultivatePiece != null && ZNetScene.instance.GetPrefab(Crops.CultivatePiece) != null}");
            checks.Add($"작물 목록·갈기 조각 ({Crops.All.Count}, {Crops.CultivatePiece})", Crops.All.Count > 0 && Crops.CultivatePiece != null);
            if (Crops.CultivatePiece == null) yield break;
            TestKit.ClearBaseStock(w, Crops.Cultivator, Seed, Produce);
            int bag0 = Bag(w, Seed);

            Vector3 right = Vector3.Cross(Vector3.up, w.Player.transform.forward).normalized;
            Vector3 at = GatherTest.Ground(w.Home - right * 12f);
            var field = new FarmField(at.x, at.z, 3);
            made(field);
            Clean(field);
            int biome = Scout.BiomeAt(at);
            var want = new List<Ingredient> { new Ingredient(Produce, 4) };
            FarmPlan noStock = Farm.Plan(Crops.All, biome, 100, item => 0, want);
            FarmPlan plan = Farm.Plan(Crops.All, biome, 100, item => item == Seed ? 6 : 0, want);
            checks.Add($"계획: 씨앗 6 → 당근 새싹 4 (씨앗 없으면 0, 바이옴 {biome})", plan.Count("sapling_carrot") == 4 && noStock.Plant.Count == 0);

            // 경작기 없으면 거절
            TestKit.PutInChest(w.Chest, Seed, 6);
            w.Claude.Ai.StartJob(new FarmJob(w.Claude, field, plan));
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 10f);
            checks.Add($"경작기 없으면 거절 ({TestKit.Waited:0}초, 새싹 {Plants(field).Count}, 씨앗 상자 {Stock(w, Seed)})", TestKit.Ok && Plants(field).Count == 0 && Stock(w, Seed) == 6);

            TestKit.PutInChest(w.Chest, Crops.Cultivator, 1);
            var job = new FarmJob(w.Claude, field, plan);
            w.Claude.Ai.StartJob(job);
            yield return TestKit.WaitFor(() => job.Planted >= 4, 120f);
            checks.Add($"새싹 4개 심음 ({TestKit.Waited:0}초, {job.Planted}, {job.StepName})", TestKit.Ok);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 120f);
            List<Plant> plants = Plants(field);
            int seeds = Stock(w, Seed) + Bag(w, Seed) - bag0;
            checks.Add($"일 끝남·돌아옴 ({TestKit.Waited:0}초, 거점까지 {Utils.DistanceXZ(w.Home, w.Claude.transform.position):0}m)", TestKit.Ok);
            checks.Add($"밭에 새싹 4개 ({plants.Count})", plants.Count == 4);
            checks.Add($"씨앗 보존 (남은 {seeds} + 심은 {plants.Count} = 6)", seeds + plants.Count == 6);
            checks.Add($"씨앗·경작기 상자에 (씨앗 상자 {Stock(w, Seed)}, 경작기 상자 {Stock(w, Crops.Cultivator)}·짐 {Bag(w, Crops.Cultivator)})",
                Stock(w, Seed) == 2 && Stock(w, Crops.Cultivator) == 1 && Bag(w, Crops.Cultivator) == 0);

            yield return new WaitForSeconds(12f); // 심은 뒤 10초가 지나야 바닐라가 상태를 판정한다
            foreach (var p in plants)
            {
                if (p == null) { TestRunner.Log("DIAG 새싹 사라짐"); continue; }
                Vector3 pp = p.transform.position;
                Heightmap hm = Heightmap.FindHeightmap(pp);
                TestRunner.Log($"DIAG 새싹 {pp} 상태 {p.GetStatus()}, 갈림 {(hm != null ? hm.IsCultivated(pp).ToString() : "땅 없음")}, 바이옴 {(hm != null ? hm.GetBiome(pp).ToString() : "?")}, 못 자라면 지움 {p.m_destroyIfCantGrow}");
            }
            plants.RemoveAll(p => p == null);
            // 자라기: 이 새싹들만 자라는 시간을 1초로 줄인다(새 월드는 시각이 짧아 심은 시각을 앞당길 수 없음)
            // → 바닐라 Plant.SUpdate(10초마다, 심은 뒤 10초부터)가 건강하면 자란 Pickable로 바꾼다
            foreach (var p in plants) { p.m_growTime = 1f; p.m_growTimeMax = 1f; }
            yield return TestKit.WaitFor(() => Grown(field) >= plants.Count && plants.Count > 0, 40f);
            var status = new List<string>();
            foreach (var p in Plants(field)) status.Add(p.GetStatus().ToString());
            checks.Add($"다 자람 ({TestKit.Waited:0}초, 당근 {Grown(field)}/{plants.Count}, 남은 새싹 상태 {string.Join(",", status.ToArray())})", TestKit.Ok);

            // 거두기: 심을 것 없이 거두기만 → 다 자란 것마다 Interact·떨군 것 줍기 → 돌아와 상자에(수량 = 떨굴 양 합)
            List<Pickable> ripe = FarmJob.Ripe(field);
            int expect = 0;
            foreach (var p in ripe) expect += p.m_dontScale ? p.m_amount : Mathf.Max(p.m_minAmountScaled, Game.instance.ScaleDrops(p.m_itemPrefab, p.m_amount));
            int produce0 = Stock(w, Produce) + Bag(w, Produce);
            var harvest = new FarmJob(w.Claude, field, null, true);
            w.Claude.Ai.StartJob(harvest);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 150f);
            checks.Add($"거두기 끝남·돌아옴 ({TestKit.Waited:0}초, 거둠 {harvest.Harvested}/{ripe.Count}, {harvest.StepName}, 거점까지 {Utils.DistanceXZ(w.Home, w.Claude.transform.position):0}m)",
                TestKit.Ok && harvest.Harvested == ripe.Count && ripe.Count > 0);
            int got = Stock(w, Produce) - produce0;
            checks.Add($"거둔 당근 상자에 ({got}/{expect}, 짐 {Bag(w, Produce)}, 땅 {OnGround(field, Produce)}, 남은 것 {FarmJob.Ripe(field).Count})",
                got == expect && Bag(w, Produce) == 0 && FarmJob.Ripe(field).Count == 0);

            // 명령 연결(4b): 밭 장부에 적고 StartFarm(!밭이 부르는 것) → 상자 씨앗으로 Farm.Fill(당근 씨앗 6 → 당근 6, 씨앗이 다 떨어지면 당근으로 당근 씨앗 심기)
            Clean(field);
            TestKit.ClearBaseStock(w, Seed, Produce);
            TestKit.PutInChest(w.Chest, Seed, 6);
            TestKit.PutInChest(w.Chest, Produce, 4);
            Brain.Farms.Set(field.X, field.Z, field.Radius);
            FarmField booked = Brain.Farms.At(field.X, field.Z);
            Commands.StartFarm(w.Claude, booked, false);
            var cmdJob = w.Claude.Ai.Job as FarmJob;
            Commands.StartFarm(w.Alarak, booked, false); // 같은 밭은 다른 일꾼이 안 맡음
            checks.Add($"!밭 → 밭일 시작, 같은 밭은 한 명만 ({(cmdJob != null ? cmdJob.StepName : "없음")}, 알라라크 {(w.Alarak.Ai.Job != null ? w.Alarak.Ai.Job.GetType().Name : "없음")})",
                cmdJob != null && !(w.Alarak.Ai.Job is FarmJob));
            // 저장 칸: 하던 밭일 → WorkItem(Farm) → 글 → 다시 읽기
            WorkItem saved = WorkSaves.FromJob(cmdJob);
            var back = WorkState.LoadQueue(WorkState.SaveQueue(new List<WorkItem> { saved }));
            checks.Add($"밭일 저장 칸 ({(back.Count > 0 ? back[0].Kind + " r" + back[0].Amount + " (" + back[0].FarmX.ToString("0.0") + "," + back[0].FarmZ.ToString("0.0") + ")" : "없음")})",
                back.Count == 1 && back[0].Kind == WorkKind.Farm && back[0].Amount == field.Radius && Mathf.Abs(back[0].FarmX - field.X) < 0.1f && Mathf.Abs(back[0].FarmZ - field.Z) < 0.1f);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 240f);
            int carrots = PlantsOf(field, "sapling_carrot"), seedCarrots = PlantsOf(field, "sapling_seedcarrot");
            int seedLeft = Stock(w, Seed) + Bag(w, Seed), produceLeft = Stock(w, Produce) + Bag(w, Produce);
            checks.Add($"!밭 심기 ({TestKit.Waited:0}초, 당근 {carrots}·당근 씨앗 {seedCarrots}, 남은 씨앗 {seedLeft}·당근 {produceLeft}, 경작기 상자 {Stock(w, Crops.Cultivator)})",
                TestKit.Ok && carrots == 6 && seedCarrots > 0 && seedLeft + carrots == 6 && produceLeft + seedCarrots == 4 && Stock(w, Crops.Cultivator) == 1);
            // 대기열·재시작 칸에서 다시 시작(거둘 것·씨앗 없음 → 갔다 와서 끝)
            QueuedWork q = WorkSaves.ToQueued(w.Claude, back[0]);
            q.Start();
            bool restarted = w.Claude.Ai.Job is FarmJob;
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 120f);
            checks.Add($"저장 칸에서 밭일 다시 시작·끝 ({restarted}, {TestKit.Waited:0}초, 짐 경작기 {Bag(w, Crops.Cultivator)})", restarted && TestKit.Ok && Bag(w, Crops.Cultivator) == 0);

            // 출발 판단 농사 연결(4c): "!당근 3" → 들에는 없는 것이라 밭에 당근 3 심기(씨앗 3 → 0). 밭이 없으면 밭일 안 함.
            Clean(field);
            TestKit.ClearBaseStock(w, Seed, Produce);
            TestKit.PutInChest(w.Chest, Seed, 3);
            var who = new Speaker("시험", ZDOID.None, ZDOID.None);
            var carrotCmd = new ChatCommand { Kind = CommandKind.Item, Item = Produce, Amount = 3 };
            Departures.Gather(who, carrotCmd, w.Claude);
            var wantJob = w.Claude.Ai.Job as FarmJob;
            checks.Add($"!당근 3 → 밭일 ({(w.Claude.Ai.Job != null ? w.Claude.Ai.Job.GetType().Name : "없음")}, 들에서 나는 곳 [{string.Join(",", ResourceCatalog.SourcesOf(Produce).ToArray())}])",
                wantJob != null);
            yield return TestKit.WaitFor(() => w.Claude.Ai.Job == null, 240f);
            carrots = PlantsOf(field, "sapling_carrot");
            seedLeft = Stock(w, Seed) + Bag(w, Seed);
            checks.Add($"!당근 3 심기 ({TestKit.Waited:0}초, 당근 {carrots}, 남은 씨앗 {seedLeft}, 경작기 상자 {Stock(w, Crops.Cultivator)})",
                TestKit.Ok && carrots == 3 && seedLeft == 0 && Stock(w, Crops.Cultivator) == 1);
            Brain.Farms.Remove(field.X, field.Z);
            Departures.Gather(who, carrotCmd, w.Claude);
            checks.Add($"밭 없으면 !당근은 밭일·채집 안 함 ({(w.Claude.Ai.Job != null ? w.Claude.Ai.Job.GetType().Name : "없음")})", w.Claude.Ai.Job == null);

            TestKit.ClearBaseStock(w, Crops.Cultivator, Seed, Produce);
        }

        static List<Plant> Plants(FarmField f)
        {
            var list = new List<Plant>();
            foreach (var p in Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
                if (p != null && f.Contains(p.transform.position.x, p.transform.position.z)) list.Add(p);
            return list;
        }

        static int PlantsOf(FarmField f, string sapling)
        {
            int n = 0;
            foreach (var p in Plants(f)) if (Utils.GetPrefabName(p.gameObject) == sapling) n++;
            return n;
        }

        static int Grown(FarmField f)
        {
            int n = 0;
            foreach (var p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
                if (p != null && p.m_itemPrefab != null && p.m_itemPrefab.name == Produce && f.Contains(p.transform.position.x, p.transform.position.z)) n++;
            return n;
        }

        /// <summary>밭(반경 +1m)의 새싹·당근을 지운다(시험 전·후).</summary>
        static void Clean(FarmField f)
        {
            var big = new FarmField(f.X, f.Z, f.Radius + 1);
            foreach (var p in Plants(big)) ZNetScene.instance.Destroy(p.gameObject);
            foreach (var p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
                if (p != null && p.m_itemPrefab != null && p.m_itemPrefab.name == Produce && big.Contains(p.transform.position.x, p.transform.position.z))
                    ZNetScene.instance.Destroy(p.gameObject);
        }

        static int OnGround(FarmField f, string prefab)
        {
            var big = new FarmField(f.X, f.Z, f.Radius + 3);
            int n = 0;
            foreach (var d in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
                if (d != null && Utils.GetPrefabName(d.gameObject) == prefab && big.Contains(d.transform.position.x, d.transform.position.z)) n += d.m_itemData.m_stack;
            return n;
        }

        static int Stock(TestKit.World w, string prefab)
        {
            int n = 0;
            foreach (var c in Chests.NearBase(w.Home)) n += GatherTest.Count(c, prefab);
            return n;
        }

        static int Bag(TestKit.World w, string prefab) =>
            w.Claude.GetInventory().CountItems(ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
    }
}
