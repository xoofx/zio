// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
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
            Assert.Throws<ArgumentNullException>(() => fs.ReadAllText("/a.txt", null));
            Assert.Throws<ArgumentNullException>(() => fs.ReadAllLines("/a.txt", null));
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
    }
}