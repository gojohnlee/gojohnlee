using System;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 동시 시험 자리 고르기: 고정 자리(TestSpots.Ring) 둘레를 나선으로 훑어 가장 점수 높은 곳을 고른다.
    /// 점수(평평함·숲·물 아님 등)는 게임 쪽이 매기고, 여기서는 훑는 순서와 고르기만 한다.
    /// </summary>
    public static class SpotSearch
    {
        /// <summary>
        /// center에서 step 간격 정사각 고리를 maxRadius까지 넓혀 가며 score(x,z)가 minScore 이상인 첫 곳을 돌려준다.
        /// 가까운 고리가 먼저라 가까운 곳이 이긴다. 못 찾으면 false.
        /// </summary>
        public static bool Find(float cx, float cz, float step, float maxRadius, float minScore,
            Func<float, float, float> score, out float x, out float z)
        {
            x = cx; z = cz;
            if (step <= 0f) return false;
            int rings = (int)(maxRadius / step);
            for (int r = 0; r <= rings; r++)
            {
                float best = float.NegativeInfinity;
                bool found = false;
                for (int i = -r; i <= r; i++)
                    for (int j = -r; j <= r; j++)
                    {
                        if (Math.Max(Math.Abs(i), Math.Abs(j)) != r) continue; // 이 고리의 테두리만
                        float px = cx + i * step, pz = cz + j * step;
                        float s = score(px, pz);
                        if (s >= minScore && s > best) { best = s; x = px; z = pz; found = true; }
                    }
                if (found) return true;
            }
            return false;
        }
    }
}
