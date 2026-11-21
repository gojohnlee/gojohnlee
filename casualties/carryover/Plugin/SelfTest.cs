using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CasualtiesCarryover
{
    /// <summary>
    /// CU_CARRYOVER_TEST=1일 때만 붙는 자동 시험(소리 끔): 1회차 시작 → 세 능력치 +3 → 죽음 → 메뉴 → 2회차 시작(+3 확인) → 바로 죽음(얻은 양 0 확인)
    /// → 메뉴(보너스 표시 없음 확인) → 결과를 CU_CARRYOVER_RESULT 파일에 쓰고 게임 종료. 상태 파일은 CU_CARRYOVER_STATE(시험용)를 쓴다.
    /// </summary>
    internal class SelfTest : MonoBehaviour
    {
        private static SelfTest inst;
        private int phase;            // 0 1회차 시작 대기, 1 1회차 중, 2 2회차 시작 대기, 3 2회차 중, 4 메뉴 확인, 5 끝
        private float phaseTime, startTime;
        private int[] start1, start2, gain1, gain2, base1;
        private bool killed, ended, sawPending;
        private readonly StringBuilder log = new StringBuilder();
        private float nextSceneLog;

        private void Awake()
        {
            inst = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            startTime = Time.realtimeSinceStartup;
            Plugin.State = new Carryover.Core.CarryoverState();   // 시험은 빈 상태에서
            Plugin.Perks = new Carryover.Core.PerkState(); Plugin.SavePerks();
            Plugin.SaveState();
            Note("selftest begin");
        }

        internal static void OnRunStarted(int[] levels)
        {
            if (inst == null) return;
            if (inst.phase == 0) { inst.start1 = levels; inst.base1 = (int[])levels.Clone(); inst.Next(1); }
            else if (inst.phase == 2) { inst.start2 = levels; inst.Next(3); }
            inst.Note($"run started: {Plugin.Fmt(levels)}");
        }

        internal static void OnRunEnded(int[] gain)
        {
            if (inst == null || inst.ended) return;
            if (inst.phase == 1) inst.gain1 = gain; else if (inst.phase == 3) inst.gain2 = gain;
            inst.ended = true;
            inst.phaseTime = Time.realtimeSinceStartup;
            inst.Note($"run ended: gain {Plugin.Fmt(gain)}");
        }

        private void Next(int p) { phase = p; phaseTime = Time.realtimeSinceStartup; killed = false; ended = false; }
        private float Since => Time.realtimeSinceStartup - phaseTime;
        private void Note(string s) { log.Append($"[{Time.realtimeSinceStartup - startTime:0.0}s] {s}\n"); Plugin.Log.LogInfo("[selftest] " + s); }

        private void Update()
        {
            AudioListener.volume = 0f;
            if (Time.realtimeSinceStartup >= nextSceneLog) { nextSceneLog = Time.realtimeSinceStartup + 15f; Note("scene " + SceneManager.GetActiveScene().name + " phase " + phase); }
            if (Time.realtimeSinceStartup - startTime > 420f && phase < 5) { Finish("TIMEOUT phase " + phase); return; }
            try { Step(); } catch (Exception e) { Finish("EXCEPTION " + e); }
        }

        private void Step()
        {
            switch (phase)
            {
                case 0:
                case 2:
                    TryStartRun();
                    break;
                case 1:
                case 3:
                    InRun();
                    break;
                case 4:
                    var pre = FindObjectOfType<PreRunScript>();
                    if (pre == null || Since < 3f) return;
                    var txt = pre.charSelectStats != null ? pre.charSelectStats.text : "(none)";
                    Note("menu stats text: " + txt.Replace("\n", " | "));
                    sawPending = base1 != null && txt.Contains($"[STR] {base1[0] + 3} (+3)") && txt.Contains($"[INT] {base1[2] + 3} (+3)");   // 능력치 유지: 3회차도 +3 그대로
                    Finish(null);
                    break;
            }
        }

        private float lastStartTry;
        private void TryStartRun()
        {
            var pre = FindObjectOfType<PreRunScript>();
            if (pre == null || Since < 3f || Time.realtimeSinceStartup - lastStartTry < 10f) return;
            lastStartTry = Time.realtimeSinceStartup;
            if (phase == 2) Note("menu stats text before run 2: " + (pre.charSelectStats != null ? pre.charSelectStats.text.Replace("\n", " | ") : "(none)"));
            Note("calling PreRunScript.StartRun");
            pre.StartRun();
        }

        private void InRun()
        {
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (body == null) return;
            if (ended)
            {
                if (Since > 4f) { Note("to main menu"); PlayerCamera.main.ToMainMenu(); Next(phase == 1 ? 2 : 4); }
                return;
            }
            if (killed || Since < 5f) return;
            if (phase == 1)
            {
                var sk = body.skills;
                for (int stat = 0; stat < 3; stat++) RaiseThree(sk, stat);
                Note($"after +3 levels: {Plugin.Fmt(Patches.Levels(sk))}");
            }
            Note("kill (brainHealth = 0)");
            body.brainHealth = 0f;
            killed = true;
        }

        // 실제 경험치 경로(Skills.AddExp → CheckForLevelUp)로 3레벨 올린다.
        private void RaiseThree(Skills sk, int stat)
        {
            int lv = stat == 0 ? sk.STR : stat == 1 ? sk.RES : sk.INT;
            float exp = stat == 0 ? sk.expSTR : stat == 1 ? sk.expRES : sk.expINT;
            float mult = Skills.xpGainMult * (stat == 0 ? sk.STRGainMult : stat == 1 ? sk.RESGainMult : sk.INTGainMult);
            if (mult <= 0f) { Note($"stat {stat}: gain multiplier {mult}, cannot raise via AddExp"); return; }
            float need = Skills.GetExperienceForLevel(lv + 3) - exp;
            sk.AddExp(stat, need / mult + 0.5f);
        }

        private void Finish(string error)
        {
            phase = 5;
            bool ok = error == null
                && start1 != null && start2 != null && gain1 != null && gain2 != null
                && gain1[0] == 3 && gain1[1] == 3 && gain1[2] == 3
                && start2[0] == base1[0] + 3 && start2[1] == base1[1] + 3 && start2[2] == base1[2] + 3
                && gain2[0] == 0 && gain2[1] == 0 && gain2[2] == 0
                && Plugin.State.Pending[0] + Plugin.State.Pending[1] + Plugin.State.Pending[2] == 0
                && sawPending;
            var r = new StringBuilder();
            r.Append(ok ? "PASS\n" : "FAIL " + (error ?? "") + "\n");
            r.Append($"run1 start {F(start1)} gain {F(gain1)}\nrun2 start {F(start2)} gain {F(gain2)}\nnext bonus {Plugin.Fmt(Plugin.State.Pending)}\n");
            r.Append(log);
            var path = Environment.GetEnvironmentVariable("CU_CARRYOVER_RESULT");
            if (!string.IsNullOrEmpty(path)) { try { File.WriteAllText(path, r.ToString()); } catch (Exception e) { Plugin.Log.LogError(e.Message); } }
            Plugin.Log.LogInfo("[selftest] " + (ok ? "PASS" : "FAIL"));
            Application.Quit();
        }

        private static string F(int[] a) => a == null ? "-" : Plugin.Fmt(a);
    }
}
