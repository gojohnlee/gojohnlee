using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// CustomChestName(GemHunter1, 사용자 결정 2026-10-22: ChestTag 대신) 상자 이름 읽기(I-2). 이름은 상자 ZDO 문자열 "ChestName"에 저장됨.
    /// 모드가 없으면 키가 없어 이름 없음으로 처리(하드 의존 없음).
    /// </summary>
    public static class ChestTagReader
    {
        const string Key = "ChestName";

        public static ChestTags Read(ZDO zdo) => ChestNames.Parse(Name(zdo));

        public static string Name(ZDO zdo)
        {
            try { return zdo.GetString(Key, ""); }
            catch { return null; }
        }
    }
}
