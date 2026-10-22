namespace ValheimHelper.Core.Npc
{
    /// <summary>
    /// 장비 수리 판단(구상서 4장, 결정 E). 바닐라 수리는 무료지만 일꾼이 매번 제작대로 걸어가지 않게
    /// 반 넘게 닳았거나 부러진 것만 고친다.
    /// </summary>
    public static class RepairRule
    {
        /// <summary>이만큼(최대 내구도에 대한 비율) 아래로 닳으면 고친다.</summary>
        public const float Below = 0.5f;

        public static bool Needs(bool useDurability, float durability, float max) =>
            useDurability && max > 0f && durability < max * Below;
    }
}
