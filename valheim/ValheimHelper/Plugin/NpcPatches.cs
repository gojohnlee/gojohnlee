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

        /// <summary>
        /// 일꾼의 곡괭이질은 허락된 때(광석이 땅속에 남았을 때·구덩이에서 경사로를 깎을 때, WorkerAI.DigAllowed)만 땅을 판다
        /// (2026-10-21 사용자: 돌 캘 때 맨땅을 파서 구덩이에 갇힘 → 땅 파기는 최대한 자제).
        /// 바닐라 Attack.DoMeleeAttack은 휘두름이 바위보다 땅에 먼저 닿으면 땅만 파고 그 휘두름을 끝낸다 — 여기서는 파는 것만 막는다.
        /// </summary>
        [HarmonyPatch(typeof(Attack), nameof(Attack.SpawnOnHitTerrain))]
        internal static class NoDig
        {
            /// <summary>시험 기록: 일꾼이 판 횟수, 막은 횟수.</summary>
            public static int Dug, Blocked;

            /// <summary>원래 땅보다 이만큼 넘게 깊이는 파지 않는다(m). 그보다 깊은 광석 조각은 남긴다(GatherJob.RockAim).</summary>
            public const float MaxDepth = 3f;

            static bool Prefix(UnityEngine.Vector3 hitPoint, Character character, ref UnityEngine.GameObject __result)
            {
                if (!(character is WorkerNpc w)) return true;
                // 경사로 깎기는 깊이 제한 없음(구덩이에서 나오려고 위로 깎는 것)
                bool tooDeep = w.Ai != null && !w.Ai.Carving && WorldGenerator.instance != null && hitPoint.y < WorldGenerator.instance.GetHeight(hitPoint.x, hitPoint.z) - MaxDepth;
                if (w.Ai != null && w.Ai.DigAllowed && !tooDeep) { Dug++; w.Ai.Digs++; return true; }
                Blocked++;
                if (w.Ai != null) w.Ai.BlockedDigs++;
                __result = null;
                return false;
            }
        }
    }
}
