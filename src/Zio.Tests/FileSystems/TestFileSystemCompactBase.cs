// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zio;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public abstract class TestFileSystemCompactBase : TestFileSystemBase
    {
        protected IFileSystem fs;

        protected TestFileSystemCompactBase()
        {
        }

        [Fact]
        public async ValueTask TestDirectory()
        {
            Assert.True(await fs.DirectoryExists("/"));
            Assert.False(await fs.DirectoryExists(null));

            // Test CreateDirectory
            await fs.CreateDirectory("/test");
            Assert.True(await fs.DirectoryExists("/test"));
            Assert.False(await fs.DirectoryExists("/test2"));

            // Test CreateDirectory (sub folders)
            await fs.CreateDirectory("/test/test1/test2/test3");
            Assert.True(await fs.DirectoryExists("/test/test1/test2/test3"));
            Assert.True(await fs.DirectoryExists("/test/test1/test2"));
            Assert.True(await fs.DirectoryExists("/test/test1"));
            Assert.True(await fs.DirectoryExists("/test"));

            // Test DeleteDirectory
            await fs.DeleteDirectory("/test/test1/test2/test3", false);
            Assert.False(await fs.DirectoryExists("/test/test1/test2/test3"));
            Assert.True(await fs.DirectoryExists("/test/test1/test2"));
            Assert.True(await fs.DirectoryExists("/test/test1"));
            Assert.True(await fs.DirectoryExists("/test"));

            // Test MoveDirectory
            await fs.MoveDirectory("/test", "/test2");
            Assert.True(await fs.DirectoryExists("/test2/test1/test2"));
            Assert.True(await fs.DirectoryExists("/test2/test1"));
            Assert.True(await fs.DirectoryExists("/test2"));

            // Test MoveDirectory to sub directory
            await fs.CreateDirectory("/testsub");
            Assert.True(await fs.DirectoryExists("/testsub"));
            await fs.MoveDirectory("/test2", "/testsub/testx");
            Assert.False(await fs.DirectoryExists("/test2"));
            Assert.True(await fs.DirectoryExists("/testsub/testx/test1/test2"));
            Assert.True(await fs.DirectoryExists("/testsub/testx/test1"));
            Assert.True(await fs.DirectoryExists("/testsub/testx"));

            // Test DeleteDirectory - recursive
            await fs.DeleteDirectory("/testsub", true);
            Assert.False(await fs.DirectoryExists("/testsub/testx/test1/test2"));
            Assert.False(await fs.DirectoryExists("/testsub/testx/test1"));
            Assert.False(await fs.DirectoryExists("/testsub/testx"));
            Assert.False(await fs.DirectoryExists("/testsub"));
        }

        [Fact]
        public async ValueTask TestDirectoryExceptions()
        {
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.DeleteDirectory("/dir", true));

            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.MoveDirectory("/dir1", "/dir2"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.CreateDirectory("/"));

            await fs.CreateDirectory("/dir1");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.DeleteFile("/dir1"));
            await Assert.ThrowsAsync<IOException>(async () => await fs.MoveDirectory("/dir1", "/dir1"));

            await fs.WriteAllText("/toto.txt", "test");
            await Assert.ThrowsAsync<IOException>(async () => await fs.CreateDirectory("/toto.txt"));
            await Assert.ThrowsAsync<IOException>(async () => await fs.DeleteDirectory("/toto.txt", true));
            await Assert.ThrowsAsync<IOException>(async () => await fs.MoveDirectory("/toto.txt", "/test"));

            await fs.CreateDirectory("/dir2");
            await Assert.ThrowsAsync<IOException>(async () => await fs.MoveDirectory("/dir1", "/dir2"));

            await fs.SetAttributes("/dir1", FileAttributes.Directory | FileAttributes.ReadOnly);
            await Assert.ThrowsAsync<IOException>(async () => await fs.DeleteDirectory("/dir1", true));
        }

        [Fact]
        public async ValueTask TestFile()
        {
            // Test CreateFile/OpenFile
            var stream = await fs.CreateFile("/toto.txt");
            var writer = new StreamWriter(stream);
            var originalContent = "This is the content";
            writer.Write(originalContent);
            writer.Flush();
            stream.Dispose();

            // Test FileExists
            Assert.False(await fs.FileExists(null));
            Assert.False(await fs.FileExists("/titi.txt"));
            Assert.True(await fs.FileExists("/toto.txt"));

            // ReadAllText
            var content = await fs.ReadAllText("/toto.txt");
            Assert.Equal(originalContent, content);

            // sleep for creation time comparison
            Thread.Sleep(16);

            // Test CopyFile
            await fs.CopyFile("/toto.txt", "/titi.txt", true);
            Assert.True(await fs.FileExists("/toto.txt"));
            Assert.True(await fs.FileExists("/titi.txt"));
            content = await fs.ReadAllText("/titi.txt");
            Assert.Equal(originalContent, content);
            
            // Test Attributes/Times
            Assert.True(await fs.GetFileLength("/toto.txt") > 0);
            Assert.Equal(await fs.GetFileLength("/toto.txt"), await fs.GetFileLength("/titi.txt"));
            Assert.Equal(await fs.GetAttributes("/toto.txt"), await fs.GetAttributes("/titi.txt"));
            Assert.NotEqual(await fs.GetCreationTime("/toto.txt"), await fs.GetCreationTime("/titi.txt"));
            // Because we read titi.txt just before, access time must be different
            // Following test is disabled as it seems unstable with NTFS?
            // Assert.NotEqual(fs.GetLastAccessTime("/toto.txt"), fs.GetLastAccessTime("/titi.txt"));
            Assert.Equal(await fs.GetLastWriteTime("/toto.txt"), await fs.GetLastWriteTime("/titi.txt"));

            var now = DateTime.Now + TimeSpan.FromSeconds(10);
            var now1 = DateTime.Now + TimeSpan.FromSeconds(11);
            var now2 = DateTime.Now + TimeSpan.FromSeconds(12);
            await fs.SetCreationTime("/toto.txt", now);
            await fs.SetLastAccessTime("/toto.txt", now1);
            await fs.SetLastWriteTime("/toto.txt", now2);
            Assert.Equal(now, await fs.GetCreationTime("/toto.txt"));
            Assert.Equal(now1, await fs.GetLastAccessTime("/toto.txt"));
            Assert.Equal(now2, await fs.GetLastWriteTime("/toto.txt"));

            Assert.NotEqual(await fs.GetCreationTime("/toto.txt"), await fs.GetCreationTime("/titi.txt"));
            Assert.NotEqual(await fs.GetLastAccessTime("/toto.txt"), await fs.GetLastAccessTime("/titi.txt"));
            Assert.NotEqual(await fs.GetLastWriteTime("/toto.txt"), await fs.GetLastWriteTime("/titi.txt"));

            // Test MoveFile
            await fs.MoveFile("/toto.txt", "/tata.txt");
            Assert.False(await fs.FileExists("/toto.txt"));
            Assert.True(await fs.FileExists("/tata.txt"));
            Assert.True(await fs.FileExists("/titi.txt"));
            content = await fs.ReadAllText("/tata.txt");
            Assert.Equal(originalContent, content);

            // Test Enumerate file
            var files = (await fs.EnumerateFiles("/")).Select(p => p.FullName).ToList();
            files.Sort();
            Assert.Equal(new List<string>() {"/tata.txt", "/titi.txt"}, files);

            var dirs = (await fs.EnumerateDirectories("/")).Select(p => p.FullName).ToList();
            Assert.Empty(dirs);

            // Check ReplaceFile
            var originalContent2 = "this is a content2";
            await fs.WriteAllText("/tata.txt", originalContent2);
            await fs.ReplaceFile("/tata.txt", "/titi.txt", "/titi.bak.txt", true);
            Assert.False(await fs.FileExists("/tata.txt"));
            Assert.True(await fs.FileExists("/titi.txt"));
            Assert.True(await fs.FileExists("/titi.bak.txt"));
            content = await fs.ReadAllText("/titi.txt");
            Assert.Equal(originalContent2, content);
            content = await fs.ReadAllText("/titi.bak.txt");
            Assert.Equal(originalContent, content);

            // Check File ReadOnly
            await fs.SetAttributes("/titi.txt", FileAttributes.ReadOnly);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.DeleteFile("/titi.txt"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.CopyFile("/titi.bak.txt", "/titi.txt", true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.OpenFile("/titi.txt", FileMode.Open, FileAccess.ReadWrite));
            await fs.SetAttributes("/titi.txt", FileAttributes.Normal);

            // Delete File
            await fs.DeleteFile("/titi.txt");
            Assert.False(await fs.FileExists("/titi.txt"));
            await fs.DeleteFile("/titi.bak.txt");
            Assert.False(await fs.FileExists("/titi.bak.txt"));
        }

        [Fact]
        public async ValueTask TestMoveFileDifferentDirectory()
        {
            await fs.WriteAllText("/toto.txt", "content");

            await fs.CreateDirectory("/dir");

            await fs.MoveFile("/toto.txt", "/dir/titi.txt");

            Assert.False(await fs.FileExists("/toto.txt"));
            Assert.True(await fs.FileExists("/dir/titi.txt"));

            Assert.Equal("content", await fs.ReadAllText("/dir/titi.txt"));
        }

        [Fact]
        public async ValueTask TestReplaceFileDifferentDirectory()
        {
            await fs.WriteAllText("/toto.txt", "content");

            await fs.CreateDirectory("/dir");
            await fs.WriteAllText("/dir/tata.txt", "content2");

            await fs.CreateDirectory("/dir2");

            await fs.ReplaceFile("/toto.txt", "/dir/tata.txt", "/dir2/titi.txt", true);
            Assert.True(await fs.FileExists("/dir/tata.txt"));
            Assert.True(await fs.FileExists("/dir2/titi.txt"));

            Assert.Equal("content", await fs.ReadAllText("/dir/tata.txt"));
            Assert.Equal("content2", await fs.ReadAllText("/dir2/titi.txt"));

            await fs.ReplaceFile("/dir/tata.txt", "/dir2/titi.txt", "/titi.txt", true);
            Assert.False(await fs.FileExists("/dir/tata.txt"));
            Assert.True(await fs.FileExists("/dir2/titi.txt"));
            Assert.True(await fs.FileExists("/titi.txt"));
        }

        [Fact]
        public async ValueTask TestOpenFileAppend()
        {
            await fs.AppendAllText("/toto.txt", "content");
            Assert.True(await fs.FileExists("/toto.txt"));
            Assert.Equal("content", await fs.ReadAllText("/toto.txt"));

            await fs.AppendAllText("/toto.txt", "content");
            Assert.True(await fs.FileExists("/toto.txt"));
            Assert.Equal("contentcontent", await fs.ReadAllText("/toto.txt"));
        }

        [Fact]
        public async ValueTask TestOpenFileTruncate()
        {
            await fs.WriteAllText("/toto.txt", "content");
            Assert.True(await fs.FileExists("/toto.txt"));
            Assert.Equal("content", await fs.ReadAllText("/toto.txt"));

            var stream = await fs.OpenFile("/toto.txt", FileMode.Truncate, FileAccess.Write);
            stream.Dispose();
            Assert.Equal<long>(0, await fs.GetFileLength("/toto.txt"));
            Assert.Equal("", await fs.ReadAllText("/toto.txt"));
        }

        [Fact]
        public async ValueTask TestFileExceptions()
        {
            await fs.CreateDirectory("/dir1");

            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.GetFileLength("/toto.txt"));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.CopyFile("/toto.txt", "/toto.bak.txt", true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.CopyFile("/dir1", "/toto.bak.txt", true));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.MoveFile("/toto.txt", "/titi.txt"));
            // If the file to be deleted does not exist, no exception is thrown.
            await fs.DeleteFile("/toto.txt");
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.OpenFile("/toto.txt", FileMode.Truncate, FileAccess.Write));

            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.GetFileLength("/dir1/toto.txt"));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.CopyFile("/dir1/toto.txt", "/toto.bak.txt", true));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.MoveFile("/dir1/toto.txt", "/titi.txt"));
            // If the file to be deleted does not exist, no exception is thrown.
            await fs.DeleteFile("/dir1/toto.txt");
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.OpenFile("/dir1/toto.txt", FileMode.Open, FileAccess.Read));

            await fs.WriteAllText("/toto.txt", "yo");
            await fs.CopyFile("/toto.txt", "/titi.txt", false);
            await fs.CopyFile("/toto.txt", "/titi.txt", true);

            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.GetFileLength("/dir1"));

            var defaultTime = new DateTime(1601, 01, 01, 0, 0, 0, DateTimeKind.Utc).ToLocalTime();
            Assert.Equal(defaultTime, await fs.GetCreationTime("/dest"));
            Assert.Equal(defaultTime, await fs.GetLastWriteTime("/dest"));
            Assert.Equal(defaultTime, await fs.GetLastAccessTime("/dest"));

            using (var stream1 = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Assert.ThrowsAsync<IOException>(async () =>
                {
                    using (var stream2 = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                    {
                    }
                });
            }

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.OpenFile("/dir1", FileMode.Open, FileAccess.Read));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.OpenFile("/dir/toto.txt", FileMode.Open, FileAccess.Read));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.CopyFile("/toto.txt", "/dest/toto.txt", true));
            await Assert.ThrowsAsync<IOException>(async () => await fs.CopyFile("/toto.txt", "/titi.txt", false));
            await Assert.ThrowsAsync<IOException>(async () => await fs.CopyFile("/toto.txt", "/dir1", true));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.MoveFile("/toto.txt", "/dest/toto.txt"));

            await fs.WriteAllText("/titi.txt", "yo2");
            await Assert.ThrowsAsync<IOException>(async () => await fs.MoveFile("/toto.txt", "/titi.txt"));

            await Assert.ThrowsAsync<FileNotFoundException>(async() => await fs.ReplaceFile("/1.txt", "/1.txt", default(UPath), true));
            await Assert.ThrowsAsync<FileNotFoundException>(async() => await fs.ReplaceFile("/1.txt", "/2.txt", "/1.txt", true));
            await Assert.ThrowsAsync<FileNotFoundException>(async() => await fs.ReplaceFile("/1.txt", "/2.txt", "/2.txt", true));
            await Assert.ThrowsAsync<FileNotFoundException>(async() => await fs.ReplaceFile("/1.txt", "/2.txt", "/3.txt", true));
            await Assert.ThrowsAsync<FileNotFoundException>(async() => await fs.ReplaceFile("/toto.txt", "/dir/2.txt", "/3.txt", true));
            await Assert.ThrowsAsync<FileNotFoundException>(async() => await fs.ReplaceFile("/toto.txt", "/2.txt", "/3.txt", true));
            await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.ReplaceFile("/toto.txt", "/2.txt", "/toto.txt", true));

            // Not same behavior in Physical vs Memory
            if (fs is MemoryFileSystem)
            {
                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.ReplaceFile("/toto.txt", "/titi.txt", "/dir/3.txt", true));

                await fs.WriteAllText("/tata.txt", "yo3");
                Assert.True(await fs.FileExists("/tata.txt"));
                await fs.ReplaceFile("/toto.txt", "/titi.txt", "/tata.txt", true);
                // TODO: check that tata.txt was correctly removed
            }
        }

        [Fact]
        public async ValueTask TestDirectoryDeleteAndOpenFile()
        {
            await fs.CreateDirectory("/dir");
            await fs.WriteAllText("/dir/toto.txt", "content");
            var stream = await fs.OpenFile("/dir/toto.txt", FileMode.Open, FileAccess.Read);

            await Assert.ThrowsAsync<IOException>(async () => await fs.DeleteFile("/dir/toto.txt"));
            await Assert.ThrowsAsync<IOException>(async () => await fs.DeleteDirectory("/dir", true));

            stream.Dispose();
            await fs.SetAttributes("/dir/toto.txt", FileAttributes.ReadOnly);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.DeleteDirectory("/dir", true));
            await fs.SetAttributes("/dir/toto.txt", FileAttributes.Normal);
            await fs.DeleteDirectory("/dir", true);

            var entries = (await fs.EnumeratePaths("/")).ToList();
            Assert.Empty(entries);
        }

        [Fact]
        public async ValueTask TestOpenFileMultipleRead()
        {
            await fs.WriteAllText("/toto.txt", "content");

            Assert.True(await fs.FileExists("/toto.txt"));

            using (var tmp = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read))
            {
                 await Assert.ThrowsAsync<IOException>(async () => await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read));
            }

            var stream1 = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read);
            var stream2 = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read);

            stream1.ReadByte();
            Assert.Equal<long>(1, stream1.Position);

            stream2.ReadByte();
            stream2.ReadByte();
            Assert.Equal<long>(2, stream2.Position);

            stream1.Dispose();
            stream2.Dispose();

            // We try to write back on the same file after closing
            await fs.WriteAllText("/toto.txt", "content2");
        }

        [Fact]
        public async ValueTask TestOpenFileReadAndWriteFail()
        {
            await fs.WriteAllText("/toto.txt", "content");

            Assert.True(await fs.FileExists("/toto.txt"));

            var stream1 = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read);

            stream1.ReadByte();
            Assert.Equal<long>(1, stream1.Position);

            // We try to write back on the same file before closing
            await Assert.ThrowsAsync<IOException>(async () => await fs.WriteAllText("/toto.txt", "content2"));

            // Make sure that checking for a file exists or directory exists doesn't throw an exception "being used"
            Assert.True(await fs.FileExists("/toto.txt"));
            Assert.False(await fs.DirectoryExists("/toto.txt"));

            stream1.Dispose();
        }

        [Fact]
        public async ValueTask TestOpenFileReadAndWriteShared()
        {
            using (var stream1 = await fs.OpenFile("/toto.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
            using (var stream2 = await fs.OpenFile("/toto.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                var buffer = Encoding.UTF8.GetBytes("abc");
                stream1.Write(buffer, 0, buffer.Length);
                stream1.Flush();

                buffer = Encoding.UTF8.GetBytes("d");
                stream2.Position = 1;
                stream2.Write(buffer, 0, buffer.Length);
                stream2.Flush();
            }

            var content = await fs.ReadAllText("/toto.txt");
            Assert.Equal("adc", content);
        }

        [Fact]
        public async ValueTask TestOpenFileReadAndWriteShared2()
        {
            await fs.WriteAllText("/toto.txt", "content");
            // No exceptions
            using (var stream1 = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var stream2 = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                }
            }
            var content = await fs.ReadAllText("/toto.txt");
            Assert.Equal("content", content);
        }

        [Fact]
        public async ValueTask TestCopyFileToSameFile()
        {
            await fs.WriteAllText("/toto.txt", "content");
            await Assert.ThrowsAsync<IOException>(async () => await fs.CopyFile("/toto.txt", "/toto.txt", true));
            await Assert.ThrowsAsync<IOException>(async () => await fs.CopyFile("/toto.txt", "/toto.txt", false));

            await fs.CreateDirectory("/dir");

            await fs.WriteAllText("/dir/toto.txt", "content");
            await Assert.ThrowsAsync<IOException>(async () => await fs.CopyFile("/dir/toto.txt", "/dir/toto.txt", true));
            await Assert.ThrowsAsync<IOException>(async () => await fs.CopyFile("/dir/toto.txt", "/dir/toto.txt", false));
        }

        [Fact]
        public async ValueTask TestEnumeratePaths()
        {
            await fs.CreateDirectory("/dir1/a/b");
            await fs.CreateDirectory("/dir1/a1");
            await fs.CreateDirectory("/dir2/c");
            await fs.CreateDirectory("/dir3");

            await fs.WriteAllText("/dir1/a/file10.txt", "content10");
            await fs.WriteAllText("/dir1/a1/file11.txt", "content11");
            await fs.WriteAllText("/dir2/file20.txt", "content20");

            await fs.WriteAllText("/file01.txt", "content1");
            await fs.WriteAllText("/file02.txt", "content2");

            var entries = (await fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Both)).ToList<UPath>();
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


            var folders = (await fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Directory)).ToList<UPath>();
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


            var files = (await fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.File)).ToList<UPath>();
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


            folders = (await fs.EnumeratePaths("/dir1", "a", SearchOption.AllDirectories, SearchTarget.Directory)).ToList<UPath>();
            folders.Sort();
            Assert.Equal(new List<UPath>()
                {
                    "/dir1/a",
                }
                , folders);


            files = (await fs.EnumeratePaths("/dir1", "file1?.txt", SearchOption.AllDirectories, SearchTarget.File)).ToList<UPath>();
            files.Sort();

            Assert.Equal(new List<UPath>()
                {
                    "/dir1/a/file10.txt",
                    "/dir1/a1/file11.txt",
                }
                , files);

            files = (await fs.EnumeratePaths("/", "file?0.txt", SearchOption.AllDirectories, SearchTarget.File)).ToList<UPath>();
            files.Sort();

            Assert.Equal(new List<UPath>()
                {
                    "/dir1/a/file10.txt",
                    "/dir2/file20.txt",
                }
                , files);
        }

        [Fact]
        public async ValueTask TestMultithreaded()
        {
            await fs.CreateDirectory("/dir1");
            await fs.WriteAllText("/toto.txt", "content");

            const int CountTest = 200;

            var thread1 = new Thread(async () =>
            {
                for (int i = 0; i < CountTest; i++)
                {
                    await fs.CopyFile("/toto.txt", "/titi.txt", true);
                    await fs.MoveFile("/titi.txt", "/tata.txt");
                    await fs.MoveFile("/tata.txt", "/dir1/tata.txt");

                    if (await fs.FileExists("/dir1/tata.txt"))
                    {
                        await fs.DeleteFile("/dir1/tata.txt");
                    }
                }
            });
            var thread2 = new Thread(async () =>
            {
                for (int i = 0; i < CountTest; i++)
                {
                    (await fs.EnumeratePaths("/")).ToList();
                }
            });

            var thread3 = new Thread(async () =>
            {
                for (int i = 0; i < CountTest; i++)
                {
                    await fs.CreateDirectory("/dir2");
                    await fs.MoveDirectory("/dir2", "/dir1/dir3");
                    await fs.DeleteDirectory("/dir1/dir3", true);

                    (await fs.CreateFile("/0.txt")).Dispose();
                    await fs.DeleteFile("/0.txt");
                }
            });

            thread1.Start();
            thread2.Start();
            thread3.Start();

            thread1.Join();
            thread2.Join();
            thread3.Join();

            await fs.DeleteDirectory("/dir1", true);
            await fs.DeleteFile("/toto.txt");
        }

        [Fact]
        public async ValueTask TestOpenFileAppendAndRead()
        {
            await fs.WriteAllText("/toto.txt", "content");

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                using (var stream = await fs.OpenFile("/toto.txt", FileMode.Append, FileAccess.Read))
                {
                }
            });
        }

        [Fact]
        public async ValueTask TestOpenFileCreateNewAlreadyExist()
        {
            await fs.WriteAllText("/toto.txt",  "content");

            await Assert.ThrowsAsync<IOException>(async () =>
            {
                using (var stream = await fs.OpenFile("/toto.txt", FileMode.CreateNew, FileAccess.Write))
                {
                }
            });

            await Assert.ThrowsAsync<IOException>(async () =>
            {
                using (var stream = await fs.OpenFile("/toto.txt", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
                {
                }
            });
        }

        [Fact]
        public async ValueTask TestOpenFileCreate()
        {
            await fs.WriteAllText("/toto.txt", "content");

            using (var stream = await fs.OpenFile("/toto.txt", FileMode.Create, FileAccess.Write))
            {
            }

            Assert.Equal(0, await fs.GetFileLength("/toto.txt"));
        }

        [Fact]
        public async ValueTask TestMoveDirectorySubFolderFail()
        {
            await fs.CreateDirectory("/dir");
            await fs.CreateDirectory("/dir/dir1");

            await Assert.ThrowsAsync<IOException>(async () => await fs.MoveDirectory("/dir", "/dir/dir1/dir2"));
        }

        [Fact]
        public async ValueTask TestReplaceFileSameFileFail()
        {
            await fs.WriteAllText("/toto.txt", "content");
            await Assert.ThrowsAsync<IOException>(async () => await fs.ReplaceFile("/toto.txt", "/toto.txt", null, true));

            await fs.WriteAllText("/tata.txt", "content2");

            await Assert.ThrowsAsync<IOException>(async () => await fs.ReplaceFile("/toto.txt", "/tata.txt", "/toto.txt", true));
        }

        [Fact]
        public async ValueTask TestStreamSeek()
        {
            //                            0123456
            await fs.WriteAllText("/toto.txt", "content", Encoding.ASCII);

            using (var stream = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read))
            {
                stream.Seek(0, SeekOrigin.Begin);
                Assert.Equal((byte) 'c', stream.ReadByte());
                Assert.Equal((byte) 'o', stream.ReadByte());

                stream.Seek(3, SeekOrigin.Begin);
                Assert.Equal((byte) 't', stream.ReadByte());

                stream.Seek(1, SeekOrigin.Current);
                Assert.Equal((byte) 'n', stream.ReadByte());

                stream.Seek(-3, SeekOrigin.End);
                Assert.Equal((byte) 'e', stream.ReadByte());

                Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
                Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
                stream.Position = 0;
                Assert.Equal((byte) 'c', stream.ReadByte());
            }
        }

        [Fact]
        public async ValueTask TestDispose()
        {
            await fs.WriteAllText("/toto.txt", "content");
            var stream = await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.ReadWrite);
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
        public async ValueTask TestDeleteDirectoryNonEmpty()
        {
            await fs.CreateDirectory("/dir/dir1");
            Assert.Throws<IOException>(() => fs.DeleteDirectory("/dir", false));
        }

        [Fact]
        public async ValueTask TestInvalidCharacter()
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () => await fs.CreateDirectory("/toto/ta:ta"));
        }

        [Fact]
        public async ValueTask TestFileAttributes()
        {
            await fs.WriteAllText("/toto.txt", "content");
            await fs.SetAttributes("/toto.txt", 0);
            Assert.Equal(FileAttributes.Normal, await fs.GetAttributes("/toto.txt"));

            await fs.CreateDirectory("/dir");
            await fs.SetAttributes("/dir", 0);
            Assert.Equal(FileAttributes.Directory, await fs.GetAttributes("/dir"));
        }
    }
}