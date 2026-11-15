using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 작은 일들(꿀·밭·낚시·가축·솎기·길들이기)이 같이 쓰는 토막(리팩터링 4번, 사용자 11-11 — 동작은 옮기기 전과 같음).
    /// </summary>
    internal static class JobKit
    {
        /// <summary>그 일의 Home 단계 한 프레임 — 걸어서 닿았거나 단계 시간이 limit을 넘어 깃발 순간이동했으면 true.
        /// 걷는 중이면 false, 순간이동도 못 하면 그 자리에서 쉬고 false(귀환 통일 — 요청 코드 정리 나: WorkerAI.TeleportHome 도착 확인, 안 되면 RestHere).</summary>
        internal static bool ArriveHome(WorkerNpc npc, float dt, float stepTime, float limit = 180f)
        {
            WorkerAI ai = npc.Ai;
            Vector3 home = npc.Home;
            if (ai.MoveToPoint(dt, home, 2.5f, true) || Utils.DistanceXZ(home, npc.transform.position) < 3f) return true;
            if (stepTime <= limit) return false;
            if (ai.TeleportHome()) return true;
            ai.RestHere(npc);
            return false;
        }

        /// <summary>at 둘레 r 안 땅의 떨굼(ItemDrop, ZNetView 유효)과 프리팹 이름.</summary>
        internal static IEnumerable<(ItemDrop drop, string name)> DropsNear(Vector3 at, float r)
        {
            foreach (Collider col in Physics.OverlapSphere(at, r, LayerMask.GetMask("item")))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d == null || !d.TryGetComponent(out ZNetView v) || !v.IsValid()) continue;
                yield return (d, Utils.GetPrefabName(d.gameObject));
            }
        }
    }
}
