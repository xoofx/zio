// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestPhysicalFileSystem : TestFileSystemBase
    {
        [Fact]
        public async ValueTask TestCommonRead()
        {
            var fs = await GetCommonPhysicalFileSystem();
            await AssertCommonRead(fs);
        }

        [Fact]
        public async ValueTask TestFileSystemInvalidDriveLetter()
        {
            var driverLetter = SystemPath[0];
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await SubFileSystem.Create(new PhysicalFileSystem(), $"/mnt/{driverLetter}"));
            using (var fs = await SubFileSystem.Create(new PhysicalFileSystem(), $"/mnt/{char.ToLowerInvariant(driverLetter)}"))
            {
            }
        }

        [Fact]
        public async ValueTask TestWatcher()
        {
            var fs = await GetCommonPhysicalFileSystem();
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

            await fs.WriteAllText("/a/watched.txt", "test");
            System.Threading.Thread.Sleep(100);
            Assert.True(gotChange);
        }

        [Fact]
        public async ValueTask TestCopyFileCross()
        {
            // TODO: Add more tests
            var from = new MemoryFileSystem();
            await from.WriteAllText("/test.txt", "test");
            var fs = new PhysicalFileSystem();
            var outputfs = await SubFileSystem.Create(fs, fs.ConvertPathFromInternal(SystemPath));
            var outputPath = (UPath)"/test.txt";
            try
            {
                await outputfs.WriteAllText(outputPath, "toto");
                await from.CopyFileCross("/test.txt", outputfs, outputPath, true);
                var content = await outputfs.ReadAllText(outputPath);
                Assert.Equal("test", content);
            }
            finally
            {
                await outputfs.DeleteFile(outputPath);
            }
        }

        [Fact]
        public async ValueTask TestDirectory()
        {
            var fs = new PhysicalFileSystem();
            var pathInfo = fs.ConvertPathFromInternal(SystemPath);
            var pathToCreate = pathInfo / "TestCreateDirectory";
            var systemPathToCreate = fs.ConvertPathToInternal(pathToCreate);
            var movedDirectory = pathInfo / "TestCreateDirectoryMoved";
            var systemMovedDirectory = fs.ConvertPathToInternal(movedDirectory);
            try
            {
                // CreateDirectory
                Assert.False(Directory.Exists(systemPathToCreate));
                await fs.CreateDirectory(pathToCreate);
                Assert.True(Directory.Exists(systemPathToCreate));

                // DirectoryExists
                Assert.True(await fs.DirectoryExists(pathToCreate));
                Assert.False(await fs.DirectoryExists(pathToCreate / "not_found"));

                // MoveDirectory
                await fs.MoveDirectory(pathToCreate, movedDirectory);
                Assert.False(Directory.Exists(systemPathToCreate));
                Assert.True(await fs.DirectoryExists(movedDirectory));

                // Delete the directory
                await fs.DeleteDirectory(movedDirectory, false);
                Assert.False(Directory.Exists(systemMovedDirectory));
            }
            finally
            {
                SafeDeleteDirectory(systemPathToCreate);
                SafeDeleteDirectory(systemMovedDirectory);
            }
        }

        [Fact]
        public async ValueTask TestDirectorySpecial()
        {
            var fs = new PhysicalFileSystem();
            // CreateDirectory
            Assert.True(await fs.DirectoryExists("/"));
            if (IsWindows)
            {
                var directories = (await fs.EnumerateDirectories("/")).ToList();
                Assert.Equal(new List<UPath>() { "/mnt" }, directories);

                var drives = (await fs.EnumerateDirectories("/mnt")).ToList();
                Assert.True(drives.Count > 0);

                var allDrives = DriveInfo.GetDrives().Select(d => d.Name[0].ToString().ToLowerInvariant()).ToList();
                var driveNames = drives.Select(d => d.GetName()).ToList();
                Assert.Equal(allDrives, driveNames);

                Assert.True(await fs.DirectoryExists("/"));
                Assert.True(await fs.DirectoryExists("/mnt"));
                Assert.True(await fs.DirectoryExists(drives[0]));

                var files = (await fs.EnumerateFiles("/")).ToList();
                Assert.True(files.Count == 0);

                files = (await fs.EnumerateFiles("/mnt")).ToList();
                Assert.True(files.Count == 0);

                var paths = (await fs.EnumeratePaths("/")).ToList();
                Assert.Equal(new List<UPath>() { "/mnt" }, paths);
            }
        }

        [Fact]
        public async ValueTask TestDirectoryExceptions()
        {
            var fs = new PhysicalFileSystem();
            // Try to create a folder on an unauthorized location
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt2"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt/yoyo"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt/c"));

            var drives = (await fs.EnumerateDirectories("/mnt")).ToList();
            Assert.True(drives.Count > 0);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.MoveDirectory("/", drives[0] / "ShouldNotHappen"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.MoveDirectory("/mnt", drives[0] / "ShouldNotHappen"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.MoveDirectory("/mnt2", drives[0] / "ShouldNotHappen"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.MoveDirectory(drives[0] / "ShouldNotHappen", "/"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.MoveDirectory(drives[0] / "ShouldNotHappen", "/mnt"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.MoveDirectory(drives[0] / "ShouldNotHappen", "/mnt2"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.DeleteDirectory("/", false));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.DeleteDirectory("/mnt", false));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.DeleteDirectory("/mnt2", false));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.DeleteDirectory("/mnt/yoyo", false));
        }

        [Fact]
        public async ValueTask TestFile()
        {
            var fs = new PhysicalFileSystem();
            var path = fs.ConvertPathFromInternal(SystemPath);
            var fileName = $"toto-{Guid.NewGuid()}.txt";
            var filePath = path / fileName;
            var filePathDest = path / Path.ChangeExtension(fileName, "dest");
            var filePathBack = path / Path.ChangeExtension(fileName, "bak");
            var systemFilePath = Path.Combine(SystemPath, fileName);
            var systemFilePathDest = fs.ConvertPathToInternal(filePathDest);
            var systemFilePathBack = fs.ConvertPathToInternal(filePathBack);
            try
            {
                // CreateFile / OpenFile
                var fileStream = await fs.CreateFile(filePath);
                var buffer = Encoding.UTF8.GetBytes("This is a test");
                fileStream.Write(buffer, 0, buffer.Length);
                fileStream.Dispose();

                // FileLength
                Assert.Equal(buffer.Length, await fs.GetFileLength(filePath));

                // LastAccessTime
                // LastWriteTime
                // CreationTime
                Assert.Equal(File.GetLastWriteTime(systemFilePath), await fs.GetLastWriteTime(filePath));
                Assert.Equal(File.GetLastAccessTime(systemFilePath), await fs.GetLastAccessTime(filePath));
                Assert.Equal(File.GetCreationTime(systemFilePath), await fs.GetCreationTime(filePath));

                var lastWriteTime = DateTime.Now + TimeSpan.FromSeconds(10);
                var lastAccessTime = DateTime.Now + TimeSpan.FromSeconds(11);
                var creationTime = DateTime.Now + TimeSpan.FromSeconds(12);
                await fs.SetLastWriteTime(filePath, lastWriteTime);
                await fs.SetLastAccessTime(filePath, lastAccessTime);
                await fs.SetCreationTime(filePath, creationTime);
                Assert.Equal(lastWriteTime, await fs.GetLastWriteTime(filePath));
                Assert.Equal(lastAccessTime, await fs.GetLastAccessTime(filePath));
                Assert.Equal(creationTime, await fs.GetCreationTime(filePath));

                // FileAttributes
                Assert.Equal(File.GetAttributes(systemFilePath), await fs.GetAttributes(filePath));

                var attributes = await fs.GetAttributes(filePath);
                attributes |= FileAttributes.ReadOnly;
                await fs.SetAttributes(filePath, attributes);

                Assert.Equal(File.GetAttributes(systemFilePath), await fs.GetAttributes(filePath));

                attributes &= ~FileAttributes.ReadOnly;
                await fs.SetAttributes(filePath, attributes);
                Assert.Equal(File.GetAttributes(systemFilePath), await fs.GetAttributes(filePath));

                // FileExists
                Assert.True(File.Exists(systemFilePath));
                Assert.True(await fs.FileExists(filePath));

                // CopyFile
                await fs.CopyFile(filePath, filePathDest, true);
                Assert.True(await fs.FileExists(filePathDest));

                // DeleteFile
                await fs.DeleteFile(filePath);
                Assert.False(File.Exists(systemFilePath));
                Assert.False(await fs.FileExists(filePath));

                // MoveFile
                await fs.MoveFile(filePathDest, filePath);
                Assert.False(File.Exists(systemFilePathDest));
                Assert.False(await fs.FileExists(filePathDest));
                Assert.True(File.Exists(systemFilePath));
                Assert.True(await fs.FileExists(filePath));

                // ReplaceFile

                // copy file to filePathDest
                await fs.CopyFile(filePath, filePathDest, true);

                // Change src file
                var filestream2 = await fs.OpenFile(filePath, FileMode.Open, FileAccess.ReadWrite);
                var buffer2 = Encoding.UTF8.GetBytes("This is a test 123");
                filestream2.Write(buffer2, 0, buffer2.Length);
                filestream2.Dispose();
                Assert.Equal(buffer2.Length, await fs.GetFileLength(filePath));

                // Perform ReplaceFile
                await fs.ReplaceFile(filePath, filePathDest, filePathBack, true);
                Assert.False(await fs.FileExists(filePath));
                Assert.True(await fs.FileExists(filePathDest));
                Assert.True(await fs.FileExists(filePathBack));

                Assert.Equal(buffer2.Length, await fs.GetFileLength(filePathDest));
                Assert.Equal(buffer.Length, await fs.GetFileLength(filePathBack));

                // RootFileSystem
                await fs.GetLastWriteTime("/");
                await fs.GetLastAccessTime("/");
                await fs.GetCreationTime("/");

                await fs.GetLastWriteTime("/mnt");
                await fs.GetLastAccessTime("/mnt");
                await fs.GetCreationTime("/mnt");

                await fs.GetLastWriteTime("/mnt/c");
                await fs.GetLastAccessTime("/mnt/c");
                await fs.GetCreationTime("/mnt/c");
                await fs.GetAttributes("/mnt/c");

                var sysAttr = FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly;
                Assert.True((await fs.GetAttributes("/") & (sysAttr)) == sysAttr);
                Assert.True((await fs.GetAttributes("/mnt") & (sysAttr)) == sysAttr);
            }
            finally
            {
                SafeDeleteFile(systemFilePath);
                SafeDeleteFile(systemFilePathDest);
                SafeDeleteFile(systemFilePathBack);
            }
        }

        [Fact]
        public async ValueTask TestEnumerate()
        {
            var fs = new PhysicalFileSystem();
            var path = fs.ConvertPathFromInternal(SystemPath);

            var files = (await fs.EnumerateFiles(path)).Select(p => fs.ConvertPathToInternal(p)).ToList();
            var expectedfiles = Directory.EnumerateFiles(SystemPath).ToList();
            Assert.Equal(expectedfiles, files);

            var dirs = (await fs.EnumerateDirectories(path / "../../..")).Select(p => fs.ConvertPathToInternal(p)).ToList();
            var expecteddirs = Directory.EnumerateDirectories(Path.GetFullPath(Path.Combine(SystemPath, "..\\..\\.."))).ToList();
            Assert.Equal(expecteddirs, dirs);

            var paths = (await fs.EnumeratePaths(path / "../..")).Select(p => fs.ConvertPathToInternal(p)).ToList();
            var expectedPaths = Directory.EnumerateFileSystemEntries(Path.GetFullPath(Path.Combine(SystemPath, "..\\.."))).ToList();
            Assert.Equal(expectedPaths, paths);
        }

        [Fact]
        public async ValueTask TestFileExceptions()
        {
            var fs = new PhysicalFileSystem();
            var path = fs.ConvertPathFromInternal(SystemPath);
            var fileName = $"toto-{Guid.NewGuid()}.txt";
            var filePath = path / fileName;
            (await fs.CreateFile(filePath)).Dispose();
            var filePathNotExist = path / "FileDoesNotExist.txt";
            var systemFilePath = Path.Combine(SystemPath, fileName);

            // Try to create a folder on an unauthorized location
            try
            {
                // CreateFile
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.CreateFile("/toto.txt"));

                // Length
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.GetFileLength("/toto.txt"));

                // ConvertPathFromInternal / ConvertPathToInternal
                Assert.Throws<NotSupportedException>(() => fs.ConvertPathFromInternal(@"\\network\toto.txt"));
                Assert.Throws<NotSupportedException>(() => fs.ConvertPathFromInternal(@"zx:\toto.txt"));

                Assert.Throws<ArgumentException>(() => fs.ConvertPathToInternal(@"/toto.txt"));
                Assert.Throws<ArgumentException>(() => fs.ConvertPathToInternal(@"/mnt/yo/toto.txt"));

                // LastWriteTime, LastAccessTime, CreationTime
                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.GetLastWriteTime("/toto.txt"));
                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.GetLastAccessTime("/toto.txt"));
                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.GetCreationTime("/toto.txt"));

                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.SetLastWriteTime("/", DateTime.Now));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.SetLastAccessTime("/", DateTime.Now));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.SetCreationTime("/", DateTime.Now));

                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.SetLastWriteTime("/mnt", DateTime.Now));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.SetLastAccessTime("/mnt", DateTime.Now));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.SetCreationTime("/mnt", DateTime.Now));

                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.SetLastWriteTime("/toto.txt", DateTime.Now));
                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.SetLastAccessTime("/toto.txt", DateTime.Now));
                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.SetCreationTime("/toto.txt", DateTime.Now));

                // FileAttributes
                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.GetAttributes("/toto.txt"));
                await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await fs.SetAttributes("/toto.txt", FileAttributes.ReadOnly));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.SetAttributes("/", FileAttributes.ReadOnly));

                // CopyFile
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.CopyFile("/toto.txt", filePath, true));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.CopyFile(filePath, "/toto.txt", true));

                // Delete
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.DeleteFile("/toto.txt"));

                // Move
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.MoveFile("/toto.txt", filePath));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.MoveFile(filePath, "/toto.txt"));

                // ReplaceFile
                await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.ReplaceFile("/toto.txt", filePath, filePath, true));
                await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.ReplaceFile(filePath, "/toto.txt", filePath, true));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fs.ReplaceFile(filePath, filePath, "/toto.txt", true));

                await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.ReplaceFile(filePathNotExist, filePath, filePath, true));
                await Assert.ThrowsAsync<FileNotFoundException>(async () => await fs.ReplaceFile(filePath, filePathNotExist, filePath, true));
            }
            finally
            {
                SafeDeleteFile(systemFilePath);
            }
        }
    }
}