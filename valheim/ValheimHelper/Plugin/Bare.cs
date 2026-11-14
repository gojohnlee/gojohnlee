using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 모드 부하 재기 "모드 끔" 판(사용자 11-14: 바닐라 대비 모드 무게 — 패치가 하나라도 걸리면 바닐라 아님).
    /// VH_BARE=1이면 Plugin.Awake가 Harmony 패치·일꾼·매니저·말풍선을 하나도 안 켜고 여기만 돈다:
    /// 메인 메뉴 → AutoStart.Run(패치 없이 리플렉션으로 누름) → 월드에 서고 VH_BARE_WAIT초(기본 20) 뒤
    /// VH_BARE_SECS초(기본 60) 프레임 시간을 재서 로그 "[모드 부하] 모드 끔 …"·diag "=== vh_bare"에 남기고 게임을 끈다.
    /// 같은 자리 모드 켬 판은 VH_FRAMESTATS=1(FrameLog)로 잰다.
    /// </summary>
    static class Bare
    {
        public static readonly bool On = Environment.GetEnvironmentVariable("VH_BARE") == "1";
        internal static readonly float WaitSecs = EnvSecs("VH_BARE_WAIT", 20f);
        internal static readonly float MeasureSecs = EnvSecs("VH_BARE_SECS", 60f);
        static readonly List<double> Frames = new List<double>();
        static bool s_started, s_done;
        static float s_look, s_inWorld, s_measured;

        static float EnvSecs(string name, float def) =>
            float.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0f ? v : def;

        public static void Update()
        {
            if (!string.IsNullOrEmpty(AutoStart.World)) TestRunner.Quiet(); // 소리 끔·시작 영상 멈춤(패치 없이)
            if (s_done) return;
            float dt = Time.unscaledDeltaTime;
            if (!s_started)
            {
                // FejdStartup.Start 패치 대신 메인 메뉴가 생길 때까지 1초마다 찾는다.
                s_look -= dt;
                if (s_look > 0f) return;
                s_look = 1f;
                var f = UnityEngine.Object.FindAnyObjectByType<FejdStartup>();
                if (f == null || string.IsNullOrEmpty(AutoStart.World)) return;
                s_started = true;
                Plugin.Log.LogInfo($"모드 끔 판: 패치 없이 월드 {AutoStart.World} 입장, {WaitSecs}초 뒤 {MeasureSecs}초 잼");
                f.StartCoroutine(AutoStart.Run(f));
                return;
            }
            if (Player.m_localPlayer == null) return;
            s_inWorld += dt;
            if (s_inWorld < WaitSecs) return;
            Frames.Add(dt);
            s_measured += dt;
            if (s_measured < MeasureSecs) return;
            s_done = true;
            string text = FrameStats.ToText(FrameStats.Summarize(Frames));
            Plugin.Log.LogInfo("[모드 부하] 모드 끔 " + text);
            try
            {
                File.WriteAllLines(Path.Combine(TestRunner.PluginDir, "diag.txt"), new[]
                {
                    $"모드 끔 판 {Plugin.FullVersion} — 패치 0",
                    $"=== vh_bare 월드 {AutoStart.World}",
                    $"판정: PASS",
                    $"시간: {s_measured:0}초",
                    "프레임: " + text,
                });
            }
            catch (IOException e) { Plugin.Log.LogWarning("모드 끔 판 diag 못 씀: " + e.Message); }
            Application.Quit();
        }
    }
}
