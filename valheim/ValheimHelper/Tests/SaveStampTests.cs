using ValheimHelper.Core.Work;
using Xunit;

namespace ValheimHelper.Tests
{
    public class SaveStampTests
    {
        [Fact]
        public void NewerNumberWinsEvenIfShorter()
        {
            Assert.True(SaveStamp.Better(5, "a", 4, "aaaa"));
            Assert.False(SaveStamp.Better(4, "aaaa", 5, "a"));
        }

        [Fact]
        public void OldSavesWithoutNumberUseLength()
        {
            Assert.True(SaveStamp.Better(0, "aaa", 0, "a"));
            Assert.False(SaveStamp.Better(0, "a", 0, "aaa"));
            Assert.True(SaveStamp.Better(1, "", 0, "aaa"));
            Assert.True(SaveStamp.Better(0, "a", 0, null));
        }
    }
}
