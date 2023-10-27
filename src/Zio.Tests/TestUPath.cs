namespace Zio.Tests;

public class TestUPath
{
    [Theory]
    // Test empty
    [InlineData("", "")]

    // Tests with regular paths
    [InlineData("/", "/")]
    [InlineData("\\", "/")]
    [InlineData("a", "a")]
    [InlineData("a/b", "a/b")]
    [InlineData("a\\b", "a/b")]
    [InlineData("a/b/", "a/b")]
    [InlineData("a\\b\\", "a/b")]
    [InlineData("a///b/c//d", "a/b/c/d")]
    [InlineData("///a///b/c//", "/a/b/c")]
    [InlineData("a/b/c", "a/b/c")]
    [InlineData("/a/b", "/a/b")]

    // Tests with "."
    [InlineData(".", ".")]
    [InlineData("/./a", "/a")]
    [InlineData("/a/./b", "/a/b")]
    [InlineData("./a/b", "a/b")]

    // Tests with ".."
    [InlineData("..", "..")]
    [InlineData("../../a/..", "../..")]
    [InlineData("a/../c", "c")]
    [InlineData("a/b/..", "a")]
    [InlineData("a/b/c/../..", "a")]
    [InlineData("a/b/c/../../..", "")]
    [InlineData("./..", "..")]
    [InlineData("../.", "..")]
    [InlineData("../..", "../..")]
    [InlineData("../../", "../..")]
    [InlineData(".a", ".a")]
    [InlineData(".a/b/..", ".a")]
    [InlineData("...a/b../", "...a/b..")]
    [InlineData("...a/..", "")]
    [InlineData("...a/b/..", "...a")]
    [InlineData("..a/b", "..a/b")]
    [InlineData("c/..d", "c/..d")]
    [InlineData("c/d..", "c/d..")]
    public void TestNormalize(string pathAsText, string expectedResult)
    {
        var path = new UPath(pathAsText);
        Assert.Equal(expectedResult, path.FullName);

        // Check Equatable
        var expectedPath = new UPath(expectedResult);
        Assert.Equal(expectedPath, path);
        Assert.True(expectedPath.Equals((object)path));
        Assert.Equal(expectedPath.GetHashCode(), path.GetHashCode());
        Assert.True(path == expectedPath);
        Assert.False(path != expectedPath);

        // Check TryParse
        UPath result;
        Assert.True(UPath.TryParse(path.FullName, out result));
    }

    [Fact]
    public void TestEquals()
    {
        var pathInfo = new UPath("x");
        Assert.False(pathInfo.Equals((object)"no"));
        Assert.False(pathInfo.Equals(null));
    }

    [Fact]
    public void TestIsNullAndEmpty()
    {
        Assert.True(default(UPath).IsNull);
        Assert.False(default(UPath).IsEmpty);
        Assert.True(new UPath("").IsEmpty);
        Assert.False(new UPath("").IsNull);
        Assert.False(new UPath("/").IsEmpty);
    }

    [Fact]
    public void TestAbsoluteAndRelative()
    {
        var path = new UPath("x");
        Assert.True(path.IsRelative);
        Assert.False(path.IsAbsolute);

        path = new UPath("..");
        Assert.True(path.IsRelative);
        Assert.False(path.IsAbsolute);

        path = new UPath("/x");
        Assert.False(path.IsRelative);
        Assert.True(path.IsAbsolute);

        Assert.Equal(path, path.ToAbsolute());
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("/", "", "/")]
    [InlineData("\\", "", "/")]
    [InlineData("//", "", "/")]
    [InlineData("\\\\", "", "/")]
    [InlineData("/", "/", "/")]
    [InlineData("\\", "\\", "/")]
    [InlineData("//", "//", "/")]
    [InlineData("", "/", "/")]
    [InlineData("a", "b", "a/b")]
    [InlineData("a/b", "c", "a/b/c")]
    [InlineData("", "b", "b")]
    [InlineData("a", "", "a")]
    [InlineData("a/b", "", "a/b")]
    [InlineData("/a", "b/", "/a/b")]
    [InlineData("/a", "/b", "/b")]
    [InlineData("/a", "", "/a")]
    [InlineData("//a", "", "/a")]
    [InlineData("a/", "", "a")]
    [InlineData("a//", "", "a")]
    [InlineData("a/", "b", "a/b")]
    [InlineData("a/", "b/", "a/b")]
    [InlineData("a//", "b//", "a/b")]
    [InlineData("a", "../b", "b")]
    [InlineData("a/../", "b", "b")]
    [InlineData("/a/..", "b", "/b")]
    [InlineData("/a/..", "", "/")]
    [InlineData("//a//..//", "", "/")]
    [InlineData("\\a", "", "/a")]
    [InlineData("\\\\a", "", "/a")]
    public void TestCombine(string path1, string path2, string expectedResult)
    {
        var path = UPath.Combine(path1, path2);
        Assert.Equal(expectedResult, (string)path);

        path = new UPath(path1) / new UPath(path2);
        Assert.Equal(expectedResult, (string)path);

        // Compare path info directly
        var expectedPath = new UPath(expectedResult);
        Assert.Equal(expectedPath, path);
        Assert.Equal(expectedPath.GetHashCode(), path.GetHashCode());
    }

