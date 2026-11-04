using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

// 네크로맨서 테일 편의 기능 모드(한글 패치와 별개). 한글 패치(NecroKoreanFont.dll)가 게임폴더\NecroHelper\NecroHelper.dll을 찾아 Entry.Init을 부른다.
// 기능: 성 밖 카메라(WideCam), 성 밖 지도 내 위치(WildMap), 지도에 퀘스트 위치(QuestMap+todo-map.tsv), 숨은 퀘스트 분석용 대화 자료 저장(GraphDump).
// 게임 mscorlib이 잘려 있음 → string.Replace(string,string)·GetComponentInChildren(bool) 등 없음. 새 API는 게임 DLL에서 먼저 확인.
namespace NecroHelper
{
    public static class Entry
    {
        public static string Dir = ".";
        static bool made;
        public static void Init(string dir)
        {
            if (made) return;
            made = true;
            Dir = dir ?? ".";
            Log.Write("편의 기능 시작");
            var go = new GameObject("NecroHelper");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Runner>();
        }
    }

    static class Log
    {
        static bool first = true;
        public static void Write(string s)
        {
            try
            {
                var path = Path.Combine(Entry.Dir, "NecroHelper.log");
                if (first) { File.WriteAllText(path, ""); first = false; }
                File.AppendAllText(path, s + "\n");
            }
            catch { }
        }
    }

    public class Runner : MonoBehaviour
    {
        void Update()
        {
            try { WideCam.Tick(); } catch { }
            try { EmptyGrey.Tick(); } catch (Exception e) { EmptyGrey.Fail(e); }
            try { WildMap.Tick(); } catch { }
            try { MapDump.Tick(); } catch { }
            try { QuestMap.Tick(); } catch (Exception e) { QuestMap.Fail(e); }
            if (!GraphDump.done) { try { GraphDump.Tick(); } catch (Exception e) { GraphDump.done = true; Log.Write("대화 자료 저장 실패: " + e); } }
        }
        // 게임 OverObjectIcon이 Update·마우스 들어옴에서 색을 흰색으로 되돌리므로 그 뒤에 칠한다
        void LateUpdate()
        {
            try { EmptyGrey.Icons(); } catch (Exception e) { EmptyGrey.Fail(e); }
        }
    }

