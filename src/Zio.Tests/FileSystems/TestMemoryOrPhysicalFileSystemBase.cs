// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Zio;

namespace Zio.Tests.FileSystems
{
    public abstract class TestMemoryOrPhysicalFileSystemBase : TestFileSystemBase, IDisposable
    {
        protected IFileSystem fs;

        protected TestMemoryOrPhysicalFileSystemBase()
        {
        }

        [Fact]
        public void TestDirectory()
        {
            Assert.True((bool) fs.DirectoryExists("/"));

            // Test CreateDirectory
            fs.CreateDirectory("/test");
            Assert.True((bool) fs.DirectoryExists("/test"));
            Assert.False((bool) fs.DirectoryExists("/test2"));

            // Test CreateDirectory (sub folders)
            fs.CreateDirectory("/test/test1/test2/test3");
            Assert.True((bool) fs.DirectoryExists("/test/test1/test2/test3"));
            Assert.True((bool) fs.DirectoryExists("/test/test1/test2"));
            Assert.True((bool) fs.DirectoryExists("/test/test1"));
            Assert.True((bool) fs.DirectoryExists("/test"));

            // Test DeleteDirectory
            fs.DeleteDirectory("/test/test1/test2/test3", false);
            Assert.False((bool) fs.DirectoryExists("/test/test1/test2/test3"));
            Assert.True((bool) fs.DirectoryExists("/test/test1/test2"));
            Assert.True((bool) fs.DirectoryExists("/test/test1"));
            Assert.True((bool) fs.DirectoryExists("/test"));

            // Test MoveDirectory
            fs.MoveDirectory("/test", "/test2");
            Assert.True((bool) fs.DirectoryExists("/test2/test1/test2"));
            Assert.True((bool) fs.DirectoryExists("/test2/test1"));
            Assert.True((bool) fs.DirectoryExists("/test2"));

            // Test MoveDirectory to sub directory
            fs.CreateDirectory("/testsub");
            Assert.True((bool) fs.DirectoryExists("/testsub"));
            fs.MoveDirectory("/test2", "/testsub/testx");
            Assert.False((bool) fs.DirectoryExists("/test2"));
            Assert.True((bool) fs.DirectoryExists("/testsub/testx/test1/test2"));
            Assert.True((bool) fs.DirectoryExists("/testsub/testx/test1"));
            Assert.True((bool) fs.DirectoryExists("/testsub/testx"));

            // Test DeleteDirectory - recursive
            fs.DeleteDirectory("/testsub", true);
            Assert.False((bool) fs.DirectoryExists("/testsub/testx/test1/test2"));
            Assert.False((bool) fs.DirectoryExists("/testsub/testx/test1"));
            Assert.False((bool) fs.DirectoryExists("/testsub/testx"));
            Assert.False((bool) fs.DirectoryExists("/testsub"));
        }

        [Fact]
        public void TestDirectoryExceptions()
        {
            Assert.Throws<DirectoryNotFoundException>(() => fs.DeleteDirectory("/dir", true));

            Assert.Throws<DirectoryNotFoundException>(() => fs.MoveDirectory("/dir1", "/dir2"));

            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/"));

            fs.CreateDirectory("/dir1");
            Assert.Throws<IOException>(() => fs.DeleteFile("/dir1"));

            FileSystemExtensions.WriteAllText(fs, "/toto.txt", "test");
            Assert.Throws<IOException>(() => fs.CreateDirectory("/toto.txt"));
            Assert.Throws<IOException>(() => fs.DeleteDirectory("/toto.txt", true));
            Assert.Throws<IOException>(() => fs.MoveDirectory("/toto.txt", "/test"));

            fs.CreateDirectory("/dir2");
            Assert.Throws<IOException>(() => fs.MoveDirectory("/dir1", "/dir2"));

            fs.SetAttributes("/dir1", FileAttributes.Directory|FileAttributes.ReadOnly);
            Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir1", true));
        }

