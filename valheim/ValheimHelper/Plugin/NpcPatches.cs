using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ValheimHelper
{
    /// <summary>NPC 몸에 바닐라 플레이어 규칙을 잇는 패치 (구상서 3장·4장: 수치는 플레이어와 같게).</summary>
    static class NpcPatches
    {
        /// <summary>
        /// Character.RPC_Damage(게임 1.0.20)는 갑옷 방어력을 플레이어만 계산한다: if (IsPlayer()) { ApplyArmor(GetBodyArmor()); DamageArmorDurability(hit); }.
        /// 바로 뒤에 GetBodyArmor가 오는 그 IsPlayer 하나만 "플레이어 또는 일꾼"으로 바꾼다(0.3.7 시험: 가죽 갑옷 7인데 둔기 20을 그대로 맞음).
        /// </summary>
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        static class ArmorCounts
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                var list = new List<CodeInstruction>(code);
                MethodInfo isPlayer = AccessTools.Method(typeof(Character), nameof(Character.IsPlayer));
                MethodInfo armor = AccessTools.Method(typeof(Character), nameof(Character.GetBodyArmor));
                MethodInfo counts = AccessTools.Method(typeof(ArmorCounts), nameof(CountsArmor));
                int patched = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].Calls(isPlayer)) continue;
                    bool nearArmor = false;
                    for (int j = i + 1; j < Math.Min(list.Count, i + 6); j++) if (list[j].Calls(armor)) nearArmor = true;
                    if (!nearArmor) continue;
                    // 스택의 this(Character)를 그대로 받는 정적 함수로 바꾼다
                    list[i] = new CodeInstruction(OpCodes.Call, counts).MoveLabelsFrom(list[i]);
                    patched++;
                }
                if (patched != 1) Plugin.Log.LogWarning($"갑옷 패치: 바꾼 자리 {patched}곳(1곳이어야 함). 게임 버전이 바뀌었는지 확인 필요");
                return list;
            }

            static bool CountsArmor(Character c) => c.IsPlayer() || c is WorkerNpc;
        }
    }
}
