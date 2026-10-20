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
        public float Value => Health + Stamina;
    }

    /// <summary>
    /// NPC가 먹은 음식 (구상서 3장: 음식 상자에서 최대 3종, 수치·지속시간은 바닐라 그대로).
    /// 게임 1.0.20 Player.CanEat/EatFood/UpdateFood와 같은 규칙: 3종까지, 같은 음식·새 음식은 먹은 것 중 하나가
    /// 절반 넘게 지났을 때만, 1초마다 남은 시간 비율^0.3만큼 효과, 10초마다 회복량 합만큼 회복.
    /// </summary>
    public sealed class Meals
    {
        public const int Max = 3;
        /// <summary>플레이어 기본값(Player.m_baseHP, m_baseStamina).</summary>
        public const float BaseHealth = 25f, BaseStamina = 75f;

        sealed class Eaten
        {
            public FoodInfo Info;
            public float Time;
            public bool CanEatAgain => Time < Info.BurnTime / 2f;
            public float Factor => (float)Math.Pow(Clamp01(Time / Info.BurnTime), 0.3);
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
            foreach (var e in m_eaten) if (e.Info.Name == f.Name) return e.CanEatAgain;
            foreach (var e in m_eaten) if (e.CanEatAgain) return true;
            return m_eaten.Count < Max;
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
        /// 있는 음식(이름 → 수치, 개수)에서 지금 먹을 것을 고른다: 좋은 것(체력+스태미나)부터, 같은 음식은 한 번.
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