        [Fact]
        public void TestFile()
        {
            // Test CreateFile/OpenFile
            var stream = FileSystemExtensions.CreateFile(fs, "/toto.txt");
            var writer = new StreamWriter(stream);
            var originalContent = "This is the content";
            writer.Write(originalContent);
            writer.Flush();
            stream.Dispose();

            // Test FileExists
            Assert.False((bool) fs.FileExists("/titi.txt"));
            Assert.True((bool) fs.FileExists("/toto.txt"));

            // ReadAllText
            var content = FileSystemExtensions.ReadAllText(fs, "/toto.txt");
            Assert.Equal(originalContent, content);

            // Test CopyFile
            fs.CopyFile("/toto.txt", "/titi.txt", true);
            Assert.True((bool) fs.FileExists("/toto.txt"));
            Assert.True((bool) fs.FileExists("/titi.txt"));
            content = FileSystemExtensions.ReadAllText(fs, "/titi.txt");
            Assert.Equal(originalContent, content);

            // Test Attributes/Times
            Assert.True(fs.GetFileLength("/toto.txt") > 0);
            Assert.Equal(fs.GetFileLength("/toto.txt"), fs.GetFileLength("/titi.txt"));
            Assert.Equal(fs.GetAttributes("/toto.txt"), fs.GetAttributes("/titi.txt"));
            Assert.Equal(fs.GetCreationTime("/toto.txt"), fs.GetCreationTime("/titi.txt"));
            // Because we read titi.txt just before, access time must be different
            Assert.NotEqual(fs.GetLastAccessTime("/toto.txt"), fs.GetLastAccessTime("/titi.txt"));
            Assert.Equal(fs.GetLastWriteTime("/toto.txt"), fs.GetLastWriteTime("/titi.txt"));

            var now = DateTime.Now + TimeSpan.FromSeconds(10);
            var now1 = DateTime.Now + TimeSpan.FromSeconds(11);
            var now2 = DateTime.Now + TimeSpan.FromSeconds(12);
            fs.SetCreationTime("/toto.txt", now);
            fs.SetLastAccessTime("/toto.txt", now1);
            fs.SetLastWriteTime("/toto.txt", now2);
            Assert.Equal(now, fs.GetCreationTime("/toto.txt"));
            Assert.Equal(now1, fs.GetLastAccessTime("/toto.txt"));
            Assert.Equal(now2, fs.GetLastWriteTime("/toto.txt"));

            Assert.NotEqual(fs.GetCreationTime("/toto.txt"), fs.GetCreationTime("/titi.txt"));
            Assert.NotEqual(fs.GetLastAccessTime("/toto.txt"), fs.GetLastAccessTime("/titi.txt"));
            Assert.NotEqual(fs.GetLastWriteTime("/toto.txt"), fs.GetLastWriteTime("/titi.txt"));

            // Test MoveFile
            fs.MoveFile("/toto.txt", "/tata.txt");
            Assert.False((bool) fs.FileExists("/toto.txt"));
            Assert.True((bool) fs.FileExists("/tata.txt"));
            Assert.True((bool) fs.FileExists("/titi.txt"));
            content = FileSystemExtensions.ReadAllText(fs, "/tata.txt");
            Assert.Equal(originalContent, content);

            // Test Enumerate file
            var files = FileSystemExtensions.EnumerateFiles(fs, "/").Select(p => p.FullName).ToList();
            files.Sort();
            Assert.Equal(new List<string>() { "/tata.txt", "/titi.txt" }, files);

            var dirs = FileSystemExtensions.EnumerateDirectories(fs, "/").Select(p => p.FullName).ToList();
            Assert.Equal(0, dirs.Count);

            // Check ReplaceFile
            var originalContent2 = "this is a content2";
            FileSystemExtensions.WriteAllText(fs, "/tata.txt", originalContent2);
            fs.ReplaceFile("/tata.txt", "/titi.txt", "/titi.bak.txt", true);
            Assert.False((bool) fs.FileExists("/tata.txt"));
            Assert.True((bool) fs.FileExists("/titi.txt"));
            Assert.True((bool) fs.FileExists("/titi.bak.txt"));
            content = FileSystemExtensions.ReadAllText(fs, "/titi.txt");
            Assert.Equal(originalContent2, content);
            content = FileSystemExtensions.ReadAllText(fs, "/titi.bak.txt");
            Assert.Equal(originalContent, content);

            // SetAttributes
            fs.SetAttributes("/titi.txt", FileAttributes.ReadOnly);
            Assert.Throws<IOException>(() => fs.DeleteFile("/titi.txt"));
            fs.SetAttributes("/titi.txt", FileAttributes.Normal);

            // Delete File
            fs.DeleteFile("/titi.txt");
            Assert.False((bool) fs.FileExists("/titi.txt"));
            fs.DeleteFile("/titi.bak.txt");
            Assert.False((bool) fs.FileExists("/titi.bak.txt"));
        }

