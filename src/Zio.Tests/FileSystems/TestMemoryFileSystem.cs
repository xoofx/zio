// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Threading.Tasks;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMemoryFileSystem : TestFileSystemBase
    {
        [Fact]
        public async ValueTask TestCommonRead()
        {
            var fs = await GetCommonMemoryFileSystem();
            await AssertCommonRead(fs);
        }

        [Fact]
        public async ValueTask TestCopyFileSystem()
        {
            var fs = await GetCommonMemoryFileSystem();

            var dest = new MemoryFileSystem();
            await fs.CopyTo(dest, UPath.Root, true);

            await AssertFileSystemEqual(fs, dest);
        }

        [Fact]
        public async ValueTask TestCopyFileSystemSubFolder()
        {
            var fs = await GetCommonMemoryFileSystem();

            var dest = new MemoryFileSystem();
            var subFolder = UPath.Root / "subfolder";
            await fs.CopyTo(dest, subFolder, true);

            var destSubFileSystem = await dest.GetOrCreateSubFileSystem(subFolder);

            await AssertFileSystemEqual(fs, destSubFileSystem);
        }
        

        [Fact]
        public async ValueTask TestWatcher()
        {
            var fs = await GetCommonMemoryFileSystem();
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

            await fs.WriteAllText("/a/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public async ValueTask TestDispose()
        {
            var memfs = new MemoryFileSystem();

            memfs.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await memfs.DirectoryExists("/"));
        }
    }
}