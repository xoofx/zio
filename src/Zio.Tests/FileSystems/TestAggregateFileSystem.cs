// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Collections;
using System.IO;
using System.Reflection;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

public class TestAggregateFileSystem : TestFileSystemBase
{
    [Fact]
    public void TestCommonReadOnly()
    {
        var fs = GetCommonAggregateFileSystem();
        AssertCommonReadOnly(fs);
    }

    [Fact]
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

        Assert.True(change1WaitHandle.WaitOne(100));
        Assert.True(change2WaitHandle.WaitOne(100));
    }

    [Fact]
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
        Assert.NotNull(watchersField);

        var watchers = (IList)watchersField.GetValue(fs);
        Assert.NotNull(watchers);
        Assert.Empty(watchers);
    }

    [Fact]
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
        Assert.Single(list);
        Assert.Equal(memfs, list[0]);

        fs.ClearFileSystems();
        Assert.Empty(fs.GetFileSystems());

        fs.SetFileSystems(list);

        fs.RemoveFileSystem(memfs);

        list = fs.GetFileSystems();
        Assert.Empty(list);
    }

    [Fact]
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
            Assert.Equal(2, entries.Count);

            Assert.IsType<FileEntry>(entries[0]);
            Assert.IsType<FileEntry>(entries[1]);
            Assert.Equal(memfs2, entries[0].FileSystem);
            Assert.Equal(memfs1, entries[1].FileSystem);
            Assert.Equal("/a.txt", entries[0].Path.FullName);
            Assert.Equal("/a.txt", entries[1].Path.FullName);
        }

        {
            var entries = fs.FindFileSystemEntries("/b");
            Assert.Single(entries);

            Assert.IsType<DirectoryEntry>(entries[0]);
            Assert.Equal(memfs2, entries[0].FileSystem);
            Assert.Equal("/b", entries[0].Path.FullName);
        }

        {
            var entry = fs.FindFirstFileSystemEntry("/a.txt");
            Assert.NotNull(entry);

            Assert.IsType<FileEntry>(entry);
            Assert.Equal(memfs2, entry.FileSystem);
            Assert.Equal("/a.txt", entry.Path.FullName);
        }
    }

    [Fact]
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
        Assert.Equal(subMemFs, findA?.FileSystem);

        var findB = fs.FindFirstFileSystemEntry("/b");
        Assert.Equal(subFsMemFs, findB?.FileSystem);

        var findC = fs.FindFirstFileSystemEntry("/c");
        Assert.Equal(root, findC?.FileSystem);

        Assert.True(fs.DirectoryExists("/c"));
        Assert.True(fs.DirectoryExists("/b"));
        Assert.True(fs.DirectoryExists("/a"));

        Assert.True(fs.FileExists("/c.txt"));
        Assert.True(fs.FileExists("/b.txt"));
        Assert.True(fs.FileExists("/a.txt"));
    }


    [Fact]
    public void TestResolvePath()
    {
        var fs = GetCommonAggregateFileSystem(out var fs1, out var fs2, out var fs3);

        // File (fs3)
        {
            var (resolvedFs, resolvedPath) = fs.ResolvePath("/A.txt");
            Assert.Equal(fs3, resolvedFs);
            Assert.Equal("/A.txt", resolvedPath);
        }

        // Directory (fs2)
        {
            var (resolvedFs, resolvedPath) = fs.ResolvePath("/a/C");
            Assert.Equal(fs2, resolvedFs);
            Assert.Equal("/a/C", resolvedPath);
        }

        // File (fs1)
        {
            var (resolvedFs, resolvedPath) = fs.ResolvePath("/a/b");
            Assert.Equal(fs1, resolvedFs);
            Assert.Equal("/a/b", resolvedPath);
        }
    }
}