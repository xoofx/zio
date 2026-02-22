// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.IO;
using System.Text;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestFileSystemEntries : TestFileSystemCompactBase
{
    public TestFileSystemEntries()
    {
        fs = new FileSystemEntryRedirect();
    }

    [TestMethod]
    public void TestRead()
    {
        var memfs = GetCommonMemoryFileSystem();
        var fsEntries = new FileSystemEntryRedirect(memfs);
        AssertCommonRead(fsEntries);            
    }

    [TestMethod]
    public void TestGetParentDirectory()
    {
        var fs = new MemoryFileSystem();
        var fileEntry = new FileEntry(fs, "/test/tata/titi.txt");
        // Shoud not throw an error
        var directory = fileEntry.Directory;
        Assert.IsFalse(directory.Exists);
        AssertEx.AreEqual(UPath.Root / "test/tata", directory.Path);
    }

    [TestMethod]
    public void TestFileEntry()
    {
        var memfs = GetCommonMemoryFileSystem();

        var file = new FileEntry(memfs, "/a/a/a1.txt");
        var file2 = new FileEntry(memfs, "/a/b.txt");

        AssertEx.AreEqual("/a/a/a1.txt", file.ToString());

        AssertEx.AreEqual("/a/a/a1.txt", file.FullName);
        AssertEx.AreEqual("a1.txt", file.Name);

        AssertEx.AreEqual("b", file2.NameWithoutExtension);
        AssertEx.AreEqual(".txt", file2.ExtensionWithDot);

        Assert.IsTrue(file.Length > 0);
        Assert.IsFalse(file.IsReadOnly);

        var dir = file.Directory;
        Assert.IsNotNull(dir);
        AssertEx.AreEqual("/a/a", dir.FullName);

        Assert.IsNull(new DirectoryEntry(memfs, "/").Parent);

        var yoyo = new FileEntry(memfs, "/a/yoyo.txt");
        using (var file1 = yoyo.Create())
        {
            file1.WriteByte(1);
            file1.WriteByte(2);
            file1.WriteByte(3);
        }

        AssertEx.AreEqual(new byte[] {1, 2, 3}, memfs.ReadAllBytes("/a/yoyo.txt"));

        Assert.Throws<FileNotFoundException>(() => memfs.GetFileSystemEntry("/wow.txt"));

        var file3 = memfs.GetFileEntry("/a/b.txt");
        Assert.IsTrue(file3.Exists);

        Assert.IsNull(memfs.TryGetFileSystemEntry("/invalid_file"));
        Assert.IsInstanceOfType<FileEntry>(memfs.TryGetFileSystemEntry("/a/b.txt"));
        Assert.IsInstanceOfType<DirectoryEntry>(memfs.TryGetFileSystemEntry("/a"));

        Assert.Throws<FileNotFoundException>(() => memfs.GetFileEntry("/invalid"));
        Assert.Throws<DirectoryNotFoundException>(() => memfs.GetDirectoryEntry("/invalid"));


        var mydir = new DirectoryEntry(memfs, "/yoyo");

        Assert.Throws<ArgumentException>(() => mydir.CreateSubdirectory("/sub"));

        var subFolder = mydir.CreateSubdirectory("sub");
        Assert.IsTrue(subFolder.Exists);

        AssertEx.Empty(mydir.EnumerateFiles());

        var subDirs = mydir.EnumerateDirectories().ToList();
        AssertEx.Single(subDirs);
        AssertEx.AreEqual("/yoyo/sub", subDirs[0].FullName);

        mydir.Delete();

        Assert.IsFalse(mydir.Exists);
        Assert.IsFalse(subFolder.Exists);

        // Test ReadAllText/WriteAllText/AppendAllText/ReadAllBytes/WriteAllBytes
        Assert.IsTrue(file.ReadAllText().Length > 0);
        Assert.IsTrue(file.ReadAllText(Encoding.UTF8).Length > 0);
        file.WriteAllText("abc");
        AssertEx.AreEqual("abc", file.ReadAllText());
        file.WriteAllText("abc", Encoding.UTF8);
        AssertEx.AreEqual("abc", file.ReadAllText(Encoding.UTF8));

        file.AppendAllText("def");
        AssertEx.AreEqual("abcdef", file.ReadAllText());
        file.AppendAllText("ghi", Encoding.UTF8);
        AssertEx.AreEqual("abcdefghi", file.ReadAllText());

        var lines = file.ReadAllLines();
        AssertEx.Single(lines);
        AssertEx.AreEqual("abcdefghi", lines[0]);

        lines = file.ReadAllLines(Encoding.UTF8);
        AssertEx.Single(lines);
        AssertEx.AreEqual("abcdefghi", lines[0]);

        AssertEx.AreEqual(new byte[] { 1, 2, 3 }, yoyo.ReadAllBytes());
        yoyo.WriteAllBytes(new byte[] {1, 2, 3, 4});
        AssertEx.AreEqual(new byte[] { 1, 2, 3, 4}, yoyo.ReadAllBytes());
    }
}



