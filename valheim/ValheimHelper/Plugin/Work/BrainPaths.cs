using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public static partial class Brain
    {
        // ---- 길 기억 다듬기(요청 11-15 ③): 건축 바뀐 근처 지움·오래된 것 지움 ----
        /// <summary>조각 놓기·부서짐 자리에서 이 가로 반경 안을 지나는 길을 지움(벽 2m·바닥 2×2 조각 끝까지 덮게).</summary>
        public const float PathForgetRadius = 3f;
        /// <summary>[기본안·확인 필요] 이만큼(월드 시간 초, 게임 10일) 안 쓴 길은 저장 때 지움.</summary>
        public const double PathMaxAge = 10 * 1800;
        /// <summary>시험용: 건축 바뀜으로 지운 길 수.</summary>
        public static int PathsForgotBuild;
        static readonly Dictionary<int, bool> s_buildPrefab = new Dictionary<int, bool>();

        static void ForgetBuild(Vector3 at, string why)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ForgetDrops(at);
            if (s_paths == null || s_paths.Count == 0) return; // 안 불러왔으면 지울 것도 없음
            int n = s_paths.ForgetNear(new PathMemory.P(at.x, at.y, at.z), PathForgetRadius);
            if (n == 0) return;
            PathsForgotBuild += n;
            Diag.Log($"길 기억 지움({why}): {at:F1} 곁 {n}개, 남은 {s_paths.Count}");
        }

        static bool IsBuildPrefab(int hash)
        {
            if (s_buildPrefab.TryGetValue(hash, out bool b)) return b;
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            return s_buildPrefab[hash] = go != null && go.GetComponent<WearNTear>() != null;
        }

        /// <summary>부서짐·철거: 주인이 누구든 ZDO 지움은 서버의 HandleDestroyedZDO를 거침(DestroyZDO → 라우티드 RPC 모두에게).</summary>
        [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
        static class ForgetOnDestroy
        {
            static void Prefix(ZDOMan __instance, ZDOID uid)
            {
                if ((s_paths == null || s_paths.Count == 0) && s_drops == null) return;
                var zdo = __instance.GetZDO(uid);
                if (zdo != null && IsBuildPrefab(zdo.GetPrefab())) ForgetBuild(zdo.GetPosition(), "부서짐");
            }
        }

        /// <summary>놓기: 바닐라 PlacePiece가 놓은 쪽에서 SetCreator. 손님이 놓은 조각은 서버가 못 봄 — 기억한 길이 막히면 Forget·바닐라 길로 돌아가는 쪽이 받침.</summary>
        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        static class ForgetOnPlace
        {
            static void Postfix(Piece __instance) { if (__instance.GetComponent<WearNTear>() != null) ForgetBuild(__instance.transform.position, "놓기"); }
        }
    }
}
