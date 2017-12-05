using Xunit;

namespace Zio.Tests
{
    public class TestFilterPattern
    {

        [Theory]
        // Test fast paths
        [InlineData("test", "", true)]
        [InlineData("test", "*", true)]
        [InlineData("test", "*.*", true)]

        // Test exact match
        [InlineData("test", "test", true)]
        [InlineData("foo", "test", false)]

        // Test start with
        [InlineData("test", "te*", true)]
        [InlineData("test.txt", "te*", true)]
        [InlineData("foo", "te*", false)]

        // Test extension wildcard
        [InlineData("test.txt", "test.*", true)]
        [InlineData("test", "test.*", true)]
        [InlineData("foo.txt", "test.*", false)]
        [InlineData("test.old.txt", "test.*", false)]

        // Test name wildcard
        [InlineData("test.mp3", "*.mp3", true)]
        [InlineData("test.ogg", "*.mp3", false)]
        [InlineData(".mp3", "*.mp3", true)]

        // Test single character wildcard
        [InlineData("test", "t?st", true)]
        [InlineData("ttest", "t?st", false)]
        [InlineData("tesst", "t?st", false)]
        [InlineData("foo.a", "foo.?", true)]
        [InlineData("foo.abc", "foo.?", false)]
        [InlineData("1998", "19??", true)]
        [InlineData("1990.o", "19??", false)]
        public void TestMatch(string fileName, string filterPattern, bool match)
        {
            var filter = FilterPattern.Parse(filterPattern);
            Assert.Equal(match, filter.Match(fileName));
        }

    }
}
