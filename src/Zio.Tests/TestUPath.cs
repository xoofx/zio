using System.IO;

namespace Zio.Tests;

[TestClass]
public class TestUPath
{
    [TestMethod]
    // Test empty
    [DataRow("", "")]

    // Tests with regular paths
    [DataRow("/", "/")]
    [DataRow("\\", "/")]
    [DataRow("a", "a")]
    [DataRow("a/b", "a/b")]
    [DataRow("a\\b", "a/b")]
    [DataRow("a/b/", "a/b")]
    [DataRow("a\\b\\", "a/b")]
    [DataRow("a///b/c//d", "a/b/c/d")]
    [DataRow("///a///b/c//", "/a/b/c")]
    [DataRow("a/b/c", "a/b/c")]
    [DataRow("/a/b", "/a/b")]

    // Tests with "."
    [DataRow(".", ".")]
    [DataRow("/./", "/")]
    [DataRow("/./a", "/a")]
    [DataRow("/a/./b", "/a/b")]
    [DataRow("./a/b", "a/b")]

    // Tests with ".."
    [DataRow("..", "..")]
    [DataRow("../../a/..", "../..")]
    [DataRow("a/../c", "c")]
    [DataRow("a/b/..", "a")]
    [DataRow("a/b/c/../..", "a")]
    [DataRow("a/b/c/../../..", "")]
    [DataRow("./..", "..")]
    [DataRow("../.", "..")]
    [DataRow("../..", "../..")]
    [DataRow("../../", "../..")]
    [DataRow(".a", ".a")]
    [DataRow(".a/b/..", ".a")]
    [DataRow("...a/b../", "...a/b..")]
    [DataRow("...a/..", "")]
    [DataRow("...a/b/..", "...a")]
    [DataRow("..a/b", "..a/b")]
    [DataRow("c/..d", "c/..d")]
    [DataRow("c/d..", "c/d..")]
    public void TestNormalize(string pathAsText, string expectedResult)
    {
        var path = new UPath(pathAsText);
        AssertEx.AreEqual(expectedResult, path.FullName);

        // Check Equatable
        var expectedPath = new UPath(expectedResult);
        AssertEx.AreEqual(expectedPath, path);
        Assert.IsTrue(expectedPath.Equals((object)path));
        AssertEx.AreEqual(expectedPath.GetHashCode(), path.GetHashCode());
        Assert.IsTrue(path == expectedPath);
        Assert.IsFalse(path != expectedPath);

        // Check TryParse
        UPath result;
        Assert.IsTrue(UPath.TryParse(path.FullName, out result));
    }

    [TestMethod]
    public void TestEquals()
    {
        var pathInfo = new UPath("x");
        Assert.IsFalse(pathInfo.Equals((object)"no"));
        Assert.IsFalse(pathInfo.Equals(null));
    }

    [TestMethod]
    public void TestIsNullAndEmpty()
    {
        Assert.IsTrue(default(UPath).IsNull);
        Assert.IsFalse(default(UPath).IsEmpty);
        Assert.IsTrue(new UPath("").IsEmpty);
        Assert.IsFalse(new UPath("").IsNull);
        Assert.IsFalse(new UPath("/").IsEmpty);
    }

    [TestMethod]
    public void TestAbsoluteAndRelative()
    {
        var path = new UPath("x");
        Assert.IsTrue(path.IsRelative);
        Assert.IsFalse(path.IsAbsolute);

        path = new UPath("..");
        Assert.IsTrue(path.IsRelative);
        Assert.IsFalse(path.IsAbsolute);

        path = new UPath("/x");
        Assert.IsFalse(path.IsRelative);
        Assert.IsTrue(path.IsAbsolute);

        AssertEx.AreEqual(path, path.ToAbsolute());
    }

