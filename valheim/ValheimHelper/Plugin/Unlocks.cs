using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 플레이어 진행으로 풀리는 일(사용자 결정 2026-10-22): 플레이어가 그 물건(보스 전리품 — 늪 열쇠, 다음엔 위시본 …)을 얻었으면 일꾼도 그 일을 한다.
    /// 얻음 = 호스트 플레이어가 그 물건을 한 번이라도 주웠다(바닐라 알려진 재료 Player.IsMaterialKnown), 또는 그 물건을 떨구는 보스가
    /// 이 월드에서 잡혔다(보스 m_defeatSetGlobalKey 월드 키 — 친구가 잡고 주운 경우). 보스는 게임 데이터(보스 CharacterDrop)에서 찾는다.
    /// 일꾼은 열쇠를 들고 다니지 않는다(플레이어 것은 그대로).
    /// </summary>
    public static class Unlocks
    {
        static Dictionary<string, string> s_bossKeys, s_bossNames;

        /// <summary>보스 월드 키 → 보스 이름(현지화). 잡담 "보스 쓰러짐" 반응용.</summary>
        public static Dictionary<string, string> BossNames()
        {
            if (s_bossNames != null || ZNetScene.instance == null) return s_bossNames;
            var map = new Dictionary<string, string>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                var boss = prefab != null ? prefab.GetComponent<Character>() : null;
                if (boss == null || !boss.m_boss || string.IsNullOrEmpty(boss.m_defeatSetGlobalKey) || map.ContainsKey(boss.m_defeatSetGlobalKey)) continue;
                map[boss.m_defeatSetGlobalKey] = Localization.instance != null ? Localization.instance.Localize(boss.m_name) : boss.m_name;
            }
            if (map.Count > 0) s_bossNames = map;
            return map;
        }

        public static bool Have(ItemDrop item)
        {
            if (item == null) return true;
            Player p = Player.m_localPlayer;
            if (p != null && p.IsMaterialKnown(item.m_itemData.m_shared.m_name)) return true;
            string key = BossKey(Utils.GetPrefabName(item.gameObject));
            return key != null && ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
        }

        /// <summary>그 물건(프리팹 이름)을 떨구는 보스가 잡히면 켜지는 월드 키. 없으면 null.</summary>
        public static string BossKey(string itemPrefab)
        {
            var map = BossKeys();
            return map != null && map.TryGetValue(itemPrefab, out string key) ? key : null;
        }

        static Dictionary<string, string> BossKeys()
        {
            if (s_bossKeys != null || ZNetScene.instance == null) return s_bossKeys;
            var map = new Dictionary<string, string>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                var boss = prefab.GetComponent<Character>();
                if (boss == null || !boss.m_boss || string.IsNullOrEmpty(boss.m_defeatSetGlobalKey)) continue;
                var drops = prefab.GetComponent<CharacterDrop>();
                if (drops == null) continue;
                foreach (var d in drops.m_drops)
                    if (d.m_prefab != null && !map.ContainsKey(d.m_prefab.name)) map[d.m_prefab.name] = boss.m_defeatSetGlobalKey;
            }
            if (map.Count > 0) s_bossKeys = map;
            return map;
        }
    }
}
