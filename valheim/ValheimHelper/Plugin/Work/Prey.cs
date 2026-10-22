using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 사냥감 목록(⑧): 게임 데이터(CharacterDrop)에서 "어떤 생물이 그 물건을 떨어뜨리나"를 읽는다.
    /// 쫓아가 칠 수 있는 것(MonsterAI: 멧돼지·늑대·그레이드워프·해골 …)과, 도망만 하는 것(AnimalAI: 사슴·토끼 — 활과 화살이 있어야 잡음, NeedsBow).
    /// 날아다니는 것(갈매기 등)·보스·플레이어·길들인 것은 뺀다. 위험한지는 잡으러 갈 때 Guard.SafePrey가 한 마리씩 판단한다.
    /// </summary>
    public static class Prey
    {
        static Dictionary<string, HashSet<string>> s_kinds;
        /// <summary>활로만 잡는 사냥감 프리팹(AnimalAI).</summary>
        static HashSet<string> s_bowOnly;

        public static void Reset() => s_kinds = null;

        /// <summary>그 물건을 떨어뜨리는 사냥감이 있는지. canShoot = false면 활로만 잡는 것은 안 친다.</summary>
        public static bool Any(string item, bool canShoot = true)
        {
            if (s_kinds == null) Build();
            if (s_kinds == null || !s_kinds.TryGetValue(item, out var kinds)) return false;
            if (canShoot) return true;
            foreach (var k in kinds) if (!s_bowOnly.Contains(k)) return true;
            return false;
        }

        /// <summary>그 생물이 그 물건의 사냥감인지. canShoot = false면 활로만 잡는 것은 아니라고 본다.</summary>
        public static bool Is(string item, Character c, bool canShoot = false)
        {
            if (s_kinds == null) Build();
            if (c == null || s_kinds == null || !s_kinds.TryGetValue(item, out var kinds)) return false;
            string name = Utils.GetPrefabName(c.gameObject);
            return kinds.Contains(name) && (canShoot || !s_bowOnly.Contains(name));
        }

        /// <summary>활로만 잡는 사냥감(도망만 하는 짐승)인지.</summary>
        public static bool NeedsBow(Character c)
        {
            if (s_kinds == null) Build();
            return c != null && s_bowOnly != null && s_bowOnly.Contains(Utils.GetPrefabName(c.gameObject));
        }

        static void Build()
        {
            s_kinds = new Dictionary<string, HashSet<string>>();
            s_bowOnly = new HashSet<string>();
            if (ZNetScene.instance == null) { s_kinds = null; return; }
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                if (p == null) continue;
                var ch = p.GetComponent<Character>();
                var drops = p.GetComponent<CharacterDrop>();
                if (ch == null || drops == null || ch is Player || ch.m_boss || ch.m_flying || ch.m_faction == Character.Faction.Players) continue;
                bool melee = p.GetComponent<MonsterAI>() != null;
                if (!melee && p.GetComponent<AnimalAI>() == null) continue;
                if (!melee) s_bowOnly.Add(p.name);
                foreach (var d in drops.m_drops)
                {
                    if (d.m_prefab == null) continue;
                    if (!s_kinds.TryGetValue(d.m_prefab.name, out var set)) s_kinds[d.m_prefab.name] = set = new HashSet<string>();
                    set.Add(p.name);
                }
            }
            Plugin.Log.LogInfo($"사냥감 목록: 물건 {s_kinds.Count}종, 활로만 잡는 것 {s_bowOnly.Count}종");
        }
    }
}
