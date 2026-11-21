using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CasualtiesCarryover
{
    /// <summary>
    /// 아공간: 착용 칸과 따로 있는 무게 없는 가방(퍽 단계 = 담을 수 있는 개수 1~3). 그 회차 안에서만 쓴다.
    /// 게임 근거(디컴파일로 확인):
    /// - 가방 창: PlayerCamera.OpenContainer(Container)가 가방 패널을 열고, 인벤토리(원형 메뉴)가 닫히면 CloseContainer로 같이 닫힌다.
    ///   패널에 끌어다 놓으면 currentContainer.LoadItem(item) — 가방과 10 안에 있어야 하고 CanHoldItem(무게·태그)을 통과해야 한다. 꺼내기도 게임 패널 그대로.
    /// - 몸 무게(Body 2788)는 몸에 든·입은 물건만 센다 → 어디에도 걸지 않은 숨은 가방은 무게 0. (게임의 gravbag도 encumberanceMult=0으로 무게를 없앤다.)
    /// - 층 이동 때 WorldGeneration.Clear는 부모 없는 아이템만 지운다 → 숨은 가방은 우리 보관 오브젝트 밑에 두어 남긴다.
    /// 숨은 가방: 게임의 legpouch를 만들어 그림·충돌을 끄고 몸 위치를 따라다니게 한다. 저장하고 나가기는 안의 물건을 따로 파일에 적었다가 이어하기 때 되살린다.
    /// </summary>
    internal static class Subspace
    {
        internal const string BagId = "legpouch";
        private static Transform holder;
        internal static Container Bag;
        internal static string FilePath;

        internal static int Capacity => Plugin.Perks?.SubspaceSlots ?? 0;

        internal static List<Item> Items
        {
            get
            {
                var l = new List<Item>();
                if (Bag != null) foreach (Transform t in Bag.transform) if (t.TryGetComponent<Item>(out var it)) l.Add(it);
                return l;
            }
        }

        internal static string Name(Item it)
        {
            try { var n = Locale.GetItem(it.id); return string.IsNullOrEmpty(n) ? it.id : n; } catch { return it.id; }
        }

        /// <summary>회차 중 아공간 퍽이 있으면 숨은 가방을 만든다(없으면 아무것도 안 함).</summary>
        internal static Container EnsureBag(Body body)
        {
            if (Bag != null || Capacity <= 0 || body == null) return Bag;
            if (holder == null) holder = new GameObject("CarryoverSubspace").transform;   // 씬 오브젝트: 메뉴로 나가면 함께 사라짐
            var go = Utils.Create(BagId, body.transform.position, 0f);
            go.transform.SetParent(holder, true);
            var item = go.GetComponent<Item>();
            item.label = "아공간";
            Bag = go.GetComponent<Container>();
            Bag.maxWeight = 9999f; Bag.maxWeightPerItem = 9999f; Bag.encumberanceMult = 0f;
            Bag.tagRestriction = new string[0]; Bag.itemsVisible = false;
            Hide(body);
            Plugin.Log.LogInfo("subspace bag created");
            return Bag;
        }

        /// <summary>매 프레임: 몸을 따라다니고(끌어다 넣기 거리 10), 보이지도 부딪히지도 않게.</summary>
        internal static void Hide(Body body)
        {
            if (Bag == null) return;
            if (body != null) Bag.transform.position = body.transform.position;
            var item = Bag.GetComponent<Item>();
            if (item != null && item.rb != null) { item.rb.simulated = false; item.rb.velocity = Vector2.zero; }
            var sr = Bag.GetComponent<SpriteRenderer>(); if (sr != null) sr.enabled = false;
            foreach (var c in Bag.GetComponents<Collider2D>()) c.enabled = false;
        }

        /// <summary>V: 인벤토리를 열고 아공간 가방 창을 연다(이미 열려 있으면 닫는다).</summary>
        internal static string Toggle(Body body)
        {
            var pc = PlayerCamera.main;
            if (EnsureBag(body) == null || pc == null) return null;
            if (pc.currentContainer == Bag) { pc.CloseContainer(); return null; }
            pc.radialOpen = true;
            pc.OpenContainer(Bag);
            return null;
        }

        /// <summary>게임 가방 창과 같은 넣기(시험에서도 씀): 이미 손에 들려 있으면 내려놓고 가방에 넣는다.</summary>
        internal static bool Put(Body body, Item it)
        {
            if (EnsureBag(body) == null) return false;
            if (body.HoldingItem(it)) body.DropItem(it);
            it.transform.position = Bag.transform.position;
            Bag.LoadItem(it);
            return it.transform.parent == Bag.transform;
        }

        /// <summary>회차가 끝남(죽음·엔딩)·새 회차·튜토리얼: 가방과 안의 물건을 없애고 저장 파일도 지운다.</summary>
        internal static void Clear()
        {
            var pc = PlayerCamera.main;
            if (pc != null && Bag != null && pc.currentContainer == Bag) pc.CloseContainer();
            if (Bag != null) UnityEngine.Object.Destroy(Bag.gameObject);
            Bag = null;
            try { if (FilePath != null && File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception e) { Plugin.Log.LogWarning("subspace file delete: " + e.Message); }
        }

        /// <summary>아공간 가방은 퍽 단계만큼만, 가방은 안 받는다. 다른 가방은 게임 그대로.</summary>
        [HarmonyPatch(typeof(Container), nameof(Container.CanHoldItem))]
        [HarmonyPostfix]
        private static void CanHoldPostfix(Container __instance, Item item, ref bool __result)
        {
            if (__instance != Bag || Bag == null) return;
            if (item.GetComponent<Container>() != null || __instance.GetActualItemCount() >= Capacity)
            {
                __result = false;
            }
        }

        /// <summary>
        /// 아공간 물건도 "가진 물건"으로 친다(사용자 11-21: 아공간 물통에 제작 액체가 안 들어감, 아공간 자물쇠 키트를 못 씀).
        /// 게임 근거: 제작 재료(GetItemsForRecipe)·액체 결과 담기(RecipeResult.SpawnResult)·자물쇠 키트(FindByIdThorough)·FindByTagThorough가
        /// 모두 Body.GetAllItemsThorough를 쓴다. 무게(GetTotalEncumberance)는 손·착용 칸만 세므로 이 패치와 상관없이 0 그대로.
        /// 가방 자체는 넣지 않는다(재료로 써 없어지면 안 됨).
        /// </summary>
        [HarmonyPatch(typeof(Body), nameof(Body.GetAllItemsThorough))]
        [HarmonyPostfix]
        private static void ThoroughPostfix(Body __instance, List<Item> __result)
        {
            if (Bag == null || __result == null || PlayerCamera.main == null || __instance != PlayerCamera.main.body) return;
            foreach (var it in Items) if (it != null && !__result.Contains(it)) __result.Add(it);
        }

        // ---- 저장하고 나가기 / 이어하기 ----
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

        internal static void SaveToFile()
        {
            var items = Items;
            if (items.Count == 0) { try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { } return; }
            var arr = new JArray();
            foreach (var it in items)
            {
                var comps = new Dictionary<Type, Component>();
                foreach (var c in it.gameObject.GetComponents<Component>())
                    if (c != null && Attribute.GetCustomAttributes(c.GetType()).Any(a => a is Saveable)) comps[c.GetType()] = c;
                arr.Add(new JObject
                {
                    ["id"] = it.id, ["condition"] = it.condition, ["favourited"] = it.favourited,
                    ["label"] = it.label, ["swatch"] = it.colorSwatch,
                    ["components"] = JObject.Parse(JsonConvert.SerializeObject(comps, Formatting.None, Settings)),
                });
            }
            File.WriteAllText(FilePath, arr.ToString(Formatting.None));
            Plugin.Log.LogInfo($"subspace saved: {items.Count} items");
        }

        internal static int RestoreFromFile(Body body)
        {
            if (FilePath == null || !File.Exists(FilePath) || EnsureBag(body) == null) return 0;
            int n = 0;
            try
            {
                foreach (JObject o in JArray.Parse(File.ReadAllText(FilePath)))
                {
                    try
                    {
                        var go = UnityEngine.Object.Instantiate(Resources.Load((string)o["id"]), Bag.transform.position, Quaternion.identity) as GameObject;
                        var it = go.GetComponent<Item>();
                        it.condition = (float)o["condition"]; it.favourited = (bool)o["favourited"];
                        it.label = (string)o["label"]; it.colorSwatch = (int)o["swatch"];
                        foreach (var p in ((JObject)o["components"]).Properties())
                        {
                            var type = AccessTools.TypeByName(p.Name);
                            var comp = type != null ? go.GetComponent(type) : null;
                            if (comp == null) continue;
                            foreach (var f in ((JObject)p.Value).Properties())
                            {
                                var field = type.GetField(f.Name);
                                if (field == null) continue;
                                try { field.SetValue(comp, f.Value.ToObject(field.FieldType)); } catch { }
                            }
                        }
                        if (Put(body, it)) n++; else UnityEngine.Object.Destroy(go);
                    }
                    catch (Exception e) { Plugin.Log.LogWarning("subspace restore item: " + e.Message); }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("subspace restore: " + e.Message); }
            Plugin.Log.LogInfo($"subspace restored: {n} items");
            return n;
        }

        [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGame))]
        [HarmonyPostfix]
        private static void OnSaveGame()
        {
            try { SaveToFile(); } catch (Exception e) { Plugin.Log.LogError("subspace save: " + e); }
        }
    }
}
