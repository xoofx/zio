// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.IO;
using System.Text;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

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

    [Fact]
    public void TestGetParentDirectory()
    {
        var fs = new MemoryFileSystem();
        var fileEntry = new FileEntry(fs, "/test/tata/titi.txt");
        // Shoud not throw an error
        var directory = fileEntry.Directory;
        Assert.False(directory.Exists);
        Assert.Equal(UPath.Root / "test/tata", directory.Path);
    }

    [Fact]
    public void TestFileEntry()
    {
        var memfs = GetCommonMemoryFileSystem();

        var file = new FileEntry(memfs, "/a/a/a1.txt");
        var file2 = new FileEntry(memfs, "/a/b.txt");

        Assert.Equal("/a/a/a1.txt", file.ToString());

        Assert.Equal("/a/a/a1.txt", file.FullName);
        Assert.Equal("a1.txt", file.Name);

        Assert.Equal("b", file2.NameWithoutExtension);
        Assert.Equal(".txt", file2.ExtensionWithDot);

        Assert.True(file.Length > 0);
        Assert.False(file.IsReadOnly);

        var dir = file.Directory;
        Assert.NotNull(dir);
        Assert.Equal("/a/a", dir.FullName);

        Assert.Null(new DirectoryEntry(memfs, "/").Parent);

        var yoyo = new FileEntry(memfs, "/a/yoyo.txt");
        using (var file1 = yoyo.Create())
        {
            file1.WriteByte(1);
            file1.WriteByte(2);
            file1.WriteByte(3);
        }

        Assert.Equal(new byte[] {1, 2, 3}, memfs.ReadAllBytes("/a/yoyo.txt"));

        Assert.Throws<FileNotFoundException>(() => memfs.GetFileSystemEntry("/wow.txt"));

        var file3 = memfs.GetFileEntry("/a/b.txt");
        Assert.True(file3.Exists);

        Assert.Null(memfs.TryGetFileSystemEntry("/invalid_file"));
        Assert.IsType<FileEntry>(memfs.TryGetFileSystemEntry("/a/b.txt"));
        Assert.IsType<DirectoryEntry>(memfs.TryGetFileSystemEntry("/a"));

        Assert.Throws<FileNotFoundException>(() => memfs.GetFileEntry("/invalid"));
        Assert.Throws<DirectoryNotFoundException>(() => memfs.GetDirectoryEntry("/invalid"));


        var mydir = new DirectoryEntry(memfs, "/yoyo");

        Assert.Throws<ArgumentException>(() => mydir.CreateSubdirectory("/sub"));

        var subFolder = mydir.CreateSubdirectory("sub");
        Assert.True(subFolder.Exists);

        Assert.Empty(mydir.EnumerateFiles());

        var subDirs = mydir.EnumerateDirectories().ToList();
        Assert.Single(subDirs);
        Assert.Equal("/yoyo/sub", subDirs[0].FullName);

        mydir.Delete();

        Assert.False(mydir.Exists);
        Assert.False(subFolder.Exists);

        // Test ReadAllText/WriteAllText/AppendAllText/ReadAllBytes/WriteAllBytes
        Assert.True(file.ReadAllText().Length > 0);
        Assert.True(file.ReadAllText(Encoding.UTF8).Length > 0);
        file.WriteAllText("abc");
        Assert.Equal("abc", file.ReadAllText());
        file.WriteAllText("abc", Encoding.UTF8);
        Assert.Equal("abc", file.ReadAllText(Encoding.UTF8));

        file.AppendAllText("def");
        Assert.Equal("abcdef", file.ReadAllText());
        file.AppendAllText("ghi", Encoding.UTF8);
        Assert.Equal("abcdefghi", file.ReadAllText());

        var lines = file.ReadAllLines();
        Assert.Single(lines);
        Assert.Equal("abcdefghi", lines[0]);

        lines = file.ReadAllLines(Encoding.UTF8);
        Assert.Single(lines);
        Assert.Equal("abcdefghi", lines[0]);

        Assert.Equal(new byte[] { 1, 2, 3 }, yoyo.ReadAllBytes());
        yoyo.WriteAllBytes(new byte[] {1, 2, 3, 4});
        Assert.Equal(new byte[] { 1, 2, 3, 4}, yoyo.ReadAllBytes());
    }
}