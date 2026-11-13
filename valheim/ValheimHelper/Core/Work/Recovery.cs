using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimHelper.Core.Work
{
    public enum RecoverWay
    {
        /// <summary>둘레에서 안전한 채집을 하며 체력이 찰 때까지.</summary>
        Nearby,
        /// <summary>집에 와서 짐 넣고 집안일 하며 체력이 찰 때까지.</summary>
        Home,
        /// <summary>같은 곳에서 너무 자주 크게 다침 — "거긴 힘들어" 하고 접음.</summary>
        GiveUp,
    }

    /// <summary>
    /// 다쳐도 일을 접지 않고 회복했다 이어 가기(사용자 11-12 23:55 요청 ②). 사냥·싸움 끼는 모으기 일이
    /// 체력 Hurt 밑이면 일을 끝내지 않고 회복 대기 → Full 이상이면 남은 개수를 채우러 다시 감.
    /// 근처 채집 대기는 짐 칸이 남고 둘레에 적 없는 채집이 있을 때만, 대기 중 또 맞으면 집 대기로(회차 480 받음).
    /// 같은 곳(Radius 안)에서 GiveUpAfter번 크게 다치면 접음. 이 기록은 저장 글로 남김(재시작에도).
    /// </summary>
    public sealed class Recovery
    {
        /// <summary>이 밑이면 크게 다침(사냥 huntHurt 0.4와 같음).</summary>
        public const float Hurt = 0.4f;
        /// <summary>이 이상이면 회복 끝.</summary>
        public const float Full = 0.95f;
        /// <summary>같은 곳으로 보는 거리(m).</summary>
        public const float Radius = 40f;
        public const int GiveUpAfter = 3;

        struct Spot { public float X, Z; public int Count; }
        readonly List<Spot> _spots = new List<Spot>();

        public static bool IsHurt(float health, float max) => max > 0f && health < max * Hurt;
        public static bool IsReady(float health, float max) => max <= 0f || health >= max * Full;
        /// <summary>사냥 중 물러남: 체력이 덜 찬 채였으면 장비·무리 탓이 아니라 다침 — 회복하고 이어 감(회차 580 vh_entrailstest, 58%에서 드라우그에 물러남).
        /// 다 찬 채 물러났으면 정말 센 것(preyPack·preyStrong).</summary>
        public static bool RetreatIsHurt(float health, float max) => !IsReady(health, max);

        /// <summary>다친 곳을 셈. 같은 곳이 GiveUpAfter번이 되면 true(접음).</summary>
        public bool Record(float x, float z)
        {
            for (int i = 0; i < _spots.Count; i++)
            {
                var s = _spots[i];
                float dx = s.X - x, dz = s.Z - z;
                if (dx * dx + dz * dz > Radius * Radius) continue;
                s.Count++;
                _spots[i] = s;
                return s.Count >= GiveUpAfter;
            }
            _spots.Add(new Spot { X = x, Z = z, Count = 1 });
            return GiveUpAfter <= 1;
        }

        public int CountAt(float x, float z)
        {
            foreach (var s in _spots)
            {
                float dx = s.X - x, dz = s.Z - z;
                if (dx * dx + dz * dz <= Radius * Radius) return s.Count;
            }
            return 0;
        }

        /// <summary>일을 다 채우거나 새 일로 바뀌면 지움.</summary>
        public void Clear() => _spots.Clear();

        /// <param name="tooHard">Record가 true였음.</param>
        /// <param name="bagSpace">짐 칸이 남음.</param>
        /// <param name="safeGatherNear">둘레에 적 없는 채집거리가 있음.</param>
        /// <param name="hitWhileWaiting">근처 대기 중 또 맞음.</param>
        public static RecoverWay Choose(bool tooHard, bool bagSpace, bool safeGatherNear, bool hitWhileWaiting)
        {
            if (tooHard) return RecoverWay.GiveUp;
            return bagSpace && safeGatherNear && !hitWhileWaiting ? RecoverWay.Nearby : RecoverWay.Home;
        }

        /// <summary>저장 글: "x,z,n;…" (소수 한 자리). 깨진 조각은 Load가 건너뜀.</summary>
        public string Save()
        {
            var parts = new List<string>();
            foreach (var s in _spots)
                parts.Add(s.X.ToString("0.0", CultureInfo.InvariantCulture) + "," + s.Z.ToString("0.0", CultureInfo.InvariantCulture) + "," + s.Count.ToString(CultureInfo.InvariantCulture));
            return string.Join(";", parts.ToArray());
        }

        public static Recovery Load(string text)
        {
            var r = new Recovery();
            if (string.IsNullOrEmpty(text)) return r;
            foreach (var part in text.Split(';'))
            {
                var f = part.Split(',');
                if (f.Length != 3) continue;
                if (!float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                if (!float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n <= 0) continue;
                r._spots.Add(new Spot { X = x, Z = z, Count = Math.Min(n, GiveUpAfter) });
            }
            return r;
        }
    }
}
