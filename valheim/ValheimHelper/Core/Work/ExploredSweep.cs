using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 밝혀진 곳은 안다 (사용자 결정 2026-10-21, 묶음 1 작업 3): 지도에 밝혀진 구역을 거점 둘레부터 조금씩 훑어 지식에 넣을 구역을 고른다.
    /// 한 번에 확인 Check개·넣기 Absorb개까지(프레임 끊김 방지). 넣은 구역은 다시 보지 않는다(그 뒤는 NPC 시야가 갱신).
    /// 밝혀졌지만 아직 생성 안 된 구역(absorb가 false)은 다음 바퀴에 다시 본다.
    /// </summary>
    public sealed class ExploredSweep
    {
        /// <summary>거점에서 몇 구역(64m)까지 보는지 — 탐험 상한(Scout.MaxRing)과 같게.</summary>
        public readonly int Ring;
        readonly HashSet<ZoneXY> _done = new HashSet<ZoneXY>();
        readonly List<ZoneXY> _order = new List<ZoneXY>();
        List<ZoneXY> _homes = new List<ZoneXY>();
        int _cursor;

        public ExploredSweep(int ring) { Ring = ring; }

        public int Done => _done.Count;
        public bool IsDone(ZoneXY z) => _done.Contains(z);

        /// <summary>다시 처음부터(시험·월드 바뀜).</summary>
        public void Reset()
        {
            _done.Clear();
            _order.Clear();
            _homes = new List<ZoneXY>();
            _cursor = 0;
        }

        /// <summary>
        /// 한 걸음. explored(z) = 지도에 밝혀졌나, absorb(z) = 지식에 넣었나(생성 안 됐으면 false). 넣은 구역 수를 돌려준다.
        /// homes가 바뀌면 순서를 새로 만든다(이사). 가까운 구역부터.
        /// </summary>
        public int Step(IList<ZoneXY> homes, Func<ZoneXY, bool> explored, Func<ZoneXY, bool> absorb, int check, int absorbMax)
        {
            if (homes == null || homes.Count == 0) return 0;
            if (!Same(homes, _homes)) Build(homes);
            if (_order.Count == 0) return 0;
            int absorbed = 0;
            for (int i = 0; i < check && absorbed < absorbMax; i++)
            {
                if (_cursor >= _order.Count) _cursor = 0;
                ZoneXY z = _order[_cursor++];
                if (_done.Contains(z) || !explored(z)) continue;
                if (!absorb(z)) continue;
                _done.Add(z);
                absorbed++;
            }
            return absorbed;
        }

        void Build(IList<ZoneXY> homes)
        {
            _homes = new List<ZoneXY>(homes);
            _order.Clear();
            _cursor = 0;
            var offsets = new List<ZoneXY>();
            for (int dx = -Ring; dx <= Ring; dx++)
                for (int dy = -Ring; dy <= Ring; dy++)
                    offsets.Add(new ZoneXY(dx, dy));
            var origin = new ZoneXY(0, 0);
            offsets.Sort((a, b) =>
            {
                int c = a.DistSq(origin).CompareTo(b.DistSq(origin));
                if (c != 0) return c;
                c = a.X.CompareTo(b.X);
                return c != 0 ? c : a.Y.CompareTo(b.Y);
            });
            var seen = new HashSet<ZoneXY>();
            foreach (var o in offsets) // 거점이 여럿이면 같은 거리끼리 번갈아
                foreach (var h in homes)
                {
                    var z = new ZoneXY(h.X + o.X, h.Y + o.Y);
                    if (seen.Add(z)) _order.Add(z);
                }
        }

        static bool Same(IList<ZoneXY> a, IList<ZoneXY> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!a[i].Equals(b[i])) return false;
            return true;
        }

        /// <summary>
        /// 구역 안 확인 점(구역 중심 기준 오프셋, m): 중심 + 네 모서리 안쪽. 지도 IsExplored는 한 점만 보므로
        /// 가장자리만 밝힌 구역을 놓치지 않으려고 여럿을 본다. 하나라도 밝혀졌으면 밝혀진 구역.
        /// </summary>
        public static float[][] SamplePoints(float zoneSize)
        {
            float d = zoneSize * 0.5f - 8f;
            return new[]
            {
                new[] { 0f, 0f }, new[] { d, d }, new[] { d, -d }, new[] { -d, d }, new[] { -d, -d },
            };
        }
    }
}
