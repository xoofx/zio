// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            // Test MoveDirectory to sub directory
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

        [Fact]
        public void TestFile()
        {
            var fs = new MemoryFileSystem();

            // Test CreateFile/OpenFile
            var stream = fs.CreateFile("/toto.txt");
            var writer = new StreamWriter(stream);
            var originalContent = "This is the content";
            writer.Write(originalContent);
            writer.Flush();
            stream.Dispose();

            // Test FileExists
            Assert.False(fs.FileExists("/titi.txt"));
            Assert.True(fs.FileExists("/toto.txt"));

            // ReadAllText
            var content = fs.ReadAllText("/toto.txt");
            Assert.Equal(originalContent, content);

            // Test CopyFile
            fs.CopyFile("/toto.txt", "/titi.txt", true);
            Assert.True(fs.FileExists("/toto.txt"));
            Assert.True(fs.FileExists("/titi.txt"));
            content = fs.ReadAllText("/titi.txt");
            Assert.Equal(originalContent, content);

            // Test MoveFile
            fs.MoveFile("/toto.txt", "/tata.txt");
            Assert.False(fs.FileExists("/toto.txt"));
            Assert.True(fs.FileExists("/tata.txt"));
            Assert.True(fs.FileExists("/titi.txt"));
            content = fs.ReadAllText("/tata.txt");
            Assert.Equal(originalContent, content);

            // TODO: Test ReplaceFile 

            // Test Enumerate file
            var files = fs.EnumerateFiles("/").Select(p => p.FullName).ToList();
            files.Sort();
            Assert.Equal(new List<string>() {"/tata.txt", "/titi.txt"}, files);

            var dirs = fs.EnumerateDirectories("/").Select(p => p.FullName).ToList();
            Assert.Equal(0, dirs.Count);
        }
    }
}