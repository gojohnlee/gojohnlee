using ValheimHelper.Core.Npc;
using Xunit;

public class SpeechStatsTests
{
    [Fact]
    public void CountsSaveAndLoadAddUp()
    {
        var s = new SpeechStats();
        for (int i = 0; i < 3; i++) s.Add("gatherStart");
        s.Add("home");
        var again = new SpeechStats();
        again.Load(s.ToText());
        again.Load(s.ToText());
        Assert.Equal(6, again["gatherStart"]);
        Assert.Equal(2, again["home"]);
        Assert.Equal(8, again.Total);
    }

    [Fact]
    public void BrokenLinesAreSkipped()
    {
        var s = new SpeechStats();
        s.Load("# 머리\nabc\tx\n-3\ty\n\n5\tz\r\n");
        Assert.Equal(5, s.Total);
        Assert.Equal(5, s["z"]);
    }

    [Fact]
    public void NeedMoreListsOftenFewLinesButNotCombat()
    {
        var s = new SpeechStats();
        for (int i = 0; i < SpeechStats.OftenCount; i++) { s.Add("gatherStart"); s.Add("fightStart"); s.Add("nope.key"); }
        s.Add("home");
        bool few = Speech.Options(Personality.Ragnar, "gatherStart").Length < SpeechStats.FewLines
            || Speech.Options(Personality.Floki, "gatherStart").Length < SpeechStats.FewLines;
        Assert.Equal(few, s.NeedMore().Contains("gatherStart"));
        Assert.DoesNotContain("fightStart", s.NeedMore());
        Assert.DoesNotContain("nope.key", s.NeedMore());
        Assert.DoesNotContain("home", s.NeedMore());
        Assert.Contains("# 늘릴 것:", s.ToText());
    }
}
