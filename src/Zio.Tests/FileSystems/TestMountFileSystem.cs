// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMountFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestCommonReadWithOnlyBackup()
        {
            var fs = GetCommonMountFileSystemWithOnlyBackup();
            AssertCommonRead(fs);
        }

        [Fact]
        public void TestCommonReadWithMounts()
        {
            var fs = GetCommonMountFileSystemWithMounts();
            AssertCommonRead(fs);
        }
        
        [Fact]
        public void TestWatcherOnRoot()
        {
            var fs = GetCommonMountFileSystemWithMounts();
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

            fs.WriteAllText("/b/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public void TestWatcherOnMount()
        {
            var fs = GetCommonMountFileSystemWithMounts();
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

            fs.WriteAllText("/b/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public void TestWatcherWithBackupOnRoot()
        {
            var fs = GetCommonMountFileSystemWithOnlyBackup();
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

            fs.WriteAllText("/b/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public void TestMount()
        {
            var fs = new MountFileSystem();
            var memfs = new MemoryFileSystem();

            Assert.Throws<ArgumentNullException>(() => fs.Mount(null, memfs));
            Assert.Throws<ArgumentNullException>(() => fs.Mount("/test", null));
            Assert.Throws<ArgumentException>(() => fs.Mount("test", memfs));
            Assert.Throws<ArgumentException>(() => fs.Mount("/", memfs));
            Assert.Throws<ArgumentException>(() => fs.Mount("/test/a", memfs));
            Assert.Throws<ArgumentException>(() => fs.Mount("/test/a/b", memfs));

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

            Assert.Equal(0, fs.GetMounts().Count);
        }


        [Fact]
        public void CreateDirectoryFail()
        {
            var mountfs = new MountFileSystem();
            Assert.Throws<UnauthorizedAccessException>(() => mountfs.CreateDirectory("/test"));
        }

        [Fact]
        public void MoveDirectoryFail()
        {
            var mountfs = new MountFileSystem();
            mountfs.Mount("/dir1", new MemoryFileSystem());
            mountfs.Mount("/dir2", new MemoryFileSystem());

            Assert.Throws<UnauthorizedAccessException>(() => mountfs.MoveDirectory("/dir1", "/dir2/yyy"));
            Assert.Throws<UnauthorizedAccessException>(() => mountfs.MoveDirectory("/dir1/xxx", "/dir2"));
            Assert.Throws<NotSupportedException>(() => mountfs.MoveDirectory("/dir1/xxx", "/dir2/yyy"));
        }

        [Fact]
        public void DeleteDirectoryFail()
        {
            var mountfs = new MountFileSystem();
            mountfs.Mount("/dir1", new MemoryFileSystem());

            Assert.Throws<UnauthorizedAccessException>(() => mountfs.DeleteDirectory("/dir1", true));
            Assert.Throws<UnauthorizedAccessException>(() => mountfs.DeleteDirectory("/dir1", false));
            Assert.Throws<DirectoryNotFoundException>(() => mountfs.DeleteDirectory("/dir2", false));
        }

        [Fact]
        public void CopyFileFail()
        {
            var mountfs = new MountFileSystem();
            mountfs.Mount("/dir1", new MemoryFileSystem());
            Assert.Throws<FileNotFoundException>(() => mountfs.CopyFile("/test", "/test2", true));
            Assert.Throws<DirectoryNotFoundException>(() => mountfs.CopyFile("/dir1/test.txt", "/test2", true));
        }

        [Fact]
        public void ReplaceFileFail()
        {
            var mountfs = new MountFileSystem();
            var memfs1 = new MemoryFileSystem();
            memfs1.WriteAllText("/file.txt", "content1");

            var memfs2 = new MemoryFileSystem();
            memfs2.WriteAllText("/file2.txt", "content1");

            mountfs.Mount("/dir1", memfs1);
            mountfs.Mount("/dir2", memfs2);
            Assert.Throws<FileNotFoundException>(() => mountfs.ReplaceFile("/dir1/file.txt", "/dir1/to.txt", "/dir1/to.bak", true));
            Assert.Throws<FileNotFoundException>(() => mountfs.ReplaceFile("/dir1/to.txt", "/dir1/file.txt", "/dir1/to.bak", true));
            Assert.Throws<NotSupportedException>(() => mountfs.ReplaceFile("/dir1/file.txt", "/dir2/file2.txt", null, true));
        }

        [Fact]
        public void GetFileLengthFail()
        {
            var mountfs = new MountFileSystem();
            Assert.Throws<FileNotFoundException>(() => mountfs.GetFileLength("/toto.txt"));
        }

        [Fact]
        public void MoveFileFail()
        {
            var mountfs = new MountFileSystem();
            var memfs1 = new MemoryFileSystem();
            memfs1.WriteAllText("/file.txt", "content1");

            var memfs2 = new MemoryFileSystem();
            memfs2.WriteAllText("/file2.txt", "content1");

            mountfs.Mount("/dir1", memfs1);
            mountfs.Mount("/dir2", memfs2);
            Assert.Throws<DirectoryNotFoundException>(() => mountfs.MoveFile("/dir1/file.txt", "/xxx/yyy.txt"));
            Assert.Throws<FileNotFoundException>(() => mountfs.MoveFile("/dir1/xxx", "/dir1/file1.txt"));
            Assert.Throws<FileNotFoundException>(() => mountfs.MoveFile("/xxx", "/dir1/file1.txt"));
        }


        [Fact]
        public void OpenFileFail()
        {
            var mountfs = new MountFileSystem();
            Assert.Throws<FileNotFoundException>(() => mountfs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read));
            Assert.Throws<UnauthorizedAccessException>(() => mountfs.OpenFile("/toto.txt", FileMode.Create, FileAccess.Read));
        }

        [Fact]
        public void AttributesFail()
        {
            var mountfs = new MountFileSystem();
            Assert.Throws<FileNotFoundException>(() => mountfs.GetAttributes("/toto.txt"));
            Assert.Throws<FileNotFoundException>(() => mountfs.SetAttributes("/toto.txt", FileAttributes.Normal));
        }

        [Fact]
        public void TimesFail()
        {
            var mountfs = new MountFileSystem();
            Assert.Throws<FileNotFoundException>(() => mountfs.SetCreationTime("/toto.txt", DateTime.Now));
            Assert.Throws<FileNotFoundException>(() => mountfs.SetLastAccessTime("/toto.txt", DateTime.Now));
            Assert.Throws<FileNotFoundException>(() => mountfs.SetLastWriteTime("/toto.txt", DateTime.Now));
        }

        [Fact]
        public void EnumerateFail()
        {
            var mountfs = new MountFileSystem();
            Assert.Throws<DirectoryNotFoundException>(() => mountfs.EnumeratePaths("/dir").ToList());
        }

        [Fact]
        public void CopyAndMoveFileCross()
        {
            var mountfs = new MountFileSystem();
            var memfs1 = new MemoryFileSystem();
            memfs1.WriteAllText("/file1.txt", "content1");
            var memfs2 = new MemoryFileSystem();

            mountfs.Mount("/dir1", memfs1);
            mountfs.Mount("/dir2", memfs2);

            mountfs.CopyFile("/dir1/file1.txt", "/dir2/file2.txt", true);

            Assert.True(memfs2.FileExists("/file2.txt"));
            Assert.Equal("content1", memfs2.ReadAllText("/file2.txt"));

            mountfs.MoveFile("/dir1/file1.txt", "/dir2/file1.txt");

            Assert.False(memfs1.FileExists("/file1.txt"));
            Assert.True(memfs2.FileExists("/file1.txt"));
            Assert.Equal("content1", memfs2.ReadAllText("/file1.txt"));
        }
    }
}