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
    /// 아공간: 그 회차 안에서만 쓰는 무게 없는 추가 칸(퍽 단계 = 칸 수).
    /// 게임 근거: 몸이 든 물건은 body.slots[i] 밑의 자식이고, 무게·저장은 slots·착용품만 센다(GetAllItems·SaveSystem.SaveGame).
    /// 층 바꿀 때 WorldGeneration.Clear는 부모가 없거나 "DOSPAWN"인 아이템만 지운다 → 우리 보관용 오브젝트 밑에 꺼 둔 아이템은 무게 0, 층 이동에도 남는다.
    /// 저장하고 나가기: SaveGame 때 게임과 같은 방식(id·condition·favourited·label·colorSwatch + [Saveable] 컴포넌트의 공개 필드)으로 따로 파일에 적고,
    /// 이어하기로 회차가 시작되면 되살린다. 죽거나 엔딩·새 회차면 비운다. 가방(Container)은 넣지 않는다.
    /// </summary>
    internal static class Subspace
    {
        internal static readonly List<Item> Items = new List<Item>();
        private static Transform holder;
        internal static string FilePath;

        internal static int Capacity => Plugin.Perks?.SubspaceSlots ?? 0;

        private static Transform Holder()
        {
            if (holder == null) holder = new GameObject("CarryoverSubspace").transform;   // 씬 오브젝트: 메뉴로 나가면 함께 사라짐
            return holder;
        }

        private static void Prune() => Items.RemoveAll(i => i == null);

        internal static string Name(Item it)
        {
            try { var n = Locale.GetItem(it.id); return string.IsNullOrEmpty(n) ? it.id : n; } catch { return it.id; }
        }

        /// <summary>손(주 손 먼저)에 든 물건을 넣는다. 결과 메시지.</summary>
        internal static string StoreFromHands(Body body)
        {
            Prune();
            if (Capacity <= 0) return null;
            if (Items.Count >= Capacity) return $"아공간이 가득 찼습니다 ({Items.Count}/{Capacity})";
            var order = new List<int> { body.handSlot };
            for (int i = 0; i < body.slots.Length; i++) if (body.slots[i].isHand && i != body.handSlot) order.Add(i);
            foreach (int s in order)
            {
                if (s < 0 || s >= body.slots.Length || !body.HoldingItem(s)) continue;
                var it = body.GetItem(s);
                if (it == null) continue;
                if (it.GetComponent<Container>() != null) return "가방은 아공간에 넣을 수 없습니다";
                Store(body, it);
                return $"아공간에 넣음: {Name(it)} ({Items.Count}/{Capacity})";
            }
            return "손에 든 물건이 없습니다";
        }

        internal static void Store(Body body, Item it)
        {
            if (body.HoldingItem(it)) body.DropItem(it);
            it.transform.SetParent(Holder(), false);
            it.gameObject.SetActive(false);
            Items.Add(it);
        }

        /// <summary>마지막에 넣은 물건을 빈 손으로 꺼낸다.</summary>
        internal static string TakeOut(Body body)
        {
            Prune();
            if (Items.Count == 0) return "아공간이 비어 있습니다";
            int? free = null;
            for (int i = 0; i < body.slots.Length; i++)
                if (body.slots[i].isHand && !body.HoldingItem(i) && body.slots[i].canPickUp) { free = i; break; }
            if (free == null) return "빈 손이 없습니다";
            var it = Items[Items.Count - 1];
            Items.RemoveAt(Items.Count - 1);
            it.transform.SetParent(null, false);
            it.transform.position = body.transform.position;
            it.gameObject.SetActive(true);
            body.PickUpItem(it, free.Value, force: true);
            return $"아공간에서 꺼냄: {Name(it)}";
        }

        /// <summary>회차가 끝남(죽음·엔딩) 또는 새 회차: 안의 물건을 없애고 저장 파일도 지운다.</summary>
        internal static void Clear()
        {
            foreach (var it in Items) if (it != null) UnityEngine.Object.Destroy(it.gameObject);
            Items.Clear();
            try { if (FilePath != null && File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception e) { Plugin.Log.LogWarning("subspace file delete: " + e.Message); }
        }

        // ---- 저장하고 나가기 / 이어하기 ----
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

        internal static void SaveToFile()
        {
            Prune();
            var arr = new JArray();
            foreach (var it in Items)
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
            Plugin.Log.LogInfo($"subspace saved: {Items.Count} items");
        }

        internal static int RestoreFromFile(Body body)
        {
            if (FilePath == null || !File.Exists(FilePath)) return 0;
            int n = 0;
            try
            {
                foreach (JObject o in JArray.Parse(File.ReadAllText(FilePath)))
                {
                    try
                    {
                        var go = UnityEngine.Object.Instantiate(Resources.Load((string)o["id"]), body.transform.position, Quaternion.identity) as GameObject;
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
                        it.transform.SetParent(Holder(), false);
                        go.SetActive(false);
                        Items.Add(it);
                        n++;
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
