using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 동시 시험(일꾼 N명)의 고정 자리: 월드 중심 둘레 원 위에 이웃 자리끼리 spacing 이상 떨어지게 N곳.
    /// 게임 무관 계산만 한다. 실제로 서 있을 수 있는 땅인지는 호출한 쪽이 근처에서 골라 옮긴다.
    /// </summary>
    public static class TestSpots
    {
        /// <summary>count가 1이면 중심(0,0), 아니면 둘레 원 위에 같은 간격. 반지름은 이웃 거리 ≥ spacing이 되도록 정한다.</summary>
        public static List<(float X, float Z)> Ring(int count, float spacing)
        {
            var list = new List<(float, float)>();
            if (count <= 0) return list;
            if (count == 1) { list.Add((0f, 0f)); return list; }
            if (count == 2) { list.Add((-spacing / 2f, 0f)); list.Add((spacing / 2f, 0f)); return list; }
            // 정다각형 변의 길이 = 2 r sin(π/n) = spacing
            double r = spacing / (2.0 * Math.Sin(Math.PI / count));
            for (int i = 0; i < count; i++)
            {
                double a = 2.0 * Math.PI * i / count;
                list.Add(((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a))));
            }
            return list;
        }
    }
}
