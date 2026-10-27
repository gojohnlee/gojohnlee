using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 밤사이 정산 시험(요청 R3 가): 1호 짐에 나무·돌, 2호는 160m 밖(거점 125m 밖)으로 보내고 실제 잠자기 넘김(EnvMan.SkipToMorning, 12초).
    /// 넘김이 끝나면 1호 짐이 걷지 않고 상자로(개수 보존, 대사), 2호(밖)는 그대로, 같은 아침 다시 불러도 안 함, 다음 아침이면 함.
    /// 제련소 결과물·다시 채우기(가2), 발효기 따르기·다시 채우기·익는 중은 그대로(가3).
    /// 거점 밭의 다 자란 당근 → 상자·씨앗만큼 같은 자리 다시 심기(가4).
    /// </summary>
    static class SettleTest
    {
        static GameObject s_smelter, s_fermenter, s_sap, s_lure;
        static readonly List<GameObject> s_wilds = new List<GameObject>();
        static Core.Work.FarmField s_field;
        const string MeadBase = "MeadBasePoisonResist", Mead = "MeadPoisonResist";

        public static void Register() => TestRunner.Register("vh_settletest", "밤사이 정산(아침 정리) 시험", Run);

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            Player player = Player.m_localPlayer;
            if (player == null || !ZNet.instance.IsServer() || EnvMan.instance == null)
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            bool god = player.InGodMode();
            player.SetGodMode(true);
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;

            GameObject wood = ObjectDB.instance.GetItemPrefab("Wood"), stone = ObjectDB.instance.GetItemPrefab("Stone");
            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            checks.Add("프리팹 있음", wood != null && stone != null && chestPrefab != null && BaseFlag.Prefab != null);
            if (wood == null || stone == null || chestPrefab == null || BaseFlag.Prefab == null) { End(player, god, checks, onHeard, heard); yield break; }

            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
            Quaternion rot = player.transform.rotation;
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f);
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Ragnar);
            WorkerNpc alarak = WorkerNpc.All.Find(n => n.Personality != Core.Npc.Personality.Ragnar);
            checks.Add("깃발에 NPC 둘 나타남", claude != null && alarak != null);
            if (claude == null || alarak == null) { End(player, god, checks, onHeard, heard); yield break; }
            if (GatherTest.ClearOldChests(home) > 0) yield return new WaitForSeconds(0.5f);
            Vector3 chestPos = GatherTest.Ground(home + rot * new Vector3(-4f, 0f, -1f));
            player.PlacePiece(chestPrefab.GetComponent<Piece>(), chestPos, rot, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container chest = GatherTest.ChestAt(chestPos);
            checks.Add("상자 놓임", chest != null);
            if (chest == null) { End(player, god, checks, onHeard, heard); yield break; }

            // 1호: 짐에 나무 20·돌 10, 2호: 나무 10 들고 160m 밖
            claude.GetInventory().AddItem(wood, 20);
            claude.GetInventory().AddItem(stone, 10);
            alarak.GetInventory().AddItem(wood, 10);
            // 설비(가2): 제련소에 구리 광석 2·숯 4를 넣고 상자에 광석 3·숯 20 → 아침에 결과물 상자로, 광석 3 채움, 연료는 녹일 만큼
            GameObject smelterPrefab = ZNetScene.instance.GetPrefab("smelter");
            checks.Add("제련소 프리팹 있음", smelterPrefab != null);
            if (smelterPrefab == null) { End(player, god, checks, onHeard, heard); yield break; }
            s_smelter = Object.Instantiate(smelterPrefab, GatherTest.Ground(home + rot * new Vector3(4f, 0f, -1f)), rot);
            Smelter smelter = s_smelter.GetComponent<Smelter>();
            ZNetView snv = s_smelter.GetComponent<ZNetView>();
            TestKit.PutInChest(chest, "CopperOre", 3);
            TestKit.PutInChest(chest, "Coal", 20);
            yield return new WaitForSeconds(1f);
            for (int i = 0; i < 2; i++) snv.InvokeRPC("RPC_AddOre", "CopperOre", false);
            for (int i = 0; i < 4; i++) snv.InvokeRPC("RPC_AddFuel");
            yield return new WaitForSeconds(0.5f);
            // 발효기(가3): 다 익은 독 저항 미드 바탕 + 상자에 바탕 1 → 아침에 미드가 상자로, 상자 바탕으로 다시 채움(지붕은 시험에서 안 봄)
            GameObject fermPrefab = ZNetScene.instance.GetPrefab("fermenter");
            checks.Add("발효기 프리팹 있음", fermPrefab != null);
            if (fermPrefab == null) { End(player, god, checks, onHeard, heard); yield break; }
            MachineStep.TestIgnoreRoof = true;
            s_fermenter = Object.Instantiate(fermPrefab, GatherTest.Ground(home + rot * new Vector3(0f, 0f, -6f)), rot);
            Fermenter fermenter = s_fermenter.GetComponent<Fermenter>();
            ZNetView fnv = s_fermenter.GetComponent<ZNetView>();
            TestKit.PutInChest(chest, MeadBase, 1);
            yield return new WaitForSeconds(0.5f);
            fnv.InvokeRPC("RPC_AddItem", MeadBase.GetStableHashCode(), false);
            yield return new WaitForSeconds(0.5f);
            ZDO fz = fnv.GetZDO();
            fz.Set(ZDOVars.s_startTime, ZNet.instance.GetTime().AddSeconds(-fermenter.m_fermentationDuration - 10).Ticks);
            int meadMade = fermenter.m_conversion.Find(c => c.m_from != null && c.m_from.gameObject.name == MeadBase)?.m_producedItems ?? -1;
            int meadBefore = GatherTest.CountBase(home, Mead);
            checks.Add($"발효기에 바탕 들어감 (내용 {fz.GetInt(ZDOVars.s_content) == MeadBase.GetStableHashCode()}, 나오는 수 {meadMade})", fz.GetInt(ZDOVars.s_content) == MeadBase.GetStableHashCode() && meadMade > 0);
            // 수액 추출기(⑬ 마): 뿌리 없이 놓고 고인 수액 3칸 → 아침에 수액이 상자로, 칸 0
            GameObject sapPrefab = ZNetScene.instance.m_prefabs.Find(g => g != null && g.GetComponent<SapCollector>() != null);
            checks.Add($"수액 추출기 프리팹 있음 ({sapPrefab?.name})", sapPrefab != null && sapPrefab.GetComponent<SapCollector>().m_spawnItem != null);
            if (sapPrefab == null || sapPrefab.GetComponent<SapCollector>().m_spawnItem == null) { End(player, god, checks, onHeard, heard); yield break; }
            s_sap = Object.Instantiate(sapPrefab, GatherTest.Ground(home + rot * new Vector3(-4f, 0f, -6f)), rot);
            SapCollector sapc = s_sap.GetComponent<SapCollector>();
            string sapItem = sapc.m_spawnItem.gameObject.name;
            yield return new WaitForSeconds(0.5f);
            ZDO sapz = s_sap.GetComponent<ZNetView>().GetZDO();
            sapz.Set(ZDOVars.s_level, 3);
            int sapMade = 0;
            for (int i = 0; i < 3; i++) sapMade += Game.instance.ScaleDrops(sapc.m_spawnItem.m_itemData, 1);
            int sapBefore = GatherTest.CountBase(home, sapItem);
            // 밭(가4): 거점 밭에 다 자란 당근 2 + 상자에 당근 씨앗 1 → 아침에 당근이 상자로, 한 자리만 다시 심음(씨앗 1)
            Core.Work.Crop carrot = Crops.ForProduce("Carrot");
            GameObject grownPrefab = carrot != null ? ZNetScene.instance.GetPrefab(carrot.Grown) : null;
            checks.Add($"당근 작물 있음 ({carrot?.Grown})", grownPrefab != null && Crops.CultivatePrefab != null);
            if (grownPrefab == null || Crops.CultivatePrefab == null) { End(player, god, checks, onHeard, heard); yield break; }
            // 깃발 45m: 예전 30m 범위 밖, 거점 범위(125m) 안(사용자 결정 2026-10-23). 2호를 보내는 쪽(예전 -80m에 땅 있음)
            Vector3 fc = GatherTest.Ground(home + rot * new Vector3(6f, 0f, -45f));
            Brain.Farms.Set(fc.x, fc.z, 3);
            s_field = Brain.Farms.At(fc.x, fc.z);
            var ripe = new List<Pickable>();
            foreach (float dx in new[] { -1f, 1f })
            {
                Vector3 sp = GatherTest.Ground(fc + rot * new Vector3(dx, 0f, 0f));
                Object.Instantiate(Crops.CultivatePrefab, sp, Quaternion.identity);
                ripe.Add(Object.Instantiate(grownPrefab, sp, Quaternion.identity).GetComponent<Pickable>());
            }
            TestKit.PutInChest(chest, carrot.Seed, 1);
            yield return new WaitForSeconds(0.5f);
            int carrotBefore = GatherTest.CountBase(home, "Carrot");
            int carrotMade = 0;
            foreach (var rp in ripe) carrotMade += rp.m_dontScale ? rp.m_amount : Mathf.Max(rp.m_minAmountScaled, Game.instance.ScaleDrops(rp.m_itemPrefab, rp.m_amount));
            checks.Add($"밭 놓임 (다 자란 것 {(s_field != null ? FarmJob.Ripe(s_field).Count : -1)}/2)", s_field != null && FarmJob.Ripe(s_field).Count == 2);
            int copperAll = 5 + GatherTest.CountBase(home, "Copper");
            Vector3 far = GatherTest.Ground(home + rot * new Vector3(0f, 0f, -160f));
            alarak.TeleportTo(far + Vector3.up * 0.5f, rot, false);
            yield return new WaitForSeconds(1f);
            int alarakDay = alarak.Zdo.GetInt(MorningSettle.KeyDay, -1);
            int woodAll = GatherTest.CountBase(home, "Wood") + claude.CountItem("Wood");
            int stoneAll = GatherTest.CountBase(home, "Stone") + claude.CountItem("Stone");
            Vector3 claudeAt = claude.transform.position;
            TestRunner.Log($"시작: 1호 나무 {claude.CountItem("Wood")}·돌 {claude.CountItem("Stone")}, 2호 거리 {Utils.DistanceXZ(alarak.transform.position, home):0}m, 하루 {EnvMan.instance.GetDay()}");

            // 위습(⑬ 마-2): 위습 분수 + 끌려온 위습 2(분수 옆) + 떠도는 위습 1(분수 40m) → 넘김 시작(밤)에 끌려온 2만 상자로.
            // 낮이면 위습이 날아가 사라지니 넘김 직전까지 밤으로 고정(DeathTest 꼴)
            // 같은 WispSpawner를 쓰는 애쉬랜드 piece_EternalPyre(FaderEmber)가 먼저 잡혀서(회차 201) 줍는 것이 Wisp인 분수만
            GameObject lurePrefab = ZNetScene.instance.m_prefabs.Find(g => g != null && g.GetComponentInChildren<WispSpawner>()?.m_wispPrefab?.GetComponent<Pickable>()?.m_itemPrefab?.name == "Wisp");
            GameObject wispPrefab = lurePrefab?.GetComponentInChildren<WispSpawner>().m_wispPrefab;
            checks.Add($"위습 분수·위습 프리팹 있음 ({lurePrefab?.name}, {wispPrefab?.name}, 줍기 {wispPrefab?.GetComponent<Pickable>()?.m_itemPrefab?.name})",
                wispPrefab != null && wispPrefab.GetComponent<Pickable>()?.m_itemPrefab != null);
            if (wispPrefab == null || wispPrefab.GetComponent<Pickable>()?.m_itemPrefab == null) { End(player, god, checks, onHeard, heard); yield break; }
            EnvMan env = EnvMan.instance;
            bool debugTime = env.m_debugTimeOfDay;
            float debugValue = env.m_debugTime;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.9f;
            s_lure = Object.Instantiate(lurePrefab, GatherTest.Ground(home + rot * new Vector3(-8f, 0f, -6f)), rot);
            yield return new WaitForSeconds(0.5f);
            Vector3 lureAt = s_lure.GetComponentInChildren<WispSpawner>().m_spawnPoint.position;
            Pickable wp = wispPrefab.GetComponent<Pickable>();
            string wispItem = wp.m_itemPrefab.name;
            var lured = new List<GameObject>();
            for (int i = 0; i < 2; i++) lured.Add(Object.Instantiate(wispPrefab, lureAt + rot * new Vector3(i * 1.5f - 0.75f, 0.5f, 0f), Quaternion.identity));
            s_wilds.Add(Object.Instantiate(wispPrefab, lureAt + rot * new Vector3(0f, 2f, 40f), Quaternion.identity));
            s_wilds.AddRange(lured);
            int wispMade = 2 * (wp.m_dontScale ? wp.m_amount : Mathf.Max(wp.m_minAmountScaled, Game.instance.ScaleDrops(wp.m_itemPrefab, wp.m_amount)));
            int wispBefore = GatherTest.CountBase(home, wispItem);
            yield return new WaitForSeconds(1f);
            env.m_debugTimeOfDay = debugTime;
            env.m_debugTime = debugValue;

            // 실제 잠자기 넘김
            EnvMan.instance.SkipToMorning();
            float t = 0f;
            while (t < 25f && EnvMan.instance.IsTimeSkipping()) { yield return new WaitForSeconds(0.5f); t += 0.5f; }
            yield return new WaitForSeconds(1f);
            int day = EnvMan.instance.GetDay();
            checks.Add($"넘김 끝남 ({t:0.0}초)", !EnvMan.instance.IsTimeSkipping());
            checks.Add($"1호 짐 → 상자 (짐 나무 {claude.CountItem("Wood")}·돌 {claude.CountItem("Stone")})", claude.CountItem("Wood") == 0 && claude.CountItem("Stone") == 0);
            int woodNow = GatherTest.CountBase(home, "Wood") + claude.CountItem("Wood"), stoneNow = GatherTest.CountBase(home, "Stone") + claude.CountItem("Stone");
            checks.Add($"개수 그대로 (나무 {woodNow}/{woodAll}, 돌 {stoneNow}/{stoneAll})", woodNow == woodAll && stoneNow == stoneAll);
            checks.Add($"걷지 않음 (움직인 거리 {Utils.DistanceXZ(claude.transform.position, claudeAt):0.0}m)", Utils.DistanceXZ(claude.transform.position, claudeAt) < 3f);
            checks.Add($"1호 아침 날짜 기록 ({claude.Zdo.GetInt(MorningSettle.KeyDay, -1)}/{day})", claude.Zdo.GetInt(MorningSettle.KeyDay, -1) == day);
            // 대사 틀은 여러 개 중 무작위라(회차 150: 틀로 만든 머리글이 실제 대사와 달라 FAIL) 정산 요약 자체가 들렸는지 본다
            string sum = MorningSettle.LastResult.TryGetValue(claude.NpcName, out string s0) ? s0 : null;
            checks.Add("1호가 정리 요약을 말함", !string.IsNullOrEmpty(sum) && heard.Exists(h => h.Contains(sum)));
            checks.Add($"2호(밖)는 빠짐 (나무 {alarak.CountItem("Wood")}/10, 날짜 {alarak.Zdo.GetInt(MorningSettle.KeyDay, -1)}/{alarakDay})",
                alarak.CountItem("Wood") == 10 && alarak.Zdo.GetInt(MorningSettle.KeyDay, -1) == alarakDay);

            // 설비: 광석·주괴 합 보존(상자 주괴 + 상자 광석 + 넣어 둔 광석 + 안 내놓은 결과물), 땅·짐에 주괴 없음, 상자 광석 다 들어감, 연료는 다 녹일 만큼
            ZDO sz = snv.GetZDO();
            int queued = sz.GetInt(ZDOVars.s_queued), pending = sz.GetInt(ZDOVars.s_spawnAmount);
            int bars = GatherTest.CountBase(home, "Copper"), oreChest = GatherTest.CountBase(home, "CopperOre");
            float fuel = sz.GetFloat(ZDOVars.s_fuel);
            TestRunner.Log($"제련소: 넣어 둔 광석 {queued}, 안 내놓은 결과 {pending}, 연료 {fuel:0.0}/{smelter.m_maxFuel}, 상자 주괴 {bars}·광석 {oreChest}·숯 {GatherTest.CountBase(home, "Coal")}, 1호 짐 주괴 {claude.CountItem("Copper")}");
            checks.Add($"구리 합 그대로 ({bars + oreChest + queued + pending + claude.CountItem("Copper")}/{copperAll})", bars + oreChest + queued + pending + claude.CountItem("Copper") == copperAll);
            checks.Add($"결과물이 상자로 (짐 {claude.CountItem("Copper")}·안 내놓은 것 {pending})", claude.CountItem("Copper") == 0 && pending == 0);
            checks.Add($"상자 광석으로 채움 (상자 광석 {oreChest}, 넣어 둔 것 {queued})", oreChest == 0 && queued >= 3);
            // 정산 뒤 제련소가 벌써 굽고 있음 → 지금 굽는 것의 진행만큼 덜 필요(1초에 굽기 1, 연료 fuelPerProduct/secPerProduct)
            float fuelNeed = queued * smelter.m_fuelPerProduct - sz.GetFloat(ZDOVars.s_bakeTimer) * smelter.m_fuelPerProduct / smelter.m_secPerProduct;
            checks.Add($"연료 다 녹일 만큼·상한 안 ({fuel:0.0}, 필요 {fuelNeed:0.0})", fuel >= fuelNeed - 0.05f && fuel <= smelter.m_maxFuel);

            // 발효기: 미드가 상자로(땅·짐 아님), 상자 바탕으로 다시 채움, 새로 익는 중
            int meadNow = GatherTest.CountBase(home, Mead);
            long fstart = fz.GetLong(ZDOVars.s_startTime, 0L);
            double fage = fstart == 0L ? -1 : (ZNet.instance.GetTime() - new System.DateTime(fstart)).TotalSeconds;
            TestRunner.Log($"발효기: 상자 미드 {meadNow - meadBefore}, 상자 바탕 {GatherTest.CountBase(home, MeadBase)}, 내용 {fz.GetInt(ZDOVars.s_content)}, 익은 시간 {fage:0}초");
            checks.Add($"발효기 미드 → 상자 ({meadNow - meadBefore}/{meadMade}, 짐 {claude.CountItem(Mead)})", meadNow - meadBefore == meadMade && claude.CountItem(Mead) == 0);
            checks.Add($"발효기 다시 채움 (상자 바탕 {GatherTest.CountBase(home, MeadBase)}, 새로 익는 중 {fage:0}초)",
                GatherTest.CountBase(home, MeadBase) == 0 && fz.GetInt(ZDOVars.s_content) == MeadBase.GetStableHashCode() && fage >= 0 && fage < fermenter.m_fermentationDuration);

            // 수액 추출기: 수액이 상자로(땅·짐 아님), 칸 0
            int sapNow = GatherTest.CountBase(home, sapItem);
            checks.Add($"수액 → 상자 ({sapNow - sapBefore}/{sapMade}, 짐 {claude.CountItem(sapItem)}, 남은 칸 {sapz.GetInt(ZDOVars.s_level)})",
                sapNow - sapBefore == sapMade && claude.CountItem(sapItem) == 0 && sapz.GetInt(ZDOVars.s_level) == 0);

            // 위습: 끌려온 2만 상자로(떠도는 것은 안 셈), 끌려온 위습은 주운 것(사라짐 또는 s_picked)
            int wispNow = GatherTest.CountBase(home, wispItem);
            int luredLeft = lured.FindAll(g => g != null && g.TryGetComponent(out ZNetView lnv) && lnv.IsValid() && !lnv.GetZDO().GetBool(ZDOVars.s_picked)).Count;
            checks.Add($"끌려온 위습 → 상자 ({wispNow - wispBefore}/{wispMade}, 짐 {claude.CountItem(wispItem)}, 안 주운 것 {luredLeft})",
                wispNow - wispBefore == wispMade && claude.CountItem(wispItem) == 0 && luredLeft == 0);

            // 밭: 당근이 상자로(땅·짐 아님), 다 자란 것 없어짐, 씨앗 1로 한 자리만 다시 심음
            int carrotNow = GatherTest.CountBase(home, "Carrot");
            int sprouts = Sprouts(carrot);
            TestRunner.Log($"밭: 상자 당근 {carrotNow - carrotBefore}/{carrotMade}, 다 자란 것 {FarmJob.Ripe(s_field).Count}, 새싹 {sprouts}, 상자 씨앗 {GatherTest.CountBase(home, carrot.Seed)}");
            checks.Add($"밭 당근 → 상자 ({carrotNow - carrotBefore}/{carrotMade}, 짐 {claude.CountItem("Carrot")})", carrotNow - carrotBefore == carrotMade && claude.CountItem("Carrot") == 0);
            checks.Add($"다 자란 것 거둠 (남은 것 {FarmJob.Ripe(s_field).Count})", FarmJob.Ripe(s_field).Count == 0);
            checks.Add($"씨앗만큼 다시 심음 (새싹 {sprouts}/1, 상자 씨앗 {GatherTest.CountBase(home, carrot.Seed)}/0)", sprouts == 1 && GatherTest.CountBase(home, carrot.Seed) == 0);

            // 같은 아침 다시(불러온 것처럼 저장된 날짜만 보고) → 안 함, 다음 아침 → 함
            claude.GetInventory().AddItem(wood, 5);
            MorningSettle.Snapshot();
            MorningSettle.SettleAll(day);
            checks.Add($"같은 아침 두 번 안 함 (짐 나무 {claude.CountItem("Wood")}/5)", claude.CountItem("Wood") == 5);
            // 다음 아침: 제련소가 아직 안 내놓은 결과물 2(ZDO) → 상자로 바로
            sz.Set(ZDOVars.s_spawnOre, "CopperOre");
            sz.Set(ZDOVars.s_spawnAmount, 2);
            int barsBefore = GatherTest.CountBase(home, "Copper");
            MorningSettle.Snapshot();
            MorningSettle.SettleAll(day + 1);
            checks.Add($"다음 아침엔 함 (짐 나무 {claude.CountItem("Wood")}/0, 상자 {GatherTest.CountBase(home, "Wood")}/{woodAll + 5})",
                claude.CountItem("Wood") == 0 && GatherTest.CountBase(home, "Wood") == woodAll + 5);
            checks.Add($"안 내놓은 결과물 → 상자 (주괴 {GatherTest.CountBase(home, "Copper")}/{barsBefore + 2}, 남은 기록 {sz.GetInt(ZDOVars.s_spawnAmount)})",
                GatherTest.CountBase(home, "Copper") == barsBefore + 2 && sz.GetInt(ZDOVars.s_spawnAmount) == 0);
            checks.Add($"익는 중인 발효기는 안 건드림 (미드 {GatherTest.CountBase(home, Mead)}/{meadNow}, 내용 그대로 {fz.GetLong(ZDOVars.s_startTime, 0L) == fstart})",
                GatherTest.CountBase(home, Mead) == meadNow && fz.GetLong(ZDOVars.s_startTime, 0L) == fstart);
            claude.Zdo.Set(MorningSettle.KeyDay, day); // 시험이 앞당긴 날짜를 되돌림
            End(player, god, checks, onHeard, heard);
        }

        /// <summary>시험 밭 안의 그 작물 새싹(Plant) 수.</summary>
        static int Sprouts(Core.Work.Crop crop)
        {
            int n = 0;
            foreach (var p in Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
                if (p != null && Utils.GetPrefabName(p.gameObject) == crop.Sapling && s_field.Contains(p.transform.position.x, p.transform.position.z)) n++;
            return n;
        }

        static void End(Player player, bool god, GatherTest.Checks checks, System.Action<string, string> onHeard, List<string> heard)
        {
            ChatNet.Heard -= onHeard;
            player.SetGodMode(god);
            if (s_smelter != null) ZNetScene.instance.Destroy(s_smelter);
            s_smelter = null;
            if (s_fermenter != null) ZNetScene.instance.Destroy(s_fermenter);
            s_fermenter = null;
            if (s_sap != null) ZNetScene.instance.Destroy(s_sap);
            s_sap = null;
            if (s_lure != null) ZNetScene.instance.Destroy(s_lure);
            s_lure = null;
            foreach (var w in s_wilds) if (w != null && w.TryGetComponent(out ZNetView wnv) && wnv.IsValid()) ZNetScene.instance.Destroy(w);
            s_wilds.Clear();
            if (s_field != null)
            {
                // 시험 밭의 새싹·작물을 지우고 밭 기록도 지움
                foreach (var p in Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
                    if (p != null && s_field.Contains(p.transform.position.x, p.transform.position.z)) ZNetScene.instance.Destroy(p.gameObject);
                foreach (var p in FarmJob.Ripe(s_field)) ZNetScene.instance.Destroy(p.gameObject);
                Brain.Farms.Remove(s_field.X, s_field.Z);
            }
            s_field = null;
            MachineStep.TestIgnoreRoof = false;
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
