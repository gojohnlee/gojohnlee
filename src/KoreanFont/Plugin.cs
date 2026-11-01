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
        bool menuDone, langYes;

        void Awake()
        {
            SceneManager.sceneLoaded += (s, m) => nextScan = 0f;
            try { Ui.Load(); Names.Load(); TMPro_EventManager.TEXT_CHANGED_EVENT.Add(Ui.OnTextChanged); }
            catch (Exception e) { Log.Write("UI 표 실패: " + e); }
        }

        // 이 게임 mscorlib는 줄어 있어 float.Parse 대신 손으로 읽는다(따로 메서드: 없는 API면 이 메서드만 실패)
        static float ReadScale(float def)
        {
            var sp = Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH") ?? "") ?? ".", "font-scale.txt");
            if (!File.Exists(sp)) return def;
            float v = 0, d = 0; bool any = false;
            foreach (var c in File.ReadAllText(sp))
            {
                if (c >= '0' && c <= '9') { any = true; if (d == 0) v = v * 10 + (c - '0'); else { v += (c - '0') / d; d *= 10; } }
                else if (c == '.' && d == 0) d = 10;
                else if (any) break;
            }
            return any && v > 0.3f && v < 2f ? v : def;
        }
        static void SetScale(TMP_FontAsset fa, float sc) { var fi = fa.faceInfo; fi.scale = sc; fa.faceInfo = fi; }

        TMP_FontAsset MakeKorean()
        {
            foreach (var f in Families)
            {
                var fa = TMP_FontAsset.CreateFontAsset(f, "Regular");
                if (fa == null) fa = TMP_FontAsset.CreateFontAsset(f, "Normal");
                if (fa != null)
                {
                    fa.name = "Korean Fallback (" + f + ")";
                    // 한글 크기 비율(font-scale.txt, 없으면 1)
                    float sc = 1f;
                    try { sc = ReadScale(sc); } catch { }
                    try { SetScale(fa, sc); } catch (Exception e) { Log.Write("크기 비율 실패: " + e.Message); }
                    Log.Write("한글 크기 비율: " + sc);
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
            Log.Write("검사: 한글 글 " + texts + "개, 글자 " + chars + "개, 없는 글자 " + miss + "개, UI 바꿈 누적 " + Ui.Replaced + ", 이벤트 " + Ui.Events);
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

        // 대화 초상 툴팁(신뢰: …)은 SetText(StringBuilder)로 넣어 TEXT_CHANGED에서 놓친다 → 30프레임마다 찾아 Reapply 목록에 넣고, 신뢰 집단 이름도 바꾼다
        void LateUpdate()
        {
            try
            {
                if (Time.frameCount % 30 == 0)
                {
                    foreach (var o in UnityEngine.Object.FindObjectsByType(typeof(ChatMugshot), FindObjectsSortMode.None)) { var m = o as ChatMugshot; if (m != null && m.overlayText != null) Ui.Track(m.overlayText); }
                    Ui.PatchList(TrustManager.trustGroupName);
                    // 게임이 켤 때마다 묻는 "Localization file found: Korean.csv — 쓸까요?" 창은 자동으로 예
                    var ad = AlertDialog.instance;
                    if (!langYes && ad != null && ad.gameObject.activeInHierarchy && ad.txtMsg != null && ad.txtMsg.text != null && ad.txtMsg.text.Contains("Localization file found"))
                    { langYes = true; ad.OnClickedOK(); Log.Write("언어 창 자동 예"); }
                }
                Ui.Reapply();
            }
            catch { }
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
            try { Names.Tick(KeepMin); } catch (Exception e) { Log.Write("이름 실패: " + e.Message); }
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
        public static int Replaced, Events;
        // 게임이 매 프레임 영어를 다시 넣는 글(빠른 설정 창 등) → 기억해 두고 LateUpdate에서 그리기 전에 다시 바꾼다
        static readonly List<TMP_Text> tracked = new List<TMP_Text>();
        static readonly HashSet<int> trackedIds = new HashSet<int>();
        static readonly Dictionary<int, string> lastOut = new Dictionary<int, string>();
        public static void Track(TMP_Text t) { if (tracked.Count < 2000 && trackedIds.Add(t.GetInstanceID())) tracked.Add(t); }
        public static void Reapply()
        {
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                var t = tracked[i];
                if (t == null) { tracked.RemoveAt(i); continue; }
                if (!t.isActiveAndEnabled) continue;
                var cur = t.text;
                if (lastOut.TryGetValue(t.GetInstanceID(), out var lo) && ReferenceEquals(lo, cur)) continue;   // 그대로면 건너뜀(매 프레임 비용 줄이기)
                var n = Translate(t.text);
                if (n != null && n != cur) { busy = true; try { t.text = n; } finally { busy = false; } }
                lastOut[t.GetInstanceID()] = t.text;
            }
        }

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

        // 번역 안 된 영어 글을 untranslated.txt에 모은다(한 번씩, 최대 3000개) → 다음 번역 표 후보
        static readonly HashSet<string> seenEn = new HashSet<string>();
        static void Untranslated(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 3 || s.Length > 600 || seenEn.Count >= 3000) return;
            int lat = 0;
            foreach (var c in s) { if (c >= 0xAC00 && c <= 0xD7A3) return; if ((c >= 65 && c <= 90) || (c >= 97 && c <= 122)) lat++; }
            if (lat < 3 || !seenEn.Add(s)) return;
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH") ?? "") ?? ".", "untranslated.txt"), EscNl(s) + "\n"); } catch { }
        }

        // 줄바꿈을 표 형식의 \n 두 글자로(이 게임 mscorlib엔 string.Replace(string,string)이 없음)
        static string EscNl(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 8);
            foreach (var c in s) { if (c == '\n') sb.Append("\\n"); else if (c != '\r') sb.Append(c); }
            return sb.ToString();
        }

        public static void OnTextChanged(UnityEngine.Object o)
        {
            Events++;
            if (busy) return;
            var t = o as TMP_Text;
            if (t == null) return;
            try
            {
                var n = Translate(t.text);
                if (n != null && n != t.text) { busy = true; t.text = n; Replaced++; Track(t); }
                else if (n == null) Untranslated(t.text);
            }
            catch (Exception e) { Log.Write("UI 바꾸기 실패: " + e.Message); }
            finally { busy = false; }
        }

        // 하수인 명령 메뉴는 정적 배열이라 직접 바꾼다
        public static void PatchList(System.Collections.Generic.List<string> l)
        {
            if (l == null) return;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && exact.TryGetValue(l[i], out var ko)) l[i] = ko;
        }

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

