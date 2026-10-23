using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// vh_peertest (묶음 1 작업 4, risks.txt "혼자 하는 멀티 시험" A): 게임 하나로 멀티 흉내.
    /// 1) 소유권 뺏기: 상자 쓰는 중(일 시작 직후)·일하는 중·싸우는 중·질문 대기 중마다 NPC ZDO를 없는 uid로 SetOwner →
    ///    그동안 NPC ZDO를 안 씀, 예외 0, 상자 잠금 풀림, 호스트가 되찾고(바닐라 2초 정리) 하던 일을 이어 감, 물건 총량 같음.
    /// 2) 남의 채팅: 가짜 이름·없는 캐릭터 ZDOID로 Commands.Handle — 명령·대답·!같이 가자·!상자가 예외 없이, 인스턴스 없는 상자는 거점 상자에서 빠짐.
    /// 3) 손님 흉내: ZNet가 서버가 아닌 것처럼 하고 모드 Update 경로를 돌려 ZDO를 하나도 안 쓰는지.
    /// </summary>
    static class PeerTest
    {
        public static void Register() => TestRunner.Register("vh_peertest", "혼자 하는 멀티(소유권·남의 채팅·손님) 시험", () => TestKit.Flat(Run()));

        const long FakeUid = 987654321L;

        /// <summary>로그 오류 세기(Unity 예외·모드 LogError 둘 다).</summary>
        sealed class Errors : ILogListener
        {
            public int Count;
            public readonly List<string> First = new List<string>();
            public void LogEvent(object sender, LogEventArgs e)
            {
                if ((e.Level & (LogLevel.Error | LogLevel.Fatal)) == 0) return;
                Count++;
                if (First.Count < 5) First.Add(e.Data?.ToString() ?? "");
            }
            public void Dispose() { }
        }

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            if (Player.m_localPlayer == null || !ZNet.instance.IsServer())
            {
                TestRunner.Log("FAIL: 월드에 들어간 뒤(싱글 또는 서버 시작) 실행해야 합니다.");
                TestRunner.Finish();
                yield break;
            }
            var w = new TestKit.World();
            var errors = new Errors();
            var spawned = new List<GameObject>();
            BepInEx.Logging.Logger.Listeners.Add(errors);
            try
            {
                yield return Body(w, checks, errors, spawned);
            }
            finally
            {
                BepInEx.Logging.Logger.Listeners.Remove(errors);
                Departures.TestAutoGo = true;
                SetServer(true);
                foreach (var go in spawned)
                    if (go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) v.Destroy();
                TestKit.Restore(w);
            }
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            foreach (var e in errors.First) TestRunner.Log("오류: " + e);
            TestKit.End(w, checks);
        }

        static IEnumerator Body(TestKit.World w, GatherTest.Checks checks, Errors errors, List<GameObject> spawned)
        {
            w.CountsCreatures = true; // 생물을 부르거나 세는 시험
            yield return TestKit.Setup(w);
            checks.Add("깃발·NPC 2명·상자", w.Ragnar != null && w.Floki != null && w.Chest != null);
            if (w.Ragnar == null || w.Floki == null || w.Chest == null) yield break;
            string n1 = w.Ragnar.NpcName, n2 = w.Floki.NpcName;
            errors.Count = 0;

            // 1) 소유권 뺏기
            GatherTest.Say($"!{n1} 나무 20", null);
            yield return new WaitForSeconds(0.3f); // 일 시작 직후: 짐 정리·준비(상자 쓰기)
            yield return Steal(w, checks, errors, w.Ragnar, "상자 쓰는 중", () => w.Ragnar.Ai.Current == WorkerAI.Mode.Work);
            yield return new WaitForSeconds(6f);
            yield return Steal(w, checks, errors, w.Ragnar, "일하는 중", () => w.Ragnar.Ai.Current == WorkerAI.Mode.Work);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);

            // 싸우는 중: 그레이드워프 하나를 8m 앞에
            GameObject gd = ZNetScene.instance.GetPrefab("Greydwarf");
            Vector3 at = w.Ragnar.transform.position + w.Ragnar.transform.forward * 8f;
            GameObject foe = Object.Instantiate(gd, GatherTest.Ground(at) + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Add(foe);
            foe.GetComponent<Character>().SetLevel(1);
            yield return TestKit.WaitFor(() => w.Ragnar.Ai.Guard != null && w.Ragnar.Ai.Guard.StateName != "Calm", 10f);
            TestRunner.Log($"싸움 시작 ({TestKit.Waited:0.0}초, Guard {w.Ragnar.Ai.Guard?.StateName})");
            if (TestKit.Ok) yield return Steal(w, checks, errors, w.Ragnar, "싸우는 중", null);
            else checks.Add("싸우는 중 소유권 뺏기(싸움이 시작 안 됨)", false);
            if (foe != null && foe.GetComponent<ZNetView>() is ZNetView fv && fv.IsValid()) fv.Destroy();
            yield return new WaitForSeconds(1f);

            // 질문 대기 중
            Departures.TestAutoGo = false;
            TestKit.PutInChest(w.Chest, "Wood", 20);
            GatherTest.Say($"!{n2} 나무 10", null);
            yield return new WaitForSeconds(1f);
            bool asked = Departures.AskedAt(w.Floki) >= 0;
            yield return Steal(w, checks, errors, w.Floki, "질문 대기 중", () => Departures.AskedAt(w.Floki) >= 0);
            checks.Add("질문 대기가 남아 있음", asked && Departures.AskedAt(w.Floki) >= 0);
            GatherTest.Say("아니", null);
            yield return new WaitForSeconds(1f);

            // 2) 남의 채팅: 가짜 이름, 호스트에 없는 캐릭터
            var fake = new Speaker("가짜친구", new ZDOID(FakeUid, 77), ZDOID.None);
            int e0 = errors.Count;
            bool threw = false;
            threw |= !Handle(fake, $"!{n2} 나무 10");
            yield return new WaitForSeconds(1f);
            bool fakeAsked = Departures.AskedAt(w.Floki) >= 0;
            threw |= !Handle(fake, "응");
            yield return new WaitForSeconds(1f);
            checks.Add($"남의 명령·대답 (질문 {(fakeAsked ? "받음" : "없음")} → {w.Floki.Ai.Current})",
                !threw && fakeAsked && Departures.AskedAt(w.Floki) < 0 && w.Floki.Ai.Current == WorkerAI.Mode.Work);
            GatherTest.Say("!멈춰", null);
            yield return new WaitForSeconds(1f);
            Departures.TestAutoGo = true;
            TestKit.TakeFromChest(w.Chest, "Wood");

            threw = !Handle(fake, $"!{n1} 같이 가자");
            yield return new WaitForSeconds(2f);
            checks.Add($"없는 사람 따라가기 → 예외 없이 그만둠 ({w.Ragnar.Ai.Current})", !threw && w.Ragnar.Ai.Current != WorkerAI.Mode.Follow);

            // !상자(이제 지정 없음, 대답만)·거점 상자 찾기: 상자 인스턴스가 호스트에 없을 때(같은 프레임에 내림)
            ZDOID chestId = w.Chest.GetComponent<ZNetView>().GetZDO().m_uid;
            Vector3 chestPos = w.Chest.transform.position;
            TestKit.Unload(w.Chest.GetComponent<ZNetView>());
            threw = !Handle(new Speaker("가짜친구", new ZDOID(FakeUid, 77), chestId), "!상자 음식");
            bool listed = Chests.NearBase(w.Home).Exists(c => c != null && c.GetComponent<ZNetView>().GetZDO() != null && c.GetComponent<ZNetView>().GetZDO().m_uid == chestId);
            checks.Add("상자 인스턴스 없이 !상자 → 예외 없음, 내린 상자는 거점 상자에서 빠짐", !threw && !listed);
            yield return TestKit.WaitFor(() => GatherTest.ChestAt(chestPos) != null, 3f);
            w.Chest = GatherTest.ChestAt(chestPos);
            checks.Add($"내린 상자 다시 생김 ({TestKit.Waited:0.0}초)", w.Chest != null);
            yield return new WaitForSeconds(0.5f);
            checks.Add($"남의 채팅 동안 오류 0 ({errors.Count - e0})", errors.Count == e0);

            // 3) 손님 흉내: 서버가 아닌 것처럼 모드 Update 경로를 60프레임
            var revs = new Dictionary<ZDOID, uint>();
            int zdosBefore = Workers.AllZdos().Count;
            int guestErrors = 0, frames = 0;
            int e1 = errors.Count;
            bool wrote = false;
            for (; frames < 60; frames++)
            {
                revs.Clear();
                foreach (var z in Watched(w)) revs[z.m_uid] = z.DataRevision;
                try
                {
                    SetServer(false);
                    float dt = Time.deltaTime;
                    Workers.Update();
                    Brain.Update(dt);
                    Departures.Tick(dt);
                    WorkSaves.Tick(dt);
                    MapSense.Tick(dt);
                }
                catch (System.Exception ex)
                {
                    guestErrors++;
                    Plugin.Log.LogWarning("손님 흉내 중 예외: " + ex);
                }
                finally
                {
                    SetServer(true);
                }
                foreach (var z in Watched(w))
                    if (revs.TryGetValue(z.m_uid, out uint r) && r != z.DataRevision) { wrote = true; TestRunner.Log($"손님 흉내 중 바뀐 ZDO: {z.m_uid}"); }
                yield return null;
            }
            checks.Add($"손님: {frames}프레임 동안 ZDO 안 씀·예외 0·NPC 수 그대로", !wrote && guestErrors == 0 && errors.Count == e1 && Workers.AllZdos().Count == zdosBefore);
            checks.Add($"전체 오류 0 ({errors.Count})", errors.Count == 0);
        }

        /// <summary>
        /// 진단: 뺏긴 동안 NPC ZDO를 누가 쓰는지. ZDO.IncreaseDataRevision(모든 Set이 부름) 앞에서 지켜보는 ZDO가 주인 아닐 때 부른 곳(호출 순서)을 적는다.
        /// </summary>
        static class WriteSpy
        {
            static Harmony s_h;
            static ZDO s_watch;
            static readonly List<string> s_seen = new List<string>();

            public static void Start(ZDO zdo)
            {
                s_seen.Clear();
                s_watch = zdo;
                if (s_h != null) return;
                s_h = new Harmony(Plugin.Guid + ".peerspy");
                s_h.Patch(AccessTools.Method(typeof(ZDO), "IncreaseDataRevision"), prefix: new HarmonyMethod(typeof(WriteSpy), nameof(Prefix)));
            }

            public static List<string> Stop()
            {
                s_watch = null;
                s_h?.UnpatchSelf();
                s_h = null;
                return new List<string>(s_seen);
            }

            static void Prefix(ZDO __instance)
            {
                if (s_watch == null || !ReferenceEquals(__instance, s_watch) || __instance.IsOwner() || s_seen.Count >= 6) return;
                var st = new System.Diagnostics.StackTrace(1, false);
                var parts = new List<string>();
                for (int i = 0; i < st.FrameCount && parts.Count < 7; i++)
                {
                    var m = st.GetFrame(i).GetMethod();
                    if (m != null) parts.Add((m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name);
                }
                string s = string.Join(" < ", parts.ToArray());
                if (!s_seen.Contains(s)) s_seen.Add(s);
            }
        }

        static readonly System.Reflection.FieldInfo IsServerField = AccessTools.Field(typeof(ZNet), "m_isServer");

        /// <summary>ZNet.IsServer()가 읽는 정적 값. 손님 흉내는 한 프레임 안에서만 바꾸고 곧바로 되돌린다.</summary>
        static void SetServer(bool on) => IsServerField.SetValue(null, on);

        static List<ZDO> Watched(TestKit.World w)
        {
            var list = new List<ZDO>(Workers.Zdos());
            foreach (var c in Chests.NearBase(w.Home)) list.Add(c.GetComponent<ZNetView>().GetZDO());
            return list;
        }

        static bool Handle(Speaker who, string text)
        {
            TestRunner.Log($"> ({who.Name}) {text}");
            try { Commands.Handle(who, text); return true; }
            catch (System.Exception e) { TestRunner.Log($"FAIL: Commands.Handle 예외 {e.GetType().Name}: {e.Message}"); return false; }
        }

        /// <summary>NPC 둘과 거점 상자의 물건 총수.</summary>
        static int Total(TestKit.World w)
        {
            int n = 0;
            foreach (var npc in WorkerNpc.All)
                if (npc != null) foreach (var it in npc.GetInventory().GetAllItems()) n += it.m_stack;
            foreach (var c in Chests.NearBase(w.Home))
                foreach (var it in c.GetInventory().GetAllItems()) n += it.m_stack;
            return n;
        }

        /// <summary>
        /// NPC ZDO를 없는 peer에게 넘기고(친구가 가져간 것처럼), 호스트가 되찾을 때까지 본다.
        /// 뺏긴 동안 NPC ZDO가 안 바뀌는지, 되찾은 순간 물건 총량, 상자 잠금, 예외, 그 뒤 하던 일(going)이 이어지는지.
        /// </summary>
        static IEnumerator Steal(TestKit.World w, GatherTest.Checks checks, Errors errors, WorkerNpc npc, string what, System.Func<bool> going)
        {
            ZNetView view = npc.GetComponent<ZNetView>();
            ZDO zdo = view.GetZDO();
            int e0 = errors.Count;
            int total0 = Total(w);
            zdo.SetOwner(FakeUid);
            uint rev0 = zdo.DataRevision;
            bool wrote = false;
            float t = 0f;
            WriteSpy.Start(zdo); // 뺏긴 동안 쓰면 누가 썼는지 남김(회차 139: Brain.Save 30초 저장이 겹쳤던 것)
            while (!zdo.IsOwner() && t < 5f)
            {
                yield return null;
                t += Time.deltaTime;
                if (!zdo.IsOwner() && zdo.DataRevision != rev0) wrote = true;
            }
            foreach (var s in WriteSpy.Stop()) TestRunner.Log($"  {what} 뺏긴 동안 쓴 곳: {s}");
            bool back = zdo.IsOwner();
            int total1 = Total(w);
            bool locked = false;
            foreach (var c in Chests.NearBase(w.Home)) if (Chests.InUse(c)) locked = true;
            TestRunner.Log($"{what}: 뺏김 {t:0.0}초, 되찾음 {back}, 물건 {total0}→{total1}, 뺏긴 동안 ZDO 씀 {wrote}, 상자 잠김 {locked}, 오류 {errors.Count - e0}");
            yield return new WaitForSeconds(1f);
            bool goingOn = going == null || going();
            checks.Add($"{what}: 3초 안에 되찾음·안 씀·잠금 풀림·물건 같음·오류 0·이어 감",
                back && t <= 3f && !wrote && !locked && total0 == total1 && errors.Count == e0 && goingOn);
        }
    }
}
