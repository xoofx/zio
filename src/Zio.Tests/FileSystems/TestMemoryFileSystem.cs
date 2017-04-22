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
    public class TestMemoryFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestDirectory()
        {
            // TODO: Split all these tests into separated methods
            var fs = new MemoryFileSystem();

            Assert.True(fs.DirectoryExists("/"));

            // Test CreateDirectory
            fs.CreateDirectory("/test");
            Assert.True(fs.DirectoryExists("/test"));
            Assert.False(fs.DirectoryExists("/test2"));

            // Test CreateDirectory (sub folders)
            fs.CreateDirectory("/test/test1/test2/test3");
            Assert.True(fs.DirectoryExists("/test/test1/test2/test3"));
            Assert.True(fs.DirectoryExists("/test/test1/test2"));
            Assert.True(fs.DirectoryExists("/test/test1"));
            Assert.True(fs.DirectoryExists("/test"));

            // Test DeleteDirectory
            fs.DeleteDirectory("/test/test1/test2/test3", false);
            Assert.False(fs.DirectoryExists("/test/test1/test2/test3"));
            Assert.True(fs.DirectoryExists("/test/test1/test2"));
            Assert.True(fs.DirectoryExists("/test/test1"));
            Assert.True(fs.DirectoryExists("/test"));

            // Test MoveDirectory
            fs.MoveDirectory("/test", "/test2");
            Assert.True(fs.DirectoryExists("/test2/test1/test2"));
            Assert.True(fs.DirectoryExists("/test2/test1"));
            Assert.True(fs.DirectoryExists("/test2"));

            // Test MoveDirectory to sub directory
            fs.CreateDirectory("/testsub");
            Assert.True(fs.DirectoryExists("/testsub"));
            fs.MoveDirectory("/test2", "/testsub/testx");
            Assert.False(fs.DirectoryExists("/test2"));
            Assert.True(fs.DirectoryExists("/testsub/testx/test1/test2"));
            Assert.True(fs.DirectoryExists("/testsub/testx/test1"));
            Assert.True(fs.DirectoryExists("/testsub/testx"));

            // Test DeleteDirectory - recursive
            fs.DeleteDirectory("/testsub", true);
            Assert.False(fs.DirectoryExists("/testsub/testx/test1/test2"));
            Assert.False(fs.DirectoryExists("/testsub/testx/test1"));
            Assert.False(fs.DirectoryExists("/testsub/testx"));
            Assert.False(fs.DirectoryExists("/testsub"));
        }

        [Fact]
        public void TestDirectoryExceptions()
        {
            var fs = new MemoryFileSystem();

            Assert.Throws<DirectoryNotFoundException>(() => fs.DeleteDirectory("/dir", true));

            Assert.Throws<DirectoryNotFoundException>(() => fs.MoveDirectory("/dir1", "/dir2"));

            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/"));

            fs.CreateDirectory("/dir1");
            Assert.Throws<IOException>(() => fs.DeleteFile("/dir1"));

            fs.WriteAllText("/toto.txt", "test");
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
            // TODO: Split all these tests into separated methods
            var fs = new MemoryFileSystem();

            // Test CreateFile/OpenFile
            var stream = fs.CreateFile("/toto.txt");
            var writer = new StreamWriter(stream);
            var originalContent = "This is the content";
            writer.Write(originalContent);
            writer.Flush();
            stream.Dispose();

            // Test FileExists
            Assert.False(fs.FileExists("/titi.txt"));
            Assert.True(fs.FileExists("/toto.txt"));

            // ReadAllText
            var content = fs.ReadAllText("/toto.txt");
            Assert.Equal(originalContent, content);

            // Test CopyFile
            fs.CopyFile("/toto.txt", "/titi.txt", true);
            Assert.True(fs.FileExists("/toto.txt"));
            Assert.True(fs.FileExists("/titi.txt"));
            content = fs.ReadAllText("/titi.txt");
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
            Assert.False(fs.FileExists("/toto.txt"));
            Assert.True(fs.FileExists("/tata.txt"));
            Assert.True(fs.FileExists("/titi.txt"));
            content = fs.ReadAllText("/tata.txt");
            Assert.Equal(originalContent, content);

            // Test Enumerate file
            var files = fs.EnumerateFiles("/").Select(p => p.FullName).ToList();
            files.Sort();
            Assert.Equal(new List<string>() { "/tata.txt", "/titi.txt" }, files);

            var dirs = fs.EnumerateDirectories("/").Select(p => p.FullName).ToList();
            Assert.Equal(0, dirs.Count);

            // Check ReplaceFile
            var originalContent2 = "this is a content2";
            fs.WriteAllText("/tata.txt", originalContent2);
            fs.ReplaceFile("/tata.txt", "/titi.txt", "/titi.bak.txt", true);
            Assert.False(fs.FileExists("/tata.txt"));
            Assert.True(fs.FileExists("/titi.txt"));
            Assert.True(fs.FileExists("/titi.bak.txt"));
            content = fs.ReadAllText("/titi.txt");
            Assert.Equal(originalContent2, content);
            content = fs.ReadAllText("/titi.bak.txt");
            Assert.Equal(originalContent, content);

            // SetAttributes
            fs.SetAttributes("/titi.txt", FileAttributes.ReadOnly);
            Assert.Throws<IOException>(() => fs.DeleteFile("/titi.txt"));
            fs.SetAttributes("/titi.txt", FileAttributes.Normal);

            // Delete File
            fs.DeleteFile("/titi.txt");
            Assert.False(fs.FileExists("/titi.txt"));
            fs.DeleteFile("/titi.bak.txt");
            Assert.False(fs.FileExists("/titi.bak.txt"));
        }

        [Fact]
        public void TestMoveFileDifferentDirectory()
        {
            var fs = new MemoryFileSystem();
            fs.WriteAllText("/toto.txt", "content");

            fs.CreateDirectory("/dir");

            fs.MoveFile("/toto.txt", "/dir/titi.txt");

            Assert.False(fs.FileExists("/toto.txt"));
            Assert.True(fs.FileExists("/dir/titi.txt"));

            Assert.Equal("content", fs.ReadAllText("/dir/titi.txt"));
        }

        [Fact]
        public void TestReplaceFileDifferentDirectory()
        {
            var fs = new MemoryFileSystem();
            fs.WriteAllText("/toto.txt", "content");

            fs.CreateDirectory("/dir");
            fs.WriteAllText("/dir/tata.txt", "content2");

            fs.CreateDirectory("/dir2");

            fs.ReplaceFile("/toto.txt", "/dir/tata.txt", "/dir2/titi.txt", true);
            Assert.True(fs.FileExists("/dir/tata.txt"));
            Assert.True(fs.FileExists("/dir2/titi.txt"));

            Assert.Equal("content", fs.ReadAllText("/dir/tata.txt"));
            Assert.Equal("content2", fs.ReadAllText("/dir2/titi.txt"));

            fs.ReplaceFile("/dir/tata.txt", "/dir2/titi.txt", "/titi.txt", true);
            Assert.False(fs.FileExists("/dir/tata.txt"));
            Assert.True(fs.FileExists("/dir2/titi.txt"));
            Assert.True(fs.FileExists("/titi.txt"));
        }

        [Fact]
        public void TestOpenFileAppend()
        {
            var fs = new MemoryFileSystem();

            fs.AppendAllText("/toto.txt", "content");
            Assert.True(fs.FileExists("/toto.txt"));
            Assert.Equal("content", fs.ReadAllText("/toto.txt"));

            fs.AppendAllText("/toto.txt", "content");
            Assert.True(fs.FileExists("/toto.txt"));
            Assert.Equal("contentcontent", fs.ReadAllText("/toto.txt"));
        }

        [Fact]
        public void TestOpenFileTruncate()
        {
            var fs = new MemoryFileSystem();

            fs.WriteAllText("/toto.txt", "content");
            Assert.True(fs.FileExists("/toto.txt"));
            Assert.Equal("content", fs.ReadAllText("/toto.txt"));

            var stream = fs.OpenFile("/toto.txt", FileMode.Truncate, FileAccess.Write);
            stream.Dispose();
            Assert.Equal(0, fs.GetFileLength("/toto.txt"));
            Assert.Equal("", fs.ReadAllText("/toto.txt"));
        }

        [Fact]
        public void TestFileExceptions()
        {
            var fs = new MemoryFileSystem();

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

            fs.WriteAllText("/toto.txt", "yo");
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

            fs.WriteAllText("/titi.txt", "yo2");
            Assert.Throws<IOException>(() => fs.MoveFile("/toto.txt", "/titi.txt"));

            Assert.Throws<IOException>(() => fs.ReplaceFile("/1.txt", "/1.txt", default(PathInfo), true));
            Assert.Throws<IOException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/1.txt", true));
            Assert.Throws<IOException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/2.txt", true));
            Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/3.txt", true));
            Assert.Throws<DirectoryNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/dir/2.txt", "/3.txt", true));
            Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/2.txt", "/3.txt", true));

            Assert.Throws<DirectoryNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/titi.txt", "/dir/3.txt", true));

            fs.WriteAllText("/tata.txt", "yo3");
            Assert.True(fs.FileExists("/tata.txt"));
            Assert.Throws<IOException>(() => fs.ReplaceFile("/toto.txt", "/titi.txt", "/tata.txt", true));
        }

        [Fact]
        public void TestDirectoryDeleteAndOpenFile()
        {
            var fs = new MemoryFileSystem();
            fs.CreateDirectory("/dir");
            fs.WriteAllText("/dir/toto.txt", "content");
            var stream = fs.OpenFile("/dir/toto.txt", FileMode.Open, FileAccess.Read);

            Assert.Throws<IOException>(() => fs.DeleteFile("/dir/toto.txt"));
            Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir", true));

            stream.Dispose();
            fs.SetAttributes("/dir/toto.txt", FileAttributes.ReadOnly);
            Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir", true));
            fs.SetAttributes("/dir/toto.txt", FileAttributes.Normal);
            fs.DeleteDirectory("/dir", true);

            var entries = fs.EnumeratePaths("/").ToList();
            Assert.Equal(0, entries.Count);
        }

        [Fact]
        public void Tester()
        {
            File.AppendAllText("toto.txt", "Yes");
            var attributes = File.GetAttributes("toto.txt");

            File.SetAttributes("toto.txt", attributes | FileAttributes.Normal);
            attributes = File.GetAttributes("toto.txt");
            File.SetAttributes("toto.txt", FileAttributes.Normal);
            attributes = File.GetAttributes("toto.txt");

        }
    }
}