using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 지원 기다리기·엇갈림 없게(사용자 11-13 00:30): 한 명 더 필요한 일을 대기열에 둔다.
    /// 손 빈 일꾼은 스스로 한 일보다 지원을 먼저 본다. 여러 번 미뤄진 요청(UrgentAfter 이상)은 맨 앞으로,
    /// 나머지는 들어온 순서대로. 그래서 같은 일이 끝없이 밀리지 않는다.
    /// </summary>
    public sealed class HelpQueue
    {
        /// <summary>이만큼 미뤄지면 다른 일보다 앞으로.</summary>
        public const int UrgentAfter = 2;

        /// <summary>대기 중인 지원 요청 하나.</summary>
        public sealed class Ask
        {
            /// <summary>무슨 일인지(예: "창자 20개").</summary>
            public string Task;
            /// <summary>더 필요한 사람 수.</summary>
            public int Needed;
            /// <summary>미뤄진 횟수.</summary>
            public int Postponed;
            /// <summary>들어온 순서.</summary>
            public long Seq;
        }

        private readonly List<Ask> _asks = new List<Ask>();
        private long _seq;

        /// <summary>대기 중인 요청 수.</summary>
        public int Count => _asks.Count;

        /// <summary>요청을 넣는다. 같은 일이 이미 있으면 더 필요한 수만 큰 쪽으로 맞춘다.</summary>
        public void Add(string task, int needed)
        {
            if (string.IsNullOrEmpty(task) || needed <= 0 || HasSep(task)) return;
            var old = Find(task);
            if (old != null)
            {
                if (needed > old.Needed) old.Needed = needed;
                return;
            }
            _asks.Add(new Ask { Task = task, Needed = needed, Seq = _seq++ });
        }

        /// <summary>지금 손 빈 일꾼이 가장 먼저 도울 요청(없으면 null). 빈 대기열은 null.</summary>
        public Ask Next()
        {
            Ask best = null;
            foreach (var a in _asks)
            {
                if (best == null || Before(a, best)) best = a;
            }
            return best;
        }

        /// <summary>그 일을 한 번 미룸(다른 일을 먼저 한 경우). 없는 일이면 무시.</summary>
        public void Postpone(string task)
        {
            var a = Find(task);
            if (a != null) a.Postponed++;
        }

        /// <summary>도우러 온 사람 수만큼 줄인다. 다 채워지면 지우고 true.</summary>
        public bool Served(string task, int helpers)
        {
            var a = Find(task);
            if (a == null) return false;
            a.Needed -= Math.Max(0, helpers);
            if (a.Needed > 0) return false;
            _asks.Remove(a);
            return true;
        }

        /// <summary>그 일 요청을 취소한다(일이 없어졌거나 포기).</summary>
        public void Remove(string task)
        {
            var a = Find(task);
            if (a != null) _asks.Remove(a);
        }

        /// <summary>저장 글: "일=필요수/미룬수;…" (들어온 순서대로). 일 이름에 ; = / 가 있으면 넣지 않음.</summary>
        public string Save()
        {
            var parts = new List<string>();
            foreach (var a in _asks)
            {
                parts.Add(a.Task + "=" + a.Needed.ToString(CultureInfo.InvariantCulture) + "/" + a.Postponed.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(";", parts.ToArray());
        }

        /// <summary>저장 글을 되읽는다. 깨진 조각은 건너뛴다. 들어온 순서는 저장 순서대로 다시 맞춘다.</summary>
        public static HelpQueue Load(string text)
        {
            var q = new HelpQueue();
            if (string.IsNullOrEmpty(text)) return q;
            foreach (var part in text.Split(';'))
            {
                int eq = part.LastIndexOf('=');
                if (eq < 1) continue;
                int slash = part.LastIndexOf('/');
                if (slash <= eq) continue;
                string task = part.Substring(0, eq);
                if (!int.TryParse(part.Substring(eq + 1, slash - eq - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int needed)) continue;
                if (!int.TryParse(part.Substring(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int postponed)) continue;
                if (needed <= 0 || HasSep(task)) continue;
                q._asks.Add(new Ask { Task = task, Needed = needed, Postponed = Math.Max(0, postponed), Seq = q._seq++ });
            }
            return q;
        }

        private static bool HasSep(string task) => task.IndexOf(';') >= 0 || task.IndexOf('=') >= 0 || task.IndexOf('/') >= 0;

        private Ask Find(string task)
        {
            foreach (var a in _asks)
            {
                if (a.Task == task) return a;
            }
            return null;
        }

        private static bool Before(Ask x, Ask y)
        {
            bool xu = x.Postponed >= UrgentAfter, yu = y.Postponed >= UrgentAfter;
            if (xu != yu) return xu;
            return x.Seq < y.Seq;
        }
    }
}
