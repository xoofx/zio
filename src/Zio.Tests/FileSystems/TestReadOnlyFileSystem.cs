// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestReadOnlyFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestApi()
        {
            var fs = new PhysicalFileSystem();
            var rofs = new ReadOnlyFileSystem(fs);

            Assert.True(rofs.DirectoryExists("/"));

            Assert.Throws<IOException>(() => rofs.CreateDirectory("/test"));
            Assert.Throws<IOException>(() => rofs.DeleteDirectory("/test", true));
            Assert.Throws<IOException>(() => rofs.MoveDirectory("/drive", "/drive2"));

            Assert.Throws<IOException>(() => rofs.CreateFile("/toto.txt"));
            Assert.Throws<IOException>(() => rofs.CopyFile("/toto.txt", "/dest.txt", true));
            Assert.Throws<IOException>(() => rofs.MoveFile("/drive", "/drive2"));
            Assert.Throws<IOException>(() => rofs.DeleteFile("/toto.txt"));
            Assert.Throws<IOException>(() => rofs.OpenFile("/toto.txt", FileMode.Create, FileAccess.ReadWrite));

            Assert.Throws<IOException>(() => rofs.SetAttributes("/toto.txt", FileAttributes.ReadOnly));
            Assert.Throws<IOException>(() => rofs.SetCreationTime("/toto.txt", DateTime.Now));
            Assert.Throws<IOException>(() => rofs.SetLastAccessTime("/toto.txt", DateTime.Now));
            Assert.Throws<IOException>(() => rofs.SetLastWriteTime("/toto.txt", DateTime.Now));
        }
    }
}