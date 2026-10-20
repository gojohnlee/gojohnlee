using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 최소화 시험(사용자 요청 2026-10-20: 시험 도는 동안 다른 게임을 한다). autotest.txt에 "min"이 있으면
    /// 이번 실행만 게임 창을 최소화하고, 소리를 끄고, 창이 비활성일 때 30fps로 줄인다(바닐라 "백그라운드 사용 줄이기").
    /// 첫 시험이 시작될 때까지는 창이 다시 올라오면 또 내린다(게임이 켜지며 화면 모드를 다시 잡을 수 있어서).
    /// 그 뒤로는 사용자가 창을 열어 봐도 그대로 둔다. 사용자 설정 파일은 건드리지 않는다.
    /// </summary>
    static class Minimized
    {
        public static bool On;
        static bool s_released;
        static float s_timer;
        static IntPtr s_window;

        public static void Update()
        {
            if (!On) return;
            AudioListener.volume = 0f; // AudioMan·연출이 1로 되돌리므로 매 프레임
            Settings.ReduceBackgroundUsage = true;
            Application.runInBackground = true;
            if (s_released) return;
            s_timer -= Time.unscaledDeltaTime;
            if (s_timer > 0f) return;
            s_timer = 0.5f;
            IntPtr h = FindWindow();
            if (h != IntPtr.Zero && !IsIconic(h)) ShowWindow(h, SwMinimize);
        }

        /// <summary>첫 시험 시작: 이제부터 창을 억지로 내리지 않는다.</summary>
        public static void Release() => s_released = true;

        /// <summary>이 프로세스의 Unity 창.</summary>
        static IntPtr FindWindow()
        {
            if (s_window != IntPtr.Zero && IsWindow(s_window)) return s_window;
            s_window = IntPtr.Zero;
            uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            var cls = new StringBuilder(64);
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint p);
                if (p != pid) return true;
                cls.Length = 0;
                GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() != "UnityWndClass") return true;
                s_window = h;
                return false;
            }, IntPtr.Zero);
            return s_window;
        }

        const int SwMinimize = 6; // 최소화하고 다음 창(하던 게임)을 활성화

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    }
}
