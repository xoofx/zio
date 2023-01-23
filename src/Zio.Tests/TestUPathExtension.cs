using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Zio.Tests
{
    public class TestUPathExtension
    {


        [Theory]
        [InlineData("/a/b","a","b")]
        [InlineData("/a/b/c","a","b/c")]
        [InlineData("a/b","a","b")]
        [InlineData("a/b/c","a","b/c")]
        public void TestGetFirstDirectory(string path, string expectedFirstDir, string expectedRest)
        {
            var pathInfo = new UPath(path);
            var firstDir = pathInfo.GetFirstDirectory(out var rest);
            Assert.Equal(expectedFirstDir,firstDir);
            Assert.Equal(expectedRest,rest);
        }
    }
}

