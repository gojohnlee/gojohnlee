using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 사냥감 목록(⑧): 게임 데이터(CharacterDrop)에서 "어떤 생물이 그 물건을 떨어뜨리나"를 읽는다.
    /// 쫓아가 칠 수 있는 것만(MonsterAI: 멧돼지·늑대·그레이드워프·해골 …). 사슴·토끼처럼 도망만 하는 것(AnimalAI)은 활이 필요해서 뺀다(다음 조각).
    /// 보스·플레이어·길들인 것은 뺀다. 위험한지는 잡으러 갈 때 Guard.SafePrey가 한 마리씩 판단한다.
    /// </summary>
    public static class Prey
    {
        static Dictionary<string, HashSet<string>> s_kinds;

        public static void Reset() => s_kinds = null;

        public static bool Any(string item)
        {
            if (s_kinds == null) Build();
            return s_kinds.ContainsKey(item);
        }

        public static bool Is(string item, Character c)
        {
            if (s_kinds == null) Build();
            return c != null && s_kinds.TryGetValue(item, out var kinds) && kinds.Contains(Utils.GetPrefabName(c.gameObject));
        }

        static void Build()
        {
            s_kinds = new Dictionary<string, HashSet<string>>();
            if (ZNetScene.instance == null) { s_kinds = null; return; }
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                if (p == null) continue;
                var ch = p.GetComponent<Character>();
                var drops = p.GetComponent<CharacterDrop>();
                if (ch == null || drops == null || ch is Player || ch.m_boss || ch.m_faction == Character.Faction.Players) continue;
                if (p.GetComponent<MonsterAI>() == null) continue;
                foreach (var d in drops.m_drops)
                {
                    if (d.m_prefab == null) continue;
                    if (!s_kinds.TryGetValue(d.m_prefab.name, out var set)) s_kinds[d.m_prefab.name] = set = new HashSet<string>();
                    set.Add(p.name);
                }
            }
            Plugin.Log.LogInfo($"사냥감 목록: 물건 {s_kinds.Count}종");
        }
    }
}
