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

            // Test MoveDirectory
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
    }
}