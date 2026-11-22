using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CasualtiesCarryover
{
    /// <summary>
    /// 회차 중 화면: 함정 감지 표시(가까운 함정 위에 빨간 네모 "!"와 "함정" 글자, 왼쪽 위에 "근처 함정 N개")와 아공간(V로 게임 가방 창을 열어 끌어다 넣고 뺌, 왼쪽 위에 목록).
    /// 함정 종류(디컴파일로 확인한 클래스): BearTrap(덫)·FanTrap(선풍기)·MineScript(지뢰)·GunmineScript(총 지뢰)·SpikeStabberScript(가시 찌르개)·BarbedFence(철조망)·CaveTicks(동굴 진드기)·SoundCannon(음파 대포)·TurretScript(포탑)·CeilingTurret(천장 포탑).
    /// </summary>
    internal class RunHud : MonoBehaviour
    {
        internal static readonly Type[] TrapTypes =
        {
            typeof(BearTrap), typeof(FanTrap), typeof(MineScript), typeof(GunmineScript),
            typeof(SpikeStabberScript), typeof(BarbedFence), typeof(CaveTicks), typeof(SoundCannon), typeof(TurretScript), typeof(CeilingTurret),
        };

        internal static readonly List<Transform> Sensed = new List<Transform>();
        private float nextScan;
        internal static int Ticks, Scans; internal static float LastRadius;
        private GUIStyle mark, hud, tagStyle, warn;
        /// <summary>마지막으로 그린 함정 표시 칸(GUI 좌표). 시험에서 화면 픽셀을 확인할 때 쓴다.</summary>
        internal static readonly List<Rect> Drawn = new List<Rect>();

        private static Body PlayerBody => PlayerCamera.main != null ? PlayerCamera.main.body : null;

        /// <summary>반경 안 함정(살아 있는 몸 기준). 시험에서도 부른다.</summary>
        internal static List<Transform> Scan(Body body, float radius)
        {
            var found = new List<Transform>();
            if (body == null || radius <= 0f) return found;
            Vector2 p = body.transform.position;
            foreach (var t in TrapTypes)
                foreach (var o in FindObjectsOfType(t))
                    if (o is Component c && c != null && ((Vector2)c.transform.position - p).sqrMagnitude <= radius * radius) found.Add(c.transform);
            return found;
        }

        private void Update()
        {
            var body = PlayerBody;
            Ticks++;
            if (body == null || Plugin.Perks == null) { Sensed.Clear(); return; }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.5f;
                Sensed.Clear();
                Sensed.AddRange(Scan(body, Plugin.Perks.TrapRadius)); Scans++; LastRadius = Plugin.Perks.TrapRadius;
            }
            if (Subspace.Bag != null) Subspace.Hide(body);
            if (Subspace.Capacity > 0 && body.alive && Input.GetKeyDown(KeyCode.V))
            {
                Subspace.Toggle(body);
            }
            if (Subspace.Bag != null)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (wheel != 0f) Subspace.Scroll(wheel > 0f ? -1 : 1);
                if (Input.GetMouseButtonDown(2)) { var it = Subspace.Hovered(); if (it != null) Subspace.TogglePin(it.id); }
            }
        }

        private void OnGUI()
        {
            var body = PlayerBody;
            if (body == null || Plugin.Perks == null) return;
            if (mark == null)
            {
                mark = new GUIStyle(GUI.skin.label) { font = UiFont.Get(), fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                mark.normal.textColor = Color.white;
                tagStyle = new GUIStyle(GUI.skin.label) { font = UiFont.Get(), fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                tagStyle.normal.textColor = new Color(1f, 0.3f, 0.25f);
                warn = new GUIStyle(tagStyle) { alignment = TextAnchor.MiddleLeft, fontSize = 16 };
                hud = new GUIStyle(GUI.skin.label) { font = UiFont.Get(), fontSize = 15 };
                hud.normal.textColor = new Color(0.85f, 0.85f, 0.8f, 0.9f);
            }
            // 표시: 빨간 네모 안에 흰 "!", 아래 "함정" 글자, 왼쪽 위 "근처 함정 N개".
            // ⚠ 기호는 맑은 고딕에 없어 빈칸으로 그려졌다(사용자 11-21 "안 뜨던데") → 네모는 글꼴과 상관없이 그린다.
            if (Event.current.type == EventType.Repaint) Drawn.Clear();
            var cam = Camera.main;
            int shown = 0;
            if (cam != null)
                foreach (var t in Sensed)
                {
                    if (t == null) continue;
                    var sp = cam.WorldToScreenPoint(t.position);
                    if (sp.z < 0f) continue;
                    float x = sp.x, y = Screen.height - sp.y;
                    var box = new Rect(x - 13, y - 52, 26, 26);
                    var old = GUI.color;
                    GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.DrawTexture(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), Texture2D.whiteTexture);
                    GUI.color = new Color(0.95f, 0.1f, 0.1f); GUI.DrawTexture(box, Texture2D.whiteTexture);
                    GUI.color = old;
                    GUI.Label(box, "!", mark);
                    GUI.Label(new Rect(x - 30, y - 26, 60, 20), "함정", tagStyle);
                    if (Event.current.type == EventType.Repaint) Drawn.Add(box);
                    shown++;
                }
            if (shown > 0) GUI.Label(new Rect(14, 96, 420, 24), $"근처 함정 {shown}개", warn);
            int cap = Subspace.Capacity;
            if (cap > 0)
            {
                var groups = Subspace.Groups();
                float w = Subspace.Bag != null ? Subspace.Bag.GetHoldingWeight() : 0f;
                var s = $"아공간 {w:0.##}/{Subspace.MaxWeight:0.##}u  (V: 아공간 가방 열기)";
                int lines = Mathf.Min(groups.Count, 8);
                for (int i = 0; i < lines; i++) s += "\n · " + Subspace.Name(groups[i][0]) + (groups[i].Count > 1 ? $" x{groups[i].Count}" : "");
                if (groups.Count > lines) { s += $"\n · 외 {groups.Count - lines}묶음"; lines++; }
                if (PlayerCamera.main != null && PlayerCamera.main.currentContainer == Subspace.Bag)
                {
                    int rows = Subspace.TotalRows(groups.Count);
                    s += "\n 가방 창: 휠 클릭 = 위로 고정/해제" + (rows > Subspace.Rows ? $", 휠 = 넘기기 ({Subspace.ScrollRow + 1}-{Subspace.ScrollRow + Subspace.Rows}/{rows}줄)" : "");
                    lines++;
                }
                GUI.Label(new Rect(14, 120, 420, 24 + 20 * lines), s, hud);
            }
        }
    }

    internal static class UiFont
    {
        private static Font f;
        internal static Font Get() => f != null ? f : (f = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 18));
    }
}