        [Fact]
        public void TestMoveFileDifferentDirectory()
        {
            FileSystemExtensions.WriteAllText(fs, "/toto.txt", "content");

            fs.CreateDirectory("/dir");

            fs.MoveFile("/toto.txt", "/dir/titi.txt");

            Assert.False((bool) fs.FileExists("/toto.txt"));
            Assert.True((bool) fs.FileExists("/dir/titi.txt"));

            Assert.Equal("content", FileSystemExtensions.ReadAllText(fs, "/dir/titi.txt"));
        }

        [Fact]
        public void TestReplaceFileDifferentDirectory()
        {
            FileSystemExtensions.WriteAllText(fs, "/toto.txt", "content");

            fs.CreateDirectory("/dir");
            FileSystemExtensions.WriteAllText(fs, "/dir/tata.txt", "content2");

            fs.CreateDirectory("/dir2");

            fs.ReplaceFile("/toto.txt", "/dir/tata.txt", "/dir2/titi.txt", true);
            Assert.True((bool) fs.FileExists("/dir/tata.txt"));
            Assert.True((bool) fs.FileExists("/dir2/titi.txt"));

            Assert.Equal("content", FileSystemExtensions.ReadAllText(fs, "/dir/tata.txt"));
            Assert.Equal("content2", FileSystemExtensions.ReadAllText(fs, "/dir2/titi.txt"));

            fs.ReplaceFile("/dir/tata.txt", "/dir2/titi.txt", "/titi.txt", true);
            Assert.False((bool) fs.FileExists("/dir/tata.txt"));
            Assert.True((bool) fs.FileExists("/dir2/titi.txt"));
            Assert.True((bool) fs.FileExists("/titi.txt"));
        }

        [Fact]
        public void TestOpenFileAppend()
        {
            FileSystemExtensions.AppendAllText(fs, "/toto.txt", "content");
            Assert.True((bool) fs.FileExists("/toto.txt"));
            Assert.Equal("content", FileSystemExtensions.ReadAllText(fs, "/toto.txt"));

            FileSystemExtensions.AppendAllText(fs, "/toto.txt", "content");
            Assert.True((bool) fs.FileExists("/toto.txt"));
            Assert.Equal("contentcontent", FileSystemExtensions.ReadAllText(fs, "/toto.txt"));
        }

        [Fact]
        public void TestOpenFileTruncate()
        {
            FileSystemExtensions.WriteAllText(fs, "/toto.txt", "content");
            Assert.True((bool) fs.FileExists("/toto.txt"));
            Assert.Equal("content", FileSystemExtensions.ReadAllText(fs, "/toto.txt"));

            var stream = fs.OpenFile("/toto.txt", FileMode.Truncate, FileAccess.Write);
            stream.Dispose();
            Assert.Equal<long>(0, fs.GetFileLength("/toto.txt"));
            Assert.Equal("", FileSystemExtensions.ReadAllText(fs, "/toto.txt"));
        }

        [Fact]
        public void TestFileExceptions()
        {
            fs.CreateDirectory("/dir1");

            Assert.Throws<FileNotFoundException>(() => fs.GetFileLength("/toto.txt"));
            Assert.Throws<FileNotFoundException>(() => fs.CopyFile("/toto.txt", "/toto.bak.txt", true));
            Assert.Throws<ArgumentException>(() => fs.CopyFile("/dir1", "/toto.bak.txt", true));
            Assert.Throws<FileNotFoundException>(() => fs.MoveFile("/toto.txt", "/titi.txt"));
            Assert.Throws<FileNotFoundException>(() => fs.DeleteFile("/toto.txt"));
            Assert.Throws<FileNotFoundException>(() => fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read));
            Assert.Throws<FileNotFoundException>(() => fs.OpenFile("/toto.txt", FileMode.Truncate, FileAccess.Write));

