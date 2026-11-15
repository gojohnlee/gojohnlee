using System.IO;

namespace ValheimHelper.Core.Work
{
    /// <summary>일꾼 기억 파일(worlds_local/&lt;월드&gt;.vh-memory)의 꼴: 판 번호 + 글자 칸들.
    /// 판 1 = 지식·장소·적, 판 2 = 그 뒤에 집 안 길 기억(PathMemory.Save, 요청 11-15 ②). 옛 판은 빈 칸으로 읽고, 모르는 판은 IOException.</summary>
    public sealed class MemoryFile
    {
        public const int Version = 2;
        public string Knowledge = "", Places = "", Foes = "", Paths = "";

        public void Write(Stream s)
        {
            using (var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true))
            {
                w.Write(Version);
                w.Write(Knowledge ?? ""); w.Write(Places ?? ""); w.Write(Foes ?? ""); w.Write(Paths ?? "");
            }
        }

        public static MemoryFile Read(Stream s)
        {
            using (var r = new BinaryReader(s, System.Text.Encoding.UTF8, true))
            {
                int v = r.ReadInt32();
                if (v < 1 || v > Version) throw new IOException($"판 {v} 모름");
                var m = new MemoryFile { Knowledge = r.ReadString(), Places = r.ReadString(), Foes = r.ReadString() };
                if (v >= 2) m.Paths = r.ReadString();
                return m;
            }
        }
    }
}
