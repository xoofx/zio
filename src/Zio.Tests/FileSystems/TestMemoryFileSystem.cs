// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMemoryFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestDirectory()
        {
            var fs = new MemoryFileSystem();

            fs.CreateDirectory("/test");

            Assert.True(fs.DirectoryExists("/test"));

            fs.CreateDirectory("/test/test2");
            Assert.True(fs.DirectoryExists("/test/test2"));

            fs.DeleteDirectory("/test", true);
            Assert.False(fs.DirectoryExists("/test"));
        }
    }
}