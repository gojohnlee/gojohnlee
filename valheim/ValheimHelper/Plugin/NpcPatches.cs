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

            /// <summary>원래 땅보다 이만큼 넘게 깊이는 파지 않는다(m) = 바닐라 땅 낮춤 한계(TerrainComp m_levelDelta ±8). 묻힌 광석은 이 안이면 다 캔다(요청 R5, 회차 107: 3m → 8m).</summary>
            public const float MaxDepth = 8f;

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

        /// <summary>진단(E020): 일꾼 20m 안에 통나무(TreeLog)가 생기면 기록 — 쓰러진 나무·쪼갠 반토막이 어디 생겼는지.</summary>
        [HarmonyPatch(typeof(TreeLog), "Awake")]
        static class LogSpawnDiag
        {
            static void Postfix(TreeLog __instance)
            {
                UnityEngine.Vector3 p = __instance.transform.position;
                WorkerNpc near = null;
                float best = 20f;
                foreach (var w in WorkerNpc.All)
                {
                    if (w == null) continue;
                    float d = UnityEngine.Vector3.Distance(w.transform.position, p);
                    if (d < best) { best = d; near = w; }
                }
                if (near == null) return;
                UnityEngine.Vector3 q = near.transform.position;
                Plugin.Log.LogInfo($"진단 통나무 생김 {Utils.GetPrefabName(__instance.gameObject)} ({p.x:0.0},{p.y:0.0},{p.z:0.0}) 기울기 {UnityEngine.Vector3.Angle(__instance.transform.up, UnityEngine.Vector3.up):0}° " +
                                   $"— {near.NpcName} ({q.x:0.0},{q.y:0.0},{q.z:0.0}) {best:0.0}m {near.Ai?.Job?.StepName}");
            }
        }

        /// <summary>진단(E020): 통나무·바위 등 ImpactEffect가 일꾼에 부딪힌 순간(바닐라 피해 판정 전) — 무엇이, 얼마나 빨리, 일꾼이 무슨 단계였는지.</summary>
        [HarmonyPatch(typeof(ImpactEffect), nameof(ImpactEffect.OnCollisionEnter))]
        static class ImpactDiag
        {
            static void Prefix(ImpactEffect __instance, UnityEngine.Collision info)
            {
                if (info == null || info.collider == null || info.contactCount == 0) return;
                var w = info.collider.GetComponentInParent<WorkerNpc>();
                if (w == null) return;
                float rel = info.relativeVelocity.magnitude;
                if (rel < __instance.m_minVelocity) return;
                var body = __instance.GetComponent<UnityEngine.Rigidbody>();
                UnityEngine.Vector3 pt = info.GetContact(0).point;
                UnityEngine.Vector3 pv = body != null ? body.GetPointVelocity(pt) : UnityEngine.Vector3.zero;
                float dot = UnityEngine.Vector3.Dot(-info.relativeVelocity.normalized, pv);
                UnityEngine.Vector3 p = __instance.transform.position, q = w.transform.position;
                Plugin.Log.LogInfo($"진단 부딪힘 {Utils.GetPrefabName(__instance.gameObject)} → {w.NpcName}: 상대속도 {rel:0.0}, 그 점 속도 {pv.magnitude:0.0}(일꾼 쪽 {dot:0.0}), " +
                                   $"최소 {__instance.m_minVelocity:0.0}·최대 {__instance.m_maxVelocity:0.0}, 피해 {__instance.m_damages.GetTotalDamage():0}, " +
                                   $"몸 속도 {(body != null ? body.linearVelocity.magnitude : 0f):0.0}·회전 {(body != null ? body.angularVelocity.magnitude : 0f):0.0}, " +
                                   $"통나무 ({p.x:0.0},{p.y:0.0},{p.z:0.0}) 일꾼 ({q.x:0.0},{q.y:0.0},{q.z:0.0}) 체력 {w.GetHealth():0} {w.Ai?.Job?.StepName}");
            }
        }
    }
}
