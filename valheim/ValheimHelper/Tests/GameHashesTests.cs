using System.Collections.Generic;
using System.Linq;
using ValheimHelper.Core.Npc;
using Xunit;

public class GameHashesTests
{
    [Fact]
    public void Hash_SameBytesSame_DifferentBytesDiffer()
    {
        string a = GameHashes.Hash(new byte[] { 1, 2, 3 });
        Assert.Equal(16, a.Length);
        Assert.Equal(a, GameHashes.Hash(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(a, GameHashes.Hash(new byte[] { 1, 2, 4 }));
        Assert.Equal("", GameHashes.Hash(null));
        Assert.Equal("", GameHashes.Hash(new byte[0]));
    }

    [Fact]
    public void FormatThenParse_RoundTrips_SkipsCommentsAndBadLines()
    {
        var now = new Dictionary<string, string> { ["ZNetScene.CreateDestroyObjects"] = "aa", ["ZDOMan.ReleaseNearbyZDOS"] = "bb" };
        var lines = GameHashes.Format(now, "게임 0.221.4");
        Assert.Equal("# 게임 0.221.4", lines[0]);
        lines.Add("나쁜 줄");
        lines.Add("=빈 이름");
        var back = GameHashes.Parse(lines);
        Assert.Equal(2, back.Count);
        Assert.Equal("aa", back["ZNetScene.CreateDestroyObjects"]);
    }

    [Fact]
    public void Compare_SameChangedNewMissing()
    {
        var known = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2", ["Gone"] = "9" };
        var now = new Dictionary<string, string> { ["A"] = "1", ["B"] = "3", ["C"] = "4" };
        var r = GameHashes.Compare(known, now).ToDictionary(x => x.Name, x => x.State);
        Assert.Equal(GameHashes.State.Same, r["A"]);
        Assert.Equal(GameHashes.State.Changed, r["B"]);
        Assert.Equal(GameHashes.State.New, r["C"]);
        Assert.Equal(GameHashes.State.Missing, r["Gone"]);
    }

    [Fact]
    public void Compare_MethodNotFound_IsChanged()
    {
        var known = new Dictionary<string, string> { ["A"] = "1" };
        var now = new Dictionary<string, string> { ["A"] = "", ["B"] = "" };
        var r = GameHashes.Compare(known, now).ToDictionary(x => x.Name, x => x.State);
        Assert.Equal(GameHashes.State.Changed, r["A"]);
        Assert.Equal(GameHashes.State.Changed, r["B"]);
    }
}
