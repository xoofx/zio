// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.IO;
using System.IO.Compression;
using System.Text;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

public class TestZipArchiveFileSystemCompact
{
    public TestZipArchiveFileSystemCompact()
    {
        fs = new ZipArchiveFileSystem();
    }
    public IFileSystem fs;

    [Fact]
    public void TestDirectory()
    {
        Assert.True(fs.DirectoryExists("/"));
        Assert.False(fs.DirectoryExists(null));

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
    public void TestDirectoryExceptions()
    {
        Assert.Throws<DirectoryNotFoundException>(() => fs.DeleteDirectory("/dir", true));

        Assert.Throws<DirectoryNotFoundException>(() => fs.MoveDirectory("/dir1", "/dir2"));

        fs.CreateDirectory("/dir1");
        Assert.Throws<IOException>(() => fs.DeleteFile("/dir1"));
        Assert.Throws<IOException>(() => fs.MoveDirectory("/dir1", "/dir1"));

        fs.WriteAllText("/toto.txt", "test");
        Assert.Throws<IOException>(() => fs.CreateDirectory("/toto.txt"));
        Assert.Throws<IOException>(() => fs.DeleteDirectory("/toto.txt", true));
        Assert.Throws<IOException>(() => fs.MoveDirectory("/toto.txt", "/test"));

        fs.CreateDirectory("/dir2");
        Assert.Throws<IOException>(() => fs.MoveDirectory("/dir1", "/dir2"));

#if !NET472
        fs.SetAttributes("/dir1", FileAttributes.Directory | FileAttributes.ReadOnly);
        Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir1", true));
#endif
    }

    [Fact]
    public void TestDirectoryDelete()
    {
        fs.CreateDirectory("/dir");
        fs.WriteAllText("/dir/file.txt", "test");

        fs.CreateDirectory("/dir2");
        fs.WriteAllText("/dir2/file2.txt", "test");

        fs.DeleteDirectory("/dir", isRecursive: true);

        Assert.False(fs.DirectoryExists("/dir"));

        Assert.True(fs.DirectoryExists("/dir2"));
        Assert.True(fs.FileExists("/dir2/file2.txt"));
    }

    [Fact]
    public void TestDirectoryMove()
    {
        fs.CreateDirectory("/dir");
        fs.WriteAllText("/dir/file.txt", "test");

        fs.CreateDirectory("/dir2");
        fs.WriteAllText("/dir2/file2.txt", "test");

        fs.MoveDirectory("/dir", "/moved");

        Assert.False(fs.DirectoryExists("/dir"));

        Assert.True(fs.DirectoryExists("/moved"));
        Assert.True(fs.FileExists("/moved/file.txt"));

        Assert.True(fs.DirectoryExists("/dir2"));
        Assert.True(fs.FileExists("/dir2/file2.txt"));
    }

    [Fact]
    public void TestFile()
    {
        // Test CreateFile/OpenFile
        var stream = fs.CreateFile("/toto.txt");
        var writer = new StreamWriter(stream);
        var originalContent = "This is the content";
        writer.Write(originalContent);
        writer.Flush();
        stream.Dispose();

        // Test FileExists
        Assert.False(fs.FileExists(null));
        Assert.False(fs.FileExists("/titi.txt"));
        Assert.True(fs.FileExists("/toto.txt"));

        // ReadAllText
        var content = fs.ReadAllText("/toto.txt");
        Assert.Equal(originalContent, content);

        // sleep for creation time comparison
        Thread.Sleep(16);

        // Test CopyFile
        fs.CopyFile("/toto.txt", "/titi.txt", true);
        Assert.True(fs.FileExists("/toto.txt"));
        Assert.True(fs.FileExists("/titi.txt"));
        content = fs.ReadAllText("/titi.txt");
        Assert.Equal(originalContent, content);

        // Test Attributes/Times
        Assert.True(fs.GetFileLength("/toto.txt") > 0);
        Assert.Equal(fs.GetFileLength("/toto.txt"), fs.GetFileLength("/titi.txt"));
        Assert.Equal(fs.GetAttributes("/toto.txt"), fs.GetAttributes("/titi.txt"));
        Assert.NotEqual(fs.GetCreationTime("/toto.txt"), fs.GetCreationTime("/titi.txt"));
        // Because we read titi.txt just before, access time must be different
        // Following test is disabled as it seems unstable with NTFS?
        // Assert.NotEqual(fs.GetLastAccessTime("/toto.txt"), fs.GetLastAccessTime("/titi.txt"));

        Assert.Equal(fs.GetLastWriteTime("/toto.txt").DayOfYear, fs.GetLastWriteTime("/titi.txt").DayOfYear);
        Assert.Equal(fs.GetLastWriteTime("/toto.txt").Hour, fs.GetLastWriteTime("/titi.txt").Hour);

        var now = DateTime.Now + TimeSpan.FromSeconds(10);
        //var now1 = DateTime.Now + TimeSpan.FromSeconds(11);
        //var now2 = DateTime.Now + TimeSpan.FromSeconds(12);
        // access and creation times are not supported by the zip standard
        fs.SetCreationTime("/toto.txt", now);
        //fs.SetLastAccessTime("/toto.txt", now1);
        //fs.SetLastWriteTime("/toto.txt", now2);
        //Assert.Equal(now, fs.GetCreationTime("/toto.txt"));
        //Assert.Equal(now1, fs.GetLastAccessTime("/toto.txt"));
        //Assert.Equal(now2, fs.GetLastWriteTime("/toto.txt"));

        //Assert.NotEqual(fs.GetCreationTime("/toto.txt"), fs.GetCreationTime("/titi.txt"));
        //Assert.NotEqual(fs.GetLastAccessTime("/toto.txt"), fs.GetLastAccessTime("/titi.txt"));
        //Assert.NotEqual(fs.GetLastWriteTime("/toto.txt"), fs.GetLastWriteTime("/titi.txt"));

        // Test MoveFile
        fs.MoveFile("/toto.txt", "/tata.txt");
        Assert.False(fs.FileExists("/toto.txt"));
        Assert.True(fs.FileExists("/tata.txt"));
        Assert.True(fs.FileExists("/titi.txt"));
        content = fs.ReadAllText("/tata.txt");
        Assert.Equal(originalContent, content);

        // Test Enumerate file
        var files = fs.EnumerateFiles("/").Select(p => p.FullName).ToList();
        files.Sort();
        Assert.Equal(new List<string>() { "/tata.txt", "/titi.txt" }, files);

        var dirs = fs.EnumerateDirectories("/").Select(p => p.FullName).ToList();
        Assert.Empty(dirs);

        // Check ReplaceFile
        var originalContent2 = "this is a content2";
        fs.WriteAllText("/tata.txt", originalContent2);
        fs.ReplaceFile("/tata.txt", "/titi.txt", "/titi.bak.txt", true);
        Assert.False(fs.FileExists("/tata.txt"));
        Assert.True(fs.FileExists("/titi.txt"));
        Assert.True(fs.FileExists("/titi.bak.txt"));
        content = fs.ReadAllText("/titi.txt");
        Assert.Equal(originalContent2, content);
        content = fs.ReadAllText("/titi.bak.txt");
        Assert.Equal(originalContent, content);

        // Check File ReadOnly
#if !NET472
        fs.SetAttributes("/titi.txt", FileAttributes.ReadOnly);
        Assert.Throws<UnauthorizedAccessException>(() => fs.DeleteFile("/titi.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => fs.CopyFile("/titi.bak.txt", "/titi.txt", true));
        Assert.Throws<UnauthorizedAccessException>(() => fs.OpenFile("/titi.txt", FileMode.Open, FileAccess.ReadWrite));
        fs.SetAttributes("/titi.txt", FileAttributes.Normal);
#endif
        // Delete File
        fs.DeleteFile("/titi.txt");
        Assert.False(fs.FileExists("/titi.txt"));
        fs.DeleteFile("/titi.bak.txt");
        Assert.False(fs.FileExists("/titi.bak.txt"));
    }

    [Fact]
    public void TestMoveFileDifferentDirectory()
    {
        fs.WriteAllText("/toto.txt", "content");

        fs.CreateDirectory("/dir");

        fs.MoveFile("/toto.txt", "/dir/titi.txt");

        Assert.False(fs.FileExists("/toto.txt"));
        Assert.True(fs.FileExists("/dir/titi.txt"));

        Assert.Equal("content", fs.ReadAllText("/dir/titi.txt"));
    }

    [Fact]
    public void TestReplaceFileDifferentDirectory()
    {
        fs.WriteAllText("/toto.txt", "content");

        fs.CreateDirectory("/dir");
        fs.WriteAllText("/dir/tata.txt", "content2");

        fs.CreateDirectory("/dir2");

        fs.ReplaceFile("/toto.txt", "/dir/tata.txt", "/dir2/titi.txt", true);
        Assert.True(fs.FileExists("/dir/tata.txt"));
        Assert.True(fs.FileExists("/dir2/titi.txt"));

        Assert.Equal("content", fs.ReadAllText("/dir/tata.txt"));
        Assert.Equal("content2", fs.ReadAllText("/dir2/titi.txt"));

        fs.ReplaceFile("/dir/tata.txt", "/dir2/titi.txt", "/titi.txt", true);
        Assert.False(fs.FileExists("/dir/tata.txt"));
        Assert.True(fs.FileExists("/dir2/titi.txt"));
        Assert.True(fs.FileExists("/titi.txt"));
    }

    [Fact]
    public void TestOpenFileAppend()
    {
        fs.AppendAllText("/toto.txt", "content");
        Assert.True(fs.FileExists("/toto.txt"));
        Assert.Equal("content", fs.ReadAllText("/toto.txt"));

        fs.AppendAllText("/toto.txt", "content");
        Assert.True(fs.FileExists("/toto.txt"));
        Assert.Equal("contentcontent", fs.ReadAllText("/toto.txt"));
    }

    [Fact]
    public void TestOpenFileTruncate()
    {
        fs.WriteAllText("/toto.txt", "content");
        Assert.True(fs.FileExists("/toto.txt"));
        Assert.Equal("content", fs.ReadAllText("/toto.txt"));

        var stream = fs.OpenFile("/toto.txt", FileMode.Truncate, FileAccess.Write);
        stream.Dispose();
        Assert.Equal<long>(0, fs.GetFileLength("/toto.txt"));
        Assert.Equal("", fs.ReadAllText("/toto.txt"));
    }

    [Fact]
    public void TestFileExceptions()
    {
        fs.CreateDirectory("/dir1");

        Assert.Throws<FileNotFoundException>(() => fs.GetFileLength("/toto.txt"));
        Assert.Throws<FileNotFoundException>(() => fs.CopyFile("/toto.txt", "/toto.bak.txt", true));
        Assert.Throws<UnauthorizedAccessException>(() => fs.CopyFile("/dir1", "/toto.bak.txt", true));
        Assert.Throws<FileNotFoundException>(() => fs.MoveFile("/toto.txt", "/titi.txt"));
        // If the file to be deleted does not exist, no exception is thrown.
        fs.DeleteFile("/toto.txt");
        Assert.Throws<FileNotFoundException>(() => fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read));
        Assert.Throws<FileNotFoundException>(() => fs.OpenFile("/toto.txt", FileMode.Truncate, FileAccess.Write));

        Assert.Throws<FileNotFoundException>(() => fs.GetFileLength("/dir1/toto.txt"));
        Assert.Throws<FileNotFoundException>(() => fs.CopyFile("/dir1/toto.txt", "/toto.bak.txt", true));
        Assert.Throws<FileNotFoundException>(() => fs.MoveFile("/dir1/toto.txt", "/titi.txt"));
        // If the file to be deleted does not exist, no exception is thrown.
        fs.DeleteFile("/dir1/toto.txt");
        Assert.Throws<FileNotFoundException>(() => fs.OpenFile("/dir1/toto.txt", FileMode.Open, FileAccess.Read));

        fs.WriteAllText("/toto.txt", "yo");
        fs.CopyFile("/toto.txt", "/titi.txt", false);
        fs.CopyFile("/toto.txt", "/titi.txt", true);

        Assert.Throws<FileNotFoundException>(() => fs.GetFileLength("/dir1"));

        var defaultTime = new DateTime(1601, 01, 01, 0, 0, 0, DateTimeKind.Utc).ToLocalTime();
        Assert.Equal(defaultTime, fs.GetCreationTime("/dest"));
        Assert.Equal(defaultTime, fs.GetLastWriteTime("/dest"));
        Assert.Equal(defaultTime, fs.GetLastAccessTime("/dest"));

        using (var stream1 = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() =>
            {
                using (var stream2 = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                }
            });
        }

        Assert.Throws<UnauthorizedAccessException>(() => fs.OpenFile("/dir1", FileMode.Open, FileAccess.Read));
        Assert.Throws<DirectoryNotFoundException>(() => fs.OpenFile("/dir/toto.txt", FileMode.Open, FileAccess.Read));
        Assert.Throws<DirectoryNotFoundException>(() => fs.CopyFile("/toto.txt", "/dest/toto.txt", true));
        Assert.Throws<IOException>(() => fs.CopyFile("/toto.txt", "/titi.txt", false));
        Assert.Throws<IOException>(() => fs.CopyFile("/toto.txt", "/dir1", true));
        Assert.Throws<DirectoryNotFoundException>(() => fs.MoveFile("/toto.txt", "/dest/toto.txt"));

        fs.WriteAllText("/titi.txt", "yo2");
        Assert.Throws<IOException>(() => fs.MoveFile("/toto.txt", "/titi.txt"));

        Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/1.txt", "/1.txt", default(UPath), true));
        Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/1.txt", true));
        Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/2.txt", true));
        Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/1.txt", "/2.txt", "/3.txt", true));
        Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/dir/2.txt", "/3.txt", true));
        Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/2.txt", "/3.txt", true));
        Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/2.txt", "/toto.txt", true));

        // Not same behavior in Physical vs Memory
        if (fs is MemoryFileSystem)
        {
            Assert.Throws<DirectoryNotFoundException>(() => fs.ReplaceFile("/toto.txt", "/titi.txt", "/dir/3.txt", true));

            fs.WriteAllText("/tata.txt", "yo3");
            Assert.True(fs.FileExists("/tata.txt"));
            fs.ReplaceFile("/toto.txt", "/titi.txt", "/tata.txt", true);
            // TODO: check that tata.txt was correctly removed
        }
    }

    [Fact]
    public void TestDirectoryDeleteAndOpenFile()
    {
        fs.CreateDirectory("/dir");
        fs.WriteAllText("/dir/toto.txt", "content");
        var stream = fs.OpenFile("/dir/toto.txt", FileMode.Open, FileAccess.Read);

        Assert.Throws<IOException>(() => fs.DeleteFile("/dir/toto.txt"));
        Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir", true));

        stream.Dispose();

#if !NET472
        fs.SetAttributes("/dir/toto.txt", FileAttributes.ReadOnly);
        Assert.Throws<UnauthorizedAccessException>(() => fs.DeleteDirectory("/dir", true));
        fs.SetAttributes("/dir/toto.txt", FileAttributes.Normal);
#endif

        fs.DeleteDirectory("/dir", true);

        var entries = fs.EnumeratePaths("/").ToList();
        Assert.Empty(entries);
    }

    [Fact]
    public void TestOpenFileMultipleRead()
    {
        var memStream = new MemoryStream();
        var writeSystem = new ZipArchiveFileSystem(memStream, ZipArchiveMode.Update, true);
        writeSystem.WriteAllText("/toto.txt", "content");
        writeSystem.Dispose();

        var readSystem = new ZipArchiveFileSystem(memStream, ZipArchiveMode.Read, true);
        Assert.True(readSystem.FileExists("/toto.txt"));
        

        using (var tmp = readSystem.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read))
        {
            Assert.Throws<IOException>(() => readSystem.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        var stream1 = readSystem.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read);
        var stream2 = readSystem.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read);

        // DeflateStream used by ZipArchive in read mode does not support Position and Length
        //Assert.Equal<long>(1, stream1.Position);
        Assert.Equal<char>('c', (char)stream1.ReadByte());

        Assert.Equal<char>('c', (char)stream2.ReadByte());
        //Assert.Equal<long>(2, stream2.Position);

        stream1.Dispose();
        stream2.Dispose();
        
        readSystem.Dispose();
        
        // We try to write back on the same file after closing
        writeSystem = new ZipArchiveFileSystem(memStream, ZipArchiveMode.Update);
        writeSystem.WriteAllText("/toto.txt", "content2");
    }

