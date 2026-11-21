using Carryover.Core;
using UnityEngine;

namespace CasualtiesCarryover
{
    /// <summary>메뉴(캐릭터 선택 화면)에서만 보이는 퍽 상점. 오른쪽 위 "영구 퍽" 버튼으로 연다.
    /// 한글은 Windows 글꼴(맑은 고딕)로 그린다.</summary>
    internal class PerkMenu : MonoBehaviour
    {
        internal static PerkMenu Inst;
        internal bool Open;
        private bool inMenu;
        private float nextCheck;
        private Font font;
        private GUIStyle box, label, small, button, title;

        private void Awake() { Inst = this; DontDestroyOnLoad(gameObject); }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            inMenu = FindObjectOfType<PreRunScript>() != null;
            if (!inMenu) Open = false;
        }

        private void Styles()
        {
            if (box != null) return;
            font = UiFont.Get();
            box = new GUIStyle(GUI.skin.box) { font = font };
            box.normal.background = Tex(new Color(0.06f, 0.06f, 0.07f, 0.94f));
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true };
            label.normal.textColor = new Color(0.9f, 0.9f, 0.88f);
            small = new GUIStyle(label) { fontSize = 14 };
            small.normal.textColor = new Color(0.65f, 0.65f, 0.62f);
            title = new GUIStyle(label) { fontSize = 21, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.85f, 0.2f, 0.2f);
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16 };
        }

        private static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void OnGUI()
        {
            if (!inMenu || Plugin.Perks == null) return;
            Styles();
            var p = Plugin.Perks;
            float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);
            var m = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float w = Screen.width / s;
            if (GUI.Button(new Rect(w - 230, 12, 218, 40), $"영구 퍽 ({p.Points}점)", button)) Open = !Open;
            if (Open)
            {
                var r = new Rect(w - 560, 60, 548, 120 + PerkState.Perks.Length * 74);
                GUI.Box(r, "", box);
                GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
                GUILayout.Label("영구 퍽", title);
                GUILayout.Label($"남은 포인트: {p.Points}점  ·  한 번 산 퍽은 모든 회차에 계속 적용됩니다.", small);
                GUILayout.Space(6);
                for (int i = 0; i < PerkState.Perks.Length; i++)
                {
                    var d = PerkState.Perks[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical(GUILayout.Width(r.width - 170));
                    GUILayout.Label($"{d.Name}  {p.Levels[i]}/{d.MaxLevel}단계", label);
                    GUILayout.Label("단계마다 " + d.PerLevel, small);
                    GUILayout.EndVertical();
                    int cost = p.NextCost(i);
                    GUI.enabled = p.CanBuy(i);
                    if (GUILayout.Button(cost < 0 ? "최대" : $"구매 ({cost}점)", button, GUILayout.Width(130), GUILayout.Height(40)) && p.Buy(i))
                    {
                        Plugin.SavePerks();
                        Plugin.Log.LogInfo($"perk bought: {d.Id} -> {p.Levels[i]}, points left {p.Points}");
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
                GUILayout.Label("포인트는 죽거나 엔딩을 볼 때 받습니다: 10분 버틸 때마다 1, 새로 얻은 능력치 3마다 1, 도달한 층 2마다 1 (한 회차 최대 6, 3분 안에 끝나면 0).", small);
                GUILayout.EndArea();
            }
            GUI.matrix = m;
        }
    }
}
