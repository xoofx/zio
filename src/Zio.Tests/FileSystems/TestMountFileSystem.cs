// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestMountFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestMount()
        {
            var fs = new MountFileSystem();
            var memfs = new MemoryFileSystem();

            Assert.Throws<ArgumentNullException>(() => fs.Mount(null, memfs));
            Assert.Throws<ArgumentNullException>(() => fs.Mount("/test", null));
            Assert.Throws<ArgumentException>(() => fs.Mount("test", memfs));
            Assert.Throws<ArgumentException>(() => fs.Mount("/", memfs));
            Assert.Throws<ArgumentException>(() => fs.Mount("/test/a", memfs));
            Assert.Throws<ArgumentException>(() => fs.Mount("/test/a/b", memfs));

            Assert.False(fs.IsMounted("/test"));
            fs.Mount("/test", memfs);
            Assert.True(fs.IsMounted("/test"));
            Assert.Throws<ArgumentException>(() => fs.Mount("/test", memfs));

            Assert.Throws<ArgumentNullException>(() => fs.Unmount(null));
            Assert.Throws<ArgumentException>(() => fs.Unmount("test"));
            Assert.Throws<ArgumentException>(() => fs.Unmount("/"));
            Assert.Throws<ArgumentException>(() => fs.Unmount("/test/a"));
            Assert.Throws<ArgumentException>(() => fs.Unmount("/test/a/b"));
            Assert.Throws<ArgumentException>(() => fs.Unmount("/test2"));

            fs.Mount("/test2", memfs);
            Assert.True(fs.IsMounted("/test"));
            Assert.True(fs.IsMounted("/test2"));

            Assert.Equal(new Dictionary<UPath, IFileSystem>()
            {
                {"/test", memfs},
                {"/test2", memfs},
            }, fs.GetMounts());

            fs.Unmount("/test");
            Assert.False(fs.IsMounted("/test"));
            Assert.True(fs.IsMounted("/test2"));

            fs.Unmount("/test2");

            Assert.Equal(0, fs.GetMounts().Count);
        }
    }
}