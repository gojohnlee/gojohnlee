using System;
using System.Collections.Generic;

namespace ValheimHelper
{
    /// <summary>채팅의 물건 이름 → 게임 프리팹 이름. 프리팹 이름과 지금 게임 언어의 표시 이름을 받는다.</summary>
    public static class Items
    {
        static Dictionary<string, string> s_map;
        static int s_count = -1;

        public static string Resolve(string name)
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || string.IsNullOrWhiteSpace(name)) return null;
            if (s_map == null || s_count != db.m_items.Count) Build(db);
            return s_map.TryGetValue(Key(name), out var prefab) ? prefab : null;
        }

        static string Key(string s) => s.Replace(" ", "").ToLowerInvariant();

        static void Build(ObjectDB db)
        {
            s_map = new Dictionary<string, string>();
            s_count = db.m_items.Count;
            foreach (var go in db.m_items)
            {
                if (go == null) continue;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                if (drop == null) continue;
                s_map[Key(go.name)] = go.name;
                string shown = Localization.instance?.Localize(drop.m_itemData.m_shared.m_name);
                if (!string.IsNullOrWhiteSpace(shown) && !s_map.ContainsKey(Key(shown))) s_map[Key(shown)] = go.name;
            }
        }
    }
}
