using System;
using System.IO;
using UnityEngine;

namespace CasualtiesCarryover
{
    /// <summary>CU_CARRYOVER_TEST=lang일 때만: 메뉴가 뜬 뒤 게임의 언어 목록(Locale.GetAllLanguages, 설정 > 언어 메뉴가 쓰는 것)을
    /// CU_CARRYOVER_RESULT에 적고 종료(소리 끔).</summary>
    internal class LangCheck : MonoBehaviour
    {
        private float t0;
        private void Awake() { DontDestroyOnLoad(gameObject); Application.runInBackground = true; t0 = Time.realtimeSinceStartup; }

        private void Update()
        {
            AudioListener.volume = 0f;
            if (Time.realtimeSinceStartup - t0 < 8f || FindObjectOfType<PreRunScript>() == null) return;
            var text = "";
            try { foreach (var l in Locale.GetAllLanguages()) text += l.reference + " | " + l.name + "\n"; }
            catch (Exception e) { text = "EXCEPTION " + e; }
            var path = Environment.GetEnvironmentVariable("CU_CARRYOVER_RESULT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, text);
            Plugin.Log.LogInfo("[langcheck]\n" + text);
            enabled = false;
            Application.Quit();
        }
    }
}
