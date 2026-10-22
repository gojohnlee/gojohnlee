using System.Collections.Generic;
using ValheimHelper.Core.Work;
using Xunit;

public class CryptsTests
{
    static CryptSpot C(float x, float z, bool open = true, bool avoid = false) => new CryptSpot { X = x, Z = z, Open = open, Avoid = avoid };

    [Fact]
    public void NoneGivesMinusOne()
    {
        Assert.Equal(-1, Crypts.Choose(0, 0, new List<CryptSpot>()));
    }

    [Fact]
    public void ClosedCryptIsNotChosen()
    {
        Assert.Equal(-1, Crypts.Choose(0, 0, new List<CryptSpot> { C(100, 0, open: false) }));
    }

    [Fact]
    public void NearestOpenIsChosen()
    {
        var list = new List<CryptSpot> { C(500, 0), C(50, 0, open: false), C(200, 0), C(-300, 0) };
        Assert.Equal(2, Crypts.Choose(0, 0, list));
    }

    [Fact]
    public void AvoidedCryptIsSkipped()
    {
        var list = new List<CryptSpot> { C(100, 0, avoid: true), C(400, 0) };
        Assert.Equal(1, Crypts.Choose(0, 0, list));
    }

    [Fact]
    public void TooFarIsSkipped()
    {
        Assert.Equal(-1, Crypts.Choose(0, 0, new List<CryptSpot> { C(Crypts.MaxDistance + 10, 0) }));
    }
}
