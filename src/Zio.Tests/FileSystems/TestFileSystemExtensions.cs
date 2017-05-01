// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestFileSystemExtensions : TestFileSystemBase
    {
        [Fact]
        public void TestExceptions()
        {
            var fs = new MemoryFileSystem();

            Assert.Throws<ArgumentNullException>(() => fs.AppendAllText("/a.txt", null));
            Assert.Throws<ArgumentNullException>(() => fs.WriteAllText("/a.txt", null));
            Assert.Throws<ArgumentNullException>(() => fs.WriteAllText("/a.txt", "content", null));
            Assert.Throws<ArgumentNullException>(() => fs.WriteAllText("/a.txt", null, null));
            Assert.Throws<ArgumentNullException>(() => fs.ReadAllText("/a.txt", null));
            Assert.Throws<ArgumentNullException>(() => fs.ReadAllLines("/a.txt", null));
            Assert.Throws<ArgumentNullException>(() => fs.WriteAllBytes("/a", null));
            Assert.Throws<ArgumentNullException>(() => fs.AppendAllText("/a", null, null));
            Assert.Throws<ArgumentNullException>(() => fs.AppendAllText("/a", "content", null));
            Assert.Throws<ArgumentNullException>(() => fs.EnumeratePaths("*", null).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumeratePaths("*", null, SearchOption.AllDirectories).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateFiles("*", null).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateFiles("*", null, SearchOption.AllDirectories).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateDirectories("*", null).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateDirectories("*", null, SearchOption.AllDirectories).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateFileEntries("*", null).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateFileEntries("*", null, SearchOption.AllDirectories).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateDirectoryEntries("*", null).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateDirectoryEntries("*", null, SearchOption.AllDirectories).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateFileSystemEntries("*", null).First());
            Assert.Throws<ArgumentNullException>(() => fs.EnumerateFileSystemEntries("*", null, SearchOption.AllDirectories).First());
            Assert.Throws<FileNotFoundException>(() => fs.GetFileEntry("/a.txt"));
            Assert.Throws<DirectoryNotFoundException>(() => fs.GetDirectoryEntry("/a"));
        }

        [Fact]
        public void TestWriteReadAppendAllTextAndLines()
        {
            var fs = new MemoryFileSystem();
            fs.AppendAllText("/a.txt", "test");
            fs.AppendAllText("/a.txt", "test");
            Assert.Equal("testtest", fs.ReadAllText("/a.txt"));

            fs.WriteAllText("/a.txt", "content");
            Assert.Equal("content", fs.ReadAllText("/a.txt"));

            fs.WriteAllText("/a.txt", "test1", Encoding.UTF8);
            fs.AppendAllText("/a.txt", "test2", Encoding.UTF8);
            Assert.Equal("test1test2", fs.ReadAllText("/a.txt", Encoding.UTF8));

            Assert.Equal(new[] {"test1test2"}, fs.ReadAllLines("/a.txt"));
            Assert.Equal(new[] { "test1test2" }, fs.ReadAllLines("/a.txt", Encoding.UTF8));
        }

        [Fact]
        public void TestReadWriteAllBytes()
        {
            var fs = new MemoryFileSystem();

            fs.WriteAllBytes("/toto.txt", new byte[] {1,2,3});
            Assert.Equal(new byte[]{1,2,3}, fs.ReadAllBytes("/toto.txt"));

            fs.WriteAllBytes("/toto.txt", new byte[] { 5 });
            Assert.Equal(new byte[] { 5 }, fs.ReadAllBytes("/toto.txt"));

            fs.WriteAllBytes("/toto.txt", new byte[] { });
            Assert.Equal(new byte[] { }, fs.ReadAllBytes("/toto.txt"));
        }
    }
}