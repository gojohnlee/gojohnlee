using System;
using System.Collections.Generic;
using UnityEngine;

namespace CasualtiesCarryover
{
    /// <summary>
    /// 회차 중 화면: 함정 감지 표시(가까운 함정 위에 빨간 ⚠ 표시)와 아공간(V 넣기, Shift+V 꺼내기, 왼쪽 위에 칸 목록).
    /// 함정 종류(디컴파일로 확인한 클래스): BearTrap(덫)·FanTrap(선풍기)·MineScript(지뢰)·GunmineScript(총 지뢰)·SpikeStabberScript(가시 찌르개)·BarbedFence(철조망)·CaveTicks(동굴 진드기).
    /// </summary>
    internal class RunHud : MonoBehaviour
    {
        internal static readonly Type[] TrapTypes =
        {
            typeof(BearTrap), typeof(FanTrap), typeof(MineScript), typeof(GunmineScript),
            typeof(SpikeStabberScript), typeof(BarbedFence), typeof(CaveTicks),
        };

        internal static readonly List<Transform> Sensed = new List<Transform>();
        private float nextScan;
        private GUIStyle mark, hud;

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
            if (body == null || Plugin.Perks == null) { Sensed.Clear(); return; }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.5f;
                Sensed.Clear();
                Sensed.AddRange(Scan(body, Plugin.Perks.TrapRadius));
            }
            if (Subspace.Capacity > 0 && body.alive && Input.GetKeyDown(KeyCode.V))
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                var msg = shift ? Subspace.TakeOut(body) : Subspace.StoreFromHands(body);
                if (!string.IsNullOrEmpty(msg)) { try { PlayerCamera.main.DoAlert(msg); } catch { } }
            }
        }

        private void OnGUI()
        {
            var body = PlayerBody;
            if (body == null || Plugin.Perks == null) return;
            if (mark == null)
            {
                mark = new GUIStyle(GUI.skin.label) { font = UiFont.Get(), fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                mark.normal.textColor = new Color(1f, 0.25f, 0.2f);
                hud = new GUIStyle(GUI.skin.label) { font = UiFont.Get(), fontSize = 15 };
                hud.normal.textColor = new Color(0.85f, 0.85f, 0.8f, 0.9f);
            }
            var cam = Camera.main;
            if (cam != null)
                foreach (var t in Sensed)
                {
                    if (t == null) continue;
                    var sp = cam.WorldToScreenPoint(t.position);
                    if (sp.z < 0f) continue;
                    GUI.Label(new Rect(sp.x - 20, Screen.height - sp.y - 44, 40, 30), "⚠", mark);
                }
            int cap = Subspace.Capacity;
            if (cap > 0)
            {
                var s = $"아공간 {Subspace.Items.Count}/{cap}  (V 넣기 · Shift+V 꺼내기)";
                foreach (var it in Subspace.Items) if (it != null) s += "\n · " + Subspace.Name(it);
                GUI.Label(new Rect(14, 120, 420, 24 + 20 * cap), s, hud);
            }
        }
    }

    internal static class UiFont
    {
        private static Font f;
        internal static Font Get() => f != null ? f : (f = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial" }, 18));
    }
}
