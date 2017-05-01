// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Zio.Tests.FileSystems
{
    public class TestFileSystemEntries : TestFileSystemCompactBase
    {
        public TestFileSystemEntries()
        {
            fs = new FileSystemEntryRedirect();
        }

        [Fact]
        public void TestRead()
        {
            var memfs = GetCommonMemoryFileSystem();
            var fsEntries = new FileSystemEntryRedirect(memfs);
            AssertCommonRead(fsEntries);            
        }

        [Fact]
        public void TestFileEntry()
        {
            var memfs = GetCommonMemoryFileSystem();

            var file = new FileEntry(memfs, "/a/a/a.txt");
            var file2 = new FileEntry(memfs, "/a/b.txt");

            Assert.Equal("/a/a/a.txt", file.ToString());

            Assert.Equal("/a/a/a.txt", file.FullName);
            Assert.Equal("a.txt", file.Name);

            Assert.Equal("b", file2.NameWithoutExtension);
            Assert.Equal(".txt", file2.ExtensionWithDot);

            Assert.True(file.Length > 0);
            Assert.False(file.IsReadOnly);

            var dir = file.Directory;
            Assert.NotNull(dir);
            Assert.Equal("/a/a", dir.FullName);

            Assert.Null(new DirectoryEntry(memfs, "/").Parent);


            using (var file1 = new FileEntry(memfs, "/a/yoyo.txt").Create())
            {
                file1.WriteByte(1);
                file1.WriteByte(2);
                file1.WriteByte(3);
            }

            Assert.Equal(new byte[] {1, 2, 3}, memfs.ReadAllBytes("/a/yoyo.txt"));

            Assert.Throws<FileNotFoundException>(() => memfs.GetFileSystemEntry("/wow.txt"));

            var file3 = memfs.GetFileEntry("/a/b.txt");
            Assert.True(file3.Exists);

            Assert.Null(memfs.TryGetFileSystemEntry("/invalid_file"));
            Assert.IsType<FileEntry>(memfs.TryGetFileSystemEntry("/a/b.txt"));
            Assert.IsType<DirectoryEntry>(memfs.TryGetFileSystemEntry("/a"));

            Assert.Throws<FileNotFoundException>(() => memfs.GetFileEntry("/invalid"));
            Assert.Throws<DirectoryNotFoundException>(() => memfs.GetDirectoryEntry("/invalid"));


            var mydir = new DirectoryEntry(memfs, "/yoyo");

            Assert.Throws<ArgumentException>(() => mydir.CreateSubdirectory("/sub"));

            var subFolder = mydir.CreateSubdirectory("sub");
            Assert.True(subFolder.Exists);

            Assert.Equal(0, mydir.EnumerateFiles().ToList().Count);

            var subDirs = mydir.EnumerateDirectories().ToList();
            Assert.Equal(1, subDirs.Count);
            Assert.Equal("/yoyo/sub", subDirs[0].FullName);

            mydir.Delete();

            Assert.False(mydir.Exists);
            Assert.False(subFolder.Exists);
        }
    }
}