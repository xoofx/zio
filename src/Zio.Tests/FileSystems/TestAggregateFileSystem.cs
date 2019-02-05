// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections;
using System.Reflection;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
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

            var gotChange1 = false;
            var gotChange2 = false;
            watcher.Created += (sender, args) =>
            {
                if (args.FullPath == "/b/watched.txt")
                {
                    gotChange1 = true;
                }

                if (args.FullPath == "/C/watched.txt")
                {
                    gotChange2 = true;
                }
            };

            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            fs1.WriteAllText("/b/watched.txt", "test");
            fs2.WriteAllText("/C/watched.txt", "test");

            System.Threading.Thread.Sleep(100);

            Assert.True(gotChange1);
            Assert.True(gotChange2);
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
        }
    }
}