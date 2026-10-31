using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 멀티 시험 마(서버 다시 켠 뒤 일 복원): 시험 실행 두 번, 사이에 전용 서버 stop·start.
    /// vh_guestrestart1 = 손님이 깃발·상자를 세우고 "!이름 나무 10" → 일꾼이 나무 하는 중(저장 vh_queue에 Wood)인 걸 보고 상태 파일에 씀.
    /// vh_guestrestart2 = 서버 재시작 뒤(VH_RESET=0) 같은 일꾼이 나타나 저장된 나무 일을 이어 받아(vh_queue) 상자 나무가 10 늘 때까지 하는지.
    /// 손님은 서버 일꾼의 일을 직접 못 보므로 ZDO(vh_queue)·대답·상자(서버 ZDO로 맞음)로 본다. 상태는 손님 플러그인 폴더 guestrestart-state.txt.
    /// </summary>
    static class GuestRestartTest
    {
        static string StatePath => Path.Combine(TestRunner.PluginDir, "guestrestart-state.txt");
        const string W = "$item_wood";
        const int Want = 10;

        public static void Register()
        {
            TestRunner.Register("vh_guestrestart1", "멀티 재시작 1: 손님이 일 시킴·하는 중 기록", Run1, 300f);
            TestRunner.Register("vh_guestrestart2", "멀티 재시작 2: 서버 다시 켠 뒤 일 이어 함(VH_RESET=0)", Run2, 420f);
        }

        static readonly List<string> s_heard = new List<string>();
        static void OnHeard(string name, string text) => s_heard.Add($"{name}: {text}");

        static bool Guest(GatherTest.Checks checks)
        {
            Player p = Player.m_localPlayer;
            bool guest = ZNet.instance != null && !ZNet.instance.IsServer() && p != null;
            checks.Add("손님으로 들어옴", guest);
            if (guest) p.SetGodMode(true);
            return guest;
        }

        /// <summary>거점 범위(일꾼 집 125m) 상자 전부의 나무(일꾼은 범위 아무 상자에나 넣음 — 앞 시험이 남긴 먼 상자일 수도, 회차 309. 손님 화면 상자는 서버 ZDO로 맞음).</summary>
        static int Wood(Vector3 flag)
        {
            int n = 0;
            foreach (Container c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                if (c.GetInventory() != null && Vector3.Distance(c.transform.position, flag) < 130f) n += c.GetInventory().CountItems(W);
            return n;
        }

        static WorkerNpc Nearest(Vector3 at, string name = null)
        {
            WorkerNpc best = null;
            foreach (var n in WorkerNpc.All)
                if (n != null && (name == null || n.NpcName == name) && (best == null || Vector3.Distance(n.transform.position, at) < Vector3.Distance(best.transform.position, at))) best = n;
            return best;
        }

        static string Queue(WorkerNpc npc) => npc?.Zdo?.GetString(WorkSaves.KeyQueue) ?? "";

        /// <summary>from에서 250~400m, 초원이고 물보다 3m 넘게 높은 곳(손님도 같은 WorldGenerator). 방향은 매번 무작위.</summary>
        static Vector3? FreshMeadow(Vector3 from)
        {
            float a0 = Random.Range(0f, 360f);
            for (float r = 250f; r <= 400f; r += 50f)
                for (float a = 0f; a < 360f; a += 20f)
                {
                    Vector3 d = Quaternion.Euler(0f, a0 + a, 0f) * Vector3.forward;
                    Vector3 q = from + d * r;
                    float h = WorldGenerator.instance.GetHeight(q.x, q.z);
                    if (WorldGenerator.instance.GetBiome(q) != Heightmap.Biome.Meadows || h < ZoneSystem.instance.m_waterLevel + 3f) continue;
                    return new Vector3(q.x, h, q.z);
                }
            return null;
        }

        /// <summary>
        /// 서버 월드 시작점 둘레에 밤 해골이 돌아다님(회차 308: 일꾼이 위험이라 나무를 버리고 도망만 함 — 시험할 것은 복원이지 위험 판단이 아님).
        /// 집 80m 안 적을 없앤다. 손님의 Damage는 그 생물 주인(서버)에게 RPC로 간다.
        /// </summary>
        static void Clear(Vector3 home)
        {
            foreach (Character c in Character.GetAllCharacters())
                if (c != null && !c.IsPlayer() && !(c is WorkerNpc) && !c.IsDead() && c.GetComponent<MonsterAI>() != null && !c.IsTamed()
                    && Vector3.Distance(c.transform.position, home) < 80f)
                {
                    var hit = new HitData();
                    hit.m_damage.m_damage = 1e6f;
                    hit.m_point = c.transform.position;
                    c.Damage(hit);
                }
        }

        /// <summary>일꾼이 든 나무(서버가 ZDO vh_inv에 저장 — 손님은 그걸 읽음). 나무는 다 차야 상자에 넣으므로 일 중간은 짐+상자로 본다.</summary>
        static int Carried(WorkerNpc npc)
        {
            byte[] data = npc?.Zdo?.GetByteArray(WorkerNpc.KeyInventory);
            if (data == null) return 0;
            var inv = new Inventory("vh", null, npc.GetInventory().GetWidth(), npc.GetInventory().GetHeight());
            inv.Load(new ZPackage(data));
            return inv.CountItems(W);
        }

        static IEnumerator Run1()
        {
            var checks = new GatherTest.Checks();
            try { File.Delete(StatePath); } catch (IOException) { }
            if (!Guest(checks)) { End(checks); yield break; }
            Player p = Player.m_localPlayer;
            s_heard.Clear();
            ChatNet.Heard += OnHeard;

            // 서버 월드는 시험마다 안 되돌림 → 시작점 둘레는 앞 시험들이 남긴 상자·"위험 구역" 기억(밤 해골)으로 막힘(회차 308).
            // 매번 250~400m 떨어진 새 초원으로 옮겨 깃발을 세운다(월드에 거점은 하나 — 일꾼이 새 깃발로 옮겨 옴).
            Vector3? spot = FreshMeadow(p.transform.position);
            checks.Add($"새 초원 자리 {spot}", spot != null);
            if (spot == null) { End(checks); yield break; }
            p.TeleportTo(spot.Value + Vector3.up, p.transform.rotation, true);
            float until = Time.time + 30f;
            yield return new WaitForSeconds(1f);
            while (Time.time < until && p.IsTeleporting()) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(3f);
            Vector3 fAt = GatherTest.Ground(p.transform.position + p.transform.forward * 6f);
            GameObject flag = Object.Instantiate(BaseFlag.Prefab, fAt, Quaternion.Euler(0f, p.transform.eulerAngles.y, 0f));
            flag.GetComponent<BaseFlagPiece>().OnPlaced();
            TestRunner.Log($"깃발 세움 {fAt}");
            until = Time.time + 120f;
            while (Time.time < until && (Nearest(fAt) == null || Vector3.Distance(Nearest(fAt).transform.position, fAt) > 20f)) yield return new WaitForSeconds(1f);
            WorkerNpc npc = Nearest(fAt);
            checks.Add($"일꾼 {npc?.NpcName} ({WorkerNpc.All.Count}명)", npc != null);
            if (npc == null) { End(checks); yield break; }
            Vector3 at = npc.Home;
            Vector3 cAt = GatherTest.Ground(Vector3.Lerp(p.transform.position, at, 0.5f) + Vector3.right * 2f);
            p.PlacePiece(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>(), cAt, p.transform.rotation, doAttack: false);
            TestRunner.Log($"일꾼 집 {at} ({Vector3.Distance(p.transform.position, at):0}m), 상자 {cAt}");
            yield return new WaitForSeconds(2f);
            Say($"!{npc.NpcName} 멈춰");                 // 앞 시험이 남긴 일·대기열 비움
            yield return new WaitForSeconds(1f);
            Say($"!{npc.NpcName} 전부 취소");
            yield return new WaitForSeconds(2f);
            TestRunner.Log($"비운 뒤 일 \"{Queue(npc)}\"");
            s_heard.Clear();
            Clear(at);
            int wood0 = Wood(at);
            Say($"!{npc.NpcName} 나무 {Want}");
            until = Time.time + 10f;
            while (Time.time < until && !Queue(npc).Contains("i=Wood")) yield return new WaitForSeconds(0.5f);
            if (!Queue(npc).Contains("i=Wood") && s_heard.Exists(s => s.StartsWith(npc.NpcName)))
            {
                // 서버 월드는 안 되돌림 → 앞 시험이 남긴 상자 나무가 거점 범위에 있으면 "더 캘까?"를 묻는다(회차 309) → 응
                Say("응"); // 대답은 "!" 없는 그냥 말(ChatParser: "!"로 시작하면 명령으로만 읽음)
                until = Time.time + 10f;
                while (Time.time < until && !Queue(npc).Contains("i=Wood")) yield return new WaitForSeconds(0.5f);
            }
            checks.Add($"서버가 일 저장(vh_queue \"{Queue(npc)}\")", Queue(npc).Contains("i=Wood"));
            checks.Add($"일꾼 대답 {s_heard.Count}개", s_heard.Exists(s => s.StartsWith(npc.NpcName)));
            foreach (var s in s_heard) TestRunner.Log("  들음 " + s);
            // 일 중간: 나무 2개 이상 상자에(아직 다 안 함)
            until = Time.time + 200f;
            while (Time.time < until && Wood(at) - wood0 + Carried(npc) < 2) { Clear(at); yield return new WaitForSeconds(1f); }
            int got = Wood(at) - wood0, carry = Carried(npc);
            checks.Add($"일 중간까지 감 (상자 +{got}, 짐 {carry})", got + carry >= 2 && got < Want && Queue(npc).Contains("i=Wood"));
            yield return new WaitForSeconds(1.5f); // 서버 저장 Tick(1초)
            var st = new[] { "flag=" + V(at), "name=" + npc.NpcName, "wood0=" + wood0, "got=" + got, "carry=" + carry, "queue=" + Queue(npc) };
            File.WriteAllLines(StatePath, st);
            TestRunner.Log("저장한 상태: " + string.Join(", ", st));
            checks.Add("상태 파일 씀", File.Exists(StatePath));
            End(checks);
        }

        static IEnumerator Run2()
        {
            var checks = new GatherTest.Checks();
            if (!Guest(checks)) { End(checks); yield break; }
            var st = new Dictionary<string, string>();
            try { foreach (var l in File.ReadAllLines(StatePath)) { int i = l.IndexOf('='); if (i > 0) st[l.Substring(0, i)] = l.Substring(i + 1); } }
            catch (IOException) { }
            checks.Add("상태 파일 읽음", st.ContainsKey("flag") && st.ContainsKey("name"));
            if (!st.ContainsKey("flag") || !st.ContainsKey("name")) { End(checks); yield break; }
            try { File.Delete(StatePath); } catch (IOException) { }
            s_heard.Clear();
            ChatNet.Heard += OnHeard;
            string[] f = st["flag"].Split(',');
            Vector3 at = new Vector3(float.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture));
            string name = st["name"];
            int wood0 = int.Parse(st["wood0"]), got0 = int.Parse(st["got"]);
            Player p = Player.m_localPlayer;
            TestRunner.Log($"손님 위치 {p.transform.position}, 깃발 {at} ({Vector3.Distance(p.transform.position, at):0}m)");
            Clear(at);
            if (Vector3.Distance(p.transform.position, at) > 30f) p.TeleportTo(at + Vector3.up, p.transform.rotation, true);

            float until = Time.time + 40f;
            WorkerNpc npc = null;
            while (Time.time < until && (npc = Nearest(at, name)) == null) yield return new WaitForSeconds(1f);
            checks.Add($"서버 다시 켠 뒤 같은 일꾼 {name} 나타남", npc != null);
            if (npc == null) { End(checks); yield break; }
            yield return new WaitForSeconds(3f);
            // 일꾼 ZDO는 움직일 때마다 손님에게 통째로 다시 감 → 크면 서버→손님 ZDO 보내기를 다 써 상자 등이 안 옴(E052 회차 309: 기억 6KB가 들어 7.1KB)
            var zp = new ZPackage();
            npc.Zdo?.Serialize(zp);
            checks.Add($"일꾼 ZDO 작음 ({zp.Size()}B ≤ 3000)", zp.Size() <= 3000);
            int now = Wood(at) - wood0;
            checks.Add($"상자 나무 그대로 (+{now}, 끄기 전 +{got0})", now >= got0);
            checks.Add($"저장된 나무 일 남음 (vh_queue \"{Queue(npc)}\")", Queue(npc).Contains("i=Wood") || now >= Want);

            Say($"!{name} 상태");
            until = Time.time + 6f;
            while (Time.time < until && !s_heard.Exists(s => s.StartsWith(name))) yield return new WaitForSeconds(0.25f);
            foreach (var s in s_heard) TestRunner.Log("  들음 " + s);
            checks.Add("!상태 대답", s_heard.Exists(s => s.StartsWith(name)));

            until = Time.time + 300f;
            float next = 0f;
            while (Time.time < until && Wood(at) - wood0 < Want)
            {
                if (Time.time >= next)
                {
                    next = Time.time + 30f;
                    TestRunner.Log($"  {npc?.NpcName} 위치 {npc?.Zdo?.GetPosition()} 주인 {npc?.Zdo?.GetOwner()} (서버 {ZNet.instance.GetServerPeer()?.m_uid}, 나 {ZDOMan.GetSessionID()}) 짐 {Carried(npc)} 상자 +{Wood(at) - wood0}");
                    foreach (Container c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                        if (c.GetInventory() != null && Vector3.Distance(c.transform.position, at) < 125f) TestRunner.Log($"    상자 {c.transform.position:F1} ({Vector3.Distance(c.transform.position, at):0}m) 나무 {c.GetInventory().CountItems(W)} 주인 {c.GetComponent<ZNetView>()?.GetZDO()?.GetOwner()}");
                    {
                        // 진단(회차 309): 손님이 상자 인스턴스를 못 보면 ZDO(서버가 보낸 것)는 있는지
                        var zl = new List<ZDO>();
                        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), ZNet.instance.GetSyncedSimulationDistance(), zl, null);
                        int ch = 0, chw = 0; int hash = "piece_chest_wood".GetStableHashCode();
                        foreach (var z in zl) if (z.GetPrefab() == hash) { ch++; if (ZNetScene.instance.FindInstance(z) != null) chw++; }
                        TestRunner.Log($"    상자 ZDO {ch}개(인스턴스 {chw}), 장면 Container {Object.FindObjectsByType<Container>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}개");
                    }
                }
                Clear(at);
                yield return new WaitForSeconds(1f);
            }
            yield return new WaitForSeconds(3f);
            int done = Wood(at) - wood0;
            checks.Add($"이어서 끝까지 (상자 +{done}/{Want}, 일 \"{Queue(npc)}\")", done >= Want && !Queue(npc).Contains("i=Wood"));
            checks.Add($"다시 안 함 (+{done} ≤ {Want + 10})", done <= Want + 10);
            End(checks);
        }

        static string V(Vector3 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00}", v.x, v.y, v.z);

        static void Say(string text)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text);
        }

        static void End(GatherTest.Checks checks)
        {
            ChatNet.Heard -= OnHeard;
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
