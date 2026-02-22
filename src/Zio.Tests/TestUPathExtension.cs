namespace Zio.Tests;

[TestClass]
public class TestUPathExtension
{
    [TestMethod]
    [DataRow("/a/b", "a", "b")]
    [DataRow("/a/b/c", "a", "b/c")]
    [DataRow("a/b", "a", "b")]
    [DataRow("a/b/c", "a", "b/c")]
    [DataRow("", "", "")]
    [DataRow("/z","z","")]
    public void TestGetFirstDirectory(string path, string expectedFirstDir, string expectedRest)
    {
        var pathInfo = new UPath(path);
        var firstDir = pathInfo.GetFirstDirectory(out var rest);
        AssertEx.AreEqual(expectedFirstDir,firstDir);
        AssertEx.AreEqual(expectedRest,rest);
    }
}


