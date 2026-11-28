using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CasualtiesCarryover
{
    /// <summary>
    /// 시야 적응력(사용자 11-28: 튼튼한 다리 대신, 광원 없이 주변 타일이 보임).
    /// 게임 근거: 화면 밝기는 URP 2D 조명(Light2D). 어두운 곳은 WorldGeneration.ambientLight(전역, 설정에 따라 0·0.12·0.4)만 비춘다.
    /// → 플레이어 머리(limbs[0])에 그림자 없는 약한 원형 조명을 붙인다. 비추는 정렬 층은 전역 조명과 같게(UI 등은 안 밝힘).
    /// 벽 너머가 보이게 하지는 않는다(시야 가림 VisionMask는 그대로).
    /// </summary>
    internal static class Vision
    {
        internal static Light2D Light;
        internal const float Intensity = 2.5f;   // 11-28 "낙원급으로": 랜턴(1.5)보다 밝게

        /// <summary>매 프레임(RunHud.Update). 퍽 단계가 0이거나 죽었으면 끈다.</summary>
        internal static bool Hold;   // 시험: 켜고 끄는 걸 시험이 직접 함
        private static bool failed;   // 랜턴 조명 복제 실패 → 다시 시도 안 함(매 프레임 랜턴을 만들지 않게)
        internal static void Tick(Body body)
        {
            if (Hold) return;
            float r = Plugin.Perks?.VisionRadius ?? 0f;
            if (body == null || body.limbs == null || body.limbs.Length == 0 || body.limbs[0] == null) return;
            if (r <= 0f || !body.alive) { if (Light != null) Light.enabled = false; return; }
            var head = body.limbs[0].transform;
            if (Light == null || Light.transform.parent != head)
            {
                if (Light != null) Object.Destroy(Light.gameObject);
                // 런타임에 AddComponent로 만든 Light2D는 화면에 안 그려졌다(시험 11-28: 밝기 그대로) → 게임 랜턴의 조명을 복제해 쓴다
                if (failed) return;
                var go = MakeLightObject();
                if (go == null) { failed = true; return; }
                go.name = "CarryoverVision";
                go.transform.SetParent(head, false);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
                Light = go.GetComponent<Light2D>();
                Light.lightType = Light2D.LightType.Point;
                Light.shadowsEnabled = false;
                Light.color = new Color(0.85f, 0.9f, 1f);
                Plugin.Log.LogInfo($"vision light on, radius {r}");
            }
            Light.intensity = Intensity;
            Light.pointLightOuterRadius = r;
            Light.pointLightInnerRadius = r * 0.3f;
            Light.enabled = true;
        }

        /// <summary>전역 밝기 바닥(사용자 11-28 "낙원급으로"): WorldGeneration.Update가 매 프레임 UpdateAmbientLight로 ambientLight.intensity를
        /// 회차 설정값(0·0.12·0.4, 콘솔 fullbright 0.7)으로 다시 정한다 → 그 뒤에 퍽 단계별 바닥(0.15·0.3·0.5·0.7)보다 낮으면 올린다. 4단계 = fullbright.</summary>
        internal static readonly float[] AmbientFloor = { 0f, 0.15f, 0.3f, 0.5f, 0.7f };
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
        /// VisionMask.UpdateMask(몸 위치, 점들)를 부른다(점 바깥은 가림). 퍽 반경 안에서 막힌 점은 반경까지 밀어내 그 안은 벽 너머도 보이게 한다.</summary>
        [HarmonyLib.HarmonyPatch(typeof(VisionMask), nameof(VisionMask.UpdateMask))]
        [HarmonyLib.HarmonyPrefix]
        private static void MaskPrefix(Vector2 playerPos, System.Collections.Generic.List<Vector2> hitPoints)
        {
            float r = Plugin.Perks?.VisionRadius ?? 0f;
            if (r <= 0f || hitPoints == null || PlayerCamera.main == null || !PlayerCamera.main.body.alive) return;
            for (int i = 0; i < hitPoints.Count; i++)
            {
                var d = hitPoints[i] - playerPos;
                if (d.sqrMagnitude < r * r && d.sqrMagnitude > 0.0001f) hitPoints[i] = playerPos + d.normalized * r;
            }
        }

        /// <summary>게임 랜턴(lantern) 아이템을 잠깐 만들어 그 안의 Light2D 오브젝트만 복제한다(다른 스크립트는 지움).</summary>
        private static GameObject MakeLightObject()
        {
            GameObject lantern = null;
            try
            {
                lantern = Utils.Create("lantern", new Vector2(0f, -10000f), 0f);
                var src = lantern != null ? lantern.GetComponentInChildren<Light2D>(true) : null;
                if (src == null) { Plugin.Log.LogWarning("vision: lantern has no Light2D"); return null; }
                Plugin.Log.LogInfo($"vision: cloning lantern light '{src.name}' type {src.lightType} r {src.pointLightOuterRadius} i {src.intensity} root {src.gameObject == lantern}");
                var copy = Object.Instantiate(src.gameObject);
                foreach (var c in copy.GetComponentsInChildren<Component>(true))
                    if (!(c is Transform) && !(c is Light2D)) Object.DestroyImmediate(c);
                foreach (Transform ch in copy.transform) Object.Destroy(ch.gameObject);
                var l = copy.GetComponent<Light2D>(); l.enabled = true;
                copy.SetActive(true);
                return copy;
            }
            catch (System.Exception e) { Plugin.Log.LogError("vision light: " + e); return null; }
            finally { if (lantern != null) Object.DestroyImmediate(lantern); }
        }
    }
}
