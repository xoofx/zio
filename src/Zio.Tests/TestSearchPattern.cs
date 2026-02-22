// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

namespace Zio.Tests;

[TestClass]
public class TestSearchPattern
{
    [TestMethod]
    // Test any-alone '*' match
    [DataRow("/a/b/c", "*", "x", true)]
    [DataRow("/a/b/c", "*", "x/y", true)]

    [DataRow("/a/b/c", "d/*", "x", true)]
    [DataRow("/a/b/c", "d/*", "x/y", true)]

    [DataRow("/a/b/c", "d/x", "x", true)]

    // Test exact match
    [DataRow("/a/b/c", "x", "x", true)]
    [DataRow("/a/b/c", "x", "d/x", true)]
    [DataRow("/a/b/c", "x", "d/e/x", true)]

    // Test regex match: ? pattern
    [DataRow("/a/b/c", "?", "x", true)]
    [DataRow("/a/b/c", "x?z", "xyz", true)]
    [DataRow("/a/b/c", "x?z", "d/xyz", true)]

    [DataRow("/a/b/c", "?yz", "xyz", true)]
    [DataRow("/a/b/c", "xy?", "xyz", true)]
    [DataRow("/a/b/c", "??", "ab", true)]
    [DataRow("/a/b/c", "??", "c", false)]
    [DataRow("/a/b/c", "?", "abc", false)]

    // Test regex match: * pattern
    [DataRow("/a/b/c", "x*", "x", true)]
    [DataRow("/a/b/c", "x*", "xyz", true)]
    [DataRow("/a/b/c", "*z", "z", true)]
    [DataRow("/a/b/c", "*z", "xyz", true)]
    [DataRow("/a/b/c", "x*z", "xyz", true)]
    [DataRow("/a/b/c", "x*z", "xblablaz", true)]
    [DataRow("/a/b/c", "x*z", "axyz", false)]
    [DataRow("/a/b/c", "x*z", "xyza", false)]
    [DataRow("/a/b/c", "x*.txt", "xyoyo.txt", true)]
    [DataRow("/a/b/c", "x*.txt", "x.txt", true)]
    [DataRow("/a/b/c", "*.txt", "x.txt", true)]
    [DataRow("/a/b/c", "*.txt", "x.txt1", false)] // No 8.3 truncating
    [DataRow("/a/b/c", "*.i", "x.i", true)]
    [DataRow("/a/b/c", "*.i", "x.i1", false)]
    [DataRow("/a/b/c", "x*.txt", "x_txt", false)]
    public void TestMatch(string path, string searchPattern, string pathToSearch, bool match = true)
    {
        var pathInfo = new UPath(path);
        var pathInfoCopy = pathInfo;
        var search = SearchPattern.Parse(ref pathInfoCopy, ref searchPattern);

        {
            var pathInfoCopy2 = pathInfoCopy;
            var searchPattern2 = searchPattern;
            SearchPattern.Normalize(ref pathInfoCopy2, ref searchPattern2);
            AssertEx.AreEqual(pathInfoCopy, pathInfoCopy2);
            AssertEx.AreEqual(searchPattern, searchPattern2);
        }

        var pathToSearchInfo = new UPath(pathToSearch);
        AssertEx.AreEqual(match, search.Match(pathToSearchInfo));
    }


    [TestMethod]
    public void TestExpectedExceptions()
    {
        var path = new UPath("/yoyo");
        Assert.Throws<ArgumentNullException>(() =>
        {
            var nullPath = new UPath();
            string searchPattern = "valid";
            SearchPattern.Parse(ref nullPath, ref searchPattern);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            string searchPattern = null;
            SearchPattern.Parse(ref path, ref searchPattern);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            string searchPattern = "/notvalid";
            SearchPattern.Parse(ref path, ref searchPattern);
        });

        {
            var searchPattern = "*";
            var search = SearchPattern.Parse(ref path, ref searchPattern);
            Assert.Throws<ArgumentNullException>(() => search.Match(((string)null)!));
        }
    }

    [TestMethod]
    public void TestExtensions()
    {
        {
            var path = new UPath("/a/b/c/d.txt");
            AssertEx.AreEqual(new UPath("/a/b/c"), path.GetDirectory());
            AssertEx.AreEqual("d.txt", path.GetName());
            AssertEx.AreEqual("d", path.GetNameWithoutExtension());
            AssertEx.AreEqual(".txt", path.GetExtensionWithDot());
            var newPath = path.ChangeExtension(".zip");
            AssertEx.AreEqual("/a/b/c/d.zip", newPath.FullName);
            AssertEx.AreEqual(new UPath("a/b/c/d.txt"), path.ToRelative());
            AssertEx.AreEqual(path, path.AssertAbsolute());
            Assert.Throws<ArgumentNullException>(() => new UPath().AssertNotNull());
            Assert.Throws<ArgumentException>(() => new UPath("not_absolute").AssertAbsolute());
        }

        {
            var path = new UPath("d.txt");
            AssertEx.AreEqual(UPath.Empty, path.GetDirectory());
            AssertEx.AreEqual("d.txt", path.GetName());
            AssertEx.AreEqual("d", path.GetNameWithoutExtension());
            AssertEx.AreEqual(".txt", path.GetExtensionWithDot());
            var newPath = path.ChangeExtension(".zip");
            AssertEx.AreEqual("d.zip", newPath.FullName);
            AssertEx.AreEqual(new UPath("d.txt"), path.ToRelative());
        }
    }

}



