// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Linq;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestAggregateFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestCommonReadOnly()
        {
            var fs = GetCommonAggregateFileSystem();
            AssertCommonReadOnly(fs);
        }

        [Fact]
        public void TestAddRemoveFileSystem()
        {
            var fs = new AggregateFileSystem();

            Assert.Throws<ArgumentNullException>(() => fs.AddFileSystem(null));
            Assert.Throws<ArgumentException>(() => fs.AddFileSystem(fs));

            var memfs = new MemoryFileSystem();
            fs.AddFileSystem(memfs);
            Assert.Throws<ArgumentException>(() => fs.AddFileSystem(memfs));

            Assert.Throws<ArgumentNullException>(() => fs.RemoveFileSystem(null));

            var memfs2 = new MemoryFileSystem();
            Assert.Throws<ArgumentException>(() => fs.RemoveFileSystem(memfs2));

            var list = fs.GetFileSystems();
            Assert.Equal(1, list.Count);
            Assert.Equal(memfs, list[0]);

            fs.RemoveFileSystem(memfs);

            list = fs.GetFileSystems();
            Assert.Equal(0, list.Count);
        }
    }
}