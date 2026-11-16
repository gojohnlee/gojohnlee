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
        /// Character.RPC_Damage(게임 1.0.20·1.0.21)는 갑옷 방어력을 플레이어만 계산한다: if (IsPlayer()) { ApplyArmor(GetBodyArmor()); DamageArmorDurability(hit); }.
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

            /// <summary>시험 기록: 일꾼마다 마지막으로 받은 나무 밖 피해(종류·양·공격자) — 원인 모를 체력 감소(E118 stucktest) FAIL 문구에.</summary>
            public static readonly Dictionary<string, string> LastHurt = new Dictionary<string, string>();

            /// <summary>시험 기록: 일꾼마다 받은 적 공격(EnemyHit) 피해 합 — 발 묶인 시험에서 치우기 전 순간의 적 피해를 순간이동 탓과 가름(E118).</summary>
            public static readonly Dictionary<string, float> EnemyHurt = new Dictionary<string, float>();

            static bool Prefix(Character __instance, HitData hit)
            {
                if (!(__instance is WorkerNpc w) || hit == null) return true;
                if (hit.m_hitType != HitData.HitType.Tree)
                {
                    Character by = hit.GetAttacker();
                    LastHurt[w.NpcName] = $"{hit.m_hitType} {hit.GetTotalDamage():0} {(by != null ? by.m_name : "-")}";
                    if (hit.m_hitType == HitData.HitType.EnemyHit) { EnemyHurt.TryGetValue(w.NpcName, out float sum); EnemyHurt[w.NpcName] = sum + hit.GetTotalDamage(); }
                    return true;
                }
                Spared++;
                Plugin.Log.LogInfo($"나무 피해 막음 {w.NpcName}: {hit.GetTotalDamage():0}");
                return false;
            }
        }

        /// <summary>
        /// Character.RPC_Damage(게임 1.0.20·1.0.21)의 몬스터 전용 피해 배율 둘을 일꾼에게서 뺀다(플레이어처럼, 회차 166):
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

        /// <summary>
        /// 집 곁 통나무(사용자 11-12): 일꾼이 친 통나무가 건축물 6m 안이면 미는 방향(TreeLog.RPC_Damage: hit.m_dir × m_pushForce × 2)을
        /// 가장 가까운 조각 반대쪽(수평)으로 — 집 쪽으로 밀려 벽·바닥에 박히지 않게. 피해·쪼개짐은 그대로.
        /// </summary>
        [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
        static class LogPushAwayFromHouse
        {
            static void Prefix(TreeLog __instance, HitData hit)
            {
                if (hit == null || !(hit.GetAttacker() is WorkerNpc)) return;
                Buildings.NoteWorkerLog(__instance.transform.position);
                UnityEngine.Vector3 p = __instance.transform.position, away = UnityEngine.Vector3.zero;
                float best = 6f;
                foreach (var zdo in Senses.Nearby(p, best))
                {
                    if (!Buildings.IsPiece(zdo.GetPrefab())) continue;
                    UnityEngine.Vector3 d = p - zdo.GetPosition();
                    d.y = 0f;
                    if (d.magnitude < best) { best = d.magnitude; away = d; }
                }
                if (away.sqrMagnitude < 0.01f) return;
                hit.m_dir = away.normalized;
            }
        }

        /// <summary>
        /// 연쇄 쓰러짐(사용자 11-12): 일꾼 통나무가 부딪힌 동안(ImpactEffect.OnCollisionEnter → 피해 RPC는 주인이면 바로 처리) 쓰러진 나무(TreeBase.SpawnLog)·
        /// 쪼개진 통나무(TreeLog.Destroy)를 그 일꾼의 자기 통나무 자리로(GatherJob.NoteChain). 부딪힘 피해엔 공격자가 없어 이렇게 잇는다.
        /// </summary>
        [HarmonyPatch(typeof(ImpactEffect), nameof(ImpactEffect.OnCollisionEnter))]
        static class ChainFallSource
        {
            internal static bool On;
            internal static UnityEngine.Vector3 From;
            static void Prefix(ImpactEffect __instance)
            {
                On = false;
                UnityEngine.Vector3 p = __instance.transform.position;
                if (__instance.GetComponent<TreeLog>() == null || !Buildings.NearWorkerLog(p, 25f)) return;
                On = true; From = p;
            }
            static void Postfix() => On = false;
        }

        /// <summary>일꾼이 직접 친 나무가 쓰러지는 그 순간(TreeBase.RPC_Damage 안의 SpawnLog)에 자기 통나무 자리로 — 다음 AI 틱(UpdateWork)을 기다리면
        /// 그 사이 넘어지던 통나무가 옆 나무에 부딪혀도 일꾼 통나무로 안 보여 연쇄가 0번(E115).</summary>
        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        static class WorkerFellTree
        {
            internal static WorkerNpc By;
            static void Prefix(HitData hit) => By = hit?.GetAttacker() as WorkerNpc;
            static void Postfix() => By = null;
        }

        [HarmonyPatch(typeof(TreeBase), "SpawnLog")]
        static class ChainFallTree
        {
            static void Prefix(TreeBase __instance)
            {
                if (ChainFallSource.On) GatherJob.NoteChain(ChainFallSource.From, __instance.transform.position);
                else if (WorkerFellTree.By?.Ai?.Job is GatherJob g) g.FelledNow(__instance.transform.position);
            }
        }

        [HarmonyPatch(typeof(TreeLog), "Destroy")]
        static class ChainFallLog
        {
            static void Prefix(TreeLog __instance)
            {
                if (ChainFallSource.On) GatherJob.NoteChain(ChainFallSource.From, __instance.transform.position);
            }
        }


    }
}
