using System;
using System.IO;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>실플레이 상황별 대사 횟수(사용자 10-29) → 플러그인 폴더 speech-count.txt. 10분마다·게임 끌 때 저장,
    /// 시험 중(TestRunner.Running)·시험 판(autotest.txt로 켠 판)엔 안 센다. 꼴과 "# 늘릴 것" 줄은 Core SpeechStats.</summary>
    static class SpeechCount
    {
        const float SaveEvery = 600f;
        static SpeechStats s_stats;
        static bool s_dirty;
        static float s_nextSave;

        static string FilePath => Path.Combine(TestRunner.PluginDir, "speech-count.txt");

        static SpeechStats Stats()
        {
            if (s_stats != null) return s_stats;
            s_stats = new SpeechStats();
            try { if (File.Exists(FilePath)) s_stats.Load(File.ReadAllText(FilePath)); }
            catch (Exception e) { Plugin.Log.LogWarning("speech-count.txt 읽기 실패: " + e.Message); }
            return s_stats;
        }

        public static void Add(string key)
        {
            if (TestRunner.Running || TestRunner.AutoSession) return;
            Stats().Add(key);
            s_dirty = true;
        }

        public static void Tick()
        {
            if (UnityEngine.Time.realtimeSinceStartup < s_nextSave) return;
            s_nextSave = UnityEngine.Time.realtimeSinceStartup + SaveEvery;
            Save();
        }

        public static void Save()
        {
            if (!s_dirty || s_stats == null) return;
            try { File.WriteAllText(FilePath, s_stats.ToText()); s_dirty = false; }
            catch (Exception e) { Plugin.Log.LogWarning("speech-count.txt 저장 실패: " + e.Message); }
        }
    }
}
