using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 채팅 연결 (구상서 5장, 10장).
    /// - 바닐라 일반 채팅은 15m 안에만 간다. 그래서 모든 접속자의 모드가 자기가 보낸 채팅을 호스트로도 보낸다(VH_Chat).
    ///   호스트는 이것만 해석하므로 NPC·시스템 메시지를 다시 해석할 일이 없다.
    /// - NPC 말은 호스트가 모두에게 보낸다(VH_Say): 채팅창에 NPC 이름으로, NPC가 보이면 머리 위 말풍선으로.
    /// </summary>
    public static class ChatNet
    {
        /// <summary>시험용: NPC가 한 말을 받는 쪽에서 본다.</summary>
        public static event Action<string, string> Heard;

        [HarmonyPatch(typeof(Game), "Start")]
        static class GameStart
        {
            static void Postfix()
            {
                ZRoutedRpc.instance.Register<string, ZDOID>("VH_Chat", RPC_Chat);
                ZRoutedRpc.instance.Register<ZDOID, string, string>("VH_Say", RPC_Say);
            }
        }

        [HarmonyPatch(typeof(Chat), nameof(Chat.SendText))]
        static class SendText
        {
            static void Prefix(Talker.Type type, string text)
            {
                if (type != Talker.Type.Normal && type != Talker.Type.Shout) return;
                if (ZRoutedRpc.instance == null || Player.m_localPlayer == null || string.IsNullOrWhiteSpace(text)) return;
                ZRoutedRpc.instance.InvokeRoutedRPC("VH_Chat", text, HoveredContainer());
            }
        }

        /// <summary>말한 사람이 바라보고 있는 상자("!상자 나무"용). 없으면 None.</summary>
        static ZDOID HoveredContainer()
        {
            GameObject hover = Player.m_localPlayer.GetHoverObject();
            Container c = hover != null ? hover.GetComponentInParent<Container>() : null;
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            return view != null && view.IsValid() ? view.GetZDO().m_uid : ZDOID.None;
        }

        /// <summary>시험에서 호스트 자신이 채팅한 것처럼 보낸다(같은 RPC 경로). hover = 바라보는 상자 대신.</summary>
        public static void SendAsLocal(string text, ZDOID hover = default) =>
            ZRoutedRpc.instance.InvokeRoutedRPC("VH_Chat", text, hover.IsNone() ? ZDOID.None : hover);

        static void RPC_Chat(long sender, string text, ZDOID hover)
        {
            if (!ZNet.instance.IsServer()) return;
            try
            {
                Speaker who;
                ZNetPeer peer = ZNet.instance.GetPeer(sender);
                if (peer != null) who = new Speaker(peer.m_playerName, peer.m_characterID, hover);
                else if (Player.m_localPlayer != null) who = new Speaker(Player.m_localPlayer.GetPlayerName(), Player.m_localPlayer.GetZDOID(), hover);
                else return;
                Commands.Handle(who, text);
            }
            catch (Exception e) { Plugin.Log.LogError("채팅 처리 오류: " + e); }
        }

        public static void NpcSay(WorkerNpc npc, string text)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(text)) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "VH_Say", npc.GetZDOID(), npc.NpcName, text);
        }

        static readonly AccessTools.FieldRef<Chat, float> HideTimer = AccessTools.FieldRefAccess<Chat, float>("m_hideTimer");

        static void RPC_Say(long sender, ZDOID npc, string name, string text)
        {
            Heard?.Invoke(name, text);
            Plugin.Log.LogInfo($"<{name}> {text}");
            Chat chat = Chat.instance;
            if (chat == null) return;
            // Terminal.AddString(title, …)과 같은 모양. 그 오버로드는 Splatform 참조가 필요해서 한 줄짜리를 쓴다.
            chat.AddString($"<color=orange>{name}</color>: {text}");
            HideTimer(chat) = 0f; // 채팅창을 잠깐 띄운다(바닐라 새 메시지와 같게)
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(npc) : null;
            if (go != null) chat.SetNpcText(go, Vector3.up * 2.2f, 30f, 7f, "", text, false);
        }
    }

    public readonly struct Speaker
    {
        public readonly string Name;
        public readonly ZDOID CharacterId;
        /// <summary>말할 때 바라보던 상자.</summary>
        public readonly ZDOID Hover;
        public Speaker(string name, ZDOID id, ZDOID hover) { Name = name; CharacterId = id; Hover = hover; }
    }
}
