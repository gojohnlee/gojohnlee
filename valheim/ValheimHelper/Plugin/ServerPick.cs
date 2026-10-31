using HarmonyLib;
using UnityEngine;

namespace ValheimHelper
{
    /// <summary>
    /// 전용 서버에서 거두기(Pickable) 고침(E052, 회차 309). 게임 1.0.20 Pickable.RPC_Pick은 효과를 만들 때 Player.m_localPlayer.GetZDOID()를 쓰는데
    /// 전용 서버엔 로컬 플레이어가 없어 매번 NullReference → 떨굼·m_picked 없이 끝남 → 서버 일꾼이 같은 나뭇가지를 끝없이 거둬 예외가 쌓이고(1회 시험 15천 번) 서버가 느려짐.
    /// 로컬 플레이어가 있으면 원본 그대로, 없을 때만 원본과 같은 순서로 하되 효과 주인을 ZDOID.None으로.
    /// </summary>
    [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
    static class ServerPick
    {
        static readonly AccessTools.FieldRef<Pickable, ZNetView> NView = AccessTools.FieldRefAccess<Pickable, ZNetView>("m_nview");
        static readonly AccessTools.FieldRef<Pickable, bool> Picked = AccessTools.FieldRefAccess<Pickable, bool>("m_picked");
        static readonly System.Func<Pickable, Vector3> SpawnOffset =
            AccessTools.MethodDelegate<System.Func<Pickable, Vector3>>(AccessTools.Method(typeof(Pickable), "GetSpawnOffset"));
        static readonly System.Action<Pickable, GameObject, int, int> Drop =
            AccessTools.MethodDelegate<System.Action<Pickable, GameObject, int, int>>(AccessTools.Method(typeof(Pickable), "Drop"));

        static int s_diag;

        static bool Prefix(Pickable __instance, int bonus)
        {
            if (Player.m_localPlayer != null) return true;
            ZNetView nview = NView(__instance);
            if (s_diag++ < 30) Plugin.Log.LogInfo($"DIAG 서버 거두기 {__instance.name} 주인 {nview?.GetZDO()?.GetOwner()} 나 {(nview != null && nview.IsOwner())} 거둠 {Picked(__instance)}");
            if (nview == null || !nview.IsOwner() || Picked(__instance)) return false;
            Vector3 pos = __instance.transform.position;
            __instance.m_pickEffector.Create(__instance.m_pickEffectAtSpawnPoint ? pos + SpawnOffset(__instance) : pos, Quaternion.identity, null, 1f, -1, ZDOID.None);
            int num = (__instance.m_dontScale ? __instance.m_amount : Mathf.Max(__instance.m_minAmountScaled, Game.instance.ScaleDrops(__instance.m_itemPrefab, __instance.m_amount))) + bonus;
            int n = 0;
            for (int i = 0; i < num; i++) Drop(__instance, __instance.m_itemPrefab, n++, 1);
            if (!__instance.m_extraDrops.IsEmpty())
                foreach (ItemDrop.ItemData d in __instance.m_extraDrops.GetDropListItems()) Drop(__instance, d.m_dropPrefab, n++, d.m_stack);
            if (__instance.m_aggravateRange > 0f) BaseAI.AggravateAllInArea(pos, __instance.m_aggravateRange, BaseAI.AggravatedReason.Theif);
            nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
            return false;
        }
    }
}