            Assert.Throws<FileNotFoundException>(() => fs.GetFileLength("/dir1/toto.txt"));
            Assert.Throws<FileNotFoundException>(() => fs.CopyFile("/dir1/toto.txt", "/toto.bak.txt", true));
            Assert.Throws<FileNotFoundException>(() => fs.MoveFile("/dir1/toto.txt", "/titi.txt"));
            Assert.Throws<FileNotFoundException>(() => fs.DeleteFile("/dir1/toto.txt"));
            Assert.Throws<FileNotFoundException>(() => fs.OpenFile("/dir1/toto.txt", FileMode.Open, FileAccess.Read));

            FileSystemExtensions.WriteAllText(fs, "/toto.txt", "yo");
            fs.CopyFile("/toto.txt", "/titi.txt", false);
            fs.CopyFile("/toto.txt", "/titi.txt", true);

            Assert.Throws<IOException>(() => fs.GetFileLength("/dir1"));
            Assert.Throws<IOException>(() => fs.GetLastAccessTime("/dest"));

            Assert.Throws<NotSupportedException>(() => fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            Assert.Throws<IOException>(() => fs.OpenFile("/dir1", FileMode.Open, FileAccess.Read));
            Assert.Throws<DirectoryNotFoundException>(() => fs.OpenFile("/dir/toto.txt", FileMode.Open, FileAccess.Read));
            Assert.Throws<DirectoryNotFoundException>(() => fs.CopyFile("/toto.txt", "/dest/toto.txt", true));
            Assert.Throws<IOException>(() => fs.CopyFile("/toto.txt", "/titi.txt", false));
            Assert.Throws<ArgumentException>(() => fs.CopyFile("/toto.txt", "/dir1", true));
            Assert.Throws<DirectoryNotFoundException>(() => fs.MoveFile("/toto.txt", "/dest/toto.txt"));

            FileSystemExtensions.WriteAllText(fs, "/titi.txt", "yo2");
            Assert.Throws<IOException>(() => fs.MoveFile("/toto.txt", "/titi.txt"));

            Assert.Throws<IOException>(() => fs.ReplaceFile("/1.txt", "/1.txt", default(PathInfo), true));
            Assert.Throws<IOException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/1.txt", true));
            Assert.Throws<IOException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/2.txt", true));
            Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/3.txt", true));
            Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/dir/2.txt", "/3.txt", true));
            Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/2.txt", "/3.txt", true));

            Assert.Throws<DirectoryNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/titi.txt", "/dir/3.txt", true));

            FileSystemExtensions.WriteAllText(fs, "/tata.txt", "yo3");
            Assert.True((bool) fs.FileExists("/tata.txt"));
            fs.ReplaceFile("/toto.txt", "/titi.txt", "/tata.txt", true);
            // TODO: check that tata.txt was correctly removed
        }

        [Fact]
        public void TestDirectoryDeleteAndOpenFile()
        {
            fs.CreateDirectory("/dir");
            FileSystemExtensions.WriteAllText(fs, "/dir/toto.txt", "content");
            var stream = fs.OpenFile("/dir/toto.txt", FileMode.Open, FileAccess.Read);

            Assert.Throws<IOException>(() => fs.DeleteFile("/dir/toto.txt"));
            Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir", true));

            stream.Dispose();
            fs.SetAttributes("/dir/toto.txt", FileAttributes.ReadOnly);
            Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir", true));
            fs.SetAttributes("/dir/toto.txt", FileAttributes.Normal);
            fs.DeleteDirectory("/dir", true);

            var entries = FileSystemExtensions.EnumeratePaths(fs, "/").ToList();
            Assert.Equal(0, entries.Count);
        }