    [Theory]
    [InlineData("", "", "", "")]
    [InlineData("a", "b", "c", "a/b/c")]
    [InlineData("a/b", "c", "d", "a/b/c/d")]
    [InlineData("", "b", "", "b")]
    [InlineData("a", "", "", "a")]
    [InlineData("a/b", "", "", "a/b")]
    [InlineData("/a", "b/", "c/", "/a/b/c")]
    [InlineData("/a", "/b", "/c", "/c")]
    public void TestCombine3(string path1, string path2, string path3, string expectedResult)
    {
        var path = UPath.Combine(path1, path2, path3);
        Assert.Equal(expectedResult, (string)path);

        // Compare path info directly
        var expectedPath = new UPath(expectedResult);
        Assert.Equal(expectedPath, path);
        Assert.Equal(expectedPath.GetHashCode(), path.GetHashCode());
    }

    [Theory]
    [InlineData("", "", "", "", "")]
    [InlineData("a", "b", "c", "d", "a/b/c/d")]
    [InlineData("a/b", "c", "d/e", "f", "a/b/c/d/e/f")]
    [InlineData("", "b", "", "", "b")]
    [InlineData("a", "", "", "", "a")]
    [InlineData("a/b", "", "", "", "a/b")]
    [InlineData("/a", "b/", "c/", "", "/a/b/c")]
    [InlineData("/a", "/b", "/c", "/d", "/d")]
    [InlineData("a", "b", "..", "c", "a/c")]
    public void TestCombine4(string path1, string path2, string path3, string path4, string expectedResult)
    {
        var path = UPath.Combine(path1, path2, path3, path4);
        Assert.Equal(expectedResult, (string)path);

        // Compare path info directly
        var expectedPath = new UPath(expectedResult);
        Assert.Equal(expectedPath, path);
        Assert.Equal(expectedPath.GetHashCode(), path.GetHashCode());
    }

