// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMemoryFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestCommonRead()
        {
            var fs = GetCommonMemoryFileSystem();
            AssertCommonRead(fs);
        }

        [Fact]
        public void TestCopyFileSystem()
        {
            var fs = GetCommonMemoryFileSystem();

            var dest = new MemoryFileSystem();
            fs.CopyTo(dest, UPath.Root, true);

            AssertFileSystemEqual(fs, dest);
        }

        [Fact]
        public void TestCopyFileSystemSubFolder()
        {
            var fs = GetCommonMemoryFileSystem();

            var dest = new MemoryFileSystem();
            var subFolder = UPath.Root / "subfolder";
            fs.CopyTo(dest, subFolder, true);

            var destSubFileSystem = dest.GetOrCreateSubFileSystem(subFolder);
            
            AssertFileSystemEqual(fs, destSubFileSystem);
        }
        

        [Fact]
        public void TestWatcher()
        {
            var fs = GetCommonMemoryFileSystem();
            var watcher = fs.Watch("/a");

            var gotChange = false;
            watcher.Created += (sender, args) =>
            {
                if (args.FullPath == "/a/watched.txt")
                {
                    gotChange = true;
                }
            };

            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            fs.WriteAllText("/a/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public void TestDispose()
        {
            var memfs = new MemoryFileSystem();

            memfs.Dispose();
            Assert.Throws<ObjectDisposedException>(() => memfs.DirectoryExists("/"));
        }
    }
}