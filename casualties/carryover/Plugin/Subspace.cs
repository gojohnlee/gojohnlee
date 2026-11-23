using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

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

        internal static int Capacity => Plugin.Perks?.SubspaceSlots ?? 0;   // 퍽 단계(0이면 아공간 없음)
        internal static float MaxWeight => Plugin.Perks?.SubspaceMaxWeight ?? 0f;   // 1단계 20u, 2단계 40u …
        private const float Eps = 0.001f;

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
            Bag.maxWeight = Bag.maxWeightPerItem = MaxWeight + Eps; Bag.encumberanceMult = 0f;
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
            Bag.maxWeight = Bag.maxWeightPerItem = MaxWeight + Eps;   // 0.001: 소수 더하기 오차로 딱 맞는 무게(예 89.1+0.9=90)가 막히지 않게
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
            ScrollRow = 0;
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

        /// <summary>회차가 끝남(죽음·엔딩): 안의 물건을 파일에 적고 가방만 없앤다 → 다음 회차 시작 때 되살린다(사용자 11-21: 아공간 유지).</summary>
        internal static void EndRun()
        {
            try { if (Bag != null) SaveToFile(); } catch (Exception e) { Plugin.Log.LogError("subspace keep: " + e); }
            DropBag();
        }

        /// <summary>가방만 없앤다(파일은 그대로).</summary>
        internal static void DropBag()
        {
            var pc = PlayerCamera.main;
            if (pc != null && Bag != null && pc.currentContainer == Bag) pc.CloseContainer();
            if (Bag != null) UnityEngine.Object.Destroy(Bag.gameObject);
            Bag = null;
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

        /// <summary>
        /// 아공간 가방: 무게로 제한(퍽 1단계마다 20u, 사용자 11-22). 가방은 안 받는다.
        /// 게임 Container.CanHoldItem: item.totalWeight ≤ maxWeightPerItem && GetHoldingWeight() + totalWeight ≤ maxWeight → maxWeight만 맞추면 게임이 거른다.
        /// 가방 창 위 글자도 게임 UpdateContainerInfoText의 "{들어 있는 무게:0.##}/{maxWeight:0.##}u" 그대로(게임 무게 단위 u).
        /// 몸 무게(GetTotalEncumberance)는 손·착용 칸만 세므로 아공간 무게는 몸에 안 더해진다.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.CanHoldItem))]
        [HarmonyPostfix]
        private static void CanHoldPostfix(Container __instance, Item item, ref bool __result)
        {
            if (__instance != Bag || Bag == null) return;
            if (item.GetComponent<Container>() != null) __result = false;
        }

        /// <summary>넣기(끌어다 놓기·Put)가 무게 때문에 막히면 한국어 알림. 게임 LoadItem은 CanHoldItem이 거짓이면 아무 말 없이 넘어간다.</summary>
        [HarmonyPatch(typeof(Container), nameof(Container.LoadItem))]
        [HarmonyPrefix]
        private static void LoadPrefix(Container __instance, Item item)
        {
            if (Bag == null || __instance != Bag || item == null || item.transform.parent == Bag.transform || item.GetComponent<Container>() != null) return;
            float left = MaxWeight + Eps - Bag.GetHoldingWeight();
            if (item.totalWeight <= left) return;
            LastAlert = $"아공간 무게 초과: {Subspace.Name(item)} {item.totalWeight:0.##}u, 남은 용량 {Mathf.Max(0f, left):0.##}u (최대 {MaxWeight:0.##}u)";
            try { PlayerCamera.main?.DoAlert(LastAlert); } catch (Exception e) { Plugin.Log.LogWarning("subspace alert: " + e.Message); }
        }
        internal static string LastAlert;

        /// <summary>
        /// 넣을 때 같은 물건끼리 자동으로 합친다(사용자 11-22: 고철 5% + 4% → 9% 하나). 게임 Body.CombineItems와 같은 셈:
        /// 받는 쪽 condition += min(1 - 받는 쪽, 넣는 쪽), 넣는 쪽은 그만큼 줄고 0이 되면 없앤다. 게임이 합치기를 허용하는 물건(Stats.combineable)만,
        /// 액체 그릇·탄약·총은 게임도 따로 다루므로 뺀다. 받는 쪽은 많이 찬 것부터 → 100%에 가까운 것이 먼저 채워진다.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.LoadItem))]
        [HarmonyPostfix]
        private static void LoadPostfix(Container __instance, Item item)
        {
            if (Bag == null || __instance != Bag || item == null || item.transform.parent != Bag.transform) return;
            try { MergeInto(item); } catch (Exception e) { Plugin.Log.LogWarning("subspace merge: " + e.Message); }
        }

        internal static void MergeInto(Item it)
        {
            if (it.Stats == null || !it.Stats.combineable || it.GetComponent<WaterContainerItem>() || it.GetComponent<AmmoScript>() || it.GetComponent<GunScript>()) return;
            foreach (var o in Items.Where(o => o != it && o.id == it.id && o.condition > 0f && o.condition < 1f).OrderByDescending(o => o.condition))
            {
                float m = Mathf.Min(1f - o.condition, it.condition);
                o.condition += m; it.condition -= m;
                if (it.condition <= 0.0001f)
                {
                    it.gameObject.SetActive(false); it.transform.SetParent(holder, false);   // 가방 밖으로 빼고(목록에서 빠짐) 지운다
                    UnityEngine.Object.Destroy(it.gameObject);
                    return;
                }
            }
        }

        /// <summary>
        /// 가방 창 묶음(사용자 11-22: 물건이 많아 누르기 힘듦 → 같은 물건은 단추 하나, 액체류만 내용물로 구별). 무게 제한은 그대로이고 겹치기는 보이는 것만:
        /// 물건은 여전히 하나씩 들어 있고, 단추를 끌면 게임대로 그중 하나가 나온다.
        /// 게임 PlayerCamera.RepopulateContainer는 물건마다 단추(3열, 66px) → 아공간이면 묶어서 다시 만들고 오른쪽 아래에 "x개수"(게임 containerText 복제, ×는 글꼴에 없어 x).
        /// </summary>
        internal static string StackKey(Item it)
        {
            if (!it.TryGetComponent<WaterContainerItem>(out var w) || w.stack == null || w.stack.Count == 0) return it.id;
            return it.id + "|" + string.Join(",", w.stack.Where(l => l.amount >= 0.5f).OrderBy(l => l.liquidId).Select(l => l.liquidId + ":" + Mathf.RoundToInt(l.amount)));
        }

        private static List<List<Item>> groupCache; private static float groupTime; private static int groupCount = -1;
        /// <summary>묶음(넣은 순서). 화면 표시는 0.5초 캐시, 가방 창은 fresh=true.</summary>
        internal static List<List<Item>> Groups(bool fresh = false)
        {
            var items = Items;
            if (!fresh && groupCache != null && groupCount == items.Count && Time.unscaledTime < groupTime) return groupCache;
            var order = Order();
            groupCache = items.GroupBy(StackKey).Select(g => g.ToList())
                .OrderBy(g => { int i = order.IndexOf(g[0].id); return i < 0 ? int.MaxValue : i; }).ToList();   // 사용자가 정한 순서 먼저, 나머지는 넣은 순서
            groupCount = items.Count; groupTime = Time.unscaledTime + 0.5f;
            return groupCache;
        }

        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.RepopulateContainer))]
        [HarmonyPostfix]
        private static void RepopulatePostfix(PlayerCamera __instance)
        {
            if (Bag == null || __instance.currentContainer != Bag) return;
            try
            {
                __instance.ClearContainer();
                var all = Groups(true);
                ScrollRow = ClampRow(ScrollRow, all.Count);
                int num = 0;
                foreach (var g in all.Skip(ScrollRow * 3).Take(Rows * 3))
                {
                    var go = UnityEngine.Object.Instantiate(Resources.Load("ContainerItem"), __instance.contPopulate.transform) as GameObject;
                    go.GetComponent<RectTransform>().anchoredPosition = new Vector2(34.5f + (num % 3) * 66f, -34.5f - (num / 3) * 66f);
                    var b = go.GetComponent<InvButton>();
                    b.refItem = g[0]; b.UpdateRefs(); b.UpdateGraphic();
                    if (g.Count > 1 && __instance.containerText != null)
                    {
                        var t = UnityEngine.Object.Instantiate(__instance.containerText.gameObject, go.transform).GetComponent<TMPro.TextMeshProUGUI>();
                        var rt = t.rectTransform;
                        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
                        rt.anchoredPosition = new Vector2(-3f, 2f); rt.sizeDelta = new Vector2(60f, 20f);
                        t.alignment = TMPro.TextAlignmentOptions.BottomRight; t.fontSize = 16f; t.enableAutoSizing = false; t.raycastTarget = false;
                        t.text = "x" + g.Count; t.name = "CarryoverStackCount";
                    }
                    __instance.containerUnloadButtons.Add(go);
                    num++;
                }
            }
            catch (Exception e) { Plugin.Log.LogError("subspace panel: " + e); }
        }

        /// <summary>가방 창에 한 번에 보이는 줄 수(게임 창은 12줄, 3칸씩). 넘치면 마우스 휠로 한 줄씩 넘긴다(사용자 11-22: 밑에 것이 안 보임).</summary>
        internal const int Rows = 12;
        internal static int ScrollRow;
        internal static int TotalRows(int groups) => (groups + 2) / 3;
        internal static int ClampRow(int row, int groups) => Math.Max(0, Math.Min(row, TotalRows(groups) - Rows));

        /// <summary>휠 한 칸 = 한 줄. 물건을 끄는 중에는 창을 다시 그리지 않는다(끄는 중 창이 바뀌면 놓는 곳이 어긋남).</summary>
        internal static void Scroll(int delta)
        {
            var pc = PlayerCamera.main;
            if (Bag == null || pc == null || pc.currentContainer != Bag || pc.dragItem != null) return;
            int row = ClampRow(ScrollRow + delta, Groups().Count);
            if (row == ScrollRow) return;
            ScrollRow = row;
            pc.RepopulateContainer();
        }

        // 순서 바꾸기(사용자 11-23: 휠 클릭 말고 끌어서). 가방 창에서 물건을 끌어 다른 물건 위에 놓으면 그 앞으로 간다. 물건 id 단위, 회차가 바뀌어도 남는다.
        // 11-22에 휠 클릭 "위로 고정"을 넣었다가 끄는 중 휠 클릭하면 물건이 밖으로 떨어져 뺐다.
        private static List<string> orderCache;
        internal static string OrderPath => FilePath == null ? null : Path.ChangeExtension(FilePath, ".order.txt");
        internal static List<string> Order()
        {
            if (orderCache != null) return orderCache;
            orderCache = new List<string>();
            try { if (OrderPath != null && File.Exists(OrderPath)) orderCache.AddRange(File.ReadAllLines(OrderPath).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct()); }
            catch (Exception e) { Plugin.Log.LogWarning("subspace order read: " + e.Message); }
            return orderCache;
        }

        /// <summary>id 묶음을 target 묶음 바로 앞으로 옮긴다(지금 보이는 순서를 기준으로 적어 둔다).</summary>
        internal static bool MoveBefore(string id, string target)
        {
            if (id == null || target == null || id == target) return false;
            var ids = Groups(true).Select(g => g[0].id).Distinct().ToList();
            if (!ids.Contains(id) || !ids.Contains(target)) return false;
            ids.Remove(id);
            ids.Insert(ids.IndexOf(target), id);
            var order = Order();
            foreach (var old in order) if (!ids.Contains(old)) ids.Add(old);   // 지금 없는 물건의 자리도 기억
            order.Clear(); order.AddRange(ids);
            try { if (OrderPath != null) File.WriteAllLines(OrderPath, order); } catch (Exception e) { Plugin.Log.LogWarning("subspace order save: " + e.Message); }
            groupCache = null;
            return true;
        }

        /// <summary>가방 창 안에서 끌어 다른 종류 물건 위에 놓기 → 순서만 바꾼다. 같은 종류(합치기 등)·가방 밖에서 끌어온 것은 게임 그대로.
        /// 게임 근거: 놓을 때 HandleDragging → TryPerformUIActions → TryPerformInventoryAction(hit, casts), true면 RepopulateContainer.</summary>
        [HarmonyPatch(typeof(PlayerCamera), "TryPerformInventoryAction")]
        [HarmonyPrefix]
        private static bool ReorderPrefix(PlayerCamera __instance, RaycastResult hit, ref bool __result)
        {
            try
            {
                if (Bag == null || __instance.currentContainer != Bag || __instance.dragItem == null || __instance.dragItem.transform.parent != Bag.transform || hit.gameObject == null) return true;
                if (!__instance.containerUnloadButtons.Contains(hit.gameObject)) return true;
                var target = hit.gameObject.GetComponent<InvButton>()?.refItem;
                if (target == null || target.id == __instance.dragItem.id) return true;
                MoveBefore(__instance.dragItem.id, target.id);
                __result = true;
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("subspace reorder: " + e); return true; }
        }

        /// <summary>바깥 물건을 가방 창 어디에 놓아도(빈 곳·물건 칸 위·창 아래쪽) 아공간에 넣는다(사용자 11-23: 물건이 많아 창이 차니 놓아도 안 들어가고 물건 칸 위에 놓아야만 들어감).
        /// 게임은 놓은 자리에 ContainerBack 배경이 맞아야 넣는데, 칸이 꽉 차면 배경이 가려진다. 게임이 아무것도 못 했을 때(TryPerformUIActions false)만 끼어든다.</summary>
        [HarmonyPatch(typeof(PlayerCamera), "TryPerformUIActions")]
        [HarmonyPostfix]
        private static void DropPostfix(PlayerCamera __instance, ref bool __result)
        {
            if (__result) return;
            try { if (TryDropInto(__instance, Input.mousePosition)) __result = true; }
            catch (Exception e) { Plugin.Log.LogError("subspace drop: " + e); }
        }

        /// <summary>screen 자리가 아공간 가방 창 안이면 끌던 물건을 넣는다. 넣었으면 true(무게 초과면 알림만 뜨고 false).</summary>
        internal static bool TryDropInto(PlayerCamera pc, Vector2 screen)
        {
            var it = pc.dragItem;
            if (Bag == null || pc.currentContainer != Bag || it == null || it.transform.parent == Bag.transform || it.GetComponent<Container>() != null) return false;
            if (!OverPanel(pc, screen)) return false;
            if (pc.body != null && it.Stats.wearable && (UnityEngine.Object)pc.body.GetWearable(it.id) == it) return false;   // 입은 옷은 게임 그대로
            if (it.transform.parent != null && it.transform.parent.TryGetComponent<Container>(out var from)) from.UnloadItem(it);
            var body = pc.body;
            if (body != null && body.HoldingItem(it)) body.DropItem(it);
            it.transform.position = Bag.transform.position;
            Bag.LoadItem(it);
            return it.transform.parent == Bag.transform;
        }

        internal static bool OverPanel(PlayerCamera pc, Vector2 screen)
        {
            var rects = new List<RectTransform>();
            var pop = pc.contPopulate != null ? pc.contPopulate.GetComponent<RectTransform>() : null;
            if (pop != null) { rects.Add(pop); if (pop.parent is RectTransform par) rects.Add(par); }
            foreach (var go in pc.containerUnloadButtons) if (go != null && go.TryGetComponent<RectTransform>(out var r)) rects.Add(r);
            foreach (var r in rects)
            {
                var canvas = r.GetComponentInParent<Canvas>();
                var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (RectTransformUtility.RectangleContainsScreenPoint(r, screen, cam)) return true;
            }
            return false;
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

        /// <summary>어디에 있든 썩지 않는 물건(사용자 11-22: 나뭇잎·로프 유통기한 무한). id는 게임 Lang/ko.json으로 확인.</summary>
        internal static readonly HashSet<string> NoDecayIds = new HashSet<string> { "foliage", "redfoliage", "dryfoliage", "rope", "climbingrope" };

        /// <summary>
        /// 아공간 안은 시간이 멈춘다(사용자 11-21: 로프·잎·음식 유통기한). 게임 Item.Update → HandleDecay가 매 프레임
        /// condition -= rotSpeed × decayMultiplier × (젖음 6배) × globalDecayRate × dt (배터리 물건은 충전량을 깎음) → 아공간 가방 바로 안 물건이면 건너뛴다.
        /// </summary>
        [HarmonyPatch(typeof(Item), nameof(Item.HandleDecay))]
        [HarmonyPrefix]
        private static bool DecayPrefix(Item __instance) => !NoDecayIds.Contains(__instance.id) && (Bag == null || __instance.transform.parent != Bag.transform);

        // ---- 저장하고 나가기 / 이어하기 ----
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

        internal static string CompsJson(Item it)
        {
            var comps = new Dictionary<Type, Component>();
            foreach (var c in it.gameObject.GetComponents<Component>())
                if (c != null && Attribute.GetCustomAttributes(c.GetType()).Any(a => a is Saveable)) comps[c.GetType()] = c;
            return JsonConvert.SerializeObject(comps, Formatting.None, Settings);
        }


        internal static void SaveToFile()
        {
            if (Bag == null) return;   // 가방이 없을 때(죽은 뒤 등) 저장이 유지 파일을 지우지 않게
            var items = Items;
            if (items.Count == 0) { try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { } return; }
            var arr = new JArray();
            foreach (var it in items)
            {
                arr.Add(new JObject
                {
                    ["id"] = it.id, ["condition"] = it.condition, ["favourited"] = it.favourited,
                    ["label"] = it.label, ["swatch"] = it.colorSwatch,
                    ["components"] = JObject.Parse(CompsJson(it)),
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
