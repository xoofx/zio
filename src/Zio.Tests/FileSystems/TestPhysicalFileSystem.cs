// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestPhysicalFileSystem : TestFileSystemBase
    {
        [Fact]
        public void TestCommonRead()
        {
            var fs = GetCommonPhysicalFileSystem();
            AssertCommonRead(fs);
        }

        [Fact]
        public void TestDirectory()
        {
            var fs = new PhysicalFileSystem();
            var pathInfo = fs.ConvertFromSystem(SystemPath);
            var pathToCreate = pathInfo / "TestCreateDirectory";
            var systemPathToCreate = fs.ConvertToSystem(pathToCreate);
            var movedDirectory = pathInfo / "TestCreateDirectoryMoved";
            var systemMovedDirectory = fs.ConvertToSystem(movedDirectory);
            try
            {
                // CreateDirectory
                Assert.False(Directory.Exists(systemPathToCreate));
                fs.CreateDirectory(pathToCreate);
                Assert.True(Directory.Exists(systemPathToCreate));

                // DirectoryExists
                Assert.True(fs.DirectoryExists(pathToCreate));
                Assert.False(fs.DirectoryExists(pathToCreate / "not_found"));

                // MoveDirectory
                fs.MoveDirectory(pathToCreate, movedDirectory);
                Assert.False(Directory.Exists(systemPathToCreate));
                Assert.True(fs.DirectoryExists(movedDirectory));

                // Delete the directory
                fs.DeleteDirectory(movedDirectory, false);
                Assert.False(Directory.Exists(systemMovedDirectory));
            }
            finally
            {
                SafeDeleteDirectory(systemPathToCreate);
                SafeDeleteDirectory(systemMovedDirectory);
            }
        }

        [Fact]
        public void TestDirectorySpecial()
        {
            var fs = new PhysicalFileSystem();
            // CreateDirectory
            Assert.True(fs.DirectoryExists("/"));
            if (IsWindows)
            {
                var directories = fs.EnumerateDirectories("/").ToList();
                Assert.Equal(new List<UPath>() { "/mnt" }, directories);

                var drives = fs.EnumerateDirectories("/mnt").ToList();
                Assert.True(drives.Count > 0);

                var allDrives = DriveInfo.GetDrives().Select(d => d.Name[0].ToString().ToLowerInvariant()).ToList();
                var driveNames = drives.Select(d => d.GetName()).ToList();
                Assert.Equal(allDrives, driveNames);

                Assert.True(fs.DirectoryExists("/"));
                Assert.True(fs.DirectoryExists("/mnt"));
                Assert.True(fs.DirectoryExists(drives[0]));

                var files = fs.EnumerateFiles("/").ToList();
                Assert.True(files.Count == 0);

                files = fs.EnumerateFiles("/mnt").ToList();
                Assert.True(files.Count == 0);

                var paths = fs.EnumeratePaths("/").ToList();
                Assert.Equal(new List<UPath>() { "/mnt" }, paths);
            }
        }

        [Fact]
        public void TestDirectoryExceptions()
        {
            var fs = new PhysicalFileSystem();
            // Try to create a folder on an unauthorized location
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt2"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt/yoyo"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt/c"));

            var drives = fs.EnumerateDirectories("/mnt").ToList();
            Assert.True(drives.Count > 0);

            Assert.Throws<UnauthorizedAccessException>(() => fs.MoveDirectory("/", drives[0] / "ShouldNotHappen"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.MoveDirectory("/mnt", drives[0] / "ShouldNotHappen"));
            Assert.Throws<DirectoryNotFoundException>(() => fs.MoveDirectory("/mnt2", drives[0] / "ShouldNotHappen"));

            Assert.Throws<UnauthorizedAccessException>(() => fs.MoveDirectory(drives[0] / "ShouldNotHappen", "/"));
            Assert.Throws<UnauthorizedAccessException>(() => fs.MoveDirectory(drives[0] / "ShouldNotHappen", "/mnt"));
            Assert.Throws<DirectoryNotFoundException>(() => fs.MoveDirectory(drives[0] / "ShouldNotHappen", "/mnt2"));

            Assert.Throws<UnauthorizedAccessException>(() => fs.DeleteDirectory("/", false));
            Assert.Throws<UnauthorizedAccessException>(() => fs.DeleteDirectory("/mnt", false));
            Assert.Throws<DirectoryNotFoundException>(() => fs.DeleteDirectory("/mnt2", false));
            Assert.Throws<DirectoryNotFoundException>(() => fs.DeleteDirectory("/mnt/yoyo", false));
        }

        [Fact]
        public void TestFile()
        {
            var fs = new PhysicalFileSystem();
            var path = fs.ConvertFromSystem(SystemPath);
            var fileName = $"toto-{Guid.NewGuid()}.txt";
            var filePath = path / fileName;
            var filePathDest = path / Path.ChangeExtension(fileName, "dest");
            var filePathBack = path / Path.ChangeExtension(fileName, "bak");
            var systemFilePath = Path.Combine(SystemPath, fileName);
            var systemFilePathDest = fs.ConvertToSystem(filePathDest);
            var systemFilePathBack = fs.ConvertToSystem(filePathBack);
            try
            {
                // CreateFile / OpenFile
                var fileStream = fs.CreateFile(filePath);
                var buffer = Encoding.UTF8.GetBytes("This is a test");
                fileStream.Write(buffer, 0, buffer.Length);
                fileStream.Dispose();

                // FileLength
                Assert.Equal(buffer.Length, fs.GetFileLength(filePath));

                // LastAccessTime
                // LastWriteTime
                // CreationTime
                Assert.Equal(File.GetLastWriteTime(systemFilePath), fs.GetLastWriteTime(filePath));
                Assert.Equal(File.GetLastAccessTime(systemFilePath), fs.GetLastAccessTime(filePath));
                Assert.Equal(File.GetCreationTime(systemFilePath), fs.GetCreationTime(filePath));

                var lastWriteTime = DateTime.Now + TimeSpan.FromSeconds(10);
                var lastAccessTime = DateTime.Now + TimeSpan.FromSeconds(11);
                var creationTime = DateTime.Now + TimeSpan.FromSeconds(12);
                fs.SetLastWriteTime(filePath, lastWriteTime);
                fs.SetLastAccessTime(filePath, lastAccessTime);
                fs.SetCreationTime(filePath, creationTime);
                Assert.Equal(lastWriteTime, fs.GetLastWriteTime(filePath));
                Assert.Equal(lastAccessTime, fs.GetLastAccessTime(filePath));
                Assert.Equal(creationTime, fs.GetCreationTime(filePath));

                // FileAttributes
                Assert.Equal(File.GetAttributes(systemFilePath), fs.GetAttributes(filePath));

                var attributes = fs.GetAttributes(filePath);
                attributes |= FileAttributes.ReadOnly;
                fs.SetAttributes(filePath, attributes);

                Assert.Equal(File.GetAttributes(systemFilePath), fs.GetAttributes(filePath));

                attributes &= ~FileAttributes.ReadOnly;
                fs.SetAttributes(filePath, attributes);
                Assert.Equal(File.GetAttributes(systemFilePath), fs.GetAttributes(filePath));

                // FileExists
                Assert.True(File.Exists(systemFilePath));
                Assert.True(fs.FileExists(filePath));

                // CopyFile
                fs.CopyFile(filePath, filePathDest, true);
                Assert.True(File.Exists(systemFilePathDest));
                Assert.True(fs.FileExists(filePathDest));

                // DeleteFile
                fs.DeleteFile(filePath);
                Assert.False(File.Exists(systemFilePath));
                Assert.False(fs.FileExists(filePath));

                // MoveFile
                fs.MoveFile(filePathDest, filePath);
                Assert.False(File.Exists(systemFilePathDest));
                Assert.False(fs.FileExists(filePathDest));
                Assert.True(File.Exists(systemFilePath));
                Assert.True(fs.FileExists(filePath));

                // ReplaceFile

                // copy file to filePathDest
                fs.CopyFile(filePath, filePathDest, true);

                // Change src file
                var filestream2 = fs.OpenFile(filePath, FileMode.Open, FileAccess.ReadWrite);
                var buffer2 = Encoding.UTF8.GetBytes("This is a test 123");
                filestream2.Write(buffer2, 0, buffer2.Length);
                filestream2.Dispose();
                Assert.Equal(buffer2.Length, fs.GetFileLength(filePath));

                // Perform ReplaceFile
                fs.ReplaceFile(filePath, filePathDest, filePathBack, true);
                Assert.False(fs.FileExists(filePath));
                Assert.True(fs.FileExists(filePathDest));
                Assert.True(fs.FileExists(filePathBack));

                Assert.Equal(buffer2.Length, fs.GetFileLength(filePathDest));
                Assert.Equal(buffer.Length, fs.GetFileLength(filePathBack));

                // RootFileSystem
                fs.GetLastWriteTime("/");
                fs.GetLastAccessTime("/");
                fs.GetCreationTime("/");

                fs.GetLastWriteTime("/mnt");
                fs.GetLastAccessTime("/mnt");
                fs.GetCreationTime("/mnt");

                fs.GetLastWriteTime("/mnt/c");
                fs.GetLastAccessTime("/mnt/c");
                fs.GetCreationTime("/mnt/c");
                fs.GetAttributes("/mnt/c");

                var sysAttr = FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly;
                Assert.True((fs.GetAttributes("/") & (sysAttr)) == sysAttr);
                Assert.True((fs.GetAttributes("/mnt") & (sysAttr)) == sysAttr);
            }
            finally
            {
                SafeDeleteFile(systemFilePath);
                SafeDeleteFile(systemFilePathDest);
                SafeDeleteFile(systemFilePathBack);
            }
        }

        [Fact]
        public void TestEnumerate()
        {
            var fs = new PhysicalFileSystem();
            var path = fs.ConvertFromSystem(SystemPath);

            var files = fs.EnumerateFiles(path).Select(p => fs.ConvertToSystem(p)).ToList();
            var expectedfiles = Directory.EnumerateFiles(SystemPath).ToList();
            Assert.Equal(expectedfiles, files);

            var dirs = fs.EnumerateDirectories(path / "../../..").Select(p => fs.ConvertToSystem(p)).ToList();
            var expecteddirs = Directory.EnumerateDirectories(Path.GetFullPath(Path.Combine(SystemPath, "..\\..\\.."))).ToList();
            Assert.Equal(expecteddirs, dirs);

            var paths = fs.EnumeratePaths(path / "../..").Select(p => fs.ConvertToSystem(p)).ToList();
            var expectedPaths = Directory.EnumerateFileSystemEntries(Path.GetFullPath(Path.Combine(SystemPath, "..\\.."))).ToList();
            Assert.Equal(expectedPaths, paths);
        }

        [Fact]
        public void TestFileExceptions()
        {
            var fs = new PhysicalFileSystem();
            var path = fs.ConvertFromSystem(SystemPath);
            var fileName = $"toto-{Guid.NewGuid()}.txt";
            var filePath = path / fileName;
            fs.CreateFile(filePath).Dispose();
            var filePathNotExist = path / "FileDoesNotExist.txt";
            var systemFilePath = Path.Combine(SystemPath, fileName);

            // Try to create a folder on an unauthorized location
            try
            {
                // CreateFile
                Assert.Throws<UnauthorizedAccessException>(() => fs.CreateFile("/toto.txt"));

                // Length
                Assert.Throws<UnauthorizedAccessException>(() => fs.GetFileLength("/toto.txt"));

                // ConvertFromSystem / ConvertToSystem
                Assert.Throws<NotSupportedException>(() => fs.ConvertFromSystem(@"\\network\toto.txt"));
                Assert.Throws<NotSupportedException>(() => fs.ConvertFromSystem(@"zx:\toto.txt"));

                Assert.Throws<ArgumentException>(() => fs.ConvertToSystem(@"/toto.txt"));
                Assert.Throws<ArgumentException>(() => fs.ConvertToSystem(@"/mnt/yo/toto.txt"));

                // LastWriteTime, LastAccessTime, CreationTime
                Assert.Throws<DirectoryNotFoundException>(() => fs.GetLastWriteTime("/toto.txt"));
                Assert.Throws<DirectoryNotFoundException>(() => fs.GetLastAccessTime("/toto.txt"));
                Assert.Throws<DirectoryNotFoundException>(() => fs.GetCreationTime("/toto.txt"));

                Assert.Throws<UnauthorizedAccessException>(() => fs.SetLastWriteTime("/", DateTime.Now));
                Assert.Throws<UnauthorizedAccessException>(() => fs.SetLastAccessTime("/", DateTime.Now));
                Assert.Throws<UnauthorizedAccessException>(() => fs.SetCreationTime("/", DateTime.Now));

                Assert.Throws<UnauthorizedAccessException>(() => fs.SetLastWriteTime("/mnt", DateTime.Now));
                Assert.Throws<UnauthorizedAccessException>(() => fs.SetLastAccessTime("/mnt", DateTime.Now));
                Assert.Throws<UnauthorizedAccessException>(() => fs.SetCreationTime("/mnt", DateTime.Now));

                Assert.Throws<DirectoryNotFoundException>(() => fs.SetLastWriteTime("/toto.txt", DateTime.Now));
                Assert.Throws<DirectoryNotFoundException>(() => fs.SetLastAccessTime("/toto.txt", DateTime.Now));
                Assert.Throws<DirectoryNotFoundException>(() => fs.SetCreationTime("/toto.txt", DateTime.Now));

                // FileAttributes
                Assert.Throws<DirectoryNotFoundException>(() => fs.GetAttributes("/toto.txt"));
                Assert.Throws<DirectoryNotFoundException>(() => fs.SetAttributes("/toto.txt", FileAttributes.ReadOnly));
                Assert.Throws<UnauthorizedAccessException>(() => fs.SetAttributes("/", FileAttributes.ReadOnly));

                // CopyFile
                Assert.Throws<UnauthorizedAccessException>(() => fs.CopyFile("/toto.txt", filePath, true));
                Assert.Throws<UnauthorizedAccessException>(() => fs.CopyFile(filePath, "/toto.txt", true));

                // Delete
                Assert.Throws<UnauthorizedAccessException>(() => fs.DeleteFile("/toto.txt"));

                // Move
                Assert.Throws<UnauthorizedAccessException>(() => fs.MoveFile("/toto.txt", filePath));
                Assert.Throws<UnauthorizedAccessException>(() => fs.MoveFile(filePath, "/toto.txt"));

                // ReplaceFile
                Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile("/toto.txt", filePath, filePath, true));
                Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile(filePath, "/toto.txt", filePath, true));
                Assert.Throws<UnauthorizedAccessException>(() => fs.ReplaceFile(filePath, filePath, "/toto.txt", true));

                Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile(filePathNotExist, filePath, filePath, true));
                Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile(filePath, filePathNotExist, filePath, true));
            }
            finally
            {
                SafeDeleteFile(systemFilePath);
            }
        }
    }
}