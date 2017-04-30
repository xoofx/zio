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
    }
}