    [TestMethod]
    [DataRow("", "", "")]
    [DataRow("/", "", "/")]
    [DataRow("\\", "", "/")]
    [DataRow("//", "", "/")]
    [DataRow("\\\\", "", "/")]
    [DataRow("/", "/", "/")]
    [DataRow("\\", "\\", "/")]
    [DataRow("//", "//", "/")]
    [DataRow("", "/", "/")]
    [DataRow("a", "b", "a/b")]
    [DataRow("a/b", "c", "a/b/c")]
    [DataRow("", "b", "b")]
    [DataRow("a", "", "a")]
    [DataRow("a/b", "", "a/b")]
    [DataRow("/a", "b/", "/a/b")]
    [DataRow("/a", "/b", "/b")]
    [DataRow("/a", "", "/a")]
    [DataRow("//a", "", "/a")]
    [DataRow("a/", "", "a")]
    [DataRow("a//", "", "a")]
    [DataRow("a/", "b", "a/b")]
    [DataRow("a/", "b/", "a/b")]
    [DataRow("a//", "b//", "a/b")]
    [DataRow("a", "../b", "b")]
    [DataRow("a/../", "b", "b")]
    [DataRow("/a/..", "b", "/b")]
    [DataRow("/a/..", "", "/")]
    [DataRow("//a//..//", "", "/")]
    [DataRow("\\a", "", "/a")]
    [DataRow("\\\\a", "", "/a")]
    public void TestCombine(string path1, string path2, string expectedResult)
    {
        var path = UPath.Combine(path1, path2);
        AssertEx.AreEqual(expectedResult, (string)path);

        path = new UPath(path1) / new UPath(path2);
        AssertEx.AreEqual(expectedResult, (string)path);

        // Compare path info directly
        var expectedPath = new UPath(expectedResult);
        AssertEx.AreEqual(expectedPath, path);
        AssertEx.AreEqual(expectedPath.GetHashCode(), path.GetHashCode());
    }

    [TestMethod]
    [DataRow("", "", "", "")]
    [DataRow("a", "b", "c", "a/b/c")]
    [DataRow("a/b", "c", "d", "a/b/c/d")]
    [DataRow("", "b", "", "b")]
    [DataRow("a", "", "", "a")]
    [DataRow("a/b", "", "", "a/b")]
    [DataRow("/a", "b/", "c/", "/a/b/c")]
    [DataRow("/a", "/b", "/c", "/c")]
    public void TestCombine3(string path1, string path2, string path3, string expectedResult)
    {
        var path = UPath.Combine(path1, path2, path3);
        AssertEx.AreEqual(expectedResult, (string)path);

        // Compare path info directly
        var expectedPath = new UPath(expectedResult);
        AssertEx.AreEqual(expectedPath, path);
        AssertEx.AreEqual(expectedPath.GetHashCode(), path.GetHashCode());
    }

    [TestMethod]
    [DataRow("", "", "", "", "")]
    [DataRow("a", "b", "c", "d", "a/b/c/d")]
    [DataRow("a/b", "c", "d/e", "f", "a/b/c/d/e/f")]
    [DataRow("", "b", "", "", "b")]
    [DataRow("a", "", "", "", "a")]
    [DataRow("a/b", "", "", "", "a/b")]
    [DataRow("/a", "b/", "c/", "", "/a/b/c")]
    [DataRow("/a", "/b", "/c", "/d", "/d")]
    [DataRow("a", "b", "..", "c", "a/c")]
    public void TestCombine4(string path1, string path2, string path3, string path4, string expectedResult)
    {
        var path = UPath.Combine(path1, path2, path3, path4);
        AssertEx.AreEqual(expectedResult, (string)path);

        // Compare path info directly
        var expectedPath = new UPath(expectedResult);
        AssertEx.AreEqual(expectedPath, path);
        AssertEx.AreEqual(expectedPath.GetHashCode(), path.GetHashCode());
    }

