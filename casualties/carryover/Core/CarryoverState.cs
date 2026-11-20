using System;
using System.Globalization;
using System.Text;

namespace Carryover.Core
{
    /// <summary>
    /// 회차 보너스 규칙: 다음 회차는 직전 한 회차에서 새로 얻은 능력치(끝 값 - 시작 값, 시작 값은 물려받은 보너스 포함)만 받는다. 누적 없음.
    /// 능력치 순서는 게임과 같다: 0=STR(힘), 1=RES(회복력), 2=INT(지능).
    /// </summary>
    public sealed class CarryoverState
    {
        public const int StatCount = 3;

        /// <summary>지금 회차를 시작할 때 값(보너스 포함). RunActive일 때만 뜻이 있다.</summary>
        public int[] Start = new int[StatCount];

        /// <summary>다음 새 회차에 줄 보너스(= 마지막으로 끝난 회차에서 얻은 양).</summary>
        public int[] Pending = new int[StatCount];

        /// <summary>셈하는 회차가 진행 중인가(튜토리얼은 아님).</summary>
        public bool RunActive;

        /// <summary>새 회차 시작: 게임이 정한 기본값에 보너스를 더한 값을 돌려주고, 그 값을 이번 회차 시작 값으로 기억한다.
        /// 보너스는 회차가 끝나야(OnRunEnd) 바뀐다 — 끝나지 않은 회차(나가기·재시작)는 셈하지 않는다.</summary>
        public int[] OnNewRun(int[] baseLevels)
        {
            Check(baseLevels);
            var levels = new int[StatCount];
            for (int i = 0; i < StatCount; i++)
                levels[i] = Math.Max(0, baseLevels[i] + Pending[i]);
            Array.Copy(levels, Start, StatCount);
            RunActive = true;
            return levels;
        }

        /// <summary>저장한 회차를 이어 함: 시작 값은 그대로. 모드를 회차 도중에 깔았으면 지금 값을 시작 값으로 삼는다.</summary>
        public void OnLoadedRun(int[] currentLevels)
        {
            Check(currentLevels);
            if (RunActive) return;
            Array.Copy(currentLevels, Start, StatCount);
            RunActive = true;
        }

        /// <summary>셈하지 않는 회차(튜토리얼) 시작.</summary>
        public void OnUncountedRun() => RunActive = false;

        /// <summary>회차 끝(죽음·엔딩): 얻은 양을 다음 회차 보너스로. 같은 회차에서 여러 번 불려도(되살아남) 시작 값 기준이라 결과가 같다.
        /// 셈하는 회차가 아니면 아무것도 바꾸지 않고 null.</summary>
        public int[] OnRunEnd(int[] endLevels)
        {
            Check(endLevels);
            if (!RunActive) return null;
            var gain = Gain(Start, endLevels);
            Array.Copy(gain, Pending, StatCount);
            return gain;
        }

        public static int[] Gain(int[] start, int[] end)
        {
            Check(start); Check(end);
            var g = new int[StatCount];
            for (int i = 0; i < StatCount; i++) g[i] = Math.Max(0, end[i] - start[i]);
            return g;
        }

        // 저장 꼴: key=value 줄. 모르는 키·깨진 줄은 무시(기본값).
        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("version=1\n");
            sb.Append("runActive=").Append(RunActive ? "1" : "0").Append('\n');
            sb.Append("start=").Append(Join(Start)).Append('\n');
            sb.Append("pending=").Append(Join(Pending)).Append('\n');
            return sb.ToString();
        }

        public static CarryoverState Parse(string text)
        {
            var s = new CarryoverState();
            if (string.IsNullOrEmpty(text)) return s;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "runActive": s.RunActive = val == "1"; break;
                    case "start": TrySplit(val, s.Start); break;
                    case "pending": TrySplit(val, s.Pending); break;
                }
            }
            return s;
        }

        private static string Join(int[] a) => string.Join(",", Array.ConvertAll(a, x => x.ToString(CultureInfo.InvariantCulture)));

        private static void TrySplit(string val, int[] into)
        {
            var parts = val.Split(',');
            if (parts.Length != StatCount) return;
            var tmp = new int[StatCount];
            for (int i = 0; i < StatCount; i++)
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tmp[i]) || tmp[i] < 0) return;
            Array.Copy(tmp, into, StatCount);
        }

        private static void Check(int[] a)
        {
            if (a == null || a.Length != StatCount) throw new ArgumentException("stats must be 3 (STR, RES, INT)");
        }
    }
}
