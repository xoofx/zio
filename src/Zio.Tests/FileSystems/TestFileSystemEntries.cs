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
    public class TestFileSystemEntries : TestFileSystemCompactBase
    {
        public TestFileSystemEntries()
        {
            fs = new FileSystemEntryRedirect();
        }

        [Fact]
        public async Task TestRead()
        {
            var memfs = await GetCommonMemoryFileSystem();
            var fsEntries = new FileSystemEntryRedirect(memfs);
            await AssertCommonRead(fsEntries);            
        }

        [Fact]
        public async Task TestGetParentDirectory()
        {
            var fs = new MemoryFileSystem();
            var fileEntry = new FileEntry(fs, "/test/tata/titi.txt");
            // Shoud not throw an error
            var directory = fileEntry.Directory;
            Assert.False(await directory.Exists);
            Assert.Equal(UPath.Root / "test/tata", directory.Path);
        }

        [Fact]
        public async Task TestFileEntry()
        {
            var memfs = await GetCommonMemoryFileSystem();

            var file = new FileEntry(memfs, "/a/a/a1.txt");
            var file2 = new FileEntry(memfs, "/a/b.txt");

            Assert.Equal("/a/a/a1.txt", file.ToString());

            Assert.Equal("/a/a/a1.txt", file.FullName);
            Assert.Equal("a1.txt", file.Name);

            Assert.Equal("b", file2.NameWithoutExtension);
            Assert.Equal(".txt", file2.ExtensionWithDot);

            Assert.True(await file.Length > 0);
            Assert.False(await file.IsReadOnly());

            var dir = file.Directory;
            Assert.NotNull(dir);
            Assert.Equal("/a/a", dir.FullName);

            Assert.Null(new DirectoryEntry(memfs, "/").Parent);

            var yoyo = new FileEntry(memfs, "/a/yoyo.txt");
            using (var file1 = await yoyo.Create())
            {
                file1.WriteByte(1);
                file1.WriteByte(2);
                file1.WriteByte(3);
            }

            Assert.Equal(new byte[] {1, 2, 3}, await memfs.ReadAllBytes("/a/yoyo.txt"));

            await Assert.ThrowsAsync <FileNotFoundException>(async () => await memfs.GetFileSystemEntry("/wow.txt"));

            var file3 = await memfs.GetFileEntry("/a/b.txt");
            Assert.True(await file3.Exists);

            Assert.Null(await memfs.TryGetFileSystemEntry("/invalid_file"));
            Assert.IsType<FileEntry>(await memfs.TryGetFileSystemEntry("/a/b.txt"));
            Assert.IsType<DirectoryEntry>(await memfs.TryGetFileSystemEntry("/a"));

            await Assert.ThrowsAsync<FileNotFoundException>(async () => await memfs.GetFileEntry("/invalid"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await memfs.GetDirectoryEntry("/invalid"));


            var mydir = new DirectoryEntry(memfs, "/yoyo");

            await Assert.ThrowsAsync<ArgumentException>(async () => await mydir.CreateSubdirectory("/sub"));

            var subFolder = await mydir.CreateSubdirectory("sub");
            Assert.True(await subFolder.Exists);

            Assert.Empty(await mydir.EnumerateFiles());

            var subDirs = (await mydir.EnumerateDirectories()).ToList();
            Assert.Single(subDirs);
            Assert.Equal("/yoyo/sub", subDirs[0].FullName);

            await mydir.Delete();

            Assert.False(await mydir.Exists);
            Assert.False(await subFolder.Exists);

            // Test ReadAllText/WriteAllText/AppendAllText/ReadAllBytes/WriteAllBytes
            Assert.True((await file.ReadAllText()).Length > 0);
            Assert.True((await file.ReadAllText(Encoding.UTF8)).Length > 0);
            await file.WriteAllText("abc");
            Assert.Equal("abc", await file.ReadAllText());
            await file.WriteAllText("abc", Encoding.UTF8);
            Assert.Equal("abc", await file.ReadAllText(Encoding.UTF8));

            await file.AppendAllText("def");
            Assert.Equal("abcdef", await file.ReadAllText());
            await file.AppendAllText("ghi", Encoding.UTF8);
            Assert.Equal("abcdefghi", await file.ReadAllText());

            var lines = await file.ReadAllLines();
            Assert.Single(lines);
            Assert.Equal("abcdefghi", lines[0]);

            lines = await file.ReadAllLines(Encoding.UTF8);
            Assert.Single(lines);
            Assert.Equal("abcdefghi", lines[0]);

            Assert.Equal(new byte[] { 1, 2, 3 }, await yoyo.ReadAllBytes());
            await yoyo.WriteAllBytes(new byte[] {1, 2, 3, 4});
            Assert.Equal(new byte[] { 1, 2, 3, 4}, await yoyo.ReadAllBytes());
        }
    }
}