        [Fact]
        public void TestOpenFileMultipleRead()
        {
            FileSystemExtensions.WriteAllText(fs, "/toto.txt", "content");

            Assert.True((bool) fs.FileExists("/toto.txt"));

            var stream1 = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read);
            var stream2 = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read);

            stream1.ReadByte();
            Assert.Equal<long>(1, stream1.Position);

            stream2.ReadByte();
            stream2.ReadByte();
            Assert.Equal<long>(2, stream2.Position);

            stream1.Dispose();
            stream2.Dispose();

            // We try to write back on the same file after closing
            FileSystemExtensions.WriteAllText(fs, "/toto.txt", "content2");
        }

        [Fact]
        public void TestOpenFileReadAndWriteFail()
        {
            FileSystemExtensions.WriteAllText(fs, "/toto.txt", "content");

            Assert.True((bool) fs.FileExists("/toto.txt"));

            var stream1 = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read);

            stream1.ReadByte();
            Assert.Equal<long>(1, stream1.Position);

            // We try to write back on the same file after closing
            Assert.Throws<IOException>(() => FileSystemExtensions.WriteAllText(fs, "/toto.txt", "content2"));

            stream1.Dispose();
        }

        [Fact]
        public void TestEnumeratePaths()
        {
            fs.CreateDirectory("/dir1/a/b");
            fs.CreateDirectory("/dir1/a1");
            fs.CreateDirectory("/dir2/c");
            fs.CreateDirectory("/dir3");

            FileSystemExtensions.WriteAllText(fs, "/dir1/a/file10.txt", "content10");
            FileSystemExtensions.WriteAllText(fs, "/dir1/a1/file11.txt", "content11");
            FileSystemExtensions.WriteAllText(fs, "/dir2/file20.txt", "content20");

            FileSystemExtensions.WriteAllText(fs, "/file01.txt", "content1");
            FileSystemExtensions.WriteAllText(fs, "/file02.txt", "content2");

            var entries = Enumerable.ToList<PathInfo>(fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Both));
            entries.Sort();

            Assert.Equal(new List<PathInfo>()
                {
                    "/dir1",
                    "/dir1/a",
                    "/dir1/a/b",
                    "/dir1/a/file10.txt",
                    "/dir1/a1",
                    "/dir1/a1/file11.txt",
                    "/dir2",
                    "/dir2/c",
                    "/dir2/file20.txt",
                    "/dir3",
                    "/file01.txt",
                    "/file02.txt",
                }
                , entries);


            var folders = Enumerable.ToList<PathInfo>(fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Directory));
            folders.Sort();

            Assert.Equal(new List<PathInfo>()
                {
                    "/dir1",
                    "/dir1/a",
                    "/dir1/a/b",
                    "/dir1/a1",
                    "/dir2",
                    "/dir2/c",
                    "/dir3",
                }
                , folders);


            var files = Enumerable.ToList<PathInfo>(fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.File));
            files.Sort();

            Assert.Equal(new List<PathInfo>()
                {
                    "/dir1/a/file10.txt",
                    "/dir1/a1/file11.txt",
                    "/dir2/file20.txt",
                    "/file01.txt",
                    "/file02.txt",
                }
                , files);
            

            folders = Enumerable.ToList<PathInfo>(fs.EnumeratePaths("/dir1", "a", SearchOption.AllDirectories, SearchTarget.Directory));
            folders.Sort();
            Assert.Equal(new List<PathInfo>()
                {
                    "/dir1/a",
                }
                , folders);


            files = Enumerable.ToList<PathInfo>(fs.EnumeratePaths("/dir1", "file1?.txt", SearchOption.AllDirectories, SearchTarget.File));
            files.Sort();

            Assert.Equal(new List<PathInfo>()
                {
                    "/dir1/a/file10.txt",
                    "/dir1/a1/file11.txt",
                }
                , files);

            files = Enumerable.ToList<PathInfo>(fs.EnumeratePaths("/", "file?0.txt", SearchOption.AllDirectories, SearchTarget.File));
            files.Sort();

            Assert.Equal(new List<PathInfo>()
                {
                    "/dir1/a/file10.txt",
                    "/dir2/file20.txt",
                }
                , files);
        }

        public virtual void Dispose()
        {
        }
    }
}