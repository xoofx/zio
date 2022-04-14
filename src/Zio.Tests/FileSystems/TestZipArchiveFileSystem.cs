// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{

    public class TestZipArchiveFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestCommonRead()
        {
            var fs = this.GetCommonZipArchiveFileSystem();
            this.AssertCommonRead(fs);
        }

        [Fact]
        public void TestCopyFileSystem()
        {
            var fs = this.GetCommonZipArchiveFileSystem();

            var dest = new ZipArchiveFileSystem(new MemoryStream());
            fs.CopyTo(dest, UPath.Root, true);

            this.AssertFileSystemEqual(fs, dest);
        }
        
        [Fact]
        public void TestCopyFileSystemSubFolder()
        {
            var fs = GetCommonZipArchiveFileSystem();

            var dest = new ZipArchiveFileSystem(new MemoryStream());
            var subFolder = UPath.Root / "subfolder";
            fs.CopyTo(dest, subFolder, true);

            var destSubFileSystem = dest.GetOrCreateSubFileSystem(subFolder);

            AssertFileSystemEqual(fs, destSubFileSystem);
        }
        
        [Fact]
        public void TestWatcher()
        {
            var fs = this.GetCommonZipArchiveFileSystem();
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
            Thread.Sleep(100);
            Assert.True(gotChange);
        }
        
        [Fact]
        public void TestFileEntry()
        {
            var fs = GetCommonZipArchiveFileSystem();

            var file = new FileEntry(fs, "/a/a/a1.txt");
            var file2 = new FileEntry(fs, "/a/b.txt");

            Assert.Equal("/a/a/a1.txt", file.ToString());

            Assert.Equal("/a/a/a1.txt", file.FullName);
            Assert.Equal("a1.txt", file.Name);

            Assert.Equal("b", file2.NameWithoutExtension);
            Assert.Equal(".txt", file2.ExtensionWithDot);

            Assert.True(file.Length > 0);
            Assert.False(file.IsReadOnly);

            var dir = file.Directory;
            Assert.NotNull(dir);
            Assert.Equal("/a/a", dir.FullName);

            Assert.Null(new DirectoryEntry(fs, "/").Parent);

            var yoyo = new FileEntry(fs, "/a/yoyo.txt");
            using (var file1 = yoyo.Create())
            {
                file1.WriteByte(1);
                file1.WriteByte(2);
                file1.WriteByte(3);
            }

            Assert.Equal(new byte[] { 1, 2, 3 }, fs.ReadAllBytes("/a/yoyo.txt"));

            Assert.Throws<FileNotFoundException>(() => fs.GetFileSystemEntry("/wow.txt"));

            var file3 = fs.GetFileEntry("/a/b.txt");
            Assert.True(file3.Exists);

            Assert.Null(fs.TryGetFileSystemEntry("/invalid_file"));
            Assert.IsType<FileEntry>(fs.TryGetFileSystemEntry("/a/b.txt"));
            Assert.IsType<DirectoryEntry>(fs.TryGetFileSystemEntry("/a"));

            Assert.Throws<FileNotFoundException>(() => fs.GetFileEntry("/invalid"));
            Assert.Throws<DirectoryNotFoundException>(() => fs.GetDirectoryEntry("/invalid"));


            var mydir = new DirectoryEntry(fs, "/yoyo");

            Assert.Throws<ArgumentException>(() => mydir.CreateSubdirectory("/sub"));

            var subFolder = mydir.CreateSubdirectory("sub");
            Assert.True(subFolder.Exists);

            Assert.Empty(mydir.EnumerateFiles());

            var subDirs = mydir.EnumerateDirectories();
            Assert.Single(subDirs);
            Assert.Equal("/yoyo/sub", subDirs.First().FullName);

            mydir.Delete();

            Assert.False(mydir.Exists);
            Assert.False(subFolder.Exists);

            // Test ReadAllText/WriteAllText/AppendAllText/ReadAllBytes/WriteAllBytes
            Assert.True(file.ReadAllText().Length > 0);
            Assert.True(file.ReadAllText(Encoding.UTF8).Length > 0);
            file.WriteAllText("abc");
            Assert.Equal("abc", file.ReadAllText());
            file.WriteAllText("abc", Encoding.UTF8);
            Assert.Equal("abc", file.ReadAllText(Encoding.UTF8));

            file.AppendAllText("def");
            Assert.Equal("abcdef", file.ReadAllText());
            file.AppendAllText("ghi", Encoding.UTF8);
            Assert.Equal("abcdefghi", file.ReadAllText());

            var lines = file.ReadAllLines();
            Assert.Single(lines);
            Assert.Equal("abcdefghi", lines[0]);

            lines = file.ReadAllLines(Encoding.UTF8);
            Assert.Single(lines);
            Assert.Equal("abcdefghi", lines[0]);

            Assert.Equal(new byte[] { 1, 2, 3 }, yoyo.ReadAllBytes());
            yoyo.WriteAllBytes(new byte[] { 1, 2, 3, 4 });
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, yoyo.ReadAllBytes());
        }

        [Fact]
        public void TestGetParentDirectory()
        {
            var fs = new ZipArchiveFileSystem();
            var fileEntry = new FileEntry(fs, "/test/tata/titi.txt");
            // Shoud not throw an error
            var directory = fileEntry.Directory;
            Assert.False(directory.Exists);
            Assert.Equal(UPath.Root / "test/tata", directory.Path);
        }

        [Fact]
        public void TestReplaceFile()
        {
            var fs = GetCommonZipArchiveFileSystem();

            var file = new FileEntry(fs, "/a/b.txt");
            file.WriteAllText("abc");
            Assert.Equal("abc", file.ReadAllText());
            var file2 = new FileEntry(fs, "/a/copy.txt");
            file2.WriteAllText("def");
            Assert.Equal("def", file2.ReadAllText());

            fs.ReplaceFile(file.Path, new UPath("/a/copy.txt"), new UPath("/b/backup.txt"), true);
            Assert.False(file.Exists);

            var copy = fs.GetFileEntry("/a/copy.txt");
            Assert.True(copy.Exists);
            Assert.Equal("abc", copy.ReadAllText());
            Assert.Equal("def", fs.GetFileEntry("/b/backup.txt").ReadAllText());
        }
    }
}