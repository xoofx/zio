// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

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
        }
    }
}