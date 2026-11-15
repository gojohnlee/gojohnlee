using System.Collections.Generic;
using System.IO;
using ValheimHelper.Core.Work;
using Xunit;
using P = ValheimHelper.Core.Work.PathMemory.P;

public class MemoryFileTests
{
    [Fact]
    public void RoundTrip_WithPaths()
    {
        var pm = new PathMemory();
        pm.Remember(new P(0, 0, 0), new P(5, 0, 0), new List<P> { new P(0, 0, 0), new P(2.5f, 0, 1), new P(5, 0, 0) }, 7);
        var ms = new MemoryStream();
        new MemoryFile { Knowledge = "지식", Places = "장소", Foes = "적", Paths = pm.Save() }.Write(ms);
        ms.Position = 0;
        var m = MemoryFile.Read(ms);
        Assert.Equal("지식", m.Knowledge); Assert.Equal("장소", m.Places); Assert.Equal("적", m.Foes);
        var back = new PathMemory();
        Assert.Equal(1, back.Load(m.Paths));
        Assert.NotNull(back.Find(new P(0, 0, 0), new P(5, 0, 0), 8));
    }

    [Fact]
    public void Version1_ReadsWithEmptyPaths()
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) { w.Write(1); w.Write("k"); w.Write("p"); w.Write("f"); }
        ms.Position = 0;
        var m = MemoryFile.Read(ms);
        Assert.Equal("k", m.Knowledge); Assert.Equal("f", m.Foes); Assert.Equal("", m.Paths);
    }

    [Fact]
    public void UnknownVersion_Throws()
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) { w.Write(3); w.Write("k"); }
        ms.Position = 0;
        Assert.Throws<IOException>(() => MemoryFile.Read(ms));
    }
}