    [TestMethod]
    [DataRow(new[] { "", "" }, "")]
    [DataRow(new[] { "a", "b", "c", "d", "e" }, "a/b/c/d/e")]
    [DataRow(new[] { "a", "..", "c", "..", "e" }, "e")]
    [DataRow(new[] { "a", "b", "c", "/d", "e" }, "/d/e")]
    [DataRow(new[] { "a", "", "", "", "e" }, "a/e")]
    public void TestCombineN(string[] parts, string expectedResult)
    {
        var path = UPath.Combine(parts.Select(a => (UPath)a).ToArray());
        AssertEx.AreEqual(expectedResult, (string)path);

        // Compare path info directly
        var expectedPath = new UPath(expectedResult);
        AssertEx.AreEqual(expectedPath, path);
        AssertEx.AreEqual(expectedPath.GetHashCode(), path.GetHashCode());
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("/", "")]
    [DataRow("/a", "a")]
    [DataRow("/a/b", "b")]
    [DataRow("/a/b/c.txt", "c.txt")]
    [DataRow("a", "a")]
    [DataRow("../a", "a")]
    [DataRow("../../a/b", "b")]
    public void TestGetName(string path1, string expectedName)
    {
        var path = (UPath) path1;
        var result = path.GetName();
        AssertEx.AreEqual(expectedName, result);
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("/", "")]
    [DataRow("/a", "a")]
    [DataRow("/a/b", "b")]
    [DataRow("/a/b/c.txt", "c")]
    [DataRow("a", "a")]
    [DataRow("../a", "a")]
    [DataRow("../../a/b", "b")]
    public void TestGetNameWithoutExtension(string path1, string expectedName)
    {
        var path = (UPath)path1;
        var result = path.GetNameWithoutExtension();
        AssertEx.AreEqual(expectedName, result);
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("/", "")]
    [DataRow("/a.txt", ".txt")]
    [DataRow("/a.", "")]
    [DataRow("/a.txt.bak", ".bak")]
    [DataRow("a.txt", ".txt")]
    [DataRow("a.", "")]
    [DataRow("a.txt.bak", ".bak")]
    public void TestGetExtension(string path1, string expectedName)
    {
        var path = (UPath)path1;
        var result = path.GetExtensionWithDot();
        AssertEx.AreEqual(expectedName, result);
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("/", null)]
    [DataRow("/a", "/")]
    [DataRow("/a/b", "/a")]
    [DataRow("/a/b/c.txt", "/a/b")]
    [DataRow("a", "")]
    [DataRow("../a", "..")]
    [DataRow("../../a/b", "../../a")]
    public void TestGetDirectory(string path1, string expectedDir)
    {
        var path = (UPath)path1;
        var result = path.GetDirectory();
        AssertEx.AreEqual(expectedDir, result);
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("/", "")]
    [DataRow("/a", "/")]
    [DataRow("/a/b", "/a")]
    [DataRow("/a/b/c.txt", "/a/b")]
    [DataRow("a", "")]
    [DataRow("../a", "..")]
    [DataRow("../../a/b", "../../a")]
    public void TestGetDirectoryAsSpan(string path1, string expectedDir)
    {
        var path = (UPath)path1;
        var result = path.GetDirectoryAsSpan().ToString();
        AssertEx.AreEqual(expectedDir, result);
    }

    [TestMethod]
    [DataRow("", ".txt", "")]
    [DataRow("/", ".txt", "/.txt")]
    [DataRow("/a", ".txt", "/a.txt")]
    [DataRow("/a/b", ".txt", "/a/b.txt")]
    [DataRow("/a/b/c.bin", ".txt", "/a/b/c.txt")]
    [DataRow("a", ".txt", "a.txt")]
    [DataRow("../a", ".txt", "../a.txt")]
    [DataRow("../../a/b", ".txt", "../../a/b.txt")]
    public void TestChangeExtension(string path1, string newExt, string expectedPath)
    {
        var path = (UPath)path1;
        var result = path.ChangeExtension(newExt);
        AssertEx.AreEqual(expectedPath, result);
    }

    [TestMethod]
    // Test automatic separator insertion
    [DataRow("/a/b/c", "/a/b", false, true)]
    [DataRow("/a/bc", "/a/b", false, false)]

    // Test trailing separator
    [DataRow("/a/b/", "/a", false, true)]
    [DataRow("/a/b", "/a/", false, true)]
    [DataRow("/a/b/", "/a/", false, true)]

    // Test recursive option
    [DataRow("/a/b/c", "/a", true, true)]
    [DataRow("/a/b/c", "/a", false, false)]
    
    // Test relative paths
    [DataRow("a/b", "a", false, true)]
    
    // Test exact match
    [DataRow("/a/b/", "/a/b/", false, true)]
    [DataRow("/a/b/", "/a/b/", true, true)]
    [DataRow("/a/b", "/a/b", false, true)]
    [DataRow("/a/b", "/a/b", true, true)]
    public void TestIsInDirectory(string path1, string directory, bool recursive, bool expected)
    {
        var path = (UPath)path1;
        var result = path.IsInDirectory(directory, recursive);
        AssertEx.AreEqual(expected, result);
    }

