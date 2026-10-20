using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>플레이어 건축물 보호: NPC의 도구질·공격은 건축물에 피해를 주지 않는다. NPC는 건축물 근처를 캐지 않는다.</summary>
    public static class Buildings
    {
        static readonly Dictionary<int, bool> s_isPiece = new Dictionary<int, bool>();

        public static bool IsPiece(int prefabHash)
        {
            if (s_isPiece.TryGetValue(prefabHash, out bool v)) return v;
            GameObject p = ZNetScene.instance.GetPrefab(prefabHash);
            v = p != null && p.GetComponent<WearNTear>() != null && p.GetComponent<Piece>() != null;
            s_isPiece[prefabHash] = v;
            return v;
        }

        /// <summary>pos에서 range 안에 건축물이 있는지(한 곳만 볼 때. 여러 곳은 GatherJob처럼 목록을 한 번 만들어 쓴다).</summary>
        public static bool Near(Vector3 pos, float range)
        {
            foreach (var zdo in Senses.Nearby(pos, range))
                if (IsPiece(zdo.GetPrefab())) return true;
            return false;
        }

        // 바닐라 Attack은 비플레이어 공격이 건축물(상자 포함)을 때리게 둔다. 일꾼 공격만 막는다.
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
        static class WearNTearDamage
        {
            static bool Prefix(HitData hit) => !(hit.GetAttacker() is WorkerNpc);
        }
    }
}
