using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 게임 데이터 → 제작 계획용 RecipeBook (설계 C-1). 월드를 불러온 뒤 처음 쓸 때 한 번 만든다.
    /// 제작법은 ObjectDB.m_recipes 전부(사용자 확정: NPC가 다 안다), 가공은 프리팹의 Smelter(제련소·숯가마·용광로·물레·풍차·정제기)·
    /// Fermenter·CookingStation, 채집 출처는 ResourceCatalog(캐거나 줍는 것)와 생물 드롭표(사냥, ⑧).
    /// [게임 시험 안 됨 — 2026-10-21 클라우드 세션]
    /// </summary>
    public static class Recipes
    {
        public const float BaseRange = 30f;
        /// <summary>설비 찾기 층: 발효기는 piece 층이 아닌 충돌체만 있을 수 있어(회차 57 시험에서 못 찾음) 넓게 본다.</summary>
        public static int MachineMask => LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "Default_small");
        static RecipeBook s_book;
        static ObjectDB s_for;
        /// <summary>설비 키(가공 설비는 프리팹 이름, 제작대는 m_name) → 보여 줄 이름.</summary>
        static readonly Dictionary<string, string> s_names = new Dictionary<string, string>();

        public static RecipeBook Book()
        {
            if (s_book != null && s_for == ObjectDB.instance) return s_book;
            s_for = ObjectDB.instance;
            s_names.Clear();
            var b = new RecipeBook();
            foreach (Recipe r in ObjectDB.instance.m_recipes)
            {
                if (r == null || !r.m_enabled || r.m_item == null || r.m_resources == null || r.m_resources.Length == 0) continue;
                var cr = new Core.Work.Recipe
                {
                    Output = Utils.GetPrefabName(r.m_item.gameObject),
                    OutputAmount = Mathf.Max(1, r.m_amount),
                    Station = r.m_craftingStation != null ? r.m_craftingStation.m_name : null,
                    StationLevel = Mathf.Max(1, r.m_minStationLevel),
                    OnlyOne = r.m_requireOnlyOneIngredient,
                };
                foreach (var req in r.m_resources)
                    if (req.m_resItem != null && req.m_amount > 0) cr.Inputs.Add(new Ingredient(Utils.GetPrefabName(req.m_resItem.gameObject), req.m_amount));
                if (cr.Inputs.Count == 0) continue;
                if (cr.Station != null) s_names[cr.Station] = Localization.instance.Localize(cr.Station);
                b.Add(cr);
            }
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                string key = prefab.name;
                if (prefab.TryGetComponent(out Smelter sm))
                {
                    s_names[key] = Localization.instance.Localize(sm.m_name);
                    foreach (var c in sm.m_conversion)
                        if (c.m_from != null && c.m_to != null)
                            b.Add(new Process
                            {
                                Station = key, Input = Utils.GetPrefabName(c.m_from.gameObject), Output = Utils.GetPrefabName(c.m_to.gameObject),
                                Fuel = sm.m_fuelItem != null ? Utils.GetPrefabName(sm.m_fuelItem.gameObject) : null,
                                FuelPerProduct = sm.m_fuelItem != null ? sm.m_fuelPerProduct : 0,
                                SecondsPerProduct = sm.m_secPerProduct, Capacity = sm.m_maxOre,
                            });
                }
                if (prefab.TryGetComponent(out Fermenter fe))
                {
                    s_names[key] = Localization.instance.Localize(fe.m_name);
                    foreach (var c in fe.m_conversion)
                        if (c.m_from != null && c.m_to != null)
                            b.Add(new Process
                            {
                                Station = key, Input = Utils.GetPrefabName(c.m_from.gameObject), Output = Utils.GetPrefabName(c.m_to.gameObject),
                                OutputAmount = Mathf.Max(1, c.m_producedItems), SecondsPerProduct = fe.m_fermentationDuration, Capacity = 1,
                            });
                }
                if (prefab.TryGetComponent(out CookingStation cs))
                {
                    s_names[key] = Localization.instance.Localize(cs.m_name);
                    foreach (var c in cs.m_conversion)
                        if (c.m_from != null && c.m_to != null)
                            b.Add(new Process
                            {
                                Station = key, Input = Utils.GetPrefabName(c.m_from.gameObject), Output = Utils.GetPrefabName(c.m_to.gameObject),
                                SecondsPerProduct = c.m_cookTime, Capacity = cs.m_slots != null ? Mathf.Max(1, cs.m_slots.Length) : 1,
                                Fuel = cs.m_fuelItem != null ? Utils.GetPrefabName(cs.m_fuelItem.gameObject) : null,
                                FuelPerProduct = 0, // 화덕 연료는 시간당이라 셈하지 않는다(⑧에서)
                                Attended = true, // 두 배 시간이면 타므로 곁에서 지켜 꺼낸다(MachineStep)
                            });
                }
                if (prefab.TryGetComponent(out CharacterDrop drops))
                    foreach (var d in drops.m_drops)
                        if (d.m_prefab != null && b.SourceOf(d.m_prefab.name) == SourceKind.None) b.SetSource(d.m_prefab.name, SourceKind.Hunt);
            }
            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (item == null) continue;
                if (ResourceCatalog.Gatherable(item.name)) b.SetSource(item.name, SourceKind.Gather); // 캐거나 줍기가 사냥보다 먼저
                // 밭에서만 나는 것(당근 — 들에 없음)은 농사(⑨ 4d, Departures.Farming과 같은 규칙)
                if (Crops.ForProduce(item.name) != null && !ResourceCatalog.AnySourceBut(item.name, Crops.IsGrown)) b.SetSource(item.name, SourceKind.Farm);
            }
            s_book = b;
            Plugin.Log.LogInfo($"제작 계획: 제작법·가공표·출처 준비됨");
            return s_book;
        }

        public static string StationName(string key)
        {
            int colon = key.LastIndexOf(':');
            string k = colon > 0 ? key.Substring(0, colon) : key;
            string name = s_names.TryGetValue(k, out string n) ? n : k;
            return colon > 0 ? name + " " + key.Substring(colon + 1) + "단계" : name;
        }

        /// <summary>거점 설비: 제작대(확장 포함 레벨)와 가공 설비 대수(거점 30m 안).</summary>
        public static Facilities At(Vector3 home)
        {
            Book(); // 제작대 이름 목록
            var f = new Facilities();
            var stationNames = new HashSet<string>();
            foreach (var kv in s_names) if (kv.Key.StartsWith("$")) stationNames.Add(kv.Key);
            foreach (string name in stationNames)
            {
                CraftingStation cs = CraftingStation.FindClosestStationInRange(name, home, BaseRange);
                if (cs != null) f.Stations[name] = cs.GetLevel();
            }
            var seen = new HashSet<GameObject>();
            foreach (Collider col in Physics.OverlapSphere(home, BaseRange, MachineMask))
            {
                Component c = (Component)col.GetComponentInParent<Smelter>() ?? (Component)col.GetComponentInParent<Fermenter>() ?? col.GetComponentInParent<CookingStation>();
                if (c == null || !seen.Add(c.gameObject)) continue;
                string key = Utils.GetPrefabName(c.gameObject);
                f.Processors.TryGetValue(key, out int n);
                f.Processors[key] = n + 1;
            }
            return f;
        }
    }
}