    [TestMethod]
    public void TestSplit()
    {
        AssertEx.AreEqual(new List<string>(), ((UPath)"").Split());
        AssertEx.AreEqual(new List<string>(), ((UPath)"/").Split());
        AssertEx.AreEqual(new List<string>() { "a" }, ((UPath)"/a").Split());
        AssertEx.AreEqual(new List<string>() {"a", "b", "c"}, ((UPath) "/a/b/c").Split());
        AssertEx.AreEqual(new List<string>() { "a" }, ((UPath)"a").Split());
        AssertEx.AreEqual(new List<string>() { "a", "b" }, ((UPath)"a/b").Split());
        AssertEx.AreEqual(new List<string>() { "a", "b", "c" }, ((UPath)"a/b/c").Split());
    }

    [TestMethod]
    public void TestSplitSpan()
    {
        AssertEx.AreEqual(new List<string>(), ToList((UPath)""));
        AssertEx.AreEqual(new List<string>(), ToList((UPath)"/"));
        AssertEx.AreEqual(new List<string>() { "a" }, ToList((UPath)"/a"));
        AssertEx.AreEqual(new List<string>() {"a", "b", "c"}, ToList((UPath) "/a/b/c"));
        AssertEx.AreEqual(new List<string>() { "a" }, ToList((UPath)"a"));
        AssertEx.AreEqual(new List<string>() { "a", "b" }, ToList((UPath)"a/b"));
        AssertEx.AreEqual(new List<string>() { "a", "b", "c" }, ToList((UPath)"a/b/c"));
        return;

        List<string> ToList(UPath path)
        {
            var enumerator = path.SpanSplit();
            var list = new List<string>(enumerator.Count);

            foreach (var span in enumerator)
            {
                list.Add(span.ToString());
            }

            AssertEx.AreEqual(enumerator.Count, list.Count);

            return list;
        }
    }


    [TestMethod]
    public void TestExpectedException()
    {
        Assert.Throws<ArgumentException>(() => new UPath("/../a"));
        Assert.Throws<ArgumentException>(() => new UPath("/../"));
        Assert.Throws<ArgumentException>(() => new UPath("/..\\"));
        Assert.Throws<ArgumentException>(() => new UPath("..."));
        Assert.Throws<ArgumentException>(() => new UPath("/.../"));
        Assert.Throws<ArgumentException>(() => new UPath("/...\\"));
        Assert.Throws<ArgumentException>(() => new UPath("a/..."));
        Assert.Throws<ArgumentException>(() => new UPath(".../a"));
        Assert.Throws<ArgumentException>(() => UPath.Combine("/", ".."));
        AssertEx.AreEqual("path1", Assert.Throws<ArgumentNullException>(() => UPath.Combine(null, "")).ParamName);
        AssertEx.AreEqual("path2", Assert.Throws<ArgumentNullException>(() => UPath.Combine("", null)).ParamName);
        Assert.Throws<ArgumentException>(() => UPathExtensions.IsInDirectory("/a", "b", true));
        Assert.Throws<ArgumentException>(() => UPathExtensions.IsInDirectory("a", "/b", true));
    }


    [TestMethod]
    public void TestComparers()
    {
        {
            var list = new SortedSet<UPath>(UPath.DefaultComparer)
            {
                "/C.txt",
                "/b.txt",
                "/do.txt",
                "/A.txt",
                "/a.txt"
            };
            AssertEx.AreEqual(new List<UPath>()
            {
                "/A.txt",
                "/C.txt",
                "/a.txt",
                "/b.txt",
                "/do.txt"
            }, list.ToList());
        }

        {
            var list = new List<UPath>()
            {
                "/C.txt",
                "/b.txt",
                "/do.txt",
                "/A.txt",
                "/a.txt"
            };
            list.Sort(UPath.DefaultComparerIgnoreCase);
            AssertEx.AreEqual(new List<UPath>()
            {
                "/A.txt",
                "/a.txt",
                "/b.txt",
                "/C.txt",
                "/do.txt"
            }, list.ToList());
        }
    }
}





