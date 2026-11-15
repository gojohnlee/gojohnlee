using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 일 사이 빈틈 재기(사용자 11-12 16:24 "최종 실전 시험 덧붙임"): 일꾼이 "무엇 하느라" 가만히 서 있었나를 일꾼·동작별 횟수·평균·최악으로 모은다.
    /// 게임 쪽은 일꾼마다 지금 동작 이름과 "가만히 섬" 여부를 자주 넘긴다(Tick). 서기 시작해서 다시 움직이거나 동작이 바뀔 때까지가 한 번.
    /// 낱개 기록은 안 두고 합계만 둬서 크기가 안 늘어난다(동작 종류 상한 MaxKeys — 넘으면 "기타"로 모음).
    /// </summary>
    public sealed class GapLog
    {
        /// <summary>이보다 짧게 선 것은 안 셈(걸음 사이 잠깐 멈춤).</summary>
        public const double MinSecs = 0.3;
        public const int MaxKeys = 300;
        public const string Other = "기타";

        public sealed class Row
        {
            public string Worker, Step;
            public int Count;
            public double Total, Worst;
            public double Avg => Count > 0 ? Total / Count : 0;
        }

        sealed class Open { public string Step; public double Since; }

        readonly Dictionary<string, Open> m_open = new Dictionary<string, Open>();
        readonly Dictionary<string, Row> m_rows = new Dictionary<string, Row>();

        /// <summary>still: 지금 가만히 서 있나(걷지도 휘두르지도 않음). now: 초(늘 커지는 시계).</summary>
        public void Tick(string worker, string step, bool still, double now)
        {
            if (string.IsNullOrEmpty(worker)) return;
            step = string.IsNullOrEmpty(step) ? "?" : step;
            if (m_open.TryGetValue(worker, out var o) && (!still || o.Step != step))
            {
                Add(worker, o.Step, now - o.Since);
                m_open.Remove(worker);
                o = null;
            }
            if (still && o == null) m_open[worker] = new Open { Step = step, Since = now };
        }

        /// <summary>일꾼이 사라짐(죽음·구역 내림) 또는 표를 뽑기 직전: 서 있던 것을 지금까지로 닫는다.</summary>
        public void Close(string worker, double now)
        {
            if (worker == null || !m_open.TryGetValue(worker, out var o)) return;
            Add(worker, o.Step, now - o.Since);
            m_open.Remove(worker);
        }

        public void CloseAll(double now)
        {
            foreach (var w in m_open.Keys.ToList()) Close(w, now);
        }

        public void Clear() { m_open.Clear(); m_rows.Clear(); }

        void Add(string worker, string step, double secs)
        {
            if (secs < MinSecs || double.IsNaN(secs)) return;
            string key = worker + "\n" + step;
            if (!m_rows.TryGetValue(key, out var r))
            {
                if (m_rows.Count >= MaxKeys)
                {
                    step = Other;
                    key = worker + "\n" + step;
                    m_rows.TryGetValue(key, out r);
                }
                if (r == null) m_rows[key] = r = new Row { Worker = worker, Step = step };
            }
            r.Count++;
            r.Total += secs;
            if (secs > r.Worst) r.Worst = secs;
        }

        /// <summary>최악이 긴 순(같으면 합이 큰 순).</summary>
        public List<Row> Rows() => m_rows.Values.OrderByDescending(r => r.Worst).ThenByDescending(r => r.Total).ToList();

        /// <summary>history·diag용 마크다운 표. minWorst(초)보다 최악이 짧은 줄은 뺀다. 줄이 없으면 머리만.</summary>
        public static List<string> Table(IEnumerable<Row> rows, double minWorst = 0)
        {
            var lines = new List<string> { "| 일꾼 | 동작 | 횟수 | 평균(초) | 최악(초) | 합(초) |", "|---|---|---|---|---|---|" };
            foreach (var r in rows)
                if (r.Worst >= minWorst)
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "| {0} | {1} | {2} | {3:0.0} | {4:0.0} | {5:0} |", r.Worker, r.Step, r.Count, r.Avg, r.Worst, r.Total));
            return lines;
        }
    }
}
