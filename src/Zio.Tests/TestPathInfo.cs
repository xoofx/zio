using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Zio.Tests
{
    public class TestPathInfo
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
        public void TestNormalize(string pathAsText, string expectedResult)
        {
            var path = new PathInfo(pathAsText);
            Assert.Equal(expectedResult, path.FullName);

            // Check Equatable
            var expectedPath = new PathInfo(expectedResult);
            Assert.Equal(expectedPath, path);
            Assert.True(expectedPath.Equals((object)path));
            Assert.Equal(expectedPath.GetHashCode(), path.GetHashCode());
            Assert.True(path == expectedPath);
            Assert.False(path != expectedPath);

            // Check TryParse
            PathInfo result;
            Assert.True(PathInfo.TryParse(path.FullName, out result));
        }

        [Fact]
        public void TestEquals()
        {
            var pathInfo = new PathInfo("x");
            Assert.False(pathInfo.Equals((object)"no"));
            Assert.False(pathInfo.Equals(null));
        }

        [Fact]
        public void TestAbsoluteAndRelative()
        {
            var path = new PathInfo("x");
            Assert.True(path.IsRelative);
            Assert.False(path.IsAbsolute);

            path = new PathInfo("..");
            Assert.True(path.IsRelative);
            Assert.False(path.IsAbsolute);

            path = new PathInfo("/x");
            Assert.False(path.IsRelative);
            Assert.True(path.IsAbsolute);

            path = new PathInfo();
            Assert.True(path.IsNull);
        }

        [Theory]
        [InlineData("", "", "")]
        [InlineData("a", "b", "a/b")]
        [InlineData("a/b", "c", "a/b/c")]
        [InlineData("", "b", "b")]
        [InlineData("a", "", "a")]
        [InlineData("a/b", "", "a/b")]
        [InlineData("/a", "b/", "/a/b")]
        [InlineData("/a", "/b", "/b")]
        public void TestCombine(string path1, string path2, string expectedResult)
        {
            var path = PathInfo.Combine(path1, path2);
            Assert.Equal(expectedResult, (string)path);

            path = new PathInfo(path1) / new PathInfo(path2);
            Assert.Equal(expectedResult, (string)path);

            // Compare path info directly
            var expectedPath = new PathInfo(expectedResult);
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
            var path = (PathInfo) path1;
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
            var path = (PathInfo)path1;
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
            var path = (PathInfo)path1;
            var result = path.GetDotExtension();
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
            var path = (PathInfo)path1;
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
            var path = (PathInfo)path1;
            var result = path.ChangeExtension(newExt);
            Assert.Equal(expectedPath, result);
        }

        [Fact]
        public void TestSplit()
        {
            Assert.Equal(new List<string>(), ((PathInfo)"").Split().ToList());
            Assert.Equal(new List<string>(), ((PathInfo)"/").Split().ToList());
            Assert.Equal(new List<string>() { "a" }, ((PathInfo)"/a").Split().ToList());
            Assert.Equal(new List<string>() {"a", "b", "c"}, ((PathInfo) "/a/b/c").Split().ToList());
            Assert.Equal(new List<string>() { "a" }, ((PathInfo)"a").Split().ToList());
            Assert.Equal(new List<string>() { "a", "b" }, ((PathInfo)"a/b").Split().ToList());
            Assert.Equal(new List<string>() { "a", "b", "c" }, ((PathInfo)"a/b/c").Split().ToList());
        }


        [Fact]
        public void TestExpectedException()
        {
            Assert.Throws<ArgumentException>(() => new PathInfo("/../a"));
            Assert.Throws<ArgumentException>(() => new PathInfo("..."));
            Assert.Throws<ArgumentException>(() => new PathInfo("a/..."));
            Assert.Throws<ArgumentException>(() => new PathInfo(".../a"));
            Assert.Throws<InvalidOperationException>(() => PathInfo.Combine("/", ".."));
            Assert.Equal("path1", Assert.Throws<ArgumentNullException>(() => PathInfo.Combine(null, "")).ParamName);
            Assert.Equal("path2", Assert.Throws<ArgumentNullException>(() => PathInfo.Combine("", null)).ParamName);
        }
    }
}

