namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 거점 범위(사용자 결정 2026-10-23): 깃발에서 125m. 상자·제작대·설비·밭·밤사이 정산의 "거점에 있음"이 모두 이것
    /// (예전 상자 20m·설비 30m는 집만으로 거의 차서 마당·바깥 밭이 빠짐).
    /// 게임 시뮬레이션 거리 기본(근거리 2구역)이면 깃발 구역 둘레 ≥128m가 불러와져 있어 이 안은 다 보인다(가장 낮은 설정이면 ≥64m만 — 안 보이는 건 건너뜀).
    /// </summary>
    public static class BaseArea
    {
        public const float Radius = 125f;

        public static bool Contains(float distanceXZ) => distanceXZ <= Radius;
    }
}
