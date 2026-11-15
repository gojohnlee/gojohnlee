using HarmonyLib;

namespace ValheimHelper
{
    /// <summary>진단 패치(로그 줄 머리 "진단 "): 시험 모드에만 있다(사용자 11-15) — 본 모드 NpcPatches에서 옮김.</summary>
    static class DiagPatches
    {
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
