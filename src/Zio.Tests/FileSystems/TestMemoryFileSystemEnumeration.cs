// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestMemoryFileSystemEnumeration
{
    [TestMethod]
    public void TestEnumeratedPathsAreNormalized()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/a/b");
        fs.WriteAllText("/root.txt", "");
        fs.WriteAllText("/a/.hidden", "");
        fs.WriteAllText("/a/b/c d.txt", "");
        fs.WriteAllText("/a/b/x..y", "");
        fs.WriteAllText("/a/b/.z.", "");

        var expected = new UPath[] { "/a", "/a/.hidden", "/a/b", "/a/b/.z.", "/a/b/c d.txt", "/a/b/x..y", "/root.txt" };

        var paths = fs.EnumeratePaths("/", "*", SearchOption.AllDirectories).OrderBy(path => path.FullName, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(expected, paths);

        var items = fs.EnumerateItems("/", SearchOption.AllDirectories).OrderBy(item => item.Path.FullName, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(expected, items.Select(item => item.Path).ToList());
        CollectionAssert.AreEqual(expected, items.Select(item => item.AbsolutePath).ToList());

        foreach (var path in paths.Concat(items.Select(item => item.Path)))
        {
            // Same path as combining the parent directory and the name, and already normalized
            Assert.AreEqual(UPath.Combine(path.GetDirectory(), path.GetName()), path);
            Assert.AreEqual(new UPath(path.FullName).FullName, path.FullName);
        }

        // Search patterns with a directory, or that don't match the directories that are followed
        CollectionAssert.AreEqual(new UPath[] { "/a/b/c d.txt" }, fs.EnumerateFiles("/a", "b/*.txt").ToList());
        CollectionAssert.AreEqual(new UPath[] { "/a/b/c d.txt", "/root.txt" }, fs.EnumerateFiles("/", "*.txt", SearchOption.AllDirectories).OrderBy(path => path.FullName, StringComparer.Ordinal).ToList());
        CollectionAssert.AreEqual(new UPath[] { "/a/b/x..y" }, fs.EnumerateFiles("/", "x*", SearchOption.AllDirectories).ToList());
        CollectionAssert.AreEqual(new UPath[] { "/a/b" }, fs.EnumerateDirectories("/", "b", SearchOption.AllDirectories).ToList());
        CollectionAssert.AreEqual(new UPath[] { "/a", "/a/b" }, fs.EnumerateDirectories("/", "*", SearchOption.AllDirectories).OrderBy(path => path.FullName, StringComparer.Ordinal).ToList());
        CollectionAssert.AreEqual(new UPath[] { "/a/.hidden", "/a/b" }, fs.EnumeratePaths("/a", "*", SearchOption.TopDirectoryOnly).OrderBy(path => path.FullName, StringComparer.Ordinal).ToList());
    }

    [TestMethod]
    public void TestEnumeratedItemTimesMatchTheFileSystemTimes()
    {
        var fs = new MemoryFileSystem();

        // All the times are the same
        fs.CreateDirectory("/directory");

        // The last write and last access times are the same, the creation time is different
        fs.WriteAllText("/written.txt", "content");

        // All the times are different
        fs.WriteAllText("/read.txt", "content");
        Assert.AreEqual("content", fs.ReadAllText("/read.txt"));

        // Same ticks but different kinds
        var local = new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Local);
        fs.WriteAllText("/kinds.txt", "content");
        fs.SetCreationTime("/kinds.txt", local);
        fs.SetLastWriteTime("/kinds.txt", DateTime.SpecifyKind(local, DateTimeKind.Utc));
        fs.SetLastAccessTime("/kinds.txt", DateTime.SpecifyKind(local, DateTimeKind.Unspecified));

        // Same ticks and kinds, set explicitly
        var utc = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        fs.WriteAllText("/utc.txt", "content");
        fs.SetCreationTime("/utc.txt", utc);
        fs.SetLastWriteTime("/utc.txt", local);
        fs.SetLastAccessTime("/utc.txt", utc);

        var items = fs.EnumerateItems("/", SearchOption.AllDirectories).ToList();
        Assert.AreEqual(5, items.Count);
        foreach (var item in items)
        {
            AssertSameDateTimeOffset(fs.GetCreationTime(item.Path), item.CreationTime, item.Path);
            AssertSameDateTimeOffset(fs.GetLastWriteTime(item.Path), item.LastWriteTime, item.Path);
            AssertSameDateTimeOffset(fs.GetLastAccessTime(item.Path), item.LastAccessTime, item.Path);
        }
    }

    private static void AssertSameDateTimeOffset(DateTime expected, DateTimeOffset actual, UPath path)
    {
        var expectedOffset = new DateTimeOffset(expected);
        Assert.IsTrue(expectedOffset.EqualsExact(actual), $"Expecting `{expectedOffset:o}` but got `{actual:o}` for `{path}`");
    }
}
