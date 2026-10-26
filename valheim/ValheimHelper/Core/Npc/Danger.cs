using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>판단에 쓰는 적 하나(게임에서 읽거나 겉모습으로 짐작한 값).</summary>
    public sealed class Foe
    {
        public string Kind;
        /// <summary>남은 체력.</summary>
        public float Health;
        /// <summary>한 번 맞을 때 피해(별 레벨·난이도 반영, 갑옷 전).</summary>
        public float Damage;
        public bool Boss;
        public float Distance;
    }

    public enum Verdict
    {
        /// <summary>적 없음 또는 신경 쓸 거리 아님.</summary>
        Calm,
        Fight,
        /// <summary>온 길로 물러났다가 다시 시도(8장 d).</summary>
        Back,
        /// <summary>절대 회피: 그 지역을 벗어나고 그 목적지는 포기(8장 a).</summary>
        Flee,
        /// <summary>체력이 낮다: 체력 미드부터(8장 b).</summary>
        Drink,
    }

    /// <summary>동료 부르기(Danger.ShouldCallMate).</summary>
    public enum MateCall
    {
        None,
        /// <summary>불러 놓고 동료가 올 때까지 물러나 기다림.</summary>
        Wait,
        /// <summary>불러 놓고 바로 싸움.</summary>
        Join,
    }

    /// <summary>
    /// 위험 판단 (구상서 8장, 0.5초마다, 규칙). 기획서의 "위협도 ÷ 전투력"을 "이 싸움에서 잃을 체력 ÷ 지금 체력"으로 계산한다:
    /// 약한 적부터 잡는다고 보고, 적 하나를 잡는 동안(그 적 체력 ÷ 내 한 방) 아직 살아 있는 적 모두에게 맞는다(갑옷 반영).
    /// 적 하나·서로 같은 공격 속도면 기획서 공식(적 체력×적 공격력 ÷ 내 체력×내 공격력)과 같다.
    /// </summary>
    public static class Danger
    {
        /// <summary>잃을 비율이 이 이하면 싸움 — "죽을 각만 아니면 싸움"(사용자 결정 2026-10-24, 전 0.6·0.8).</summary>
        public const float FightRagnar = 0.85f, FightFloki = 0.95f;
        public const float LowHealthRagnar = 0.4f, LowHealthFloki = 0.3f;
        /// <summary>잃을 체력이 지금 체력의 이만큼을 넘으면 절대 회피.</summary>
        public const float AvoidRatio = 2f;

        /// <param name="myHit">내 한 방 피해(동행이 있으면 합친다).</param>
        /// <param name="taken">적의 한 방이 갑옷을 거쳐 나한테 들어오는 피해.</param>
        public static float ExpectedLoss(IList<Foe> foes, float myHit, Func<float, float> taken)
        {
            if (foes.Count == 0) return 0f;
            if (myHit <= 0f) return float.PositiveInfinity;
            var list = new List<Foe>(foes);
            list.Sort((a, b) => a.Health.CompareTo(b.Health));
            float incoming = 0f;
            foreach (var f in list) incoming += taken(f.Damage);
            float loss = 0f;
            foreach (var f in list)
            {
                float hits = (float)Math.Ceiling(Math.Max(f.Health, 1f) / myHit);
                loss += hits * incoming;
                incoming -= taken(f.Damage);
            }
            return loss;
        }

        public static float Ratio(IList<Foe> foes, float myHealth, float myHit, Func<float, float> taken) =>
            ExpectedLoss(foes, myHit, taken) / Math.Max(myHealth, 1f);

        /// <summary>
        /// 순서: 체력이 낮으면 미드부터(마시는 건 바로 끝난다) → 보스, 또는 체력이 가득해도 이길 수 없는 적(최대 체력 기준 2배)은 도망 →
        /// 지금 체력으로 잃을 비율이 기준 이하이고 그 뒤에도 적 한 방을 더 버틸 체력이 남으면 싸움, 아니면 물러남
        /// (체력이 낮은데 미드가 없어도 같은 기준으로 — 사용자 결정 2026-10-24).
        /// (0.3.7 시험: 체력 25%에서 회색난쟁이를 보고 미드 대신 도망부터 쳤다 — 도망 기준을 지금 체력으로 재서.)
        /// </summary>
        public static Verdict Judge(Personality p, IList<Foe> foes, float health, float maxHealth, float myHit, Func<float, float> taken, bool haveMead)
        {
            if (foes.Count == 0) return Verdict.Calm;
            bool alarak = p != Personality.Ragnar; // 플로키·호위병은 대담한 기준
            bool low = health <= maxHealth * (alarak ? LowHealthFloki : LowHealthRagnar);
            if (low && haveMead) return Verdict.Drink;
            foreach (var f in foes) if (f.Boss) return Verdict.Flee;
            if (Ratio(foes, maxHealth, myHit, taken) > AvoidRatio) return Verdict.Flee;
            // 계산대로 끝나도 적 한 방은 더 버틸 체력이 남아야 싸움(체력이 낮을 때 비율만 보면 운 나쁜 한 방에 죽음 — 회차 145 fighttest, 체력 18에 회색난쟁이)
            float loss = ExpectedLoss(foes, myHit, taken), spare = 0f;
            foreach (var f in foes) spare = Math.Max(spare, taken(f.Damage));
            return loss / Math.Max(health, 1f) <= (alarak ? FightFloki : FightRagnar) && health - loss > spare ? Verdict.Fight : Verdict.Back;
        }

        /// <summary>혼자도 싸움이지만 비율이 이보다 크거나 적이 이만큼 이상이면 동료를 부른다(같이 패기, 사용자 결정 2026-10-24 나).</summary>
        public const float MateCallRatio = 0.6f;
        public const int MateCallFoes = 3;

        /// <summary>
        /// 동료를 부를지(사용자 결정 2026-10-24 "더 공격적으로" 나): 혼자면 물러남·도망인데 동료 한 방을 합치면 싸움 → 불러 놓고 물러나 기다림(Wait),
        /// 혼자도 싸움이지만 빡빡하면(비율 MateCallRatio 넘음·적 MateCallFoes 이상) → 불러 놓고 바로 싸움(Join). 보스는 같이여도 도망이라 안 부름.
        /// </summary>
        public static MateCall ShouldCallMate(Verdict alone, Verdict together, float aloneRatio, int foes)
        {
            if (alone == Verdict.Fight) return aloneRatio > MateCallRatio || foes >= MateCallFoes ? MateCall.Join : MateCall.None;
            if ((alone == Verdict.Back || alone == Verdict.Flee) && together == Verdict.Fight) return MateCall.Wait;
            return MateCall.None;
        }

        /// <summary>
        /// 처음 보는 적의 한 방 짐작(8-2장: 겉모습으로만). 체력은 플레이어처럼 체력바로 보이니 게임 값을 쓰고, 피해만 짐작한다:
        /// 사람 크기(키 1.8m, 반지름 0.4m) = 한 방 10으로 보고 부피의 제곱근만큼(0.5~6배), 별 하나마다 ×1.5(바닐라 레벨 규칙).
        /// 싸워 보면 실제 값을 배운다(FoeBook). (0.3.7: 체력까지 몸집으로 짐작했더니 멧돼지를 체력 229로 보고 도망쳤다.)
        /// </summary>
        public static float GuessDamage(float height, float radius, int level)
        {
            double size = Math.Sqrt(Math.Max(0.01, height * radius * radius) / (1.8 * 0.4 * 0.4));
            size = Math.Min(6.0, Math.Max(0.5, size));
            int stars = Math.Max(0, level - 1);
            return (float)(10.0 * size * (1 + 0.5 * stars));
        }
    }

    /// <summary>
    /// 싸워 보고 배운 적 (8-2장: 지식은 두 NPC가 같이 쓰고 죽어도 남는다). 종류(프리팹 이름)마다 별 1개 기준 최대 체력·한 방 피해.
    /// 저장: "종류=체력,피해;…".
    /// </summary>
    public sealed class FoeBook
    {
        readonly Dictionary<string, float[]> m_known = new Dictionary<string, float[]>();

        public int Count => m_known.Count;
        public bool Knows(string kind) => m_known.ContainsKey(kind);

        public bool TryGet(string kind, out float health, out float damage)
        {
            health = damage = 0f;
            if (!m_known.TryGetValue(kind, out var v)) return false;
            health = v[0];
            damage = v[1];
            return true;
        }

        /// <summary>배운다. 피해는 본 것 중 가장 센 한 방을 남긴다.</summary>
        public void Learn(string kind, float health, float damage)
        {
            if (string.IsNullOrEmpty(kind) || kind.IndexOfAny(new[] { '=', ',', ';' }) >= 0) return;
            if (m_known.TryGetValue(kind, out var v)) { v[0] = Math.Max(v[0], health); v[1] = Math.Max(v[1], damage); }
            else m_known[kind] = new[] { health, damage };
        }

        public string Save()
        {
            var parts = new List<string>();
            foreach (var kv in m_known)
                parts.Add(kv.Key + "=" + kv.Value[0].ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                          + "," + kv.Value[1].ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(";", parts.ToArray());
        }

        public static FoeBook Load(string text)
        {
            var b = new FoeBook();
            if (string.IsNullOrEmpty(text)) return b;
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string[] v = part.Substring(eq + 1).Split(',');
                if (v.Length != 2) continue;
                if (float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h)
                    && float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d))
                    b.Learn(part.Substring(0, eq), h, d);
            }
            return b;
        }
    }
}
