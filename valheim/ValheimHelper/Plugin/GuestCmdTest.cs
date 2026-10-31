using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// vh_guestcmdtest (멀티 시험 다): 전용 서버에 손님으로 들어온 뒤(vh_jointest와 같은 길) 깃발을 세우고(바닐라처럼 만들자마자 VH_BaseSet)
    /// 서버가 만든 일꾼에게 "!" 명령 → 손님 화면에서 대답(채팅)·말풍선·외모 장비(VisEquipment)·걷기 애니메이션(forward_speed)·움직임을 본다.
    /// </summary>
    static class GuestCmdTest
    {
        public static void Register() => TestRunner.Register("vh_guestcmdtest", "멀티: 손님 명령·대답·말풍선·장비·애니메이션 시험", Run, 180f);

        static readonly List<string> s_heard = new List<string>();
        static void OnHeard(string name, string text) => s_heard.Add($"{name}: {text}");
        static readonly AccessTools.FieldRef<Chat, List<Chat.NpcText>> NpcTexts = AccessTools.FieldRefAccess<Chat, List<Chat.NpcText>>("m_npcTexts");

        static IEnumerator Run()
        {
            var checks = new GatherTest.Checks();
            Player player = Player.m_localPlayer;
            bool guest = ZNet.instance != null && !ZNet.instance.IsServer() && player != null;
            checks.Add("손님으로 들어옴", guest);
            if (!guest) { End(checks); yield break; }
            s_heard.Clear();
            ChatNet.Heard += OnHeard;
            player.SetGodMode(true);

            // 1) 깃발: 앞 6m 땅에. 바닐라 Player.PlacePiece처럼 만들자마자 OnPlaced(→ VH_BaseSet)
            Vector3 at = player.transform.position + player.transform.forward * 6f;
            if (ZoneSystem.instance.GetGroundHeight(at, out float h)) at.y = h;
            GameObject flag = Object.Instantiate(BaseFlag.Prefab, at, Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f));
            flag.GetComponent<BaseFlagPiece>().OnPlaced();
            TestRunner.Log($"깃발 세움 {at}");

            float until = Time.time + 30f;
            while (Time.time < until && WorkerNpc.All.Count < 2) yield return new WaitForSeconds(1f);
            checks.Add($"서버가 만든 일꾼이 손님 화면에 나타남 ({WorkerNpc.All.Count}명)", WorkerNpc.All.Count >= 2);
            if (WorkerNpc.All.Count < 2) { End(checks); yield break; }
            WorkerNpc npc = WorkerNpc.All[0];
            long owner = npc.Zdo.GetOwner(), server = ZNet.instance.GetServerPeer()?.m_uid ?? 0;
            TestRunner.Log($"일꾼 {npc.NpcName} 주인 {owner} (서버 {server}, 나 {ZDOMan.GetSessionID()}) 거리 {Vector3.Distance(npc.transform.position, player.transform.position):0.0}m");
            checks.Add("일꾼 주인이 손님 아님", owner != ZDOMan.GetSessionID());

            // 2) !상태 → 둘 다 대답(채팅) + 머리 위 말풍선
            Say("!상태");
            until = Time.time + 6f;
            while (Time.time < until && !(Count(WorkerNpc.All[0].NpcName) >= 1 && Count(WorkerNpc.All[1].NpcName) >= 1)) yield return new WaitForSeconds(0.25f);
            foreach (var s in s_heard) TestRunner.Log("  들음 " + s);
            checks.Add("!상태 → 둘 다 대답", Count(WorkerNpc.All[0].NpcName) >= 1 && Count(WorkerNpc.All[1].NpcName) >= 1);
            var texts = Chat.instance != null ? NpcTexts(Chat.instance) : null;
            checks.Add($"머리 위 말풍선 ({texts?.Count ?? 0}개)", texts != null && texts.Exists(t => t.m_go == npc.gameObject || t.m_go == WorkerNpc.All[1].gameObject));

            // 장비: 서버가 쓴 외모(머리·수염)가 손님 VisEquipment에 그려짐
            VisEquipment vis = npc.GetComponent<VisEquipment>();
            int hair = npc.Zdo.GetInt(ZDOVars.s_hairItem), beard = npc.Zdo.GetInt(ZDOVars.s_beardItem);
            int drawnHair = vis != null ? Traverse.Create(vis).Field<int>("m_currentHairItemHash").Value : 0;
            int drawnBeard = vis != null ? Traverse.Create(vis).Field<int>("m_currentBeardItemHash").Value : 0;
            checks.Add($"외모 장비가 손님 화면에 그려짐 (머리 {hair}/{drawnHair}, 수염 {beard}/{drawnBeard})", hair != 0 && hair == drawnHair && beard == drawnBeard);

            // 3) 나무 일 → 걷는다(애니메이션·위치). 갓 생긴 일꾼은 도끼가 없어 맨손 나뭇가지부터 — 손 도구는 기록만
            Vector3 start = npc.transform.position;
            Animator anim = npc.GetComponentInChildren<Animator>();
            Say($"!{npc.NpcName} 나무 10");
            float maxSpeed = 0f, moved = 0f;
            int right = 0;
            until = Time.time + 45f;
            while (Time.time < until && (maxSpeed < 0.3f || moved < 3f))
            {
                yield return new WaitForSeconds(0.25f);
                if (npc == null) break;
                if (right == 0) right = npc.Zdo.GetInt(ZDOVars.s_rightItem);
                if (anim != null) maxSpeed = Mathf.Max(maxSpeed, anim.GetFloat("forward_speed"));
                moved = Mathf.Max(moved, Vector3.Distance(start, npc.transform.position));
            }
            GameObject held = right != 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(right) : null;
            TestRunner.Log($"손에 든 것 {(held != null ? held.name : "없음")}");
            checks.Add($"걷기 애니메이션 forward_speed 최대 {maxSpeed:0.00}", maxSpeed >= 0.3f);
            checks.Add($"움직임 {moved:0.0}m", moved >= 3f);

            Say("!멈춰");
            yield return new WaitForSeconds(2f);
            End(checks);
        }

        static void Say(string text)
        {
            TestRunner.Log($"> {text}");
            ChatNet.SendAsLocal(text);
        }

        static int Count(string name) => s_heard.FindAll(x => x.StartsWith(name + ":")).Count;

        static void End(GatherTest.Checks checks)
        {
            ChatNet.Heard -= OnHeard;
            TestRunner.Log($"판정: {(checks.Passed == checks.Total ? "PASS" : "FAIL")} ({checks.Passed}/{checks.Total})");
            TestRunner.Finish();
        }
    }
}
