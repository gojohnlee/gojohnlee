using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Doorstop(winhttp.dll)가 게임 시작 때 부르는 진입점. 게임 mscorlib이 줄어 있어(DateTime.ToString(string) 등 없음) 단순한 API만 쓴다.
// Module.GetPEKind가 없어 시작하다 멈추므로 BepInEx 없이 직접 띄운다.
namespace Doorstop
{
    public static class Entrypoint
    {
        public static void Start()
        {
            try
            {
                NecroKoreanFont.Log.Write("시작");
                // 지금은 Unity가 아직 준비 전 → UnityEngine이 올라온 뒤 첫 장면이 열릴 때 붙는다
                AppDomain.CurrentDomain.AssemblyLoad += (s, e) =>
                {
                    // 이 게임 mscorlib엔 e.LoadedAssembly 게터가 없음 → 올라온 어셈블리 목록에서 직접 찾는다
                    if (NecroKoreanFont.Boot.Ready) return;
                    try { foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) if (a.GetName().Name == "Assembly-CSharp") { NecroKoreanFont.Log.Write("게임 코드 올라옴"); NecroKoreanFont.Boot.Hook(); break; } }
                    catch (Exception x) { NecroKoreanFont.Log.Write("훅 실패: " + x); }
                };
            }
            catch (Exception e)
            {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "KoreanFont-start-error.txt"), e.ToString()); } catch { }
            }
        }
    }
}