    // 빈 상자(Container 모든 칸 비어 있음, 가게·하수인·플레이어 가방 제외)와 이미 시체를 파냈거나 시체가 없는 무덤(DiggableGrave.dugByPlayer / !hasSkeleton·!hasCorpse)은 마우스를 올렸을 때 이름(OverObjectText)을 회색으로.
    // 글자 대신 색만 바꿈(글을 감싸면 한글 패치의 화면 글 표가 못 알아봄). 1초마다 목록 갱신, 매 프레임 마우스가 올라간 것(InteractiveObject.mouseIsOverMe)만 봄.
    // 무덤은 이름 글 없이 삽 아이콘(OverObjectIcon)만 뜸(사용자 11-04) → 빈 것의 아이콘도 회색(투명도는 게임 값 그대로).
    static class EmptyGrey
    {
        static readonly List<InteractiveObject> empty = new List<InteractiveObject>();
        static readonly List<OverObjectIcon> icons = new List<OverObjectIcon>(); static bool iconsLogged;
        public static void Icons()
        {
            foreach (var ic in icons)
            {
                if (ic == null || ic.image == null || !ic.gameObject.activeInHierarchy) continue;
                var c = ic.image.color; if (c.r != Grey.r) ic.image.color = new Color(Grey.r, Grey.g, Grey.b, c.a);
            }
        }
        static float next; static TMP_Text txt; static Color normal; static bool grey, failed;
        static readonly Color Grey = new Color(0.55f, 0.55f, 0.55f, 1f);
        public static void Fail(Exception e) { if (!failed) { failed = true; Log.Write("빈 상자 회색 실패: " + e); } }
        public static void Tick()
        {
            if (GameManager.gameState != GameStates.PLAYING || OverObjectText.instance == null) return;
            if (Time.unscaledTime >= next) { next = Time.unscaledTime + 1f; Rebuild(); }
            if (txt == null) { txt = OverObjectText.instance.GetComponent<TMP_Text>(); if (txt == null) return; normal = txt.color; }
            bool over = false;
            if (OverObjectText.isShown) foreach (var io in empty) if (io != null && io.mouseIsOverMe) { over = true; break; }
            if (over) { if (!grey) { normal = txt.color; grey = true; } txt.color = Grey; }
            else if (grey) { txt.color = normal; grey = false; }
        }
        static void Rebuild()
        {
            empty.Clear();
            foreach (var o in UnityEngine.Object.FindObjectsByType(typeof(Container), FindObjectsSortMode.None))
            {
                var c = o as Container;
                if (c == null || c.isShop || c.isMinion || c.isPlayerEquipmentContainer || c.isPlayerInventoryContainer) continue;
                // contents는 칸 수만큼 있고 빈 칸은 stackSize 0(sharedData가 남을 수 있음). 아직 안 채워진(칸 0개) 상자는 빈 걸로 치지 않음
                var l = c.GetContents(); if (l == null || l.Count == 0) continue;
                bool any = false; foreach (var r in l) if (r != null && r.stackSize > 0 && r.sharedData != null) { any = true; break; }
                if (any) continue;
                var io = c.GetComponent<InteractiveObject>(); if (io != null) empty.Add(io);
            }
            foreach (var o in UnityEngine.Object.FindObjectsByType(typeof(DiggableGrave), FindObjectsSortMode.None))
            {
                var g = o as DiggableGrave;
                // 이미 시체를 파냈거나, 처음부터 해골·시체가 없는 무덤(사용자 11-04 결정). 잘라의 무덤은 특별 이야기라 제외
                if (g == null || g.isZalasGrave || !(g.dugByPlayer || (!g.hasSkeleton && !g.hasCorpse))) continue;
                var io = g.GetComponent<InteractiveObject>(); if (io != null) empty.Add(io);
            }
            // 빈 것에서 빠진 아이콘은 흰색으로 되돌리고 새 목록을 만든다(아이콘은 꺼져 있을 때가 많아 꺼진 것도 찾음)
            var set = new HashSet<InteractiveObject>(empty);
            foreach (var ic in icons) if (ic != null && ic.image != null && (ic.interactiveObject == null || !set.Contains(ic.interactiveObject))) { var c = ic.image.color; ic.image.color = new Color(1f, 1f, 1f, c.a); }
            icons.Clear();
            foreach (var o in UnityEngine.Object.FindObjectsByType(typeof(OverObjectIcon), FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var ic = o as OverObjectIcon;
                if (ic != null && ic.interactiveObject != null && set.Contains(ic.interactiveObject)) icons.Add(ic);
            }
            if (!iconsLogged && icons.Count > 0) { iconsLogged = true; Log.Write("빈 것 아이콘 회색: " + icons.Count + "개"); }
        }
    }

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
                    string label = tmp != null && tmp.text != null ? GraphDump.Clean(tmp.text) : "";
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
                if (built != mm) { built = mm; tw = null; dots.Clear(); dotColor.Clear(); list = null; }
                if (tw == null) BuildTown(mm);
                var todo = JournalManager.GetJournalEntry(0);
                if (todo != null && todo.opened)
                    foreach (var v in todo.addedParts)
                    {
                        if (v == null || v.name == null || !v.name.StartsWith("JournalToDo.")) continue;
                        string[] ts; if (!table.TryGetValue(v.name.Substring(12), out ts)) continue;
                        Vector2 at;
                        if (Place(mm, ts, out at)) Dot(mm, used++, at, Gold);
                    }
                // 숨은 퀘스트(hidden-quests.tsv): 진행 중이면 하늘색 점 + 지도 왼쪽 위 목록
                var sb = new System.Text.StringBuilder();
                int ch = GameManager.CurrentChapter();
                foreach (var h in hidden)
                {
                    if (ch < h.from || ch > h.to || !Any(h.start) || Any(h.end)) continue;
                    sb.Append(sb.Length == 0 ? "숨은 퀘스트\n" : "\n").Append("· ").Append(h.name).Append(" — ").Append(h.todo);
                    Vector2 at;
                    if (Place(mm, h.targets, out at)) Dot(mm, used++, at, Blue);
                }
                List(mm, sb.ToString());
            }
            else if (list != null && list.gameObject.activeSelf) list.gameObject.SetActive(false);
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
        static readonly Color Gold = new Color(1f, 0.78f, 0.15f, 1f), Blue = new Color(0.45f, 0.8f, 1f, 1f);
        static readonly List<Color> dotColor = new List<Color>();
        static void Dot(MapManager mm, int i, Vector2 at, Color col)
        {
            while (dots.Count <= i)
            {
                var go = UnityEngine.Object.Instantiate(mm.rtIAmHere.gameObject, mm.rtIAmHere.parent);
                go.name = "QuestDot";
                foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.raycastTarget = false;
                var rt = go.GetComponent<RectTransform>(); rt.localScale = mm.rtIAmHere.localScale * 0.8f;
                dots.Add(rt); dotColor.Add(Color.clear);
            }
            var d = dots[i];
            if (dotColor[i] != col) { dotColor[i] = col; foreach (var g in d.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.color = col; }
            if (!d.gameObject.activeSelf) d.gameObject.SetActive(true);
            d.anchoredPosition = at;
            d.SetSiblingIndex(Math.Max(0, mm.rtIAmHere.GetSiblingIndex()));
        }
        static void Load()
        {
            table = new Dictionary<string, string[]>();
            try
            {
                var path = Path.Combine(Entry.Dir, "todo-map.tsv");
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
            try
            {
                var path = Path.Combine(Entry.Dir, "hidden-quests.tsv");
                if (!File.Exists(path)) { Log.Write("숨은 퀘스트: hidden-quests.tsv 없음"); return; }
                int bad = 0;
                foreach (var raw in File.ReadAllText(path).Split('\n'))
                {
                    var line = raw.TrimEnd('\r'); if (line.Length == 0 || line[0] == '#') continue;
                    var f = line.Split('\t'); if (f.Length < 6) continue;
                    var h = new Hidden { name = f[0], start = Ids(f[1], ref bad), end = Ids(f[2], ref bad), targets = f[4].Split(','), todo = f[5] };
                    int dash = f[3].IndexOf('-');
                    if (dash > 0) { h.from = Num(f[3].Substring(0, dash)); h.to = Num(f[3].Substring(dash + 1)); }
                    hidden.Add(h);
                }
                Log.Write("숨은 퀘스트: " + hidden.Count + "개" + (bad > 0 ? ", 게임에 없는 노드 " + bad + "개" : ""));
            }
            catch (Exception e) { Log.Write("숨은 퀘스트 표 읽기 실패: " + e.Message); }
        }

        class Hidden { public string name, todo; public long[] start, end; public string[] targets; public int from = 0, to = 999; }
        static readonly List<Hidden> hidden = new List<Hidden>();
        static TextMeshProUGUI list;
        static bool Any(long[] ids)
        {
            foreach (var id in ids) { ArticyModel m; if (ArticyPlayer.models.TryGetValue(id, out m) && m != null && m.hasBeenVisited) return true; }
            return false;
        }
        // 이 게임 mscorlib이 잘려 있어 16진 읽기는 손으로
        static long[] Ids(string s, ref int bad)
        {
            var r = new List<long>();
            foreach (var part in s.Split(','))
            {
                if (part.Length == 0) continue;
                long v = 0; foreach (var c in part) v = v * 16 + (c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
                if (!ArticyPlayer.models.ContainsKey(v)) bad++;
                r.Add(v);
            }
            return r.ToArray();
        }
        static int Num(string s) { int v = 0; foreach (var c in s) if (c >= '0' && c <= '9') v = v * 10 + (c - '0'); return v; }
        static void List(MapManager mm, string text)
        {
            if (list == null)
            {
                if (text.Length == 0) return;
                TMP_Text font = null;
                foreach (var t in mm.mapFrame.GetComponentsInChildren<TMP_Text>(true)) if (t != null && t.font != null) { font = t; break; }
                if (font == null) return;
                var go = new GameObject("HiddenQuestList", typeof(RectTransform));
                go.transform.SetParent(mm.mapFrame.transform, false);
                list = go.AddComponent<TextMeshProUGUI>();
                list.font = font.font; list.fontSize = 22; list.color = Blue; list.raycastTarget = false; list.textWrappingMode = TextWrappingModes.NoWrap;
                var rt = list.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); rt.anchoredPosition = new Vector2(40f, -40f); rt.sizeDelta = new Vector2(900f, 400f);
                Log.Write("숨은 퀘스트 목록 만듦(글꼴 " + font.font.name + ")");
            }
            if (list.text != text) list.text = text;
            bool on = text.Length > 0; if (list.gameObject.activeSelf != on) list.gameObject.SetActive(on);
        }
    }

    // 숨은 퀘스트 분석용: 게임이 불러온 대화 자료(ArticyPlayer.models·pins)를 KoreanFont\\dialogue-graph.tsv로 한 번 저장(이미 있으면 안 함).
    // 게임 회사 자료라 공개 저장소에 올리지 않는다(사용자가 대화로 보내 줌).
    static class GraphDump
    {
        public static bool done;
        // 이 게임 mscorlib엔 string.Replace(string,string)이 없음 → 한 글자씩
        public static string Clean(string t)
        {
            if (t == null) return "";
            var sb = new System.Text.StringBuilder(t.Length);
            foreach (var ch in t) { if (ch == '\n') sb.Append(" / "); else if (ch == '\t' || ch == '\r') sb.Append(' '); else sb.Append(ch); }
            return sb.ToString();
        }
        static string Ids(List<long> l) { var sb = new System.Text.StringBuilder(); if (l != null) foreach (var x in l) { if (sb.Length > 0) sb.Append(','); sb.Append(x.ToString("X")); } return sb.ToString(); }
        public static void Tick()
        {
            if (!ArticyPlayer.articySetupComplete) return;
            done = true;
            var path = Path.Combine(Entry.Dir, "dialogue-graph.tsv");
            if (File.Exists(path)) return;
            var sb = new System.Text.StringBuilder(8 << 20);
            sb.Append("# M id type speaker name text item drop var visited in out custom chapter | P id type owner targets expr\n");
            foreach (var kv in ArticyPlayer.models)
            {
                var m = kv.Value; if (m == null) continue;
                sb.Append("M\t").Append(m.id.ToString("X")).Append('\t').Append(m.type.ToString()).Append('\t').Append(m.speaker.ToString("X")).Append('\t')
                  .Append(Clean(m.displayName)).Append('\t').Append(Clean(m.text)).Append('\t').Append(m.linkToInventoryItem.ToString("X")).Append('\t')
                  .Append(m.isItemDrop ? "1" : "0").Append('\t').Append(m.linkedArticyVariable != null ? m.linkedArticyVariable.name : "").Append('\t')
                  .Append(m.hasBeenVisited ? "1" : "0").Append('\t').Append(Ids(m.myInputPins)).Append('\t').Append(Ids(m.myOutputPins)).Append('\t').Append(Clean(m.CUSTOMCODE)).Append('\t').Append(m.inChapter.ToString()).Append('\n');
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
}