    [Theory]
    [InlineData(new[] { "", "" }, "")]
    [InlineData(new[] { "a", "b", "c", "d", "e" }, "a/b/c/d/e")]
    [InlineData(new[] { "a", "..", "c", "..", "e" }, "e")]
    [InlineData(new[] { "a", "b", "c", "/d", "e" }, "/d/e")]
    [InlineData(new[] { "a", "", "", "", "e" }, "a/e")]
    public void TestCombineN(string[] parts, string expectedResult)
    {
        var path = UPath.Combine(parts.Select(a => (UPath)a).ToArray());
        Assert.Equal(expectedResult, (string)path);

        // Compare path info directly
        var expectedPath = new UPath(expectedResult);
        Assert.Equal(expectedPath, path);
        Assert.Equal(expectedPath.GetHashCode(), path.GetHashCode());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("/a", "a")]
    [InlineData("/a/b", "b")]
    [InlineData("/a/b/c.txt", "c.txt")]
    [InlineData("a", "a")]
    [InlineData("../a", "a")]
    [InlineData("../../a/b", "b")]
    public void TestGetName(string path1, string expectedName)
    {
        var path = (UPath) path1;
        var result = path.GetName();
        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("/a", "a")]
    [InlineData("/a/b", "b")]
    [InlineData("/a/b/c.txt", "c")]
    [InlineData("a", "a")]
    [InlineData("../a", "a")]
    [InlineData("../../a/b", "b")]
    public void TestGetNameWithoutExtension(string path1, string expectedName)
    {
        var path = (UPath)path1;
        var result = path.GetNameWithoutExtension();
        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("/a.txt", ".txt")]
    [InlineData("/a.", "")]
    [InlineData("/a.txt.bak", ".bak")]
    [InlineData("a.txt", ".txt")]
    [InlineData("a.", "")]
    [InlineData("a.txt.bak", ".bak")]
    public void TestGetExtension(string path1, string expectedName)
    {
        var path = (UPath)path1;
        var result = path.GetExtensionWithDot();
        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/", null)]
    [InlineData("/a", "/")]
    [InlineData("/a/b", "/a")]
    [InlineData("/a/b/c.txt", "/a/b")]
    [InlineData("a", "")]
    [InlineData("../a", "..")]
    [InlineData("../../a/b", "../../a")]
    public void TestGetDirectory(string path1, string expectedDir)
    {
        var path = (UPath)path1;
        var result = path.GetDirectory();
        Assert.Equal(expectedDir, result);
    }

    [Theory]
    [InlineData("", ".txt", "")]
    [InlineData("/", ".txt", "/.txt")]
    [InlineData("/a", ".txt", "/a.txt")]
    [InlineData("/a/b", ".txt", "/a/b.txt")]
    [InlineData("/a/b/c.bin", ".txt", "/a/b/c.txt")]
    [InlineData("a", ".txt", "a.txt")]
    [InlineData("../a", ".txt", "../a.txt")]
    [InlineData("../../a/b", ".txt", "../../a/b.txt")]
    public void TestChangeExtension(string path1, string newExt, string expectedPath)
    {
        var path = (UPath)path1;
        var result = path.ChangeExtension(newExt);
        Assert.Equal(expectedPath, result);
    }

    [Theory]
    // Test automatic separator insertion
    [InlineData("/a/b/c", "/a/b", false, true)]
    [InlineData("/a/bc", "/a/b", false, false)]

    // Test trailing separator
    [InlineData("/a/b/", "/a", false, true)]
    [InlineData("/a/b", "/a/", false, true)]
    [InlineData("/a/b/", "/a/", false, true)]

    // Test recursive option
    [InlineData("/a/b/c", "/a", true, true)]
    [InlineData("/a/b/c", "/a", false, false)]
    
    // Test relative paths
    [InlineData("a/b", "a", false, true)]
    
    // Test exact match
    [InlineData("/a/b/", "/a/b/", false, true)]
    [InlineData("/a/b/", "/a/b/", true, true)]
    [InlineData("/a/b", "/a/b", false, true)]
    [InlineData("/a/b", "/a/b", true, true)]
    public void TestIsInDirectory(string path1, string directory, bool recursive, bool expected)
    {
        var path = (UPath)path1;
        var result = path.IsInDirectory(directory, recursive);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TestSplit()
    {
        Assert.Equal(new List<string>(), ((UPath)"").Split());
        Assert.Equal(new List<string>(), ((UPath)"/").Split());
        Assert.Equal(new List<string>() { "a" }, ((UPath)"/a").Split());
        Assert.Equal(new List<string>() {"a", "b", "c"}, ((UPath) "/a/b/c").Split());
        Assert.Equal(new List<string>() { "a" }, ((UPath)"a").Split());
        Assert.Equal(new List<string>() { "a", "b" }, ((UPath)"a/b").Split());
        Assert.Equal(new List<string>() { "a", "b", "c" }, ((UPath)"a/b/c").Split());
    }


    [Fact]
    public void TestExpectedException()
    {
        Assert.Throws<ArgumentException>(() => new UPath("/../a"));
        Assert.Throws<ArgumentException>(() => new UPath("..."));
        Assert.Throws<ArgumentException>(() => new UPath("a/..."));
        Assert.Throws<ArgumentException>(() => new UPath(".../a"));
        Assert.Throws<ArgumentException>(() => UPath.Combine("/", ".."));
        Assert.Equal("path1", Assert.Throws<ArgumentNullException>(() => UPath.Combine(null, "")).ParamName);
        Assert.Equal("path2", Assert.Throws<ArgumentNullException>(() => UPath.Combine("", null)).ParamName);
        Assert.Throws<ArgumentException>(() => UPathExtensions.IsInDirectory("/a", "b", true));
        Assert.Throws<ArgumentException>(() => UPathExtensions.IsInDirectory("a", "/b", true));
    }


    [Fact]
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
            Assert.Equal(new List<UPath>()
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
            Assert.Equal(new List<UPath>()
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

