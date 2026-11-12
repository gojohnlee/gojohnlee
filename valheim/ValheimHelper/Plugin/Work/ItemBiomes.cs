using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// 물건 → 나는 바이옴(비트 합, 사용자 11-13 "못 찾을 때 그 재료가 나는 바이옴은 알고 가기").
    /// 게임 생성 표에서 한 번 읽는다: 식생·광맥(ZoneSystem.m_vegetation의 m_biome, 자식 Pickable 포함)과
    /// 생물(SpawnSystem.m_spawnLists의 m_biome + CharacterDrop). 정확한 자리는 모른다(바이옴만 앎).
    /// </summary>
    public static class ItemBiomes
    {
        static Dictionary<string, int> s_mask;

        public static void Reset() => s_mask = null;

        /// <summary>그 물건이 나는 바이옴(비트 합). 모르면 0.</summary>
        public static int Of(string item)
        {
            if (s_mask == null) Build();
            return s_mask != null && s_mask.TryGetValue(item, out int m) ? m : 0;
        }

        static void Add(string item, int biome)
        {
            if (string.IsNullOrEmpty(item) || biome == 0) return;
            s_mask.TryGetValue(item, out int m);
            s_mask[item] = m | biome;
        }

        static void Build()
        {
            if (ZoneSystem.instance == null || ZNetScene.instance == null) return;
            s_mask = new Dictionary<string, int>();
            foreach (var v in ZoneSystem.instance.m_vegetation)
            {
                if (!v.m_enable || v.m_prefab == null) continue;
                int b = (int)v.m_biome;
                ResourceInfo info = ResourceCatalog.Get(v.m_prefab.name.GetStableHashCode());
                if (info != null) foreach (string it in info.Produces) Add(it, b);
                foreach (var p in v.m_prefab.GetComponentsInChildren<Pickable>(true))
                    if (p.m_itemPrefab != null) Add(p.m_itemPrefab.name, b);
            }
            SpawnSystem ss = ZoneSystem.instance.m_zoneCtrlPrefab != null ? ZoneSystem.instance.m_zoneCtrlPrefab.GetComponentInChildren<SpawnSystem>(true) : null;
            if (ss != null)
                foreach (SpawnSystemList list in ss.m_spawnLists)
                    foreach (SpawnSystem.SpawnData d in list.m_spawners)
                    {
                        if (!d.m_enabled || d.m_prefab == null) continue;
                        var cd = d.m_prefab.GetComponent<CharacterDrop>();
                        if (cd == null) continue;
                        foreach (var dr in cd.m_drops) if (dr.m_prefab != null) Add(dr.m_prefab.name, (int)d.m_biome);
                    }
            Plugin.Log.LogInfo($"물건 바이옴 표: {s_mask.Count}종(구리 {BiomeNames.Of(Of0("CopperOre"))}, 창자 {BiomeNames.Of(Of0("Entrails"))})");
        }

        static int Of0(string item) => s_mask.TryGetValue(item, out int m) ? m : 0;
    }
}
