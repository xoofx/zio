// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMountFileSystem : TestFileSystemBase
    {
        [Fact]
        public async Task TestCommonReadWithOnlyBackup()
        {
            var fs = await GetCommonMountFileSystemWithOnlyBackup();
            await AssertCommonRead(fs);
        }

        [Fact]
        public async Task TestCommonReadWithMounts()
        {
            var fs = await GetCommonMountFileSystemWithMounts();
            await AssertCommonRead(fs);
        }
        
        [Fact]
        public async Task TestWatcherOnRoot()
        {
            var fs = await GetCommonMountFileSystemWithMounts();
            var watcher = fs.Watch("/");

            var gotChange = false;
            watcher.Created += (sender, args) =>
            {
                if (args.FullPath == "/b/watched.txt")
                {
                    gotChange = true;
                }
            };

            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            await fs.WriteAllText("/b/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public async Task TestWatcherOnMount()
        {
            var fs = await GetCommonMountFileSystemWithMounts();
            var watcher = fs.Watch("/b");

            var gotChange = false;
            watcher.Created += (sender, args) =>
            {
                if (args.FullPath == "/b/watched.txt")
                {
                    gotChange = true;
                }
            };

            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            await fs.WriteAllText("/b/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public async Task TestWatcherWithBackupOnRoot()
        {
            var fs = await GetCommonMountFileSystemWithOnlyBackup();
            var watcher = fs.Watch("/");

            var gotChange = false;
            watcher.Created += (sender, args) =>
            {
                if (args.FullPath == "/b/watched.txt")
                {
                    gotChange = true;
                }
            };

            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            await fs.WriteAllText("/b/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public async Task TestMount()
        {
            var fs = new MountFileSystem();
            var memfs = new MemoryFileSystem();

            Assert.Throws<ArgumentNullException>(() => fs.Mount(null, memfs));
            Assert.Throws<ArgumentNullException>(() => fs.Mount("/test", null));
            Assert.Throws<ArgumentException>(() => fs.Mount("test", memfs));
            Assert.Throws<ArgumentException>(() => fs.Mount("/", memfs));

            Assert.False(fs.IsMounted("/test"));
            fs.Mount("/test", memfs);
            Assert.True(fs.IsMounted("/test"));
            Assert.Throws<ArgumentException>(() => fs.Mount("/test", memfs));
            Assert.Throws<ArgumentException>(() => fs.Mount("/test", fs));

            Assert.Throws<ArgumentNullException>(() => fs.Unmount(null));
            Assert.Throws<ArgumentException>(() => fs.Unmount("test"));
            Assert.Throws<ArgumentException>(() => fs.Unmount("/"));
            Assert.Throws<ArgumentException>(() => fs.Unmount("/test/a"));
            Assert.Throws<ArgumentException>(() => fs.Unmount("/test/a/b"));
            Assert.Throws<ArgumentException>(() => fs.Unmount("/test2"));

            fs.Mount("/test2", memfs);
            Assert.True(fs.IsMounted("/test"));
            Assert.True(fs.IsMounted("/test2"));

            Assert.Equal(new Dictionary<UPath, IFileSystem>()
            {
                {"/test", memfs},
                {"/test2", memfs},
            }, fs.GetMounts());

            fs.Unmount("/test");
            Assert.False(fs.IsMounted("/test"));
            Assert.True(fs.IsMounted("/test2"));

            fs.Unmount("/test2");

            Assert.Empty(fs.GetMounts());

            var innerFs = await GetCommonMemoryFileSystem();
            fs.Mount("/x/y", innerFs);
            fs.Mount("/x/y/b", innerFs);
            Assert.True(await fs.FileExists("/x/y/A.txt"));
            Assert.True(await fs.FileExists("/x/y/b/A.txt"));
        }

        [Fact]
        public async Task TestWatcherRemovedWhenDisposed()
        {
            var fs = await GetCommonMountFileSystemWithMounts();

            var watcher = fs.Watch("/");
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;
            watcher.Dispose();

            System.Threading.Thread.Sleep(100);

            var watchersField = typeof(MountFileSystem).GetTypeInfo()
                .GetField("_watchers", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(watchersField);

            var watchers = (IList)watchersField.GetValue(fs);
            Assert.NotNull(watchers);
            Assert.Empty(watchers);
        }

        [Fact]
        public async Task EnumerateDeepMount()
        {
            var fs = await GetCommonMemoryFileSystem();
            var mountFs = new MountFileSystem();
            mountFs.Mount("/x/y/z", fs);
            Assert.True(await mountFs.FileExists("/x/y/z/A.txt"));

            var expected = new List<UPath>
            {
                "/x",
                "/x/y",
                "/x/y/z",
                "/x/y/z/a",
                "/x/y/z/A.txt"
            };

            // only concerned with the first few because it should list the mount parts first
            var actual = (await mountFs.EnumeratePaths("/", "*", SearchOption.AllDirectories)).Take(5).ToList();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task EnumerateDeepMountPartial()
        {
            var fs = await GetCommonMemoryFileSystem();
            var mountFs = new MountFileSystem();
            mountFs.Mount("/x/y/z", fs);
            Assert.True(await mountFs.FileExists("/x/y/z/A.txt"));

            var expected = new List<UPath>
            {
                "/x/y",
                "/x/y/z",
                "/x/y/z/a",
                "/x/y/z/A.txt"
            };

            // only concerned with the first few because it should list the mount parts first
            var actual = (await mountFs.EnumeratePaths("/x", "*", SearchOption.AllDirectories)).Take(4).ToList();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task EnumerateMountsOverride()
        {
            var baseFs = new MemoryFileSystem();
            await baseFs.CreateDirectory("/foo/bar");
            await baseFs.WriteAllText("/base.txt", "test");
            await baseFs.WriteAllText("/foo/base.txt", "test");
            await baseFs.WriteAllText("/foo/bar/base.txt", "test");

            var mountedFs = new MemoryFileSystem();
            await mountedFs.WriteAllText("/mounted.txt", "test");

            var deepMountedFs = new MemoryFileSystem();
            await deepMountedFs.WriteAllText("/deep_mounted.txt", "test");

            var mountFs = new MountFileSystem(baseFs);
            mountFs.Mount("/foo", mountedFs);
            mountFs.Mount("/foo/bar", deepMountedFs);
            
            var expected = new List<UPath>
            {
                "/base.txt",
                "/foo",
                "/foo/bar",
                "/foo/mounted.txt",
                "/foo/bar/deep_mounted.txt"
            };

            var actual = (await mountFs.EnumeratePaths("/", "*", SearchOption.AllDirectories)).ToList();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task EnumerateEmptyOnRoot()
        {
            var mountFs = new MountFileSystem();
            var expected = Array.Empty<UPath>();
            var actual = (await mountFs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Both)).ToList();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task EnumerateDoesntExist()
        {
            var mountFs = new MountFileSystem();
            mountFs.Mount("/x", new MemoryFileSystem());
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => (await mountFs.EnumeratePaths("/y", "*", SearchOption.AllDirectories, SearchTarget.Both)).ToList());
        }

        [Fact]
        public async Task EnumerateBackupDoesntExist()
        {
            var mountFs = new MountFileSystem(new MemoryFileSystem());            
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => (await mountFs.EnumeratePaths("/y", "*", SearchOption.AllDirectories, SearchTarget.Both)).ToList());
        }

        [Fact]
        public async Task DirectoryExistsPartialMountName()
        {
            var fs = new MemoryFileSystem();
            var mountFs = new MountFileSystem();
            mountFs.Mount("/x/y/z", fs);

            Assert.True(await mountFs.DirectoryExists("/x"));
            Assert.True(await mountFs.DirectoryExists("/x/y"));
            Assert.True(await mountFs.DirectoryExists("/x/y/z"));
            Assert.False(await mountFs.DirectoryExists("/z"));
        }

        [Fact]
        public async Task DirectoryEntryPartialMountName()
        {
            var fs = new MemoryFileSystem();
            await fs.CreateDirectory("/w");

            var mountFs = new MountFileSystem();
            mountFs.Mount("/x/y/z", fs);

            Assert.NotNull(await mountFs.GetDirectoryEntry("/x"));
            Assert.NotNull(await mountFs.GetDirectoryEntry("/x/y"));
            Assert.NotNull(await mountFs.GetDirectoryEntry("/x/y/z"));
            Assert.NotNull(await mountFs.GetDirectoryEntry("/x/y/z/w"));
        }

        [Fact]
        public async Task CreateDirectoryFail()
        {
            var mountfs = new MountFileSystem();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await mountfs.CreateDirectory("/test"));
        }

        [Fact]
        public async Task MoveDirectoryFail()
        {
            var mountfs = new MountFileSystem();
            mountfs.Mount("/dir1", new MemoryFileSystem());
            mountfs.Mount("/dir2", new MemoryFileSystem());

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await mountfs.MoveDirectory("/dir1", "/dir2/yyy"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await mountfs.MoveDirectory("/dir1/xxx", "/dir2"));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await mountfs.MoveDirectory("/dir1/xxx", "/dir2/yyy"));
        }

        [Fact]
        public async Task DeleteDirectoryFail()
        {
            var mountfs = new MountFileSystem();
            mountfs.Mount("/dir1", new MemoryFileSystem());

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await mountfs.DeleteDirectory("/dir1", true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await mountfs.DeleteDirectory("/dir1", false));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await mountfs.DeleteDirectory("/dir2", false));
        }

        [Fact]
        public async Task CopyFileFail()
        {
            var mountfs = new MountFileSystem();
            mountfs.Mount("/dir1", new MemoryFileSystem());
            await Assert.ThrowsAsync <FileNotFoundException>(async () => await mountfs.CopyFile("/test", "/test2", true));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await mountfs.CopyFile("/dir1/test.txt", "/test2", true));
        }

        [Fact]
        public async Task ReplaceFileFail()
        {
            var mountfs = new MountFileSystem();
            var memfs1 = new MemoryFileSystem();
            await memfs1.WriteAllText("/file.txt", "content1");

            var memfs2 = new MemoryFileSystem();
            await memfs2.WriteAllText("/file2.txt", "content1");

            mountfs.Mount("/dir1", memfs1);
            mountfs.Mount("/dir2", memfs2);
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.ReplaceFile("/dir1/file.txt", "/dir1/to.txt", "/dir1/to.bak", true));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.ReplaceFile("/dir1/to.txt", "/dir1/file.txt", "/dir1/to.bak", true));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await mountfs.ReplaceFile("/dir1/file.txt", "/dir2/file2.txt", null, true));
        }

        [Fact]
        public async Task GetFileLengthFail()
        {
            var mountfs = new MountFileSystem();
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.GetFileLength("/toto.txt"));
        }

        [Fact]
        public async Task MoveFileFail()
        {
            var mountfs = new MountFileSystem();
            var memfs1 = new MemoryFileSystem();
            await memfs1.WriteAllText("/file.txt", "content1");

            var memfs2 = new MemoryFileSystem();
            await memfs2.WriteAllText("/file2.txt", "content1");

            mountfs.Mount("/dir1", memfs1);
            mountfs.Mount("/dir2", memfs2);
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await mountfs.MoveFile("/dir1/file.txt", "/xxx/yyy.txt"));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.MoveFile("/dir1/xxx", "/dir1/file1.txt"));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.MoveFile("/xxx", "/dir1/file1.txt"));
        }


        [Fact]
        public async Task OpenFileFail()
        {
            var mountfs = new MountFileSystem();
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await mountfs.OpenFile("/toto.txt", FileMode.Create, FileAccess.Read));
        }

        [Fact]
        public async Task AttributesFail()
        {
            var mountfs = new MountFileSystem();
            await Assert.ThrowsAsync<FileNotFoundException>(async() => await mountfs.GetAttributes("/toto.txt"));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.SetAttributes("/toto.txt", FileAttributes.Normal));
        }

        [Fact]
        public async Task TimesFail()
        {
            var mountfs = new MountFileSystem();
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.SetCreationTime("/toto.txt", DateTime.Now));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.SetLastAccessTime("/toto.txt", DateTime.Now));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await mountfs.SetLastWriteTime("/toto.txt", DateTime.Now));
        }

        [Fact]
        public async Task EnumerateFail()
        {
            var mountfs = new MountFileSystem();
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => (await mountfs.EnumeratePaths("/dir")).ToList());
        }

        [Fact]
        public async Task CopyAndMoveFileCross()
        {
            var mountfs = new MountFileSystem();
            var memfs1 = new MemoryFileSystem();
            await memfs1.WriteAllText("/file1.txt", "content1");
            var memfs2 = new MemoryFileSystem();

            mountfs.Mount("/dir1", memfs1);
            mountfs.Mount("/dir2", memfs2);

            await mountfs.CopyFile("/dir1/file1.txt", "/dir2/file2.txt", true);

            Assert.True(await memfs2.FileExists("/file2.txt"));
            Assert.Equal("content1", await memfs2.ReadAllText("/file2.txt"));

            await mountfs.MoveFile("/dir1/file1.txt", "/dir2/file1.txt");

            Assert.False(await memfs1.FileExists("/file1.txt"));
            Assert.True(await memfs2.FileExists("/file1.txt"));
            Assert.Equal("content1", await memfs2.ReadAllText("/file1.txt"));
        }
    }
}