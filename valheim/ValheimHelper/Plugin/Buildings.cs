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
        // 일꾼이 쓰러뜨리거나 친 통나무가 굴러 부딪힌 피해(ImpactEffect, HitType.Tree·공격자 없음)도 막는다(E083 회차 474: 벽 곁 통나무 반토막에 벌통 셋 체력 22·81·86).
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
        static class WearNTearDamage
        {
            static bool Prefix(WearNTear __instance, HitData hit) =>
                !(hit.GetAttacker() is WorkerNpc) && !(hit.m_hitType == HitData.HitType.Tree && hit.GetAttacker() == null && NearWorkerLog(__instance.transform.position));
        }

        /// <summary>일꾼이 쓰러뜨리거나 친 통나무 자리(시각 함께) — 15m·120초 안이면 그 통나무 충돌은 집 조각을 안 다치게.</summary>
        static readonly List<Vector3> s_logAt = new List<Vector3>();
        static readonly List<float> s_logT = new List<float>();
        public static void NoteWorkerLog(Vector3 p)
        {
            s_logAt.Add(p); s_logT.Add(Time.time);
            if (s_logAt.Count > 64) { s_logAt.RemoveAt(0); s_logT.RemoveAt(0); }
        }
        public static bool NearWorkerLog(Vector3 p, float range = 15f)
        {
            for (int i = s_logAt.Count - 1; i >= 0; i--)
                if (Time.time - s_logT[i] < 120f && Utils.DistanceXZ(s_logAt[i], p) < range) return true;
            return false;
        }
    }
}
