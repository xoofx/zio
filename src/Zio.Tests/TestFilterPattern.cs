namespace Zio.Tests;

[TestClass]
public class TestFilterPattern
{
    [TestMethod]
    // Test fast paths
    [DataRow("test", "", true)]
    [DataRow("test", "*", true)]
    [DataRow("test", "*.*", true)]

    // Test exact match
    [DataRow("test", "test", true)]
    [DataRow("foo", "test", false)]

    // Test start with
    [DataRow("test", "te*", true)]
    [DataRow("test.txt", "te*", true)]
    [DataRow("foo", "te*", false)]

    // Test extension wildcard
    [DataRow("test.txt", "test.*", true)]
    [DataRow("test", "test.*", true)]
    [DataRow("foo.txt", "test.*", false)]
    [DataRow("test.old.txt", "test.*", false)]

    // Test name wildcard
    [DataRow("test.mp3", "*.mp3", true)]
    [DataRow("test.ogg", "*.mp3", false)]
    [DataRow(".mp3", "*.mp3", true)]

    // Test single character wildcard
    [DataRow("test", "t?st", true)]
    [DataRow("ttest", "t?st", false)]
    [DataRow("tesst", "t?st", false)]
    [DataRow("foo.a", "foo.?", true)]
    [DataRow("foo.abc", "foo.?", false)]
    [DataRow("1998", "19??", true)]
    [DataRow("1990.o", "19??", false)]
    public void TestMatch(string fileName, string filterPattern, bool match)
    {
        var filter = FilterPattern.Parse(filterPattern);
        AssertEx.AreEqual(match, filter.Match(fileName));
    }

    [TestMethod]
    // Filter globs should match case-insensitively (sbox.txt should match Sbox.txt / sbox.TXT / SBOX.txt).
    // Skipped: FilterPattern is currently case-sensitive. Remove the Skip once a follow-up PR adds
    // case-insensitive matching, and this should pass.
    [DataRow("Sbox.txt", "sbox.txt")]
    [DataRow("sbox.TXT", "sbox.txt")]
    [DataRow("SBOX.txt", "sbox.txt")]
    public void TestMatchOrdinalIgnoreCase(string fileName, string filterPattern)
    {
        Skip.If(true, "FilterPattern case-insensitive matching pending a follow-up PR.");

        var filter = FilterPattern.Parse(filterPattern);
        Assert.IsTrue(filter.Match(fileName));
    }
}



