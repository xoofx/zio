// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.IO;
using System.IO.Compression;
using System.Text;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestZipArchiveFileSystem : TestFileSystemBase
{
    [TestMethod]
    public void TestCommonRead()
    {
        var fs = this.GetCommonZipArchiveFileSystem();
        
        // The ZIP is created on Windows, so it has the FileAttributes of a Windows system.
        this.AssertCommonRead(fs, isWindows: true);
    }

    [TestMethod]
    public void TestCopyFileSystem()
    {
        var fs = this.GetCommonZipArchiveFileSystem();

        var dest = new ZipArchiveFileSystem(new MemoryStream());
        fs.CopyTo(dest, UPath.Root, true);

        this.AssertFileSystemEqual(fs, dest);
    }
    
    [TestMethod]
    public void TestCopyFileSystemSubFolder()
    {
        var fs = GetCommonZipArchiveFileSystem();

        var dest = new ZipArchiveFileSystem(new MemoryStream());
        var subFolder = UPath.Root / "subfolder";
        fs.CopyTo(dest, subFolder, true);

        var destSubFileSystem = dest.GetOrCreateSubFileSystem(subFolder);

        AssertFileSystemEqual(fs, destSubFileSystem);
    }
    
    [TestMethod]
    public void TestWatcher()
    {
        var fs = this.GetCommonZipArchiveFileSystem();
        var watcher = fs.Watch("/a");

        var gotChange = false;
        watcher.Created += (sender, args) =>
            {
                if (args.FullPath == "/a/watched.txt")
                {
                    gotChange = true;
                }
            };

        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        fs.WriteAllText("/a/watched.txt", "test");
        Thread.Sleep(100);
        Assert.IsTrue(gotChange);
    }
    
    [TestMethod]
    public void TestFileEntry()
    {
        var fs = GetCommonZipArchiveFileSystem();

        var file = new FileEntry(fs, "/a/a/a1.txt");
        var file2 = new FileEntry(fs, "/a/b.txt");

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

        Assert.IsNull(new DirectoryEntry(fs, "/").Parent);

        var yoyo = new FileEntry(fs, "/a/yoyo.txt");
        using (var file1 = yoyo.Create())
        {
            file1.WriteByte(1);
            file1.WriteByte(2);
            file1.WriteByte(3);
        }

        AssertEx.AreEqual(new byte[] { 1, 2, 3 }, fs.ReadAllBytes("/a/yoyo.txt"));

        Assert.Throws<FileNotFoundException>(() => fs.GetFileSystemEntry("/wow.txt"));

        var file3 = fs.GetFileEntry("/a/b.txt");
        Assert.IsTrue(file3.Exists);

        Assert.IsNull(fs.TryGetFileSystemEntry("/invalid_file"));
        Assert.IsInstanceOfType<FileEntry>(fs.TryGetFileSystemEntry("/a/b.txt"));
        Assert.IsInstanceOfType<DirectoryEntry>(fs.TryGetFileSystemEntry("/a"));

        Assert.Throws<FileNotFoundException>(() => fs.GetFileEntry("/invalid"));
        Assert.Throws<DirectoryNotFoundException>(() => fs.GetDirectoryEntry("/invalid"));


        var mydir = new DirectoryEntry(fs, "/yoyo");

        Assert.Throws<ArgumentException>(() => mydir.CreateSubdirectory("/sub"));

        var subFolder = mydir.CreateSubdirectory("sub");
        Assert.IsTrue(subFolder.Exists);

        AssertEx.Empty(mydir.EnumerateFiles());

        var subDirs = mydir.EnumerateDirectories();
        AssertEx.Single(subDirs);
        AssertEx.AreEqual("/yoyo/sub", subDirs.First().FullName);

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
        yoyo.WriteAllBytes(new byte[] { 1, 2, 3, 4 });
        AssertEx.AreEqual(new byte[] { 1, 2, 3, 4 }, yoyo.ReadAllBytes());
    }

    [TestMethod]
    public void TestGetParentDirectory()
    {
        var fs = new ZipArchiveFileSystem();
        var fileEntry = new FileEntry(fs, "/test/tata/titi.txt");
        // Shoud not throw an error
        var directory = fileEntry.Directory;
        Assert.IsFalse(directory.Exists);
        AssertEx.AreEqual(UPath.Root / "test/tata", directory.Path);
    }

    [TestMethod]
    public void TestReplaceFile()
    {
        var fs = GetCommonZipArchiveFileSystem();

        var file = new FileEntry(fs, "/a/b.txt");
        file.WriteAllText("abc");
        AssertEx.AreEqual("abc", file.ReadAllText());
        var file2 = new FileEntry(fs, "/a/copy.txt");
        file2.WriteAllText("def");
        AssertEx.AreEqual("def", file2.ReadAllText());

        fs.ReplaceFile(file.Path, new UPath("/a/copy.txt"), new UPath("/b/backup.txt"), true);
        Assert.IsFalse(file.Exists);

        var copy = fs.GetFileEntry("/a/copy.txt");
        Assert.IsTrue(copy.Exists);
        AssertEx.AreEqual("abc", copy.ReadAllText());
        AssertEx.AreEqual("def", fs.GetFileEntry("/b/backup.txt").ReadAllText());
    }

    [TestMethod]
    public void TestOpenStreamsMultithreaded()
    {
        var memStream = new MemoryStream();
        var writeFs = new ZipArchiveFileSystem(memStream, ZipArchiveMode.Update, true);
        writeFs.WriteAllText("/test.txt", "content");
        writeFs.Dispose();

        var readFs = new ZipArchiveFileSystem(memStream, ZipArchiveMode.Read);

        readFs.OpenFile("/test.txt", FileMode.Open, FileAccess.Read, FileShare.Read).Dispose();

        const int CountTest = 2000;

        var thread1 = new Thread(() =>
        {
            for (int i = 0; i < CountTest; i++)
            {
                readFs.OpenFile("/test.txt", FileMode.Open, FileAccess.Read, FileShare.Read).Dispose();
            }
        });
        var thread2 = new Thread(() =>
        {
            for (int i = 0; i < CountTest; i++)
            {
                readFs.OpenFile("/test.txt", FileMode.Open, FileAccess.Read, FileShare.Read).Dispose();
            }
        });

        thread1.Start();
        thread2.Start();

        thread1.Join();
        thread2.Join();
    }


    [TestMethod]
    [DataRow("TestData/OsZips/Linux.zip")]
    [DataRow("TestData/OsZips/Windows.zip")]
    public void TestCaseInSensitiveZip(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var fs = new ZipArchiveFileSystem(archive);

        Assert.IsTrue(fs.DirectoryExists("/Folder"));
        Assert.IsTrue(fs.DirectoryExists("/folder"));

        Assert.IsFalse(fs.FileExists("/Folder"));
        Assert.IsFalse(fs.FileExists("/folder"));

        Assert.IsTrue(fs.FileExists("/Folder/File.txt"));
        Assert.IsTrue(fs.FileExists("/folder/file.txt"));

        Assert.IsFalse(fs.DirectoryExists("/Folder/file.txt"));
        Assert.IsFalse(fs.DirectoryExists("/folder/File.txt"));
    }

    [TestMethod]
    [DataRow("TestData/OsZips/Linux.zip")]
    [DataRow("TestData/OsZips/Windows.zip")]
    public void TestCaseSensitiveZip(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var fs = new ZipArchiveFileSystem(archive, true);

        Assert.IsTrue(fs.DirectoryExists("/Folder"));
        Assert.IsFalse(fs.DirectoryExists("/folder"));

        Assert.IsFalse(fs.FileExists("/Folder"));
        Assert.IsFalse(fs.FileExists("/folder"));

        Assert.IsTrue(fs.FileExists("/Folder/File.txt"));
        Assert.IsFalse(fs.FileExists("/folder/file.txt"));

        Assert.IsFalse(fs.DirectoryExists("/Folder/file.txt"));
        Assert.IsFalse(fs.DirectoryExists("/folder/File.txt"));
    }

    [TestMethod]
    public void TestSaveStream()
    {
        var stream = new MemoryStream();

        using var fs = new ZipArchiveFileSystem(stream);

        fs.WriteAllText("/a/b.txt", "abc");
        fs.Save();

        stream.Seek(0, SeekOrigin.Begin);

        using (var fs2 = new ZipArchiveFileSystem(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            AssertEx.AreEqual("abc", fs2.ReadAllText("/a/b.txt"));
        }

        AssertEx.AreEqual("abc", fs.ReadAllText("/a/b.txt"));
        fs.WriteAllText("/a/b.txt", "def");
        fs.Save();

        stream.Seek(0, SeekOrigin.Begin);

        using (var fs2 = new ZipArchiveFileSystem(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            AssertEx.AreEqual("def", fs2.ReadAllText("/a/b.txt"));
        }
    }

    [TestMethod]
    public void TestSaveFile()
    {
        var path = Path.Combine(SystemPath, Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            using var fs = new ZipArchiveFileSystem(path);

            AssertEx.AreEqual(0, new FileInfo(path).Length);

            fs.WriteAllText("/a/b.txt", "abc");
            fs.Save();

            // We cannot check the content because the file is still open
            AssertEx.AreNotEqual(0, new FileInfo(path).Length);

            // Ensure we can save multiple times
            fs.WriteAllText("/a/b.txt", "def");
            fs.Save();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("TestData/RootFiles/WithoutRootSlash.zip", false)]
    [DataRow("TestData/RootFiles/WithRootSlash.zip", true)]
    public void TestReadDifferentSlash(string zipPath, bool leadingRootSlash)
    {
        using var fs = new ZipArchiveFileSystem(zipPath);

        AssertEx.AreEqual(leadingRootSlash, fs.LeadingSlashInArchive);

        Assert.IsTrue(fs.FileExists("/Test.txt"));
        AssertEx.AreEqual("Test", fs.ReadAllText("/Test.txt"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TestWriteDifferentSlash(bool leadingRootSlash)
    {
        using var memStream = new MemoryStream();

        using (var fs = new ZipArchiveFileSystem(memStream, leaveOpen: true))
        {
            fs.LeadingSlashInArchive = leadingRootSlash;

            AssertEx.AreEqual(leadingRootSlash, fs.LeadingSlashInArchive);

            fs.WriteAllText("/Test.txt", "Test");
        }

        memStream.Seek(0, SeekOrigin.Begin);

        using (var fs = new ZipArchiveFileSystem(memStream, leaveOpen: true))
        {
            AssertEx.AreEqual(leadingRootSlash, fs.LeadingSlashInArchive);

            Assert.IsTrue(fs.FileExists("/Test.txt"));
            AssertEx.AreEqual("Test", fs.ReadAllText("/Test.txt"));
        }

        memStream.Seek(0, SeekOrigin.Begin);

        using var zipArchive = new ZipArchive(memStream, ZipArchiveMode.Read);

        AssertEx.AreEqual(leadingRootSlash, zipArchive.Entries[0].FullName.StartsWith("/"));
    }
}



