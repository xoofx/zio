// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMemoryFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestCommonRead()
        {
            var fs = GetCommonMemoryFileSystem();
            AssertCommonRead(fs);
        }

        [Fact]
        public void TestDispose()
        {
            var memfs = new MemoryFileSystem();

            memfs.Dispose();
            Assert.Throws<ObjectDisposedException>(() => memfs.DirectoryExists("/"));
        }
    }
}