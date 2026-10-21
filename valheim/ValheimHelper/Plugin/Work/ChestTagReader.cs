using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    /// <summary>
    /// ChestTag(Nexus 4388) 상자 이름 읽기(I-2). 이름은 상자 ZDO의 바이트 배열 "ChestTag.v1"에 저장됨:
    /// [버전 1바이트(1~4)][이름 문자열]... 이름만 읽는다. 모드가 없거나 형식이 달라도 이름 없음으로 처리(하드 의존 없음).
    /// </summary>
    public static class ChestTagReader
    {
        const string Key = "ChestTag.v1";

        public static ChestTags Read(ZDO zdo) => ChestNames.Parse(Name(zdo));

        public static string Name(ZDO zdo)
        {
            try
            {
                byte[] data = zdo.GetByteArray(Key, null);
                if (data == null || data.Length == 0) return null;
                var pkg = new ZPackage(data);
                byte ver = pkg.ReadByte();
                return ver >= 1 && ver <= 4 ? pkg.ReadString() : null;
            }
            catch { return null; }
        }
    }
}
