// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestFileSystemExtensions : TestFileSystemBase
    {
        [Fact]
        public async Task TestExceptions()
        {
            var fs = new MemoryFileSystem();

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.AppendAllText("/a.txt", null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.WriteAllText("/a.txt", null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.WriteAllText("/a.txt", "content", null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.WriteAllText("/a.txt", null, null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.ReadAllText("/a.txt", null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.ReadAllLines("/a.txt", null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.WriteAllBytes("/a", null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.AppendAllText("/a", null, null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.AppendAllText("/a", "content", null));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumeratePaths("*", null)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumeratePaths("*", null, SearchOption.AllDirectories)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateFiles("*", null)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateFiles("*", null, SearchOption.AllDirectories)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateDirectories("*", null)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateDirectories("*", null, SearchOption.AllDirectories)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateFileEntries("*", null)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateFileEntries("*", null, SearchOption.AllDirectories)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateDirectoryEntries("*", null)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateDirectoryEntries("*", null, SearchOption.AllDirectories)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateFileSystemEntries("*", null)).First());
            await Assert.ThrowsAsync<ArgumentNullException>(async () => (await fs.EnumerateFileSystemEntries("*", null, SearchOption.AllDirectories)).First());
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.GetFileEntry("/a.txt"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.GetDirectoryEntry("/a"));
        }

        [Fact]
        public async Task TestWriteReadAppendAllTextAndLines()
        {
            var fs = new MemoryFileSystem();
            await fs.AppendAllText("/a.txt", "test");
            await fs.AppendAllText("/a.txt", "test");
            Assert.Equal("testtest", await fs.ReadAllText("/a.txt"));

            await fs.WriteAllText("/a.txt", "content");
            Assert.Equal("content", await fs.ReadAllText("/a.txt"));

            await fs.WriteAllText("/a.txt", "test1", Encoding.UTF8);
            await fs.AppendAllText("/a.txt", "test2", Encoding.UTF8);
            Assert.Equal("test1test2", await fs.ReadAllText("/a.txt", Encoding.UTF8));

            Assert.Equal(new[] {"test1test2"}, await fs.ReadAllLines("/a.txt"));
            Assert.Equal(new[] { "test1test2" }, await fs.ReadAllLines("/a.txt", Encoding.UTF8));
        }

        [Fact]
        public async Task TestReadWriteAllBytes()
        {
            var fs = new MemoryFileSystem();

            await fs.WriteAllBytes("/toto.txt", new byte[] {1,2,3});
            Assert.Equal(new byte[]{1,2,3}, await fs.ReadAllBytes("/toto.txt"));

            await fs.WriteAllBytes("/toto.txt", new byte[] { 5 });
            Assert.Equal(new byte[] { 5 }, await fs.ReadAllBytes("/toto.txt"));

            await fs.WriteAllBytes("/toto.txt", new byte[] { });
            Assert.Equal(new byte[] { }, await fs.ReadAllBytes("/toto.txt"));
        }
    }
}