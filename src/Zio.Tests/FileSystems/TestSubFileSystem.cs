// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestSubFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestBasic()
        {
            var fs = new PhysicalFileSystem();
            var path = fs.ConvertPathFromInternal(SystemPath);

            // Create a filesystem / on the current folder of this assembly
            var subfs = new SubFileSystem(fs, path);

            // This test is basically testing the two methods (ConvertPathToDelegate and ConvertPathFromDelegate) in SubFileSystem

            var files = subfs.EnumeratePaths("/").Select(info => info.GetName()).ToList();
            var expectedFiles = fs.EnumeratePaths(path).Select(info => info.GetName()).ToList();
            Assert.True(files.Count > 0);
            Assert.Equal(expectedFiles, files);

            // Check that SubFileSystem is actually checking that the directory exists in the delegate filesystem
            Assert.Throws<DirectoryNotFoundException>(() => new SubFileSystem(fs, path / "does_not_exist"));

            Assert.Throws<InvalidOperationException>(() => subfs.ConvertPathFromInternal(@"C:\"));
            // TODO: We could add another test just to make sure that files can be created...etc. But the test above should already cover the code provided in SubFileSystem
        }

        [Fact]
        public void TestGetOrCreateFileSystem()
        {
            var fs = new MemoryFileSystem();
            const string subFolder = "/sub";
            var subFileSystem = fs.GetOrCreateSubFileSystem(subFolder);
            Assert.True(fs.DirectoryExists(subFolder));
            subFileSystem.WriteAllText("/test.txt", "yo");
            var text = fs.ReadAllText(subFolder + "/test.txt");
            Assert.Equal("yo", text);
        }
    }
}