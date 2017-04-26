// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMountFileSystemCompatSub : TestMemoryOrPhysicalFileSystemBase
    {
        public TestMountFileSystemCompatSub()
        {
            // Check that MountFileSystem is working with a mount with the compat test
            var mountfs = new MountFileSystem();
            mountfs.Mount("/customMount", new MemoryFileSystem());

            // Use a SubFileSystem to fake the mount to a root folder
            fs = new SubFileSystem(mountfs, "/customMount");
        }
    }
}