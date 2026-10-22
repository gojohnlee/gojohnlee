using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimHelper.Core.Work
{
    /// <summary>막힌 뒤 할 일(StuckBook.WalkFailed).</summary>
    public enum StuckNext
    {
        /// <summary>목표 쪽 가까운 자리로 순간이동해 본다(허용한 일·먼 곳만, 조각 나). 안 되면 HopFailed.</summary>
        Hop,
        /// <summary>이 목적지는 이번 일 후보에서 뺀다 → 다른 목표.</summary>
        Exclude,
        /// <summary>뺀 곳이 너무 많다(대안 없음) → 일 보류 + "갈 수가 없어요" → 집.</summary>
        GiveUp,
    }

    /// <summary>
    /// 막힘 복구(사용자 결정 2026-10-23, decisions.txt): 목적지별 실패 횟수만 센다. 막힘 감지 자체는 걷는 쪽(진척 없음, 캐기·싸움·대기 제외).
    /// 순서: 1) 비켜 가기·점프(걷기가 함) 2) [허용한 일만] 목표 쪽 순간이동 3) 같은 목적지 두 번째 실패(2번 불가 포함) → 후보에서 뺌
    /// 4) 대안 없음 → 보류·보고·집. 어느 단계든 끝나거나 실패로 닫히고, 같은 목적지로 다시 걷기를 되풀이하지 않는다.
    /// 귀환은 따로: 걷기 막힘이 HomeWalkTries번이면 깃발로 순간이동, 그것도 안 되면 그 자리 쉬기 + "집에 못 가요".
    /// 이번 일 동안만(영구 위험 지도 아님), 작업 저장에 넣어 불러와도 이어진다(Save/Load).
    /// </summary>
    public sealed class StuckBook
    {
        /// <summary>같은 목적지로 보는 칸 크기(m). 큰 광맥 조각·같은 나무 자리를 한 목적지로.</summary>
        public const double Cell = 8.0;
        /// <summary>같은 목적지 이만큼 실패하면 뺀다.</summary>
        public const int ExcludeAt = 2;
        /// <summary>서로 다른 목적지를 이만큼 뺐으면 대안 없음(그 일은 막힌 곳만 남았다).</summary>
        public const int GiveUpAt = 4;
        /// <summary>귀환 걷기 막힘 상한: 이 횟수째 막히면 더 걷지 않고 깃발로 순간이동.</summary>
        public const int HomeWalkTries = 2;
        /// <summary>깃발 순간이동 뒤 깃발에서 이보다 멀면 순간이동 실패로 본다(m).</summary>
        public const double HomeArrived = 5.0;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly Dictionary<long, int> m_fails = new Dictionary<long, int>();

        /// <summary>귀환 걷기에서 막힌 횟수(집에 닿으면 HomeReached로 0).</summary>
        public int HomeStuck { get; private set; }

        static long Key(double x, double z) =>
            ((long)(int)Math.Floor(x / Cell) << 32) | (uint)(int)Math.Floor(z / Cell);

        public int Fails(double x, double z) => m_fails.TryGetValue(Key(x, z), out int n) ? n : 0;

        public bool Excluded(double x, double z) => Fails(x, z) >= ExcludeAt;

        public int ExcludedCount
        {
            get { int c = 0; foreach (var n in m_fails.Values) if (n >= ExcludeAt) c++; return c; }
        }

        /// <summary>
        /// 그 목적지로 걷다 막혔다(비켜 가기·점프 뒤). hopAllowed = 이 일이 목표 쪽 순간이동을 허용하고 먼 곳이다.
        /// 첫 실패이고 허용이면 Hop, 아니면 순간이동 불가도 실패로 세어 Exclude(뺀 곳이 GiveUpAt 이상이면 GiveUp).
        /// </summary>
        public StuckNext WalkFailed(double x, double z, bool hopAllowed)
        {
            int n = Bump(x, z);
            if (n < ExcludeAt && hopAllowed) return StuckNext.Hop;
            if (n < ExcludeAt) Bump(x, z); // 2) 불가 = 이번 시도 실패
            return ExcludedCount >= GiveUpAt ? StuckNext.GiveUp : StuckNext.Exclude;
        }

        /// <summary>목표 쪽 순간이동을 못 했다(착지점 없음·조건 안 됨) 또는 하고도 또 막혔다 → 실패로 센다.</summary>
        public StuckNext HopFailed(double x, double z)
        {
            int n = Bump(x, z);
            if (n < ExcludeAt) Bump(x, z);
            return ExcludedCount >= GiveUpAt ? StuckNext.GiveUp : StuckNext.Exclude;
        }

        int Bump(double x, double z)
        {
            long k = Key(x, z);
            m_fails.TryGetValue(k, out int n);
            m_fails[k] = ++n;
            return n;
        }

        /// <summary>귀환 걷기가 막혔다. true = 상한, 더 걷지 말고 깃발로 순간이동.</summary>
        public bool HomeWalkFailed() => ++HomeStuck >= HomeWalkTries;

        public void HomeReached() => HomeStuck = 0;

        /// <summary>"칸x|칸z|횟수;..." + 끝에 "h|귀환 막힘"(있을 때만).</summary>
        public string Save()
        {
            var parts = new List<string>();
            foreach (var kv in m_fails)
                parts.Add(((int)(kv.Key >> 32)).ToString(Inv) + "|" + ((int)(uint)kv.Key).ToString(Inv) + "|" + kv.Value.ToString(Inv));
            if (HomeStuck > 0) parts.Add("h|" + HomeStuck.ToString(Inv));
            return string.Join(";", parts.ToArray());
        }

        public static StuckBook Load(string text)
        {
            var b = new StuckBook();
            if (string.IsNullOrEmpty(text)) return b;
            foreach (var part in text.Split(';'))
            {
                var f = part.Split('|');
                if (f.Length == 2 && f[0] == "h" && int.TryParse(f[1], NumberStyles.Integer, Inv, out int h)) { b.HomeStuck = Math.Max(0, h); continue; }
                if (f.Length != 3) continue;
                if (!int.TryParse(f[0], NumberStyles.Integer, Inv, out int cx) || !int.TryParse(f[1], NumberStyles.Integer, Inv, out int cz)
                    || !int.TryParse(f[2], NumberStyles.Integer, Inv, out int n) || n <= 0) continue;
                b.m_fails[((long)cx << 32) | (uint)cz] = n;
            }
            return b;
        }
    }
}
