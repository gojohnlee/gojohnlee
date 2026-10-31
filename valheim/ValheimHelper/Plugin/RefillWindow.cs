using System.Collections.Generic;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>F7 계속채워 현황 창(사용자 10-30): 오른쪽 위, 열렸을 때만 2초마다 다시 셈. 가장 가까운 일꾼 집 기준 거점 줄 + 내 개인 상자 줄.</summary>
    internal static class RefillWindow
    {
        const float Every = 2f;
        internal static bool Open;
        static float s_wait;
        static List<string> s_lines = new List<string>();
        static GUIStyle s_style;

        /// <summary>!계속채워 목록과 같은 줄: 거점 줄 + "개인:" 줄들(owner 0이면 개인 없음).</summary>
        internal static List<string> Lines(Vector3 home, long owner, bool personalHeader)
        {
            var parts = Brain.Refill.Status(it => Departures.RefillStock(it, home), Brain.Sets, GatherJob.ItemLabel);
            var mine = PersonalChests.StatusLines(owner, home);
            if (mine.Count > 0)
            {
                if (personalHeader) { parts.Add("개인:"); parts.AddRange(mine); }
                else parts.Add("개인: " + string.Join(", ", mine.ToArray()));
            }
            return parts;
        }

        internal static IList<string> Shown => s_lines;

        internal static void Toggle()
        {
            Open = !Open;
            s_wait = 0f;
            if (Open) Refresh();
        }

        internal static void Update(float dt)
        {
            if (Input.GetKeyDown(KeyCode.F7) && !Typing()) Toggle();
            if (!Open) return;
            s_wait += dt;
            if (s_wait < Every) return;
            s_wait = 0f;
            Refresh();
        }

        static bool Typing() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() || Menu.IsVisible();

        internal static void Refresh()
        {
            s_lines = new List<string>();
            Player me = Player.m_localPlayer;
            WorkerNpc near = null;
            float best = float.MaxValue;
            if (me != null)
                foreach (var n in WorkerNpc.All)
                {
                    if (n == null) continue;
                    float d = Utils.DistanceXZ(n.transform.position, me.transform.position);
                    if (d < best) { best = d; near = n; }
                }
            if (near == null) { s_lines.Add("일꾼이 없어요"); return; }
            s_lines = Lines(near.Home, me.GetPlayerID(), true);
            if (s_lines.Count == 0) s_lines.Add("계속 채우는 것 없음");
        }

        internal static void OnGUI()
        {
            if (!Open || s_lines.Count == 0) return;
            if (s_style == null) s_style = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = false };
            const float w = 320f, line = 22f;
            float h = 34f + line * s_lines.Count;
            var r = new Rect(Screen.width - w - 20f, 80f, w, h);
            GUI.Box(r, "계속 채우기 (F7)");
            for (int i = 0; i < s_lines.Count; i++)
                GUI.Label(new Rect(r.x + 10f, r.y + 26f + line * i, w - 20f, line), s_lines[i], s_style);
        }
    }
}