namespace NecroKoreanFont
{
    static class Log
    {
        // Doorstop이 넣어 주는 DLL 경로 → 없으면 DLL 위치 → 없으면 임시 폴더
        static readonly string FilePath = Path.Combine(Dir(), "KoreanFont.log");
        static string Dir()
        {
            try
            {
                var p = Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH");
                if (string.IsNullOrEmpty(p)) p = typeof(Log).Assembly.Location;
                var d = string.IsNullOrEmpty(p) ? null : Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(d)) return d;
            }
            catch { }
            return Path.GetTempPath();
        }
        static bool first = true;
        public static void Write(string s)
        {
            try
            {
                if (first) { File.WriteAllText(FilePath, ""); first = false; }
                File.AppendAllText(FilePath, s + "\n");
            }
            catch { }
        }
    }

    static class Boot
    {
        static bool hooked, made;
        public static bool Ready => hooked;
        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            SceneManager.sceneLoaded += OnScene;
            Log.Write("장면 대기");
        }
        static void OnScene(Scene s, LoadSceneMode m)
        {
            if (made) return;
            made = true;
            try
            {
                var go = new GameObject("KoreanFont");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<Runner>();
            }
            catch (Exception e) { Log.Write("만들기 실패: " + e); }
        }
    }

    // 게임 폰트(PERTILI SDF 등)에 한글이 없어 □로 나오는 문제:
    // Windows 한글 폰트로 동적 폰트를 만들어 모든 TMP 폰트의 대체(fallback) 목록에 붙인다.
    public class Runner : MonoBehaviour
    {
        static readonly string[] Families = { "Malgun Gothic", "맑은 고딕", "Noto Sans KR", "Gulim", "굴림" };
        TMP_FontAsset korean;
        readonly HashSet<int> done = new HashSet<int>();
        float nextScan;
        bool menuDone;

        void Awake()
        {
            SceneManager.sceneLoaded += (s, m) => nextScan = 0f;
            try { Ui.Load(); TMPro_EventManager.TEXT_CHANGED_EVENT.Add(Ui.OnTextChanged); }
            catch (Exception e) { Log.Write("UI 표 실패: " + e); }
        }

        TMP_FontAsset MakeKorean()
        {
            foreach (var f in Families)
            {
                var fa = TMP_FontAsset.CreateFontAsset(f, "Regular");
                if (fa == null) fa = TMP_FontAsset.CreateFontAsset(f, "Normal");
                if (fa != null)
                {
                    fa.name = "Korean Fallback (" + f + ")";
                    DontDestroyOnLoad(fa);
                    Log.Write("한글 대체 폰트: " + f);
                    return fa;
                }
            }
            Log.Write("한글 폰트를 찾지 못함");
            return null;
        }

        // 시험 때(NECRO_MIN=1로 켬)만 창을 계속 내려 둔다 — 평소 플레이엔 영향 없음
        static readonly bool KeepMin = Environment.GetEnvironmentVariable("NECRO_MIN") == "1";
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int c);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
        float nextMin;
        float nextCheck = 30f;

        // 시험 때만: 번역 파일 사용 여부·안내 창('Localization'·'Language File'·'community')을 자동으로 OK
        static void AcceptLanguagePrompt()
        {
            var d = AlertDialog.instance;
            if (d == null || !d.gameObject.activeInHierarchy) return;
            foreach (var t in d.GetComponentsInChildren<TMP_Text>())
            {
                var s = t.text;
                if (s != null && (s.Contains("Localization") || s.Contains("Language File") || s.Contains("community")))
                { Log.Write("언어 창 OK"); d.OnClickedOK(); return; }
            }
        }

        static void CheckGlyphs()
        {
            int texts = 0, chars = 0, miss = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null || !t.isActiveAndEnabled || t.font == null || string.IsNullOrEmpty(t.text)) continue;
                bool ko = false;
                foreach (var c in t.text)
                {
                    if (c < 0xAC00 || c > 0xD7A3) continue;
                    ko = true; chars++;
                    if (!t.font.HasCharacter(c, true, true)) miss++;
                }
                if (ko && texts++ < 6) Log.Write("보기: [" + t.font.name + "] " + (t.text.Length > 40 ? t.text.Substring(0, 40) : t.text));
            }
            Log.Write("검사: 한글 글 " + texts + "개, 글자 " + chars + "개, 없는 글자 " + miss + "개, UI 바꿈 누적 " + Ui.Replaced);
            int en = 0;   // 아직 영어로 남은 글(번역 표에 넣을 후보)
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null || !t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || en >= 12) continue;
                bool han = false, lat = false;
                foreach (var c in t.text) { if (c >= 0xAC00 && c <= 0xD7A3) han = true; else if ((c >= 65 && c <= 90) || (c >= 97 && c <= 122)) lat = true; }
                if (lat && !han) { en++; Log.Write("영어 남음: " + (t.text.Length > 60 ? t.text.Substring(0, 60) : t.text)); }
            }
        }

        // Process.MainWindowHandle도 이 게임 System.dll에서 잘려 있음 → 내 프로세스의 Unity 창을 user32로 찾는다
        static void Minimize()
        {
            uint me = GetCurrentProcessId();
            for (var h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "UnityWndClass", null); h != IntPtr.Zero;
                 h = FindWindowEx(IntPtr.Zero, h, "UnityWndClass", null))
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == me && !IsIconic(h)) ShowWindowAsync(h, 6);
            }
        }

        void Update()
        {
            if (KeepMin && Time.unscaledTime >= nextMin)
            {
                nextMin = Time.unscaledTime + 0.5f;
                try { Minimize(); } catch { }
                try { AcceptLanguagePrompt(); } catch { }
                // 시험 때 확인용(최소화라 화면은 못 찍음): 30초마다 떠 있는 한글 글자가 폰트에서 다 찾아지는지 센다
                if (Time.unscaledTime > nextCheck) { nextCheck = Time.unscaledTime + 30f; try { CheckGlyphs(); } catch (Exception e) { Log.Write("검사 실패: " + e); } }
            }
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 2f;
            try { Scan(); }
            catch (Exception e) { Log.Write("오류: " + e); nextScan = Time.unscaledTime + 30f; }
        }

        void Scan()
        {
            if (korean == null)
            {
                korean = MakeKorean();
                if (korean == null) { nextScan = float.MaxValue; return; }
            }
            // 전역 대체 목록(모든 폰트가 마지막에 봄)
            if (TMP_Settings.instance != null)
            {
                var g = TMP_Settings.fallbackFontAssets ?? new List<TMP_FontAsset>();
                if (!g.Contains(korean)) { g.RemoveAll(x => x == null); g.Add(korean); TMP_Settings.fallbackFontAssets = g; }
            }
            // 폰트마다 대체 목록 — TMP는 목록 중간에 null이 있으면 거기서 멈추므로 null을 지움
            int added = 0;
            foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (fa == korean || !done.Add(fa.GetInstanceID())) continue;
                var list = fa.fallbackFontAssetTable ?? new List<TMP_FontAsset>();
                list.RemoveAll(x => x == null);
                if (!list.Contains(korean)) list.Add(korean);
                fa.fallbackFontAssetTable = list;
                added++;
            }
            if (added > 0)
            {
                Log.Write("대체 폰트 붙임: " + added + "개");
                foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                    if (t != null && t.isActiveAndEnabled) { Ui.OnTextChanged(t); t.SetAllDirty(); }
            }
            if (!menuDone) { menuDone = true; try { Ui.PatchMenu(); } catch (Exception e) { Log.Write("메뉴 실패: " + e.Message); } }
        }
    }
}

