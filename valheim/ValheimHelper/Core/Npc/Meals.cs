using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHelper.Core.Npc
{
    /// <summary>음식 한 가지의 바닐라 수치(ItemDrop.SharedData에서 읽는다).</summary>
    public sealed class FoodInfo
    {
        public string Name;
        public float Health, Stamina, Eitr, Regen, BurnTime;
        /// <summary>음식 고르는 점수 = 체력만(사용자 11-09: 일꾼은 스태미나 무제한이라 스태미나는 쓸모없음).</summary>
        public float Value => Health;
    }

    /// <summary>
    /// NPC가 먹은 음식 (구상서 3장: 음식 상자에서 최대 3종, 수치·지속시간은 바닐라 그대로).
    /// 게임 1.0.20·1.0.21 Player.CanEat/EatFood/UpdateFood와 같은 규칙: 3종까지, 같은 음식·새 음식은 먹은 것 중 하나가
    /// 절반 넘게 지났을 때만, 1초마다 남은 시간 비율^0.3만큼 효과, 10초마다 회복량 합만큼 회복.
    /// </summary>
    public sealed class Meals
    {
        public const int Max = 3;
        /// <summary>플레이어 기본값(Player.m_baseHP, m_baseStamina).</summary>
        public const float BaseHealth = 25f, BaseStamina = 75f;

        /// <summary>
        /// 음식 효과가 끝날 때까지 줄지 않는 모드(사용자 11-09 "음식 모드"): 켜지면 일꾼도 남은 시간과 상관없이 온전한 효과(먹는 때 규칙은 그대로).
        /// 플러그인이 플레이어 상태로 알아챈다(LooksNoDecay).
        /// </summary>
        public static bool NoDecay;

        /// <summary>
        /// 음식 모드 판정(한 판에 한 번이면 됨): 바닐라라면 줄었을 음식이 있을 때(바닐라식과 온전한 합이 2 이상 차이)
        /// 최대 체력이 온전한 합이면 true(모드 있음), 바닐라식이면 false(없음). 아직 덜 지났거나 둘 다 아니면(다른 효과) null = 다시 봄.
        /// </summary>
        public static bool? DecayVerdict(float baseHp, float maxHealth, float[] food, float[] time, float[] burn)
        {
            float full = baseHp, vanilla = baseHp;
            for (int i = 0; i < food.Length; i++)
            {
                full += food[i];
                vanilla += food[i] * (float)Math.Pow(Clamp01(burn[i] > 0f ? time[i] / burn[i] : 0f), 0.3);
            }
            if (full - vanilla < 2f) return null;
            if (Math.Abs(maxHealth - full) < 1f) return true;
            if (Math.Abs(maxHealth - vanilla) < 1f) return false;
            return null;
        }

        sealed class Eaten
        {
            public FoodInfo Info;
            public float Time;
            public bool CanEatAgain => Time < Info.BurnTime / 2f;
            public float Factor => NoDecay ? (Time > 0f ? 1f : 0f) : (float)Math.Pow(Clamp01(Time / Info.BurnTime), 0.3);
        }

        readonly List<Eaten> m_eaten = new List<Eaten>();
        float m_regenTimer;

        public int Count => m_eaten.Count;
        public IEnumerable<string> Names { get { foreach (var e in m_eaten) yield return e.Info.Name; } }

        public float MaxHealth
        {
            get { float h = BaseHealth; foreach (var e in m_eaten) h += e.Info.Health * e.Factor; return h; }
        }

        public float MaxStamina
        {
            get { float s = BaseStamina; foreach (var e in m_eaten) s += e.Info.Stamina * e.Factor; return s; }
        }

        public bool CanEat(FoodInfo f)
        {
            // 음식 모드(효과가 안 줆)면 빈 칸에만 — 절반 지난 것을 새로 먹어도 효과는 같고 음식만 듦(사용자 11-09 "절약"). 끊기면 들고 간 걸 먹음(FillEmpty)
            foreach (var e in m_eaten) if (e.Info.Name == f.Name) return !NoDecay && e.CanEatAgain;
            if (m_eaten.Count < Max) return true;
            if (NoDecay) return false;
            foreach (var e in m_eaten) if (e.CanEatAgain) return true;
            return false;
        }

        /// <summary>빈 칸이 있나(음식이 끝나 칸이 빔).</summary>
        public bool HasEmptySlot => m_eaten.Count < Max;

        /// <summary>
        /// 빈 칸만 채운다(길에서 들고 간 음식 먹기, 사용자 11-09): 아직 안 먹은 음식 중 좋은 것부터, 칸이 찰 때까지. 먹은 이름들.
        /// 이미 먹은 음식은 다시 먹지 않음(바닐라에서도 절반 지난 걸 길에서 새로 먹어 들고 간 걸 써 버리지 않게).
        /// </summary>
        public List<string> FillEmpty(IEnumerable<FoodInfo> available)
        {
            var list = new List<FoodInfo>(available);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var picked = new List<string>();
            foreach (var f in list)
            {
                if (m_eaten.Count >= Max) break;
                if (picked.Contains(f.Name) || m_eaten.Exists(e => e.Info.Name == f.Name)) continue;
                m_eaten.Add(new Eaten { Info = f, Time = f.BurnTime });
                picked.Add(f.Name);
            }
            return picked;
        }

        public bool Eat(FoodInfo f)
        {
            if (!CanEat(f)) return false;
            foreach (var e in m_eaten)
                if (e.Info.Name == f.Name) { e.Info = f; e.Time = f.BurnTime; return true; }
            if (m_eaten.Count < Max) { m_eaten.Add(new Eaten { Info = f, Time = f.BurnTime }); return true; }
            Eaten most = null;
            foreach (var e in m_eaten) if (e.CanEatAgain && (most == null || e.Time < most.Time)) most = e;
            if (most == null) return false;
            most.Info = f;
            most.Time = f.BurnTime;
            return true;
        }

        /// <summary>시간이 흐른다(바닐라처럼 1초 단위, 세계 설정 음식 소모 배율은 rate). 회복할 양을 돌려준다.</summary>
        public float Tick(float seconds, float rate = 1f)
        {
            float heal = 0f;
            m_regenTimer += seconds;
            while (m_regenTimer >= 10f)
            {
                m_regenTimer -= 10f;
                foreach (var e in m_eaten) heal += e.Info.Regen;
            }
            float burn = seconds * rate;
            for (int i = m_eaten.Count - 1; i >= 0; i--)
            {
                m_eaten[i].Time -= burn;
                if (m_eaten[i].Time <= 0f) m_eaten.RemoveAt(i);
            }
            return heal;
        }

        /// <summary>
        /// 있는 음식(이름 → 수치, 개수)에서 지금 먹을 것을 고른다: 체력 높은 것부터, 같은 음식은 한 번.
        /// 먹은 것으로 치고 고른 이름을 돌려준다(호출하는 쪽이 상자에서 하나씩 뺀다).
        /// </summary>
        public List<string> EatBest(IEnumerable<FoodInfo> available)
        {
            var list = new List<FoodInfo>(available);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var picked = new List<string>();
            foreach (var f in list)
            {
                if (picked.Contains(f.Name)) continue;
                if (Eat(f)) picked.Add(f.Name);
            }
            return picked;
        }

        /// <summary>"이름:남은초;…" (수치는 불러올 때 게임 데이터에서 다시 읽는다).</summary>
        public string Save()
        {
            var sb = new StringBuilder();
            foreach (var e in m_eaten)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(e.Info.Name).Append(':').Append(e.Time.ToString("0.#", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <param name="lookup">이름 → 수치. 모르는 음식(모드 삭제 등)은 null → 버린다.</param>
        public static Meals Load(string text, Func<string, FoodInfo> lookup)
        {
            var m = new Meals();
            if (string.IsNullOrEmpty(text)) return m;
            foreach (var part in text.Split(';'))
            {
                int c = part.LastIndexOf(':');
                if (c <= 0 || m.m_eaten.Count >= Max) continue;
                if (!float.TryParse(part.Substring(c + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float t) || t <= 0f) continue;
                FoodInfo f = lookup(part.Substring(0, c));
                if (f == null || f.BurnTime <= 0f) continue;
                m.m_eaten.Add(new Eaten { Info = f, Time = Math.Min(t, f.BurnTime) });
            }
            return m;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
