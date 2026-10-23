using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 출발 준비 시험(③ 첫 조각): 깃발 → 장비 상자(가죽 갑옷·투구·바지, 사슴 망토, 나무 방패, 몽둥이, 부싯돌 도끼, 체력 미드 5)
    /// + 음식 상자(익힌 고기·꿀·산딸기·블루베리) → 1호에게 누더기 갑옷을 입혀 두고 "!Claude 나무 5" →
    /// 준비가 끝나면: 더 좋은 갑옷으로 갈아입고 누더기는 상자에 돌려놓았는지, 가장 센 근접 무기·미드 3개를 챙겼는지,
    /// 음식 3종을 먹어 최대 체력이 올랐는지, 갑옷이 실제 피해를 줄이는지 → 두 번째 일은 갈 데가 없어 바로 나가는지 →
    /// 가방을 미드로 꽉 채우고 상자에 청동 갑옷(ChatGPT-01): 상자에 자리가 없으면 가죽 갑옷을 그대로 입고, 자리가 있으면 맞바꿔 입는지.
    /// </summary>
    static class PrepTest
    {
        public static void Register() => TestRunner.Register("vh_preptest", "출발 준비(장비·음식) 시험", Run);

        static readonly string[] GearItems = { "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather", "CapeDeerHide", "ShieldWood", "Club", "AxeFlint" };
        static readonly string[] FoodItems = { "CookedMeat", "Honey", "Raspberry", "Blueberries" };

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
            player.SetGodMode(true);
            var heard = new List<string>();
            System.Action<string, string> onHeard = (n, t) => heard.Add(n + ": " + t);
            ChatNet.Heard += onHeard;
            EnvMan env = EnvMan.instance;
            bool debugTime = env.m_debugTimeOfDay;
            float debugValue = env.m_debugTime;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;

            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            var missing = new List<string>();
            foreach (var n in new List<string>(GearItems) { "MeadHealthMinor", "ArmorRagsChest", "ArmorBronzeChest", "Stone" }) if (ObjectDB.instance.GetItemPrefab(n) == null) missing.Add(n);
            foreach (var n in FoodItems) if (ObjectDB.instance.GetItemPrefab(n) == null) missing.Add(n);
            checks.Add($"프리팹 있음{(missing.Count > 0 ? " (없음: " + string.Join(", ", missing.ToArray()) + ")" : "")}", missing.Count == 0 && chestPrefab != null);
            if (missing.Count > 0 || chestPrefab == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }

            // 1) 깃발, NPC, 상자 2개
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
            Quaternion rot = player.transform.rotation;
            if (TestKit.OldClear(home) > 0) yield return new WaitForSeconds(0.5f); // 둘레 생물(적·동료 해골)을 치우고 시작(사용자 결정 2026-10-22 시험 환경)
            TestKit.OldCheck(home, player, god);
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Scv);
            checks.Add("깃발에 NPC 나타남", claude != null && WorkerNpc.All.Count == 2);
            if (claude == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }
            if (GatherTest.ClearOldChests(home) > 0) yield return new WaitForSeconds(0.5f); // 없앤 상자는 프레임 끝에 사라짐: 그 전에 땅 높이를 재면 옛 상자 위에 놓인다
            Piece chestPiece = chestPrefab.GetComponent<Piece>();
            Vector3 eqPos = GatherTest.Ground(home + rot * new Vector3(-4f, 0f, -1f));
            Vector3 foodPos = GatherTest.Ground(home + rot * new Vector3(4f, 0f, -1f));
            player.PlacePiece(chestPiece, eqPos, rot, doAttack: false);
            player.PlacePiece(chestPiece, foodPos, rot, doAttack: false);
            yield return new WaitForSeconds(1f);
            Container eq = GatherTest.ChestAt(eqPos), food = GatherTest.ChestAt(foodPos);
            checks.Add("상자 2개 놓임", eq != null && food != null);
            if (eq == null || food == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }
            Chests.TryEdit(eq, inv =>
            {
                foreach (var n in GearItems) inv.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                inv.AddItem(ObjectDB.instance.GetItemPrefab("MeadHealthMinor"), 5);
            });
            Chests.TryEdit(food, inv => { foreach (var n in FoodItems) inv.AddItem(ObjectDB.instance.GetItemPrefab(n), 3); });

            // 누더기 갑옷을 입혀 둔다(더 좋은 걸로 갈아입고 돌려놓는지 보려고)
            Inventory mine = claude.GetInventory();
            mine.AddItem(ObjectDB.instance.GetItemPrefab("ArmorRagsChest"), 1);
            ItemDrop.ItemData rags = mine.GetAllItems().Find(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "ArmorRagsChest");
            claude.Wear(new[] { rags });
            float hpBefore = claude.GetMaxHealth();
            TestRunner.Log($"준비 전: 최대 체력 {hpBefore:0}, 방어력 {claude.GetBodyArmor():0}, 누더기 입음 {rags != null && claude.IsWearing(rags)}");

            // 2) 일 시키기 → 준비
            GatherTest.Say($"!{Workers.Name1} 나무 5", null);
            yield return new WaitForSeconds(0.5f); // 명령이 일이 될 때까지
            float t = 0f;
            while (t < 60f && claude != null && claude.Ai.Job != null && (claude.Ai.Job.StepName == "Prep" || claude.Ai.Job.StepName == "Unload"))
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
            }
            checks.Add($"준비 끝 ({t:0}초)", claude != null && t < 60f);
            if (claude == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(0.5f);

            var worn = new List<string>();
            foreach (var it in mine.GetEquippedItems()) worn.Add(it.m_dropPrefab != null ? it.m_dropPrefab.name : it.m_shared.m_name);
            TestRunner.Log($"입은 것: {string.Join(", ", worn.ToArray())}");
            foreach (var n in new[] { "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather", "CapeDeerHide", "ShieldWood" })
                checks.Add($"{n} 입음", worn.Contains(n));
            checks.Add("누더기는 장비 상자로 돌려놓음", GatherTest.Count(eq, "ArmorRagsChest") == 1 && claude.CountItem("ArmorRagsChest") == 0);
            string bestWeapon = Gear.WeaponScore(Item("Club")) >= Gear.WeaponScore(Item("AxeFlint")) ? "Club" : "AxeFlint";
            checks.Add($"가장 센 근접 무기 {bestWeapon} 챙김", claude.CountItem(bestWeapon) == 1);
            checks.Add($"체력 미드 3개 챙김 (들고 {claude.CountItem("MeadHealthMinor")}, 상자 {GatherTest.Count(eq, "MeadHealthMinor")})",
                claude.CountItem("MeadHealthMinor") == 3 && GatherTest.Count(eq, "MeadHealthMinor") == 2);
            int left = 0;
            foreach (var n in FoodItems) left += GatherTest.Count(food, n);
            checks.Add($"음식 3종 먹음 ({string.Join(", ", new List<string>(claude.Meals.Names).ToArray())}, 상자에 {left}개 남음)",
                claude.Meals.Count == 3 && left == FoodItems.Length * 3 - 3);
            checks.Add($"최대 체력 오름 ({hpBefore:0} → {claude.GetMaxHealth():0})", claude.GetMaxHealth() > hpBefore + 20f);
            checks.Add("음식 저장됨", !string.IsNullOrEmpty(claude.GetComponent<ZNetView>().GetZDO().GetString(WorkerNpc.KeyFoods)));

            // 3) 갑옷이 피해를 줄이는지: 둔기 20을 맞힌다
            float armor = claude.GetBodyArmor();
            float hp = claude.GetHealth();
            var hit = new HitData { m_point = claude.GetCenterPoint(), m_dir = Vector3.forward };
            hit.m_damage.m_blunt = 20f;
            claude.Damage(hit); // 소유자(호스트)에게 가는 RPC는 ZRoutedRpc가 그 자리에서 처리 → 체력이 바로 깎임
            float lost = hp - claude.GetHealth();
            // 0.5초 뒤에 재면 그사이 10초마다 오는 음식 회복(Meals.Tick, 바닐라 Player.UpdateFood와 같음)이 섞일 수 있다(E007)
            float regen = 0f;
            foreach (var n in claude.Meals.Names) regen += Gear.FoodOf(n)?.Regen ?? 0f;
            yield return new WaitForSeconds(0.5f);
            TestRunner.Log($"피해 바로 {lost:0.0}, 0.5초 뒤 {hp - claude.GetHealth():0.0} (음식 회복 {regen:0.#}/10초)");
            float expect = HitData.DamageTypes.ApplyArmor(20f, armor); // 바닐라 갑옷 공식
            checks.Add($"갑옷 {armor:0}이 피해를 줄임 (둔기 20 → {lost:0.0}, 바닐라 공식 {expect:0.0})", armor > 0f && Mathf.Abs(lost - expect) < 0.5f && lost < 19f);
            claude.Heal(100f, false);

            // 4) 두 번째 일: 더 챙길 것도 먹을 것도 없으니 상자에 가지 않고 바로 나간다
            yield return new WaitForSeconds(2f);
            GatherTest.Say($"!{Workers.Name1} 나무 5", null);
            yield return new WaitForSeconds(0.25f);
            t = 0f;
            while (t < 30f && claude != null && claude.Ai.Job != null && (claude.Ai.Job.StepName == "Prep" || claude.Ai.Job.StepName == "Unload"))
            {
                yield return new WaitForSeconds(0.25f);
                t += 0.25f;
            }
            checks.Add($"두 번째는 바로 나감 ({t:0.0}초)", t <= 1f);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(0.5f);

            // 5) 가방이 꽉 찼을 때 더 좋은 갑옷(ChatGPT-01). 채우는 건 체력 미드. 몫(3)을 넘는 미드는 짐 정리가 상자로 옮기므로(회차 57)
            //    음식 상자도 돌로 채워 둘 곳을 없앤다. 개수 보존은 짐 + 두 상자로 센다.
            GameObject mead = ObjectDB.instance.GetItemPrefab("MeadHealthMinor"), stone = ObjectDB.instance.GetItemPrefab("Stone");
            for (int i = 0; i < 64 && mine.GetEmptySlots() > 0; i++) mine.AddItem(mead, 10);
            string meadName = mead.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            Chests.TryEdit(eq, inv => inv.RemoveItem(meadName, inv.CountItems(meadName))); // 상자 미드 더미에 짐 미드가 합쳐져 자리가 나지 않게
            Chests.TryEdit(food, inv => { for (int i = 0; i < 64 && inv.GetEmptySlots() > 0; i++) inv.AddItem(stone, 50); });
            System.Func<int> allMeads = () => claude.CountItem("MeadHealthMinor") + GatherTest.Count(eq, "MeadHealthMinor") + GatherTest.Count(food, "MeadHealthMinor");
            int meads = allMeads();
            Chests.TryEdit(eq, inv =>
            {
                inv.AddItem(ObjectDB.instance.GetItemPrefab("ArmorBronzeChest"), 1);
                for (int i = 0; i < 64 && inv.GetEmptySlots() > 0; i++) inv.AddItem(stone, 50);
            });
            checks.Add($"가방·장비 상자 꽉 참 (미드 {meads})", mine.GetEmptySlots() == 0 && eq.GetInventory().GetEmptySlots() == 0);
            for (int round = 0; round < 2; round++)
            {
                // 첫 번째: 상자에 헌 갑옷을 넣을 자리가 없다 → 그대로. 두 번째: 상자의 돌을 치워 자리를 만든다 → 맞바꾼다.
                if (round == 1) Chests.TryEdit(eq, inv => inv.RemoveItem(stone.GetComponent<ItemDrop>().m_itemData.m_shared.m_name, GatherTest.Count(eq, "Stone")));
                GatherTest.Say($"!{Workers.Name1} 나무 5", null);
                yield return new WaitForSeconds(0.5f);
                t = 0f;
                while (t < 60f && claude != null && claude.Ai.Job != null && (claude.Ai.Job.StepName == "Prep" || claude.Ai.Job.StepName == "Unload"))
                {
                    yield return new WaitForSeconds(0.5f);
                    t += 0.5f;
                }
                GatherTest.Say("!멈춰", null);
                yield return new WaitForSeconds(0.5f);
                if (claude == null) break;
                string want = round == 0 ? "ArmorLeatherChest" : "ArmorBronzeChest", other = round == 0 ? "ArmorBronzeChest" : "ArmorLeatherChest";
                ItemDrop.ItemData chestArmor = mine.GetEquippedItems().Find(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest);
                string wornName = chestArmor?.m_dropPrefab != null ? chestArmor.m_dropPrefab.name : "없음";
                checks.Add($"{(round == 0 ? "상자에 자리 없음: 헌 갑옷 그대로" : "상자에 자리 있음: 맞바꿔 입음")} (입은 것 {wornName}, 상자에 {other} {GatherTest.Count(eq, other)})",
                    wornName == want && GatherTest.Count(eq, other) == 1 && claude.CountItem(other) == 0);
                checks.Add($"물건 그대로 (갑옷 들고+상자 {claude.CountItem(want) + GatherTest.Count(eq, want)}, 미드 {allMeads()}/{meads})",
                    claude.CountItem(want) + GatherTest.Count(eq, want) == 1 && allMeads() == meads);
            }
            // 채운 돌·미드는 치운다(뒤 시험이 꽉 찬 상자를 거점 상자로 씀)
            string stoneName = stone.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            foreach (var c in new[] { eq, food }) Chests.TryEdit(c, inv => { inv.RemoveItem(stoneName, inv.CountItems(stoneName)); inv.RemoveItem(meadName, inv.CountItems(meadName)); });
            if (claude != null) claude.GetInventory().RemoveItem(meadName, System.Math.Max(0, claude.CountItem("MeadHealthMinor") - Prep.MeadsToCarry));
            End(player, god, env, debugTime, debugValue, checks, onHeard, heard);
        }

        static ItemDrop.ItemData Item(string prefab) => ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData;

        static void End(Player player, bool god, EnvMan env, bool debugTime, float debugValue, GatherTest.Checks checks, System.Action<string, string> onHeard, List<string> heard)
        {
            ChatNet.Heard -= onHeard;
            player.SetGodMode(god);
            env.m_debugTimeOfDay = debugTime;
            env.m_debugTime = debugValue;
            TestRunner.Log("--- NPC 대사 ---");
            foreach (var h in heard) TestRunner.Log(h);
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
