// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Collections;
using System.IO;
using System.Reflection;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestAggregateFileSystem : TestFileSystemBase
{
    [TestMethod]
    public void TestCommonReadOnly()
    {
        var fs = GetCommonAggregateFileSystem();
        AssertCommonReadOnly(fs);
    }

    [TestMethod]
    public void TestWatcher()
    {
        var fs = GetCommonAggregateFileSystem(out var fs1, out var fs2, out _);
        var watcher = fs.Watch("/");

        var change1WaitHandle = new ManualResetEvent(false);
        var change2WaitHandle = new ManualResetEvent(false);

        watcher.Created += (sender, args) =>
        {
            if (args.FullPath == "/b/watched.txt")
            {
                change1WaitHandle.Set();
            }

            if (args.FullPath == "/C/watched.txt")
            {
                change2WaitHandle.Set();
            }
        };

        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        fs1.WriteAllText("/b/watched.txt", "test");
        fs2.WriteAllText("/C/watched.txt", "test");

        Assert.IsTrue(change1WaitHandle.WaitOne(100));
        Assert.IsTrue(change2WaitHandle.WaitOne(100));
    }

    [TestMethod]
    public void TestWatcherRemovedWhenDisposed()
    {
        var fs = GetCommonAggregateFileSystem();

        var watcher = fs.Watch("/");
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;
        watcher.Dispose();

        System.Threading.Thread.Sleep(100);

        var watchersField = typeof(AggregateFileSystem).GetTypeInfo()
            .GetField("_watchers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(watchersField);

        var watchers = (IList)watchersField.GetValue(fs);
        Assert.IsNotNull(watchers);
        AssertEx.Empty(watchers);
    }

    [TestMethod]
    public void TestAddRemoveFileSystem()
    {
        var fs = new AggregateFileSystem();

        Assert.Throws<ArgumentNullException>(() => fs.AddFileSystem(null));
        Assert.Throws<ArgumentException>(() => fs.AddFileSystem(fs));

        var memfs = new MemoryFileSystem();
        fs.AddFileSystem(memfs);
        Assert.Throws<ArgumentException>(() => fs.AddFileSystem(memfs));

        Assert.Throws<ArgumentNullException>(() => fs.RemoveFileSystem(null));

        var memfs2 = new MemoryFileSystem();
        Assert.Throws<ArgumentException>(() => fs.RemoveFileSystem(memfs2));

        var list = fs.GetFileSystems();
        AssertEx.Single(list);
        AssertEx.AreEqual(memfs, list[0]);

        fs.ClearFileSystems();
        AssertEx.Empty(fs.GetFileSystems());

        fs.SetFileSystems(list);

        fs.RemoveFileSystem(memfs);

        list = fs.GetFileSystems();
        AssertEx.Empty(list);
    }

    [TestMethod]
    public void TestFindFileSystemEntries()
    {
        var fs = new AggregateFileSystem();

        var memfs1 = new MemoryFileSystem();
        memfs1.WriteAllText("/a.txt", "content1");
        memfs1.WriteAllText("/b", "notused");
        fs.AddFileSystem(memfs1);

        var memfs2 = new MemoryFileSystem();
        memfs2.WriteAllText("/a.txt", "content2");
        memfs2.CreateDirectory("/b");
        fs.AddFileSystem(memfs2);

        {
            var entries = fs.FindFileSystemEntries("/a.txt");
            AssertEx.AreEqual(2, entries.Count);

            Assert.IsInstanceOfType<FileEntry>(entries[0]);
            Assert.IsInstanceOfType<FileEntry>(entries[1]);
            AssertEx.AreEqual(memfs2, entries[0].FileSystem);
            AssertEx.AreEqual(memfs1, entries[1].FileSystem);
            AssertEx.AreEqual("/a.txt", entries[0].Path.FullName);
            AssertEx.AreEqual("/a.txt", entries[1].Path.FullName);
        }

        {
            var entries = fs.FindFileSystemEntries("/b");
            AssertEx.Single(entries);

            Assert.IsInstanceOfType<DirectoryEntry>(entries[0]);
            AssertEx.AreEqual(memfs2, entries[0].FileSystem);
            AssertEx.AreEqual("/b", entries[0].Path.FullName);
        }

        {
            var entry = fs.FindFirstFileSystemEntry("/a.txt");
            Assert.IsNotNull(entry);

            Assert.IsInstanceOfType<FileEntry>(entry);
            AssertEx.AreEqual(memfs2, entry.FileSystem);
            AssertEx.AreEqual("/a.txt", entry.Path.FullName);
        }
    }

    [TestMethod]
    public void TestFallback()
    {
        // aggregate_fs (fs)
        //      => aggregate_fs (subFs)
        //              => memory_fs (subFsMemFs)
        //      => memory_fs (subMemFs)
        //      => memory_fs (root)
        var root = new MemoryFileSystem();
        var fs = new AggregateFileSystem(root);
        var subFsMemFs = new MemoryFileSystem();
        var subFs = new AggregateFileSystem(subFsMemFs);
        fs.AddFileSystem(subFs);
        var subMemFs = new MemoryFileSystem();
        fs.AddFileSystem(subMemFs);

        root.CreateDirectory("/a");
        root.CreateDirectory("/b");
        root.CreateDirectory("/c");
        {
            using var a = root.OpenFile("/a.txt", FileMode.Create, FileAccess.Write);
            using var b = root.OpenFile("/b.txt", FileMode.Create, FileAccess.Write);
            using var c = root.OpenFile("/c.txt", FileMode.Create, FileAccess.Write);
        }
        subFsMemFs.CreateDirectory("/b");
        {
            using var b = subFsMemFs.OpenFile("/b.txt", FileMode.Create, FileAccess.Write);
        }
        subMemFs.CreateDirectory("/a");
        {
            using var a = subMemFs.OpenFile("/a.txt", FileMode.Create, FileAccess.Write);
        }
        
        var findA = fs.FindFirstFileSystemEntry("/a");
        AssertEx.AreEqual(subMemFs, findA?.FileSystem);

        var findB = fs.FindFirstFileSystemEntry("/b");
        AssertEx.AreEqual(subFsMemFs, findB?.FileSystem);

        var findC = fs.FindFirstFileSystemEntry("/c");
        AssertEx.AreEqual(root, findC?.FileSystem);

        Assert.IsTrue(fs.DirectoryExists("/c"));
        Assert.IsTrue(fs.DirectoryExists("/b"));
        Assert.IsTrue(fs.DirectoryExists("/a"));

        Assert.IsTrue(fs.FileExists("/c.txt"));
        Assert.IsTrue(fs.FileExists("/b.txt"));
        Assert.IsTrue(fs.FileExists("/a.txt"));
    }


    [TestMethod]
    public void TestResolvePath()
    {

        var fs = GetCommonAggregateFileSystem(out var fs1, out var fs2, out var fs3);

        // Add a SubFileSystem (fs4) to test resolving paths inside a sub filesystem
        var fs4Mem = new MemoryFileSystem();
        fs4Mem.CreateDirectory("/test");
        fs4Mem.WriteAllText("/test/hello.txt", "HelloText");
        var fs4 = new SubFileSystem(fs4Mem, "/test");

        fs.AddFileSystem(fs4);
        
        // File (fs3)
        {
            var (resolvedFs, resolvedPath) = fs.ResolvePath("/A.txt");
            AssertEx.AreEqual(fs3, resolvedFs);
            AssertEx.AreEqual("/A.txt", resolvedPath);
        }

        // Directory (fs2)
        {
            var (resolvedFs, resolvedPath) = fs.ResolvePath("/a/C");
            AssertEx.AreEqual(fs2, resolvedFs);
            AssertEx.AreEqual("/a/C", resolvedPath);
        }

        // File (fs1)
        {
            var (resolvedFs, resolvedPath) = fs.ResolvePath("/a/b");
            AssertEx.AreEqual(fs1, resolvedFs);
            AssertEx.AreEqual("/a/b", resolvedPath);
        }

        // File (fs4Mem) 
        {
            var (resolvedFs, resolvedPath) = fs.ResolvePath("/hello.txt");
            AssertEx.AreEqual(fs4Mem, resolvedFs);
            AssertEx.AreEqual("/test/hello.txt", resolvedPath);
        }
    }
}



