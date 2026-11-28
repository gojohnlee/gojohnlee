using UnityEngine;

namespace CasualtiesCarryover
{
    /// <summary>
    /// 시야 적응력(사용자 11-28: 튼튼한 다리 대신). 게임 회차 설정 두 가지를 퍽으로 준다:
    /// ① 주변광(ambientlight) 단계별 바닥, 4단계 = 낙원 프리셋 「밝음」 0.4. ② 시야 가림(VisionMask) 반경 안 걷기, 4단계는 아예 없앰.
    /// 머리에 붙이던 빛(Light2D)은 11-28에 뺐다(사용자: "기존 밝기 빼라고" — 몸 둘레 빛 번짐).
    /// </summary>
    internal static class Vision
    {
        /// <summary>주변광(사용자 11-28 "낙원급"): 게임 회차 설정 ambientlight = 칠흑 0 / 보일 만큼 0.12(느긋함 프리셋) / 밝음 0.4(낙원 프리셋).
        /// WorldGeneration.Update가 매 프레임 UpdateAmbientLight로 다시 정하므로 그 뒤에 퍽 단계별 바닥보다 낮으면 올린다. 4단계 = 낙원과 같은 0.4.</summary>
        internal static readonly float[] AmbientFloor = { 0f, 0.06f, 0.12f, 0.25f, 0.4f };
        [HarmonyLib.HarmonyPatch(typeof(WorldGeneration), "UpdateAmbientLight")]
        [HarmonyLib.HarmonyPostfix]
        private static void AmbientPostfix(WorldGeneration __instance)
        {
            int lv = Plugin.Perks?.Level("vision") ?? 0;
            if (lv <= 0 || __instance.ambientLight == null) return;
            float f = AmbientFloor[Mathf.Min(lv, AmbientFloor.Length - 1)];
            if (__instance.ambientLight.intensity < f) __instance.ambientLight.intensity = f;
        }

        /// <summary>벽 너머 보기(사용자 11-28): 게임 시야 가림은 PlayerCamera.HandleLineOfSight가 몸에서 사방으로 선을 쏴 벽에 막힌 점들로
        /// VisionMask.UpdateMask(몸 위치, 점들)를 부른다(점 바깥은 가림). 1~3단계: 퍽 반경 안에서 막힌 점은 반경까지 밀어내 그 안은 벽 너머도 보인다.
        /// 4단계(최대): 가림을 아예 없앤다 = 칩 있는 모드와 같은 화면(게임 SetUnchipped(false)도 ClearMask만 부름, 사용자 11-28 사진).
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(VisionMask), nameof(VisionMask.UpdateMask))]
        [HarmonyLib.HarmonyPrefix]
        private static bool MaskPrefix(VisionMask __instance, Vector2 playerPos, System.Collections.Generic.List<Vector2> hitPoints)
        {
            int lv = Plugin.Perks?.Level("vision") ?? 0;
            float r = Plugin.Perks?.VisionRadius ?? 0f;
            if (lv <= 0 || hitPoints == null || PlayerCamera.main == null || !PlayerCamera.main.body.alive) return true;
            if (lv >= FullLevel) { __instance.ClearMask(); return false; }
            for (int i = 0; i < hitPoints.Count; i++)
            {
                var d = hitPoints[i] - playerPos;
                if (d.sqrMagnitude < r * r && d.sqrMagnitude > 0.0001f) hitPoints[i] = playerPos + d.normalized * r;
            }
            return true;
        }
        internal const int FullLevel = 4;
    }
}
