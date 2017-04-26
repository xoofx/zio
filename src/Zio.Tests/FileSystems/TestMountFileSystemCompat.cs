// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMountFileSystemCompat : TestMemoryOrPhysicalFileSystemBase
    {
        public TestMountFileSystemCompat()
        {
            // Check that the MountFileSystem is working with only a plain backup MemoryFileSystem with the compat test
            fs = new MountFileSystem(new MemoryFileSystem());
        }
    }
}