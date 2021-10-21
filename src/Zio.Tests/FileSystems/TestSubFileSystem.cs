// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestSubFileSystem : TestFileSystemBase
    {
        [Fact]
        public async Task TestBasic()
        {
            var fs = new PhysicalFileSystem();
            var path = fs.ConvertPathFromInternal(SystemPath);

            // Create a filesystem / on the current folder of this assembly
            var subfs = await SubFileSystem.Create(fs, path);

            // This test is basically testing the two methods (ConvertPathToDelegate and ConvertPathFromDelegate) in SubFileSystem

            var files = (await subfs.EnumeratePaths("/")).Select(info => info.GetName()).ToList();
            var expectedFiles = (await fs.EnumeratePaths(path)).Select(info => info.GetName()).ToList();
            Assert.True(files.Count > 0);
            Assert.Equal(expectedFiles, files);

            // Check that SubFileSystem is actually checking that the directory exists in the delegate filesystem
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await SubFileSystem.Create(fs, path / "does_not_exist"));

            Assert.Throws<InvalidOperationException>(() => subfs.ConvertPathFromInternal(@"C:\"));
            // TODO: We could add another test just to make sure that files can be created...etc. But the test above should already cover the code provided in SubFileSystem
        }

        [Fact]
        public async Task TestGetOrCreateFileSystem()
        {
            var fs = new MemoryFileSystem();
            const string subFolder = "/sub";
            var subFileSystem = await fs.GetOrCreateSubFileSystem(subFolder);
            Assert.True(await fs.DirectoryExists(subFolder));
            await subFileSystem.WriteAllText("/test.txt", "yo");
            var text = await fs.ReadAllText(subFolder + "/test.txt");
            Assert.Equal("yo", text);
        }

        [Fact]
        public async Task TestWatcher()
        {
            var fs = await GetCommonMemoryFileSystem();
            var subFs = await fs.GetOrCreateSubFileSystem("/a/b");
            var watcher = subFs.Watch("/");

            var gotChange = false;
            watcher.Created += (sender, args) =>
            {
                if (args.FullPath == "/watched.txt")
                {
                    gotChange = true;
                }
            };

            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            await fs.WriteAllText("/a/b/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }
    }
}