namespace NecroKoreanFont
{
    // 게임 업데이트로 한글 패치의 Assembly-CSharp(코드 속 UI 문자열)가 빠짐 → ui-ko.tsv 표로 화면 글자를 바꾼다.
    // TMP가 글을 새로 그릴 때마다(TEXT_CHANGED_EVENT) 검사. 이 게임 mscorlib엔 string.Replace(string,string)이 없어 직접 바꾼다.
    static class Ui
    {
        static readonly Dictionary<string, string> exact = new Dictionary<string, string>();
        static readonly List<string> fromList = new List<string>(), toList = new List<string>();   // 부분 바꾸기, 긴 것부터
        static bool busy;
        public static int Replaced;

        public static void Load()
        {
            var dir = Path.GetDirectoryName(Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH") ?? "");
            var path = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "ui-ko.tsv");
            if (!File.Exists(path)) { Log.Write("ui-ko.tsv 없음"); return; }
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (var raw in File.ReadAllText(path).Split('\n'))
            {
                var line = raw.Length > 0 && raw[raw.Length - 1] == '\r' ? raw.Substring(0, raw.Length - 1) : raw;
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                var en = Unesc(line.Substring(0, tab)); var ko = Unesc(line.Substring(tab + 1));
                if (en[0] == '=') exact[en.Substring(1)] = ko;
                else pairs.Add(new KeyValuePair<string, string>(en, ko));
            }
            pairs.Sort((x, y) => y.Key.Length - x.Key.Length);
            foreach (var p in pairs) { fromList.Add(p.Key); toList.Add(p.Value); }
            Log.Write("UI 표: 통째 " + exact.Count + "개, 부분 " + fromList.Count + "개");
        }

        static string Unesc(string s)
        {
            const string esc = "\\n";   // 표 파일의 글자 그대로 백슬래시+n
            if (!s.Contains(esc)) return s;
            var r = ""; int i = 0, j;
            while ((j = s.IndexOf(esc, i, StringComparison.Ordinal)) >= 0) { r = string.Concat(r, s.Substring(i, j - i), "\n"); i = j + 2; }
            return r + s.Substring(i);
        }

        // 바꿀 게 있으면 바뀐 글, 없으면 null
        public static string Translate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (exact.TryGetValue(s, out var e)) return e;
            bool letters = false;
            for (int i = 0; i < s.Length && !letters; i++) { char c = s[i]; letters = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'); }
            if (!letters) return null;
            string cur = s; bool changed = false;
            for (int k = 0; k < fromList.Count; k++)
            {
                var f = fromList[k];
                if (f.Length > cur.Length || !cur.Contains(f)) continue;
                // 바꾼 뒤 글에서 다시 찾지 않게 왼쪽부터 쌓는다
                string done = "", rest = cur; int j;
                while ((j = rest.IndexOf(f, StringComparison.Ordinal)) >= 0) { done = string.Concat(done, rest.Substring(0, j), toList[k]); rest = rest.Substring(j + f.Length); }
                cur = done + rest; changed = true;
            }
            return changed ? cur : null;
        }

        public static void OnTextChanged(UnityEngine.Object o)
        {
            if (busy) return;
            var t = o as TMP_Text;
            if (t == null) return;
            try
            {
                var n = Translate(t.text);
                if (n != null && n != t.text) { busy = true; t.text = n; Replaced++; }
            }
            catch (Exception e) { Log.Write("UI 바꾸기 실패: " + e.Message); }
            finally { busy = false; }
        }

        // 하수인 명령 메뉴는 정적 배열이라 직접 바꾼다
        public static void PatchMenu()
        {
            var m = SamsContextMenu.menuText;
            if (m == null) return;
            int n = 0;
            for (int i = 0; i < m.Length; i++) if (m[i] != null && exact.TryGetValue(m[i], out var ko)) { m[i] = ko; n++; }
            if (n > 0) Log.Write("명령 메뉴 " + n + "개 바꿈");
        }
    }
}
