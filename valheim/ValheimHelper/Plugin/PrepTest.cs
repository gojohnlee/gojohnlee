using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 출발 준비 시험(③ 첫 조각): 깃발 → 장비 상자(가죽 갑옷·투구·바지, 사슴 망토, 나무 방패, 몽둥이, 부싯돌 도끼, 체력 미드 5)
    /// + 음식 상자(익힌 고기·꿀·산딸기·블루베리) → 1호에게 누더기 갑옷을 입혀 두고 "!Claude 나무 5" →
    /// 준비가 끝나면: 더 좋은 갑옷으로 갈아입고 누더기는 상자에 돌려놓았는지, 가장 센 근접 무기·미드 3개를 챙겼는지,
    /// 음식 3종을 먹어 최대 체력이 올랐는지, 갑옷이 실제 피해를 줄이는지 → 두 번째 일은 갈 데가 없어 바로 나가는지.
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
            foreach (var n in new List<string>(GearItems) { "MeadHealthMinor", "ArmorRagsChest" }) if (ObjectDB.instance.GetItemPrefab(n) == null) missing.Add(n);
            foreach (var n in FoodItems) if (ObjectDB.instance.GetItemPrefab(n) == null) missing.Add(n);
            checks.Add($"프리팹 있음{(missing.Count > 0 ? " (없음: " + string.Join(", ", missing.ToArray()) + ")" : "")}", missing.Count == 0 && chestPrefab != null);
            if (missing.Count > 0 || chestPrefab == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }

            // 1) 깃발, NPC, 상자 2개
            Workers.DespawnAll();
            yield return new WaitForSeconds(1f);
            Vector3 home = GatherTest.Ground(player.transform.position + player.transform.forward * 4f);
            Quaternion rot = player.transform.rotation;
            player.PlacePiece(BaseFlag.Prefab.GetComponent<Piece>(), home, rot, doAttack: false);
            yield return new WaitForSeconds(3f);
            WorkerNpc claude = WorkerNpc.All.Find(n => n.Personality == Core.Npc.Personality.Scv);
            checks.Add("깃발에 NPC 나타남", claude != null && WorkerNpc.All.Count == 2);
            if (claude == null) { End(player, god, env, debugTime, debugValue, checks, onHeard, heard); yield break; }
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
            GatherTest.Say("!상자 장비", eq);
            GatherTest.Say("!상자 음식", food);
            yield return new WaitForSeconds(0.5f);
            checks.Add("장비·음식 상자 지정됨", Chests.Assigned(Chests.KeyEquip(0)) == eq && Chests.Assigned(Chests.KeyFood) == food);

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
            while (t < 60f && claude != null && claude.Ai.Job != null && claude.Ai.Job.StepName == "Prep")
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
            claude.Damage(hit);
            yield return new WaitForSeconds(0.5f);
            float lost = hp - claude.GetHealth();
            float expect = HitData.DamageTypes.ApplyArmor(20f, armor); // 바닐라 갑옷 공식
            checks.Add($"갑옷 {armor:0}이 피해를 줄임 (둔기 20 → {lost:0.0}, 바닐라 공식 {expect:0.0})", armor > 0f && Mathf.Abs(lost - expect) < 0.5f && lost < 19f);
            claude.Heal(100f, false);

            // 4) 두 번째 일: 더 챙길 것도 먹을 것도 없으니 상자에 가지 않고 바로 나간다
            yield return new WaitForSeconds(2f);
            GatherTest.Say($"!{Workers.Name1} 나무 5", null);
            yield return new WaitForSeconds(0.25f);
            t = 0f;
            while (t < 30f && claude != null && claude.Ai.Job != null && claude.Ai.Job.StepName == "Prep")
            {
                yield return new WaitForSeconds(0.25f);
                t += 0.25f;
            }
            checks.Add($"두 번째는 바로 나감 ({t:0.0}초)", t <= 1f);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(0.5f);
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
