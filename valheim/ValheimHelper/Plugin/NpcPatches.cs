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
        /// 일꾼은 나무·통나무에 다치지 않는다(사용자 결정 2026-10-23, E020). 쓰러지는 나무·구르는 통나무는 ImpactEffect가 프리팹 값 HitType.Tree로
        /// IDestructible.Damage → Character.Damage → RPC_Damage를 부른다(codemap "통나무 ImpactEffect"). 그 피해만 일꾼에서 버린다(밀림도 없음).
        /// </summary>
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        internal static class NoTreeHurt
        {
            /// <summary>시험 기록: 막은 나무 피해 수.</summary>
            public static int Spared;

            static bool Prefix(Character __instance, HitData hit)
            {
                if (!(__instance is WorkerNpc w) || hit == null || hit.m_hitType != HitData.HitType.Tree) return true;
                Spared++;
                Plugin.Log.LogInfo($"나무 피해 막음 {w.NpcName}: {hit.GetTotalDamage():0}");
                return false;
            }
        }

        /// <summary>
        /// Character.RPC_Damage(게임 1.0.20)의 몬스터 전용 피해 배율 둘을 일꾼에게서 뺀다(플레이어처럼, 회차 166):
        /// - 뒤치기: if (m_baseAI != null && !m_baseAI.IsAlerted() && hit.m_backstabBonus > 1f && 300초에 한 번) hit.ApplyModifier(m_backstabBonus).
        ///   일꾼은 WorkerAI(BaseAI)인데 경계(alerted)를 켜지 않아 300초마다 첫 한 방이 공격자 무기의 뒤치기 배율(몬스터 무기 기본 4배)로 들어왔다
        ///   (vh_resisttest 진단: 늑대 69.6 × 4 − 갑옷 42 → 체력 71/71 일꾼 즉사). 플레이어는 BaseAI가 없어 안 받는다 → 일꾼이 받는 hit의 배율을 1로.
        /// - 비틀 치명타: if (IsStaggering() && !IsPlayer()) hit.ApplyModifier(2f) — 그 IsPlayer 하나만 "플레이어 또는 일꾼"으로(ArmorCounts와 같은 방식).
        /// </summary>
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        static class LikePlayerHit
        {
            static void Prefix(Character __instance, HitData hit)
            {
                if (__instance is WorkerNpc && hit != null) hit.m_backstabBonus = 1f;
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                var list = new List<CodeInstruction>(code);
                MethodInfo isPlayer = AccessTools.Method(typeof(Character), nameof(Character.IsPlayer));
                MethodInfo staggering = AccessTools.Method(typeof(Character), nameof(Character.IsStaggering));
                MethodInfo like = AccessTools.Method(typeof(LikePlayerHit), nameof(PlayerOrWorker));
                int patched = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].Calls(isPlayer)) continue;
                    bool afterStagger = false;
                    for (int j = Math.Max(0, i - 4); j < i; j++) if (list[j].Calls(staggering)) afterStagger = true;
                    if (!afterStagger) continue;
                    list[i] = new CodeInstruction(OpCodes.Call, like).MoveLabelsFrom(list[i]);
                    patched++;
                }
                if (patched != 1) Plugin.Log.LogWarning($"비틀 치명타 패치: 바꾼 자리 {patched}곳(1곳이어야 함). 게임 버전이 바뀌었는지 확인 필요");
                return list;
            }

            static bool PlayerOrWorker(Character c) => c.IsPlayer() || c is WorkerNpc;
        }

        /// <summary>
        /// 진단(회차 166): 일꾼이 한 방에 최대 체력 20% 넘게 잃거나 죽거나 뒤치기가 들어가면 한 줄 — 원래 피해·뒤치기 배율·비틀거림·체력 흐름.
        /// (vh_resisttest: 체력 71·갑옷 42 일꾼이 늑대 한 방에 죽음 → 뒤치기 4배였음, LikePlayerHit)
        /// </summary>
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        internal static class HitDiag
        {
            static readonly AccessTools.FieldRef<Character, float> BackstabTime = AccessTools.FieldRefAccess<Character, float>("m_backstabTime");

            internal struct Before { public bool On; public float Hp, Raw, Bonus, Stab; public bool Stagger, Alerted; }

            static void Prefix(Character __instance, HitData hit, out Before __state)
            {
                __state = default;
                if (!(__instance is WorkerNpc) || hit == null) return;
                var nv = __instance.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
                BaseAI ai = __instance.GetBaseAI();
                __state = new Before { On = true, Hp = __instance.GetHealth(), Raw = hit.GetTotalDamage(), Bonus = hit.m_backstabBonus, Stab = BackstabTime(__instance),
                                       Stagger = __instance.IsStaggering(), Alerted = ai != null && ai.IsAlerted() };
            }

            static void Postfix(Character __instance, HitData hit, Before __state)
            {
                if (!__state.On) return;
                var w = (WorkerNpc)__instance;
                Character a = hit.GetAttacker();
                bool stab = BackstabTime(__instance) != __state.Stab;
                float lost = __state.Hp - w.GetHealth();
                if (!stab && lost < w.GetMaxHealth() * 0.2f && w.GetHealth() > 0f) return;
                Plugin.Log.LogInfo($"진단 큰 피해 {w.NpcName} ← {(a != null ? Utils.GetPrefabName(a.gameObject) : hit.m_hitType.ToString())}: 원래 {__state.Raw:0.0}, 뒤치기 x{__state.Bonus:0.#}{(stab ? " 적용" : "")}, " +
                                   $"비틀 {__state.Stagger}, 경계 {__state.Alerted}, 갑옷 {w.GetBodyArmor():0}, 체력 {__state.Hp:0.0} → {w.GetHealth():0.0}/{w.GetMaxHealth():0}");
            }
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

            /// <summary>바닥에 닿았다고 보는 여유(m): 한계 바닥(원래 땅 −8m)을 친 휘두름은 바닐라가 더 못 낮추는데 "판 것"으로 세면 진척으로 오해한다(E021 헛파기) → 막은 것으로 세어 겨눈 조각에 직접 피해.</summary>
            const float FloorMargin = 0.4f;

            static bool Prefix(UnityEngine.Vector3 hitPoint, Character character, ref UnityEngine.GameObject __result)
            {
                if (!(character is WorkerNpc w)) return true;
                // 경사로 깎기는 깊이 제한 없음(구덩이에서 나오려고 위로 깎는 것)
                bool tooDeep = w.Ai != null && !w.Ai.Carving && WorldGenerator.instance != null && hitPoint.y < WorldGenerator.instance.GetHeight(hitPoint.x, hitPoint.z) - MaxDepth + FloorMargin;
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
