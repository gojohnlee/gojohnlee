using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 자동 시험용 월드 자동 입장: autotest.txt에 "world=이름"이 있으면 메인 메뉴에서
    /// 시작 → 마지막 캐릭터 → 그 월드(서버 공개 안 함)를 사람이 누르는 것과 같은 순서로 누른다.
    /// 그 월드가 없으면 아무것도 하지 않는다(사람이 고르면 된다). 게임을 켤 때 한 번만.
    /// </summary>
    static class AutoStart
    {
        public static string World;
        static bool s_done;

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        static class FejdStartupStart
        {
            static void Postfix(FejdStartup __instance)
            {
                if (s_done || string.IsNullOrEmpty(World)) return;
                s_done = true;
                __instance.StartCoroutine(Run(__instance));
            }
        }

        static readonly AccessTools.FieldRef<FejdStartup, World> WorldField = AccessTools.FieldRefAccess<FejdStartup, World>("m_world");
        static readonly System.Func<FejdStartup, string, World> FindWorld =
            AccessTools.MethodDelegate<System.Func<FejdStartup, string, World>>(AccessTools.Method(typeof(FejdStartup), "FindWorld"));

        static IEnumerator Run(FejdStartup f)
        {
            // 첫 화면 연출과 메인 메뉴가 뜰 때까지 기다린다(최대 60초).
            float t = 0f;
            while (t < 60f && (CinematicsManager.IsPlaying() || !f.m_mainMenu.activeInHierarchy))
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
            }
            yield return new WaitForSeconds(2f);
            Plugin.Log.LogInfo($"자동 입장: 월드 {World}");
            f.OnStartGame();
            yield return new WaitForSeconds(1.5f);
            f.OnCharacterStart(); // 마지막에 쓴 캐릭터
            yield return new WaitForSeconds(1.5f);
            World w = FindWorld(f, World);
            if (w == null)
            {
                Plugin.Log.LogWarning($"자동 입장: 월드 {World}를 못 찾음. 직접 골라 주세요.");
                yield break;
            }
            WorldField(f) = w;
            // 시험은 혼자(서버 열지 않음). Toggle은 UnityEngine.UI라 참조 없이 리플렉션으로 끈다.
            object toggle = AccessTools.Field(typeof(FejdStartup), "m_openServerToggle").GetValue(f);
            toggle?.GetType().GetProperty("isOn")?.SetValue(toggle, false);
            f.OnWorldStart();
        }
    }
}
