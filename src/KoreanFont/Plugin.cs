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
        bool menuDone, langYes, langOk;

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
                    // 이어서 뜨는 "Loaded Community Language File …" 알림도 자동 확인
                    else if (!langOk && ad != null && ad.gameObject.activeInHierarchy && ad.txtMsg != null && ad.txtMsg.text != null && ad.txtMsg.text.Contains("Loaded Community Language File"))
                    { langOk = true; ad.OnClickedOK(); Log.Write("언어 불러옴 창 자동 확인"); }
                }
                Ui.Reapply();
                Ui.Redraw();
            }
            catch { }
        }

        void Update()
        {
            try { WideCam.Tick(); } catch { }
            try { WildMap.Tick(); } catch { }
            try { MapDump.Tick(); } catch { }
            try { QuestMap.Tick(); } catch (Exception e) { QuestMap.Fail(e); }
            if (!GraphDump.done) { try { GraphDump.Tick(); } catch (Exception e) { GraphDump.done = true; Log.Write("대화 자료 저장 실패: " + e); } }
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

        static bool hangulWrap;
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
                // 한글을 글자마다가 아니라 띄어쓰기에서 줄바꿈(꺼져 있으면 "갚으/라고"처럼 낱말 중간에서 끊김)
                if (!hangulWrap)
                {
                    hangulWrap = true;
                    var f = typeof(TMP_Settings).GetField("m_UseModernHangulLineBreakingRules", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (f != null) { f.SetValue(TMP_Settings.instance, true); Log.Write("한글 낱말 줄바꿈 켬"); } else Log.Write("한글 줄바꿈 설정 없음");
                }
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
    // 성문 밖 들판에서 카메라를 더 눕히고(지평선이 보이게) 더 멀리 뺄 수 있게 한다. 성 안·실내로 돌아가면 게임 값으로 되돌림.
    // 게임 RtsCamera: Tilt 90=수직, LateUpdate가 Tilt를 MinTilt~MaxTilt로, 거리를 MinDistance~MaxDistance로 자른다(값은 장면에 저장, 코드엔 기본 30~85·8~32).
    static class WideCam
    {
        const float OutMinTilt = 15f, OutStartTilt = 35f, DistMult = 1.6f;
        static RtsCamera cam; static bool on; static float minTilt, maxDist, tiltBefore, nextLog;
        public static void Tick()
        {
            var c = RtsCamera.instance;
            if (c == null || GameManager.gameState != GameStates.PLAYING) return;
            if (c != cam) { cam = c; on = false; }
            bool outside = !TownGatesTrigger.playerIsInsideTown && Character.thePlayer != null && !Character.thePlayer.IsIndoors();
            if (outside && !on)
            {
                minTilt = c.MinTilt; maxDist = c.MaxDistance; tiltBefore = c.Tilt; on = true;
                Note(c, "성 밖 카메라 켬(게임 값)");
                if (!ConversationWindow.isVisible && c.Tilt > OutStartTilt) c.Tilt = OutStartTilt;
            }
            else if (!outside && on)
            {
                c.MinTilt = minTilt; c.MaxDistance = maxDist; on = false;
                if (!ConversationWindow.isVisible && c.Tilt < minTilt) c.Tilt = tiltBefore;
                Log.Write("성 밖 카메라 끔");
            }
            if (!on) return;
            if (c.MinTilt > OutMinTilt) c.MinTilt = OutMinTilt;
            if (c.MaxDistance < maxDist * DistMult) c.MaxDistance = maxDist * DistMult;
            if (Time.unscaledTime > nextLog) { nextLog = Time.unscaledTime + 20f; Note(c, "성 밖 카메라"); }
        }
        // 기록은 따로(없는 API로 JIT가 실패해도 카메라 보정은 살게)
        static void Note(RtsCamera c, string what)
        {
            try { Log.Write(what + ": 기울기 " + c.Tilt.ToString("0") + " (범위 " + c.MinTilt.ToString("0") + "~" + c.MaxTilt.ToString("0") + "), 거리 " + c.Distance.ToString("0.0") + "/최대 " + c.MaxDistance.ToString("0.0") + ", 잠김 " + (c.tiltIsLocked ? "예" : "아니오")); } catch { }
        }
    }

    // 성 밖 지도에도 "내 위치" 표시. 게임(MapManager.RecalcArrowPositionsAndIAmHereMarker)은 성 안에서만 rtIAmHere를 켜고 성 밖은 끈다.
    // 성 밖 기준점 = 지도 그림의 자식 중 성 밖 MapArrow(빠른 이동 지점, worldPos)와 inTown=false인 MapTiePoint(worldPos).
    // 월드 x·z → 지도 좌표를 최소제곱 1차식으로 맞추고, 기준점에서 남는 오차는 거리 가중으로 보정(기준점에선 정확히 맞음).
    static class WildMap
    {
        static List<Vector3> w; static List<Vector2> m; static double[] ax, ay; static Vector2[] res; static bool logged; static MapManager built;
        public static void Tick()
        {
            var mm = MapManager.instance;
            if (mm == null || mm.mapFrame == null || !mm.mapFrame.activeInHierarchy || TownGatesTrigger.playerIsInsideTown) return;
            var p = Character.thePlayer;
            if (p == null || p.IsIndoors() || mm.rtIAmHere == null) return;
            Vector2 at;
            if (!ToMap(mm, p.transform.position, out at)) return;
            var rt = mm.rtIAmHere;
            if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
            rt.anchoredPosition = at;
            rt.SetAsLastSibling();
        }
        // 월드 좌표 → 성 밖 지도 위 좌표(anchoredPosition)
        public static bool ToMap(MapManager mm, Vector3 pos, out Vector2 at)
        {
            at = Vector2.zero;
            if (mm.mapImage == null || mm.mapImage.mainTexture == null) return false;
            if (built != mm) { w = null; built = mm; }
            if (w == null && !Build(mm)) return false;
            var size = mm.mapImage.GetComponent<RectTransform>().sizeDelta;
            float sx = size.x / mm.mapImage.mainTexture.width, sy = size.y / mm.mapImage.mainTexture.height;
            double x = ax[0] * pos.x + ax[1] * pos.z + ax[2], y = ay[0] * pos.x + ay[1] * pos.z + ay[2];
            double ws = 0, rx = 0, ry = 0;
            for (int i = 0; i < w.Count; i++)
            {
                double dx = w[i].x - pos.x, dz = w[i].z - pos.z, d2 = dx * dx + dz * dz;
                if (d2 < 0.01) { ws = 1; rx = res[i].x; ry = res[i].y; break; }
                double f = 1.0 / d2; ws += f; rx += res[i].x * f; ry += res[i].y * f;
            }
            if (ws > 0) { x += rx / ws; y += ry / ws; }
            at = new Vector2((float)x * sx, (float)y * sy);
            return true;
        }
        static bool Build(MapManager mm)
        {
            w = new List<Vector3>(); m = new List<Vector2>();
            foreach (Transform t in mm.mapImage.transform)
            {
                var a = t.GetComponent<MapArrow>();
                if (a != null && !a.isInTown && a.worldPos != Vector3.zero) { w.Add(a.worldPos); m.Add(a.nominalPos); continue; }
                var tp = t.GetComponent<MapTiePoint>();
                if (tp != null && !tp.inTown) { w.Add(tp.worldPos); m.Add(tp.nominalPos); }
            }
            if (w.Count < 3) { Log.Write("성 밖 지도: 기준점 " + w.Count + "개라 내 위치 못 띄움"); w = null; return false; }
            // 정규방정식 (X^T X) a = X^T b, X 행 = [wx, wz, 1]
            double[,] A = new double[3, 3]; double[] bx = new double[3], by = new double[3];
            for (int i = 0; i < w.Count; i++)
            {
                double[] r = { w[i].x, w[i].z, 1 };
                for (int j = 0; j < 3; j++) { for (int k = 0; k < 3; k++) A[j, k] += r[j] * r[k]; bx[j] += r[j] * m[i].x; by[j] += r[j] * m[i].y; }
            }
            ax = Solve(A, bx); ay = Solve(A, by);
            if (ax == null || ay == null) { Log.Write("성 밖 지도: 기준점이 한 줄로 늘어서 계산 못 함"); w = null; return false; }
            res = new Vector2[w.Count]; double worst = 0;
            for (int i = 0; i < w.Count; i++)
            {
                double ex = m[i].x - (ax[0] * w[i].x + ax[1] * w[i].z + ax[2]), ey = m[i].y - (ay[0] * w[i].x + ay[1] * w[i].z + ay[2]);
                res[i] = new Vector2((float)ex, (float)ey); worst = Math.Max(worst, Math.Sqrt(ex * ex + ey * ey));
            }
            if (!logged) { logged = true; Log.Write("성 밖 지도: 기준점 " + w.Count + "개, 1차식 최대 오차 " + worst.ToString("0") + "px(그림 기준, 보정 전)"); }
            return true;
        }
        static double[] Solve(double[,] A, double[] b)
        {
            double[,] M = new double[3, 4];
            for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) M[i, j] = A[i, j]; M[i, 3] = b[i]; }
            for (int c = 0; c < 3; c++)
            {
                int piv = c; for (int r = c + 1; r < 3; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[piv, c])) piv = r;
                if (Math.Abs(M[piv, c]) < 1e-9) return null;
                for (int k = 0; k < 4; k++) { var t = M[c, k]; M[c, k] = M[piv, k]; M[piv, k] = t; }
                for (int r = 0; r < 3; r++) if (r != c) { double f = M[r, c] / M[c, c]; for (int k = 0; k < 4; k++) M[r, k] -= f * M[c, k]; }
            }
            return new[] { M[0, 3] / M[0, 0], M[1, 3] / M[1, 1], M[2, 3] / M[2, 2] };
        }
    }

    // 퀘스트 위치 표시 준비: 지도를 처음 열 때 지도 지점(이름·성 안/밖·지도 좌표·월드 좌표)과 NPC 이름·위치를 KoreanFont.log에 한 번 남긴다.
    static class MapDump
    {
        static bool done;
        public static void Tick()
        {
            if (done) return;
            var mm = MapManager.instance;
            if (mm == null || mm.mapFrame == null || !mm.mapFrame.activeInHierarchy || mm.mapImage == null) return;
            done = true;
            foreach (Transform t in mm.mapImage.transform)
            {
                try
                {
                    var tmp = t.GetComponentInChildren<TextMeshProUGUI>();
                    string label = tmp != null && tmp.text != null ? tmp.text.Replace("\n", " ") : "";
                    var a = t.GetComponent<MapArrow>(); var tp = t.GetComponent<MapTiePoint>(); var il = t.GetComponent<MapInteriorLocation>();
                    string kind = a != null ? "지점" : tp != null ? "기준" : il != null ? "실내" : "기타";
                    string town = a != null ? (a.isInTown ? "성안" : "성밖") : tp != null ? (tp.inTown ? "성안" : "성밖") : "";
                    string extra = a != null ? " 변수=" + a.articyVariableName + " 월드=" + V(a.worldPos) : tp != null ? " 월드=" + V(tp.worldPos) : il != null ? " 실내=" + il.interiorName : "";
                    Log.Write("지도목록\t" + kind + "\t" + town + "\t" + t.name + "\t" + label + "\t" + extra);
                }
                catch { }
            }
            try { foreach (var c in CharactersManager.checkForNearestCharactersList) if (c != null) Log.Write("인물목록\t" + c.myName + "\t" + c.name + "\t" + V(c.transform.position)); } catch { }
        }
        static string V(Vector3 v) { return v.x.ToString("0") + "," + v.z.ToString("0"); }
    }

    // 지도에 지금 일지 '할 일'의 위치를 금색 점으로 표시. 짝 표 = todo-map.tsv(L:GameLocation, N:CharacterName, 앞에서부터 찾아지는 것 하나).
    // 성 안 지도: 성 안 MapTiePoint 거리 가중(게임과 같은 식), 실내면 MapInteriorLocation. 성 밖 지도: WildMap.ToMap. 다른 쪽 지도에 속한 곳은 안 그림.
    static class QuestMap
    {
        static Dictionary<string, string[]> table; static readonly List<RectTransform> dots = new List<RectTransform>();
        static readonly HashSet<string> warned = new HashSet<string>(); static bool failed; static MapManager built;
        static List<Vector3> tw; static List<Vector2> tm; static Dictionary<string, Vector2> interiors; static float minX, maxX, minZ, maxZ;
        public static void Fail(Exception e) { if (!failed) { failed = true; Log.Write("퀘스트 지도 실패: " + e); } }
        public static void Tick()
        {
            var mm = MapManager.instance;
            bool open = mm != null && mm.mapFrame != null && mm.mapFrame.activeInHierarchy && mm.rtIAmHere != null;
            int used = 0;
            if (open)
            {
                if (table == null) Load();
                if (built != mm) { built = mm; tw = null; dots.Clear(); }
                if (tw == null) BuildTown(mm);
                var todo = JournalManager.GetJournalEntry(0);
                if (todo != null && todo.opened)
                    foreach (var v in todo.addedParts)
                    {
                        if (v == null || v.name == null || !v.name.StartsWith("JournalToDo.")) continue;
                        string[] ts; if (!table.TryGetValue(v.name.Substring(12), out ts)) continue;
                        Vector2 at;
                        if (Place(mm, ts, out at)) Dot(mm, used++, at);
                    }
            }
            for (int i = used; i < dots.Count; i++) if (dots[i] != null && dots[i].gameObject.activeSelf) dots[i].gameObject.SetActive(false);
        }
        static bool Place(MapManager mm, string[] ts, out Vector2 at)
        {
            at = Vector2.zero;
            foreach (var t in ts)
            {
                Vector3 pos; string inside;
                if (!Resolve(t, out pos, out inside)) continue;
                bool townMap = TownGatesTrigger.playerIsInsideTown;
                if (!string.IsNullOrEmpty(inside))
                {
                    Vector2 il;
                    if (townMap && interiors.TryGetValue(inside, out il)) { at = Scale(mm, il); return true; }
                    continue; // 성 밖 건물·지하는 지도 위 자리를 모름
                }
                bool inTown = tw.Count > 0 && pos.x >= minX && pos.x <= maxX && pos.z >= minZ && pos.z <= maxZ;
                if (townMap) { if (inTown && TownPos(mm, pos, out at)) return true; continue; }
                if (WildMap.ToMap(mm, pos, out at)) return true;
            }
            return false;
        }
        static bool Resolve(string t, out Vector3 pos, out string inside)
        {
            pos = Vector3.zero; inside = "";
            if (t.StartsWith("L:"))
            {
                GameLocation gl;
                if (!LocationMarker.gameLocationsLookup.TryGetValue(t.Substring(2), out gl)) { Warn(t); return false; }
                var lm = GameManager.GetLocation(gl, showError: false);
                if (lm == null) return false;
                pos = lm.transform.position; inside = lm.insideInteriorPrefab ?? ""; return true;
            }
            if (t.StartsWith("N:"))
            {
                CharacterName cn;
                if (!Character.characterNamesLookup.TryGetValue(t.Substring(2), out cn)) { Warn(t); return false; }
                var c = CharactersManager.GetCharacterScriptIfInstantiated(cn);
                if (c == null || !c.gameObject.activeInHierarchy) return false;
                pos = c.transform.position; inside = c.currentlyInsideInteriorPrefabName ?? ""; return true;
            }
            return false;
        }
        static void Warn(string t) { if (warned.Add(t)) Log.Write("퀘스트 지도: 게임에 없는 이름 " + t); }
        static Vector2 Scale(MapManager mm, Vector2 nominal)
        {
            var size = mm.mapImage.GetComponent<RectTransform>().sizeDelta;
            return new Vector2(nominal.x * size.x / mm.mapImage.mainTexture.width, nominal.y * size.y / mm.mapImage.mainTexture.height);
        }
        static bool TownPos(MapManager mm, Vector3 pos, out Vector2 at)
        {
            at = Vector2.zero; pos.y = 0f; double ws = 0, x = 0, y = 0;
            for (int i = 0; i < tw.Count; i++)
            {
                var d = pos - tw[i]; double d2 = d.x * d.x + d.z * d.z; if (d2 < 0.01) d2 = 0.01;
                double f = 10000.0 / d2; ws += f; x += tm[i].x * f; y += tm[i].y * f;
            }
            if (ws <= 0) return false;
            at = Scale(mm, new Vector2((float)(x / ws), (float)(y / ws))); return true;
        }
        static void BuildTown(MapManager mm)
        {
            tw = new List<Vector3>(); tm = new List<Vector2>(); interiors = new Dictionary<string, Vector2>();
            minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
            foreach (Transform t in mm.mapImage.transform)
            {
                var tp = t.GetComponent<MapTiePoint>();
                if (tp != null && tp.inTown)
                {
                    tw.Add(tp.worldPos); tm.Add(tp.nominalPos);
                    minX = Math.Min(minX, tp.worldPos.x); maxX = Math.Max(maxX, tp.worldPos.x); minZ = Math.Min(minZ, tp.worldPos.z); maxZ = Math.Max(maxZ, tp.worldPos.z);
                    continue;
                }
                var il = t.GetComponent<MapInteriorLocation>();
                if (il != null && il.interiorName != null && !interiors.ContainsKey(il.interiorName)) interiors.Add(il.interiorName, il.nominalPos);
            }
            float px = (maxX - minX) * 0.05f, pz = (maxZ - minZ) * 0.05f; minX -= px; maxX += px; minZ -= pz; maxZ += pz;
        }
        static void Dot(MapManager mm, int i, Vector2 at)
        {
            while (dots.Count <= i)
            {
                var go = UnityEngine.Object.Instantiate(mm.rtIAmHere.gameObject, mm.rtIAmHere.parent);
                go.name = "QuestDot";
                foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) { g.color = new Color(1f, 0.78f, 0.15f, 1f); g.raycastTarget = false; }
                var rt = go.GetComponent<RectTransform>(); rt.localScale = mm.rtIAmHere.localScale * 0.8f;
                dots.Add(rt);
            }
            var d = dots[i];
            if (!d.gameObject.activeSelf) d.gameObject.SetActive(true);
            d.anchoredPosition = at;
            d.SetSiblingIndex(Math.Max(0, mm.rtIAmHere.GetSiblingIndex()));
        }
        static void Load()
        {
            table = new Dictionary<string, string[]>();
            try
            {
                var dir = Path.GetDirectoryName(Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH") ?? "") ?? ".";
                var path = Path.Combine(dir, "todo-map.tsv");
                if (!File.Exists(path)) { Log.Write("퀘스트 지도: todo-map.tsv 없음"); return; }
                foreach (var raw in File.ReadAllText(path).Split('\n'))
                {
                    var line = raw.TrimEnd('\r'); if (line.Length == 0 || line[0] == '#') continue;
                    int tab = line.IndexOf('\t'); if (tab <= 0) continue;
                    table[line.Substring(0, tab)] = line.Substring(tab + 1).Split(',');
                }
                Log.Write("퀘스트 지도: 짝 " + table.Count + "줄");
            }
            catch (Exception e) { Log.Write("퀘스트 지도 표 읽기 실패: " + e.Message); }
        }
    }

    // 숨은 퀘스트 분석용: 게임이 불러온 대화 자료(ArticyPlayer.models·pins)를 KoreanFont\\dialogue-graph.tsv로 한 번 저장(이미 있으면 안 함).
    // 게임 회사 자료라 공개 저장소에 올리지 않는다(사용자가 대화로 보내 줌).
    static class GraphDump
    {
        public static bool done;
        static string Clean(string t) { return t == null ? "" : t.Replace("\t", " ").Replace("\r", " ").Replace("\n", " / "); }
        static string Ids(List<long> l) { var sb = new System.Text.StringBuilder(); if (l != null) foreach (var x in l) { if (sb.Length > 0) sb.Append(','); sb.Append(x.ToString("X")); } return sb.ToString(); }
        public static void Tick()
        {
            if (!ArticyPlayer.articySetupComplete) return;
            done = true;
            var dir = Path.GetDirectoryName(Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH") ?? "") ?? ".";
            var path = Path.Combine(dir, "dialogue-graph.tsv");
            if (File.Exists(path)) return;
            var sb = new System.Text.StringBuilder(8 << 20);
            sb.Append("# M id type speaker name text item drop var visited in out custom | P id type owner targets expr\n");
            foreach (var kv in ArticyPlayer.models)
            {
                var m = kv.Value; if (m == null) continue;
                sb.Append("M\t").Append(m.id.ToString("X")).Append('\t').Append(m.type.ToString()).Append('\t').Append(m.speaker.ToString("X")).Append('\t')
                  .Append(Clean(m.displayName)).Append('\t').Append(Clean(m.text)).Append('\t').Append(m.linkToInventoryItem.ToString("X")).Append('\t')
                  .Append(m.isItemDrop ? "1" : "0").Append('\t').Append(m.linkedArticyVariable != null ? m.linkedArticyVariable.name : "").Append('\t')
                  .Append(m.hasBeenVisited ? "1" : "0").Append('\t').Append(Ids(m.myInputPins)).Append('\t').Append(Ids(m.myOutputPins)).Append('\t').Append(Clean(m.CUSTOMCODE)).Append('\n');
            }
            foreach (var kv in ArticyPlayer.pins)
            {
                var p = kv.Value; if (p == null) continue;
                var ex = new System.Text.StringBuilder();
                if (p.expressions != null) foreach (var e in p.expressions) { if (e == null) continue; if (ex.Length > 0) ex.Append(" ;; "); ex.Append(Clean(e.unparsedExpression)); }
                sb.Append("P\t").Append(p.id.ToString("X")).Append('\t').Append(p.type.ToString()).Append('\t').Append(p.attachedToModel != null ? p.attachedToModel.id.ToString("X") : "").Append('\t')
                  .Append(Ids(p.targetPins)).Append('\t').Append(ex.ToString()).Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
            Log.Write("대화 자료 저장: " + path + " (" + ArticyPlayer.models.Count + "개 + 핀 " + ArticyPlayer.pins.Count + "개)");
        }
    }

    static class Ui
    {
        static readonly Dictionary<string, string> exact = new Dictionary<string, string>();
        static readonly List<string> fromList = new List<string>(), toList = new List<string>();   // 부분 바꾸기, 긴 것부터
        static bool busy;
        public static int Replaced, Events;
        public static void AddExact(string en, string ko) { if (!exact.ContainsKey(en)) exact[en] = ko; }

        // 번역문의 "[he]은(는)"은 게임이 [he]를 "그"로 바꿔도 조사가 "은(는)"으로 남음 → 앞 글자 받침을 보고 고른다(태그 <…>는 건너뜀)
        static readonly string[][] josaPats = {
            new[] { "은(는)", "은", "는" }, new[] { "는(은)", "은", "는" }, new[] { "이(가)", "이", "가" }, new[] { "가(이)", "이", "가" },
            new[] { "을(를)", "을", "를" }, new[] { "를(을)", "을", "를" }, new[] { "과(와)", "과", "와" }, new[] { "와(과)", "과", "와" },
            new[] { "아(야)", "아", "야" }, new[] { "(으)로", "으로", "로" }, new[] { "(이)", "이", "" } };
        public static string Josa(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('(') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                string[] hit = null;
                if (s[i] == '(' || (i + 1 < s.Length && s[i + 1] == '('))
                    foreach (var p in josaPats) if (string.CompareOrdinal(s, i, p[0], 0, p[0].Length) == 0) { hit = p; break; }
                char prev = hit == null ? '\0' : PrevSyllable(sb);
                if (prev == '\0') { sb.Append(s[i]); i++; continue; }
                int jong = (prev - 0xAC00) % 28;
                bool bat = jong != 0;
                if (hit[0] == "(으)로" && jong == 8) bat = false;   // ㄹ 받침은 "로"
                sb.Append(bat ? hit[1] : hit[2]);
                i += hit[0].Length;
            }
            return sb.ToString();
        }
        static char PrevSyllable(System.Text.StringBuilder sb)
        {
            int k = sb.Length - 1;
            while (k >= 0 && sb[k] == '>') { int lt = k; while (lt >= 0 && sb[lt] != '<') lt--; if (lt < 0) return '\0'; k = lt - 1; }
            if (k < 0) return '\0';
            var c = sb[k];
            return c >= 0xAC00 && c <= 0xD7A3 ? c : '\0';
        }

        // 손글씨 글꼴(주문서 등)은 줄 높이가 낮거나 줄 간격이 음수라 키 큰 한글이 겹침 → 한글이 든 글 중 줄 높이가 1em 미만(확실히 겹침)인 것만 1.3em으로. 괜찮던 대사·메뉴는 그대로
        const float MinLineEm = 1.3f;
        static readonly HashSet<int> spaced = new HashSet<int>();
        static void FixLineSpacing(TMP_Text t)
        {
            var fa = t.font;
            if (fa == null || spaced.Contains(t.GetInstanceID())) return;
            var s = t.text; bool ko = false;
            if (s != null) foreach (var c in s) if (c >= 0xAC00 && c <= 0xD7A3) { ko = true; break; }
            if (!ko) return;
            spaced.Add(t.GetInstanceID());
            var fi = fa.faceInfo;
            if (fi.pointSize <= 0) return;
            float em = fi.lineHeight / fi.pointSize * fi.scale + t.lineSpacing / 100f;
            if (em >= 1f) return;
            float before = t.lineSpacing;
            t.lineSpacing = before + (MinLineEm - em) * 100f;
            redraw.Add(t);
            Log.Write("줄 간격 넓힘: " + fa.name + " " + before.ToString("0") + " → " + t.lineSpacing.ToString("0") + " (" + t.name + ")");
        }
        // 명령 메뉴(무덤·하수인·시체) 한 줄은 높이 30으로 꽉 맞춘 칸 → 키 큰 한글 대체 폰트가 칸을 넘으면 잘라내기 모드에서 글이 통째로 사라짐 → 넘쳐도 보이게
        static readonly HashSet<int> menuChecked = new HashSet<int>();
        static void FixMenuLine(TMP_Text t)
        {
            if (!menuChecked.Add(t.GetInstanceID())) return;
            var p = t.transform;
            while (p != null && p.GetComponent<SamsContextMenuLine>() == null) p = p.parent;
            if (p == null) return;
            var rt = t.rectTransform;
            Log.Write("명령 메뉴 줄: \"" + t.text + "\" 넘침=" + t.overflowMode + " 높이=" + rt.rect.height.ToString("0") + " 글높이=" + t.preferredHeight.ToString("0") + " 크기=" + t.fontSize.ToString("0") + " 색a=" + t.color.a.ToString("0.0"));
            if (t.overflowMode != TextOverflowModes.Overflow) { t.overflowMode = TextOverflowModes.Overflow; redraw.Add(t); }
        }
        // 게임이 매 프레임 영어를 다시 넣는 글(빠른 설정 창 등) → 기억해 두고 LateUpdate에서 그리기 전에 다시 바꾼다
        static readonly List<TMP_Text> tracked = new List<TMP_Text>();
        static readonly HashSet<int> trackedIds = new HashSet<int>();
        static readonly Dictionary<int, string> lastOut = new Dictionary<int, string>();
        public static void Track(TMP_Text t) { if (tracked.Count < 2000 && trackedIds.Add(t.GetInstanceID())) tracked.Add(t); }
        // TEXT_CHANGED는 캔버스가 다시 그리는 도중에 와서, 거기서 바꾼 글은 다시 그리기 요청이 무시됨(지도 이름표가 마우스를 대야 한글로 바뀜) → 다음 LateUpdate에서 다시 그린다
        static readonly List<TMP_Text> redraw = new List<TMP_Text>();
        public static void Redraw()
        {
            if (redraw.Count == 0) return;
            var l = redraw.ToArray(); redraw.Clear();
            busy = true;
            try { foreach (var t in l) if (t != null && t.isActiveAndEnabled) t.SetAllDirty(); }
            finally { busy = false; }
        }
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
            try { FixLineSpacing(t); } catch { }
            try { FixMenuLine(t); } catch { }
            try
            {
                var cur = t.text;
                var n = Translate(cur);
                if (n == null) Untranslated(cur);
                var j = Josa(n ?? cur);
                if (j != null && j != cur)
                {
                    busy = true; t.text = j; redraw.Add(t);
                    if (n != null && n != cur) { Replaced++; Track(t); }
                }
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
                // 머리 위 이름표 등 이름을 바꾸기 전에 만들어진 화면 글도 바꾸도록 UI 통째 표에도 넣는다
                Ui.AddExact(line.Substring(0, tab), line.Substring(tab + 1));
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