namespace NecroKoreanFont
{
    // 인물 이름·책 제목은 게임 데이터(Articy 모델, Book 컴포넌트)에서 직접 바꾼다(글 속 부분 바꾸기는 "Mark"→"Marked" 같은 사고가 남).
    // names-ko.tsv: 영어<TAB>한국어(통째 일치). 시험 모드면 articy-dump.tsv(대사 ID·화자)와 이름·책 제목 목록을 남긴다.
    static class Names
    {
        static readonly Dictionary<string, string> map = new Dictionary<string, string>();
        static bool modelsDone;
        static readonly HashSet<string> seenTitles = new HashSet<string>();
        static string Dir => Path.GetDirectoryName(Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH") ?? "") ?? ".";

        public static void Load()
        {
            var path = Path.Combine(Dir, "names-ko.tsv");
            if (!File.Exists(path)) return;
            foreach (var raw in File.ReadAllText(path).Split('\n'))
            {
                var line = raw.Length > 0 && raw[raw.Length - 1] == '\r' ? raw.Substring(0, raw.Length - 1) : raw;
                int tab = line.IndexOf('\t');
                if (line.Length == 0 || line[0] == '#' || tab <= 0) continue;
                map[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
            Log.Write("이름 표: " + map.Count + "개");
        }

        static string Ko(string s) => s != null && map.TryGetValue(s, out var k) ? k : null;

        // 2초마다 부름
        public static void Tick(bool dump)
        {
            if (!modelsDone && ArticyPlayer.articySetupComplete && ArticyPlayer.models.Count > 0)
            {
                modelsDone = true;
                if (dump) Dump();
                int n = 0;
                foreach (var m in ArticyPlayer.models.Values)
                {
                    var k = Ko(m.characterName); if (k != null) { m.characterName = k; n++; }
                    k = Ko(m.NAMEOVERRIDE); if (k != null) { m.NAMEOVERRIDE = k; n++; }
                }
                Log.Write("이름 바꿈: " + n + "개");
            }
            foreach (var b in Resources.FindObjectsOfTypeAll<Book>())
            {
                if (b == null || string.IsNullOrEmpty(b.bookTitle)) continue;
                var k = Ko(b.bookTitle);
                if (k != null) b.bookTitle = k;
                else if (seenTitles.Add(b.bookTitle)) Log.Write("책 제목(표에 없음): " + b.bookTitle);
            }
        }

        static void Dump()
        {
            var sb = new System.Text.StringBuilder();
            var names = new HashSet<string>();
            foreach (var m in ArticyPlayer.models.Values)
            {
                string spk = "";
                if (m.speaker != 0 && ArticyPlayer.models.TryGetValue(m.speaker, out var s)) spk = s.characterName ?? "";
                if (!string.IsNullOrEmpty(m.characterName)) names.Add(m.characterName);
                if (!string.IsNullOrEmpty(m.NAMEOVERRIDE)) names.Add(m.NAMEOVERRIDE);
                sb.Append(ArticyPlayer.LongToHex(m.id)).Append('\t').Append(m.type.ToString()).Append('\t').Append(spk).Append('\t')
                  .Append(m.inChapter).Append('\t').Append(m.type == ArticyModelType.DialogueFragment ? "" : (m.displayName ?? "")).Append('\n');
            }
            File.WriteAllText(Path.Combine(Dir, "articy-dump.tsv"), sb.ToString());
            var nb = new System.Text.StringBuilder();
            foreach (var n in names) nb.Append(n).Append('\n');
            File.WriteAllText(Path.Combine(Dir, "names-dump.txt"), nb.ToString());
            Log.Write("덤프: 모델 " + ArticyPlayer.models.Count + ", 이름 " + names.Count);
        }
    }
}
