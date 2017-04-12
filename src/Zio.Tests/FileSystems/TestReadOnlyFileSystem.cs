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

            Assert.Throws<InvalidOperationException>(() => rofs.CreateDirectory("/test"));
            Assert.Throws<InvalidOperationException>(() => rofs.DeleteDirectory("/test", true));
            Assert.Throws<InvalidOperationException>(() => rofs.MoveDirectory("/drive", "/drive2"));

            Assert.Throws<InvalidOperationException>(() => rofs.CreateFile("/toto.txt"));
            Assert.Throws<InvalidOperationException>(() => rofs.CopyFile("/toto.txt", "/dest.txt", true));
            Assert.Throws<InvalidOperationException>(() => rofs.MoveFile("/drive", "/drive2"));
            Assert.Throws<InvalidOperationException>(() => rofs.DeleteFile("/toto.txt"));
            Assert.Throws<InvalidOperationException>(() => rofs.OpenFile("/toto.txt", FileMode.Create, FileAccess.ReadWrite));

            Assert.Throws<InvalidOperationException>(() => rofs.SetAttributes("/toto.txt", FileAttributes.ReadOnly));
            Assert.Throws<InvalidOperationException>(() => rofs.SetCreationTime("/toto.txt", DateTime.Now));
            Assert.Throws<InvalidOperationException>(() => rofs.SetLastAccessTime("/toto.txt", DateTime.Now));
            Assert.Throws<InvalidOperationException>(() => rofs.SetLastWriteTime("/toto.txt", DateTime.Now));
        }
    }
}