using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 콘솔 명령 vh_fartest: 먼 구역 유지 시험 (구상서 16장).
    /// 멧돼지를 소환해 추적하고, 플레이어를 약 1km 떨어진 땅으로 보낸 뒤 60초 동안
    /// 멧돼지가 계속 불러와져 있고(인스턴스), 호스트가 소유하고, 움직이는지(AI 동작) 본다.
    /// 그다음 대조 시험으로 패치를 끄고 10초 뒤 멧돼지가 사라지는지 본다.
    /// 결과는 콘솔, BepInEx 로그, 플러그인 폴더의 diag.txt에 남는다.
    /// 플러그인 폴더에 autotest.txt가 있으면(테스트 스크립트가 만든다) 월드에 들어갔을 때 저절로 시험하고 게임을 끈다.
    /// </summary>
    static class FarZoneTest
    {
        static bool s_running;
        static bool s_autoPending, s_auto;
        static float s_autoWait;
        static readonly List<string> s_report = new List<string>();

        public static void Register()
        {
            new Terminal.ConsoleCommand("vh_fartest", "[Valheim Helper] 먼 구역 유지 시험", args =>
            {
                if (s_running) { args.Context.AddString("이미 시험 중입니다."); return; }
                Plugin.Instance.StartCoroutine(Run(args.Context));
            });
        }

        public static void CheckAutoRun()
        {
            string flag = Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), "autotest.txt");
            if (!File.Exists(flag)) return;
            try { File.Delete(flag); } catch (IOException) { }
            s_autoPending = true;
            Plugin.Log.LogInfo("자동 시험 예약: 월드에 들어가면 vh_fartest를 실행합니다.");
        }

        /// <summary>자동 시험: 월드에 들어가 순간이동이 끝나고 10초 뒤 시작.</summary>
        public static void UpdateAutoRun()
        {
            if (!s_autoPending) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting()) { s_autoWait = 0f; return; }
            s_autoWait += Time.deltaTime;
            if (s_autoWait < 10f) return;
            s_autoPending = false;
            s_auto = true;
            Plugin.Instance.StartCoroutine(Run(Console.instance));
        }

        static void Log(Terminal t, string line)
        {
            s_report.Add(line);
            Plugin.Log.LogInfo(line);
            if (t != null) t.AddString("[VH] " + line);
        }

        static IEnumerator Run(Terminal t)
        {
            s_running = true;
            s_report.Clear();
            s_report.Add("=== vh_fartest " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 게임 " + Version.GetVersionString() + " ===");
            Player player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                Log(t, "FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                Finish();
                yield break;
            }

            Vector3 home = player.transform.position;
            Quaternion rot = player.transform.rotation;
            bool god = player.InGodMode();
            player.SetGodMode(true);

            GameObject prefab = ZNetScene.instance.GetPrefab("Boar");
            Character boar = Object.Instantiate(prefab, home + player.transform.forward * 4f + Vector3.up, Quaternion.identity).GetComponent<Character>();
            ZDOID id = boar.GetZDOID();
            yield return new WaitForSeconds(1f);
            FarZones.Enabled = true;
            FarZones.Track(id);
            Log(t, $"멧돼지 소환 {Fmt(home)}, 추적 시작");

            if (!FindFarLand(home, out Vector3 far))
            {
                Log(t, "FAIL: 주변 1km 안팎에 땅이 없습니다. 다른 곳에서 다시 해 주세요.");
                Cleanup(player, id, home, rot, god, false);
                Finish();
                yield break;
            }
            player.TeleportTo(far, rot, true);
            Log(t, $"플레이어 이동 → {Fmt(far)} (거리 {Vector3.Distance(home, far):0}m)");
            float wait = 0f;
            yield return new WaitForSeconds(1f);
            while (player.IsTeleporting() && wait < 40f) { wait += 0.5f; yield return new WaitForSeconds(0.5f); }

            // 1) 패치 켬: 60초 관찰
            int ok = 0, samples = 12;
            Vector3? first = null;
            float moved = 0f, minDist = float.MaxValue;
            for (int i = 0; i < samples; i++)
            {
                yield return new WaitForSeconds(5f);
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null) { Log(t, $"[{(i + 1) * 5}s] 멧돼지 데이터 없음(죽었거나 사라짐)"); continue; }
                Vector3 pos = zdo.GetPosition();
                bool inst = ZNetScene.instance.FindInstance(zdo) != null;
                bool owner = zdo.GetOwner() == ZDOMan.GetSessionID();
                bool loaded = ZoneSystem.instance.IsZoneLoaded(pos);
                float dist = Vector3.Distance(player.transform.position, pos);
                minDist = Mathf.Min(minDist, dist);
                if (first == null) first = pos;
                moved = Mathf.Max(moved, Vector3.Distance(first.Value, pos));
                if (inst && owner && loaded) ok++;
                Log(t, $"[{(i + 1) * 5}s] 인스턴스 {inst}, 호스트 소유 {owner}, 구역 {loaded}, 플레이어와 {dist:0}m, 위치 {Fmt(pos)}");
            }
            bool farEnough = minDist > 300f;
            bool alive = ok == samples;
            bool aiRuns = moved > 1f;
            Log(t, $"켬 결과: 유지 {ok}/{samples}, 최소 거리 {minDist:0}m, 최대 이동 {moved:0.0}m");

            // 2) 대조: 패치 끔 → 사라져야 정상(패치 덕분에 유지됐다는 증거)
            FarZones.Enabled = false;
            yield return new WaitForSeconds(10f);
            ZDO z2 = ZDOMan.instance.GetZDO(id);
            bool goneWhenOff = z2 == null || ZNetScene.instance.FindInstance(z2) == null;
            Log(t, $"끔 대조: 10초 뒤 인스턴스 사라짐 {goneWhenOff}");
            FarZones.Enabled = true;

            string verdict = !farEnough ? "INCONCLUSIVE(충분히 멀어지지 않음)"
                : alive && aiRuns && goneWhenOff ? "PASS"
                : alive && !aiRuns ? "PARTIAL(유지는 되지만 멧돼지가 안 움직임)"
                : "FAIL";
            Log(t, $"판정: {verdict} (유지 {alive}, AI 동작 {aiRuns}, 끔 대조 {goneWhenOff})");

            Cleanup(player, id, home, rot, god, true);
            Log(t, "집으로 돌아갑니다. 시험 끝.");
            Finish();
        }

        static void Cleanup(Player player, ZDOID id, Vector3 home, Quaternion rot, bool god, bool teleport)
        {
            FarZones.Untrack(id);
            if (teleport) player.TeleportTo(home, rot, true);
            player.SetGodMode(god);
        }

        static void Finish()
        {
            s_running = false;
            if (s_auto)
            {
                s_auto = false;
                Plugin.Instance.StartCoroutine(QuitSoon());
            }
            try
            {
                string dir = Path.GetDirectoryName(Plugin.Instance.Info.Location);
                File.WriteAllLines(Path.Combine(dir, "diag.txt"), s_report.ToArray());
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("diag.txt 쓰기 실패: " + e.Message); }
        }

        static IEnumerator QuitSoon()
        {
            for (int i = 10; i > 0; i--)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"[Valheim Helper] 시험 끝. {i}초 뒤 게임을 끕니다.");
                yield return new WaitForSeconds(1f);
            }
            Application.Quit();
        }

        /// <summary>집에서 약 1km(없으면 0.7·1.5km) 떨어진 물 위가 아닌 땅.</summary>
        static bool FindFarLand(Vector3 home, out Vector3 far)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            foreach (float d in new[] { 1000f, 700f, 1500f })
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.PI / 4f;
                    Vector3 p = home + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * d;
                    if (new Vector2(p.x, p.z).magnitude > 9500f) continue;
                    float h = WorldGenerator.instance.GetHeight(p.x, p.z);
                    if (h < water + 3f) continue;
                    p.y = h + 1.5f;
                    far = p;
                    return true;
                }
            far = default;
            return false;
        }

        static string Fmt(Vector3 v) => $"({v.x:0},{v.y:0},{v.z:0})";
    }
}
