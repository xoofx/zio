using System;
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