    [Fact]
    public void TestOpenFileReadAndWriteFail()
    {
        fs.WriteAllText("/toto.txt", "content");

        Assert.True(fs.FileExists("/toto.txt"));

        var stream1 = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read);

        stream1.ReadByte();
        Assert.Equal<long>(1, stream1.Position);

        // We try to write back on the same file before closing
        Assert.Throws<IOException>(() => fs.WriteAllText("/toto.txt", "content2"));

        // Make sure that checking for a file exists or directory exists doesn't throw an exception "being used"
        Assert.True(fs.FileExists("/toto.txt"));
        Assert.False(fs.DirectoryExists("/toto.txt"));

        stream1.Dispose();
    }

    // ZipArchive doesn't support opening multiple streams in Update and Create mode
    /*[Fact]
    public void TestOpenFileReadAndWriteShared()
    {
        using (var stream1 = fs.OpenFile("/toto.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
        using (var stream2 = fs.OpenFile("/toto.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var buffer = Encoding.UTF8.GetBytes("abc");
            stream1.Write(buffer, 0, buffer.Length);
            stream1.Flush();

            buffer = Encoding.UTF8.GetBytes("d");
            stream2.Position = 1;
            stream2.Write(buffer, 0, buffer.Length);
            stream2.Flush();
        }

        var content = fs.ReadAllText("/toto.txt");
        Assert.Equal("adc", content);
    }

    [Fact]
    public void TestOpenFileReadAndWriteShared2()
    {
        fs.WriteAllText("/toto.txt", "content");
        // No exceptions
        using (var stream1 = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using (var stream2 = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
            }
        }
        var content = fs.ReadAllText("/toto.txt");
        Assert.Equal("content", content);
    }*/

    [Fact]
    public void TestCopyFileToSameFile()
    {
        fs.WriteAllText("/toto.txt", "content");
        Assert.Throws<IOException>(() => fs.CopyFile("/toto.txt", "/toto.txt", true));
        Assert.Throws<IOException>(() => fs.CopyFile("/toto.txt", "/toto.txt", false));

        fs.CreateDirectory("/dir");

        fs.WriteAllText("/dir/toto.txt", "content");
        Assert.Throws<IOException>(() => fs.CopyFile("/dir/toto.txt", "/dir/toto.txt", true));
        Assert.Throws<IOException>(() => fs.CopyFile("/dir/toto.txt", "/dir/toto.txt", false));
    }

    [Fact]
    public void TestEnumeratePaths()
    {
        fs.CreateDirectory("/dir1/a/b");
        fs.CreateDirectory("/dir1/a1");
        fs.CreateDirectory("/dir2/c");
        fs.CreateDirectory("/dir3");

        fs.WriteAllText("/dir1/a/file10.txt", "content10");
        fs.WriteAllText("/dir1/a1/file11.txt", "content11");
        fs.WriteAllText("/dir2/file20.txt", "content20");

        fs.WriteAllText("/file01.txt", "content1");
        fs.WriteAllText("/file02.txt", "content2");

        var entries = fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Both).ToList<UPath>();
        entries.Sort();

        Assert.Equal(new List<UPath>()
            {
                "/dir1",
                "/dir1/a",
                "/dir1/a/b",
                "/dir1/a/file10.txt",
                "/dir1/a1",
                "/dir1/a1/file11.txt",
                "/dir2",
                "/dir2/c",
                "/dir2/file20.txt",
                "/dir3",
                "/file01.txt",
                "/file02.txt",
            }
            , entries);


        var folders = fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Directory).ToList<UPath>();
        folders.Sort();

        Assert.Equal(new List<UPath>()
            {
                "/dir1",
                "/dir1/a",
                "/dir1/a/b",
                "/dir1/a1",
                "/dir2",
                "/dir2/c",
                "/dir3",
            }
            , folders);


        var files = fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.File).ToList<UPath>();
        files.Sort();

        Assert.Equal(new List<UPath>()
            {
                "/dir1/a/file10.txt",
                "/dir1/a1/file11.txt",
                "/dir2/file20.txt",
                "/file01.txt",
                "/file02.txt",
            }
            , files);


        folders = fs.EnumeratePaths("/dir1", "a", SearchOption.AllDirectories, SearchTarget.Directory).ToList<UPath>();
        folders.Sort();
        Assert.Equal(new List<UPath>()
            {
                "/dir1/a",
            }
            , folders);


        files = fs.EnumeratePaths("/dir1", "file1?.txt", SearchOption.AllDirectories, SearchTarget.File).ToList<UPath>();
        files.Sort();

        Assert.Equal(new List<UPath>()
            {
                "/dir1/a/file10.txt",
                "/dir1/a1/file11.txt",
            }
            , files);

        files = fs.EnumeratePaths("/", "file?0.txt", SearchOption.AllDirectories, SearchTarget.File).ToList<UPath>();
        files.Sort();

        Assert.Equal(new List<UPath>()
            {
                "/dir1/a/file10.txt",
                "/dir2/file20.txt",
            }
            , files);
    }

    [Fact]
    public void TestMultithreaded()
    {
        fs.CreateDirectory("/dir1");
        fs.WriteAllText("/toto.txt", "content");

        const int CountTest = 2000;

        var thread1 = new Thread(() =>
        {
            for (int i = 0; i < CountTest; i++)
            {
                fs.CopyFile("/toto.txt", "/titi.txt", true);
                fs.MoveFile("/titi.txt", "/tata.txt");
                fs.MoveFile("/tata.txt", "/dir1/tata.txt");

                if (fs.FileExists("/dir1/tata.txt"))
                {
                    fs.DeleteFile("/dir1/tata.txt");
                }
            }
        });
        var thread2 = new Thread(() =>
        {
            for (int i = 0; i < CountTest; i++)
            {
                fs.EnumeratePaths("/").ToList();
            }
        });

        var thread3 = new Thread(() =>
        {
            for (int i = 0; i < CountTest; i++)
            {
                fs.CreateDirectory("/dir2");
                fs.MoveDirectory("/dir2", "/dir1/dir3");
                fs.DeleteDirectory("/dir1/dir3", true);

                fs.CreateFile("/0.txt").Dispose();
                fs.DeleteFile("/0.txt");
            }
        });

        thread1.Start();
        thread2.Start();
        thread3.Start();

        thread1.Join();
        thread2.Join();
        thread3.Join();

        fs.DeleteDirectory("/dir1", true);
        fs.DeleteFile("/toto.txt");
    }

    [Fact]
    public void TestOpenFileAppendAndRead()
    {
        fs.WriteAllText("/toto.txt", "content");

        Assert.Throws<ArgumentException>(() =>
        {
            using (var stream = fs.OpenFile("/toto.txt", FileMode.Append, FileAccess.Read))
            {
            }
        });
    }

    [Fact]
    public void TestOpenFileCreateNewAlreadyExist()
    {
        fs.WriteAllText("/toto.txt", "content");

        Assert.Throws<IOException>(() =>
        {
            using (var stream = fs.OpenFile("/toto.txt", FileMode.CreateNew, FileAccess.Write))
            {
            }
        });

        Assert.Throws<IOException>(() =>
        {
            using (var stream = fs.OpenFile("/toto.txt", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            {
            }
        });
    }

    [Fact]
    public void TestOpenFileCreate()
    {
        fs.WriteAllText("/toto.txt", "content");

        using (var stream = fs.OpenFile("/toto.txt", FileMode.Create, FileAccess.Write))
        {
        }

        Assert.Equal(0, fs.GetFileLength("/toto.txt"));
    }

    [Fact]
    public void TestMoveDirectorySubFolderFail()
    {
        fs.CreateDirectory("/dir");
        fs.CreateDirectory("/dir/dir1");

        Assert.Throws<IOException>(() => fs.MoveDirectory("/dir", "/dir/dir1/dir2"));
    }

    [Fact]
    public void TestReplaceFileSameFileFail()
    {
        fs.WriteAllText("/toto.txt", "content");
        Assert.Throws<IOException>(() => fs.ReplaceFile("/toto.txt", "/toto.txt", null, true));

        fs.WriteAllText("/tata.txt", "content2");

        Assert.Throws<IOException>(() => fs.ReplaceFile("/toto.txt", "/tata.txt", "/toto.txt", true));
    }

    [Fact]
    public void TestStreamSeek()
    {
        //                            0123456
        fs.WriteAllText("/toto.txt", "content", Encoding.ASCII);

        using (var stream = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read))
        {
            stream.Seek(0, SeekOrigin.Begin);
            Assert.Equal((byte)'c', stream.ReadByte());
            Assert.Equal((byte)'o', stream.ReadByte());

            stream.Seek(3, SeekOrigin.Begin);
            Assert.Equal((byte)'t', stream.ReadByte());

            stream.Seek(1, SeekOrigin.Current);
            Assert.Equal((byte)'n', stream.ReadByte());

            stream.Seek(-3, SeekOrigin.End);
            Assert.Equal((byte)'e', stream.ReadByte());

            Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
            stream.Position = 0;
            Assert.Equal((byte)'c', stream.ReadByte());
        }
    }

    [Fact]
    public void TestDispose()
    {
        fs.WriteAllText("/toto.txt", "content");
        var stream = fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.ReadWrite);
        stream.Dispose();
        stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => stream.WriteByte(1));
        Assert.Throws<ObjectDisposedException>(() => stream.Position);
        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => stream.Flush());
        Assert.Throws<ObjectDisposedException>(() => stream.Length);
        Assert.Throws<ObjectDisposedException>(() => stream.SetLength(0));
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void TestDeleteDirectoryNonEmpty()
    {
        fs.CreateDirectory("/dir/dir1");
        Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir", false));
    }

    [Fact]
    public void TestInvalidCharacter()
    {
        Assert.Throws<NotSupportedException>(() => fs.CreateDirectory("/toto/ta:ta"));
    }
#if !NET472
    [Fact]
    public void TestFileAttributes()
    {
        fs.WriteAllText("/toto.txt", "content");
        fs.SetAttributes("/toto.txt", 0);
        Assert.Equal(FileAttributes.Normal, fs.GetAttributes("/toto.txt"));

        fs.CreateDirectory("/dir");
        fs.SetAttributes("/dir", 0);
        Assert.Equal(FileAttributes.Directory, fs.GetAttributes("/dir"));
    }
#endif
}
