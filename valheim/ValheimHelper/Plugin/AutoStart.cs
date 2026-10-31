using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 자동 시험용 월드 자동 입장: autotest.txt에 "world=이름"이 있으면 메인 메뉴에서
    /// 시작 → 시험 캐릭터("char=", 없으면 마지막 캐릭터) → 그 월드(서버 공개 안 함)를 사람이 누르는 것과 같은 순서로 누른다.
    /// 그 월드가 없으면 아무것도 하지 않는다(사람이 고르면 된다). 게임을 켤 때 한 번만.
    /// </summary>
    static class AutoStart
    {
        public static string World;
        public static string Character; // "char=이름": 시험 전용 캐릭터(없으면 만듦). 비면 마지막 캐릭터
        public static string Join, Pass; // "join=주소:포트;pass=비번": 월드 대신 전용 서버에 손님으로(멀티 시험, scripts/vh-server.ps1)
        static bool s_done;
        static bool On => !string.IsNullOrEmpty(World) || !string.IsNullOrEmpty(Join);

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        static class FejdStartupStart
        {
            static void Postfix(FejdStartup __instance)
            {
                // 서버 접속이 실패하면 게임이 메인 메뉴로 돌아와 다시 Start → 무인 시험이 15분 기다리지 않게 끈다.
                if (s_done && !string.IsNullOrEmpty(Join))
                {
                    Plugin.Log.LogWarning($"자동 입장: 서버 {Join} 접속 실패(메인 메뉴로 돌아옴) → 게임을 끕니다.");
                    Application.Quit();
                    return;
                }
                if (s_done || !On) return;
                s_done = true;
                __instance.StartCoroutine(Run(__instance));
            }
        }

        // 자동 시험 때만 시작 인트로를 건너뛴다(사용자 설정은 건드리지 않음). 바닐라 설정 이름 그대로 가로챈다.
        [HarmonyPatch(typeof(PlatformPrefs), nameof(PlatformPrefs.GetBool))]
        static class SkipIntro
        {
            static bool Prefix(string name, ref bool __result)
            {
                if (name != "SkipIntroCinematic" || !On) return true;
                __result = true;
                return false;
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
            bool joining = !string.IsNullOrEmpty(Join);
            Plugin.Log.LogInfo(joining ? $"자동 입장: 서버 {Join}" : $"자동 입장: 월드 {World}");
            f.OnStartGame();
            // 시험 전용 캐릭터(사용자 결정 2026-10-22): 사용자 캐릭터의 장비·음식·동료 해골·진행이 시험에 안 섞이게.
            // 못 고르면 사용자 캐릭터로 시험하지 않고 게임을 끈다. 메뉴의 "마지막 캐릭터"는 사용자 것으로 되돌려 둔다.
            string userProfile = PlatformPrefs.GetString("profile");
            bool useTestChar = !string.IsNullOrEmpty(Character);
            // 캐릭터 목록이 늦게 읽히면 OnCharacterStart가 아무것도 안 하고 월드 목록이 비어 FindWorld가 예외(2026-10-21 루프 회차 8,
            // 게임이 메뉴에서 멈춰 시험 스크립트가 끝없이 기다림) → 1.5초마다 다시, 30초 넘으면 게임을 끈다(무인 시험이 안 멈추게).
            World w = null;
            bool listed = false, charOk = !useTestChar;
            for (int i = 0; i < 20 && !listed; i++)
            {
                yield return new WaitForSeconds(1.5f);
                if (useTestChar)
                {
                    charOk = SelectTestCharacter(f);
                    if (!charOk) continue;
                }
                f.OnCharacterStart(); // 시험 캐릭터, 없으면 마지막에 쓴 캐릭터
                if (useTestChar) PlatformPrefs.SetString("profile", userProfile);
                yield return new WaitForSeconds(1.5f);
                if (joining) { listed = true; break; }
                try { w = FindWorld(f, World); listed = true; }
                catch (System.Exception e) { Plugin.Log.LogInfo($"자동 입장: 월드 목록 아직 없음({e.GetType().Name}), 다시"); }
            }
            if (!charOk)
            {
                Plugin.Log.LogWarning($"자동 입장: 시험 캐릭터 {Character}를 못 고름 → 사용자 캐릭터로 시험하지 않고 게임을 끕니다.");
                Application.Quit();
                yield break;
            }
            if (joining)
            {
                // 게임의 "-joinserverwithcharacter … -password" 실행 인자와 같은 길(FejdStartup.ParseArguments): 비번은 ZNet이 묻는 순간 FejdStartup.ServerPassword로 자동 입력.
                AccessTools.Field(typeof(FejdStartup), "m_joinServer").SetValue(f, new ServerJoinData(new ServerJoinDataDedicated(Join)));
                if (!string.IsNullOrEmpty(Pass)) AccessTools.Property(typeof(FejdStartup), "ServerPassword").SetValue(null, Pass);
                f.JoinServer();
                yield break;
            }
            if (w == null)
            {
                Plugin.Log.LogWarning($"자동 입장: 월드 {World}를 못 찾음{(listed ? "" : "(목록 못 읽음)")}. 직접 골라 주세요.");
                if (!listed) Application.Quit();
                yield break;
            }
            WorldField(f) = w;
            // 시험은 혼자(서버 열지 않음). Toggle은 UnityEngine.UI라 참조 없이 리플렉션으로 끈다.
            object toggle = AccessTools.Field(typeof(FejdStartup), "m_openServerToggle").GetValue(f);
            toggle?.GetType().GetProperty("isOn")?.SetValue(toggle, false);
            f.OnWorldStart();
        }

        static readonly System.Action<FejdStartup, string> SetSelectedProfile =
            AccessTools.MethodDelegate<System.Action<FejdStartup, string>>(AccessTools.Method(typeof(FejdStartup), "SetSelectedProfile"));
        static readonly System.Action<FejdStartup, PlayerProfile> SetupCharacterPreview =
            AccessTools.MethodDelegate<System.Action<FejdStartup, PlayerProfile>>(AccessTools.Method(typeof(FejdStartup), "SetupCharacterPreview"));
        static readonly AccessTools.FieldRef<FejdStartup, List<PlayerProfile>> ProfilesField =
            AccessTools.FieldRefAccess<FejdStartup, List<PlayerProfile>>("m_profiles");
        static readonly AccessTools.FieldRef<FejdStartup, int> ProfileIndexField = AccessTools.FieldRefAccess<FejdStartup, int>("m_profileIndex");

        /// <summary>시험 캐릭터가 없으면 바닐라 새 캐릭터 만들기(OnNewCharacterDone)와 같은 순서로 로컬에 만들고, 골라졌는지 확인한다.</summary>
        static bool SelectTestCharacter(FejdStartup f)
        {
            try
            {
                if (!PlayerProfile.HaveProfile(Character))
                {
                    SetupCharacterPreview(f, null); // 빈 플레이어 인스턴스
                    Player p = f.GetPreviewPlayer();
                    if (p == null) { Plugin.Log.LogWarning("자동 입장: 시험 캐릭터 미리보기 없음, 다시"); return false; }
                    var profile = new PlayerProfile(Character, FileHelpers.FileSource.Local);
                    p.GiveDefaultItems();
                    profile.SetName(Character);
                    profile.m_firstSpawn = false; // 발키리 등장 연출 없이 바로 서게
                    profile.SavePlayerData(p);
                    if (!profile.Save()) { Plugin.Log.LogWarning("자동 입장: 시험 캐릭터 저장 실패"); return false; }
                    Plugin.Log.LogInfo($"자동 입장: 시험 캐릭터 {Character} 새로 만듦");
                }
                ProfilesField(f) = null; // 목록 다시 읽기
                SetSelectedProfile(f, Character);
                var list = ProfilesField(f);
                int i = ProfileIndexField(f);
                bool ok = list != null && i >= 0 && i < list.Count && list[i].GetFilename() == Character;
                if (!ok) Plugin.Log.LogInfo($"자동 입장: 시험 캐릭터 {Character}가 목록에 아직 없음, 다시");
                return ok;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"자동 입장: 시험 캐릭터 고르기 실패: {e}");
                return false;
            }
        }
    }
}
