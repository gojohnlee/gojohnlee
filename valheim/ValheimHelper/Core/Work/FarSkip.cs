using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 멀리선 이동 생략(사용자 결정 2026-10-23 "멀리선 단순하게"·"멀리선 이동 생략 때 지도"): 일꾼과 목적지가 모두 플레이어 64m 밖이면
    /// 길 길이 ÷ 달리기 속도만큼 기다리는 동안 지도상 위치만 길 따라 나아가고(RevealEvery초마다 밝힐 자리), 다 기다리면 몸을
    /// 불러와진 구역 안에서 HopStep m씩 목적지 쪽으로 옮긴다(ArriveNear m 안이 되면 마저 걷는다).
    /// 땅·물·위험·구역 불러와짐·플레이어 거리는 플러그인이 본다. 여기는 시간·위치 계산만.
    /// </summary>
    public sealed class FarSkip
    {
        /// <summary>이보다 짧은 걷기는 생략하지 않는다(m).</summary>
        public const double MinDistance = 60;
        /// <summary>지도상 위치를 밝히는 간격(초) — 걸을 때 Senses가 Scout.Reveal 하는 것과 같게.</summary>
        public const double RevealEvery = 2;
        /// <summary>한 번에 옮기는 거리(m): 일꾼 둘레 불러와진 구역(활성 영역 ±2구역) 안에 들게.</summary>
        public const double HopStep = 60;
        /// <summary>목적지에서 이 안이면 옮기기를 그치고 걷는다(m).</summary>
        public const double ArriveNear = 20;
        /// <summary>다 기다린 뒤 옮기기가 이 시간(초) 넘게 안 끝나면 그만두고 그 자리부터 걷는다(대체 동작).</summary>
        public const double LandLimit = 30;

        readonly List<RoughPath.P> m_path;
        double m_revealTimer;

        public double Length { get; }
        public double Seconds { get; }
        public double Elapsed { get; private set; }
        /// <summary>다 기다린 뒤 옮기는 데 쓴 시간(초).</summary>
        public double Landing { get; private set; }
        public bool Waiting => Elapsed < Seconds - 1e-6;

        public static bool Worth(double distance) => distance >= MinDistance;

        public FarSkip(IList<RoughPath.P> path, double runSpeed)
        {
            if (path == null || path.Count == 0) throw new ArgumentException("빈 길");
            if (runSpeed <= 0.1) runSpeed = 0.1;
            m_path = new List<RoughPath.P>(path);
            Length = RoughPath.Length(m_path);
            Seconds = Length / runSpeed;
        }

        /// <summary>지금 지도상 위치(길 위, 기다린 비율만큼).</summary>
        public RoughPath.P MapPos => RoughPath.PointAt(m_path, Seconds <= 0 ? Length : Length * Math.Min(1, Elapsed / Seconds));

        /// <summary>시간을 보낸다. 밝힐 때가 됐으면 true(그때 MapPos를 밝힘). 기다림이 끝나는 순간에도 한 번 true.</summary>
        public bool Tick(double dt)
        {
            if (!Waiting) { Landing += dt; return false; }
            Elapsed += dt;
            m_revealTimer += dt;
            if (m_revealTimer >= RevealEvery - 1e-6 || !Waiting) { m_revealTimer = 0; return true; }
            return false;
        }

        public bool LandTimedOut => !Waiting && Landing > LandLimit;

        /// <summary>
        /// 기다리는 도중 플레이어가 지도상 위치에 다가왔을 때 몸을 내릴 자리 차례(사용자 결정 2026-10-23 [고침]·[기본안]):
        /// 지금 지도상 위치 먼저, 그다음 길 앞쪽으로 step m씩 ahead번(도착점을 넘지 않음, 같은 자리는 한 번만).
        /// </summary>
        public IEnumerable<RoughPath.P> DropAims(double step, int ahead)
        {
            double at = Seconds <= 0 ? Length : Length * Math.Min(1, Elapsed / Seconds);
            yield return RoughPath.PointAt(m_path, at);
            double prev = at;
            for (int i = 1; i <= ahead; i++)
            {
                double d = Math.Min(Length, at + step * i);
                if (d <= prev + 1e-6) yield break; // 도착점을 이미 냈음
                yield return RoughPath.PointAt(m_path, d);
                prev = d;
            }
        }

        /// <summary>
        /// 몸(from)에서 목적지(dest) 쪽으로 다음에 내릴 자리. 목적지가 ArriveNear m 안이면 false(옮기기 끝, 걷는다).
        /// 한 번에 step m, 목적지 ArriveNear m 앞을 넘지 않는다.
        /// </summary>
        public static bool HopAim(RoughPath.P from, RoughPath.P dest, double step, out RoughPath.P aim)
        {
            double dx = dest.X - from.X, dz = dest.Z - from.Z;
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d <= ArriveNear) { aim = dest; return false; }
            double go = Math.Min(step, d - ArriveNear * 0.5);
            aim = new RoughPath.P(from.X + dx / d * go, from.Z + dz / d * go);
            return true;
        }
    }
}
