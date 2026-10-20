using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// NPC 스킬 (구상서 3-1장): 바닐라 플레이어와 같은 규칙으로 쓰면 오른다(Skills.Skill.Raise), 단 10에서 시작해 70까지만,
    /// 죽어도 안 깎인다. 스킬 종류는 게임 Skills.SkillType 값(int). NPC마다 따로 둔다.
    /// </summary>
    public sealed class SkillBook
    {
        public const float Start = 10f, Max = 70f;
        /// <summary>이만큼마다 알린다("곡괭이 25. 손에 좀 익네요.").</summary>
        public const int AnnounceEvery = 5;

        readonly Dictionary<int, float> _level = new Dictionary<int, float>();
        readonly Dictionary<int, float> _acc = new Dictionary<int, float>();

        public float Level(int type) => type == 0 ? 0f : _level.TryGetValue(type, out float v) ? v : Start;

        /// <summary>바닐라 Skills.GetSkillFactor: 레벨 ÷ 100.</summary>
        public float Factor(int type) => Math.Min(1f, Math.Max(0f, Level(type) / 100f));

        /// <summary>바닐라 Skills.GetRandomSkillFactor: 0.4~1 사이(스킬로 보간) ±0.15. rnd는 0~1.</summary>
        public float RandomFactor(int type, double rnd)
        {
            float mid = 0.4f + 0.6f * Factor(type);
            float lo = Math.Max(0f, mid - 0.15f), hi = Math.Min(1f, mid + 0.15f);
            return lo + (hi - lo) * (float)rnd;
        }

        /// <summary>평균 피해 계수(위험 판단용).</summary>
        public float MeanFactor(int type) => RandomFactor(type, 0.5);

        /// <summary>바닐라 다음 레벨 요구치: (floor(레벨+1))^1.5 × 0.5 + 0.5.</summary>
        public static float NextRequirement(float level) => (float)(Math.Pow(Math.Floor(level + 1f), 1.5) * 0.5 + 0.5);

        /// <summary>
        /// 쓴 만큼 올린다(바닐라: 쌓인 값 += 스킬 증가폭 × factor × 세계 스킬 배율, 요구치를 넘으면 +1). 70이면 안 오른다.
        /// </summary>
        /// <returns>새로 5의 배수에 닿았으면 그 레벨, 아니면 0.</returns>
        public int Raise(int type, float increaseStep, float factor, float gainRate)
        {
            if (type == 0) return 0;
            float level = Level(type);
            if (level >= Max) return 0;
            _acc.TryGetValue(type, out float acc);
            acc += increaseStep * factor * gainRate;
            Dirty = true;
            if (acc < NextRequirement(level)) { _acc[type] = acc; return 0; }
            level = Math.Min(Max, level + 1f);
            _level[type] = level;
            _acc[type] = 0f;
            int l = (int)level;
            return l % AnnounceEvery == 0 ? l : 0;
        }

        public bool Dirty;

        /// <summary>시작값에서 오른 스킬들(높은 순).</summary>
        public List<KeyValuePair<int, float>> Raised()
        {
            var list = new List<KeyValuePair<int, float>>();
            foreach (var kv in _level) if (kv.Value > Start) list.Add(kv);
            list.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
            return list;
        }

        // ---- 저장: "type=level:acc;…" ----

        public string Save()
        {
            var sb = new StringBuilder();
            var keys = new List<int>(_level.Keys);
            foreach (var k in _acc.Keys) if (!_level.ContainsKey(k)) keys.Add(k);
            keys.Sort();
            foreach (int k in keys)
            {
                if (sb.Length > 0) sb.Append(';');
                _acc.TryGetValue(k, out float a);
                sb.Append(k).Append('=').Append(Level(k).ToString("0", CultureInfo.InvariantCulture)).Append(':')
                  .Append(a.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static SkillBook Load(string text)
        {
            var b = new SkillBook();
            if (string.IsNullOrEmpty(text)) return b;
            var inv = CultureInfo.InvariantCulture;
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('='), colon = part.IndexOf(':');
                if (eq <= 0 || colon < eq) continue;
                if (!int.TryParse(part.Substring(0, eq), NumberStyles.Integer, inv, out int k)
                    || !float.TryParse(part.Substring(eq + 1, colon - eq - 1), NumberStyles.Float, inv, out float lv)
                    || !float.TryParse(part.Substring(colon + 1), NumberStyles.Float, inv, out float a)) continue;
                b._level[k] = Math.Min(Max, Math.Max(Start, lv));
                b._acc[k] = Math.Max(0f, a);
            }
            return b;
        }
    }
}
