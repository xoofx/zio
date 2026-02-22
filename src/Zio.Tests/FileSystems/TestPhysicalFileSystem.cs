// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;
using System.Security.Principal;
using System.Text;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestPhysicalFileSystem : TestFileSystemBase
{
    [TestMethod]
    public void TestCommonRead()
    {
        var fs = GetCommonPhysicalFileSystem();
        AssertCommonRead(fs);
    }

    [TestMethod]
    public void TestFileSystemInvalidDriveLetterOnWindows()
    {
        Skip.IfNot(IsWindows, "Exception is only thrown on Windows");

        var driverLetter = SystemPath[0];
        Assert.Throws<DirectoryNotFoundException>( () => new SubFileSystem(new PhysicalFileSystem(), $"/mnt/{driverLetter}"));
        using (var fs = new SubFileSystem(new PhysicalFileSystem(), $"/mnt/{char.ToLowerInvariant(driverLetter)}"))
        {
        }
    }

    [TestMethod]
    public void TestRootedPathWithoutDriveOnWindows()
    {
        Skip.IfNot(IsWindows, "Testing Windows-specific behavior");

        var driveLetter = Directory.GetCurrentDirectory()[0];
        var fs = new PhysicalFileSystem();

        var resolvedPath = fs.ConvertPathFromInternal("/test");

        AssertEx.AreEqual($"/mnt/{driveLetter}/test", resolvedPath.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void TestWatcher()
    {
        var fs = GetCommonPhysicalFileSystem();
        AssertFileCreatedEventDispatched(fs, "/a", "/a/watched.txt");
    }

    [TestMethod]
    public void TestCopyFileCross()
    {
        // TODO: Add more tests
        var from = new MemoryFileSystem();
        from.WriteAllText("/test.txt", "test");
        var fs = new PhysicalFileSystem();
        var outputfs = new SubFileSystem(fs, fs.ConvertPathFromInternal(SystemPath));
        var outputPath = (UPath)"/test.txt";
        try
        {
            outputfs.WriteAllText(outputPath, "toto");
            from.CopyFileCross("/test.txt", outputfs, outputPath, true);
            var content = outputfs.ReadAllText(outputPath);
            AssertEx.AreEqual("test", content);
        }
        finally
        {
            outputfs.DeleteFile(outputPath);
        }
    }

    [TestMethod]
    public void TestDirectory()
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
            Assert.IsFalse(Directory.Exists(systemPathToCreate));
            fs.CreateDirectory(pathToCreate);
            Assert.IsTrue(Directory.Exists(systemPathToCreate));

            // LastAccessTime
            // LastWriteTime
            // CreationTime
            if (IsWindows)
            {
                var creationTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local);
                fs.SetCreationTime(pathToCreate, creationTime);
                AssertEx.AreEqual(creationTime, fs.GetCreationTime(pathToCreate));
            }

            var lastWriteTime = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Local);
            fs.SetLastWriteTime(pathToCreate, lastWriteTime);
            AssertEx.AreEqual(lastWriteTime, fs.GetLastWriteTime(pathToCreate));

            var lastAccessTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local);
            fs.SetLastAccessTime(pathToCreate, lastAccessTime);
            AssertEx.AreEqual(lastAccessTime, fs.GetLastAccessTime(pathToCreate));

            // DirectoryExists
            Assert.IsTrue(fs.DirectoryExists(pathToCreate));
            Assert.IsFalse(fs.DirectoryExists(pathToCreate / "not_found"));

            // MoveDirectory
            fs.MoveDirectory(pathToCreate, movedDirectory);
            Assert.IsFalse(Directory.Exists(systemPathToCreate));
            Assert.IsTrue(fs.DirectoryExists(movedDirectory));

            // Delete the directory
            fs.DeleteDirectory(movedDirectory, false);
            Assert.IsFalse(Directory.Exists(systemMovedDirectory));
        }
        finally
        {
            SafeDeleteDirectory(systemPathToCreate);
            SafeDeleteDirectory(systemMovedDirectory);
        }
    }

    [TestMethod]
    public void TestDirectorySpecial()
    {
        var fs = new PhysicalFileSystem();
        // CreateDirectory
        Assert.IsTrue(fs.DirectoryExists("/"));
        if (IsWindows)
        {
            var directories = fs.EnumerateDirectories("/").ToList();
            AssertEx.AreEqual(new List<UPath>() { "/mnt" }, directories);

            var drives = fs.EnumerateDirectories("/mnt").ToList();
            Assert.IsTrue(drives.Count > 0);

            var allDrives = DriveInfo.GetDrives().Select(d => d.Name[0].ToString().ToLowerInvariant()).ToList();
            var driveNames = drives.Select(d => d.GetName()).ToList();
            AssertEx.AreEqual(allDrives, driveNames);

            Assert.IsTrue(fs.DirectoryExists("/"));
            Assert.IsTrue(fs.DirectoryExists("/mnt"));
            Assert.IsTrue(fs.DirectoryExists(drives[0]));

            var files = fs.EnumerateFiles("/").ToList();
            Assert.IsTrue(files.Count == 0);

            files = fs.EnumerateFiles("/mnt").ToList();
            Assert.IsTrue(files.Count == 0);

            var paths = fs.EnumeratePaths("/").ToList();
            AssertEx.AreEqual(new List<UPath>() { "/mnt" }, paths);
        }
    }

    [TestMethod]
    public void TestDirectoryWindowsExceptions()
    {
        Skip.IfNot(IsWindows, "Exceptions are only thrown on Windows");

        var fs = new PhysicalFileSystem();

        // Test invalid characters in path
        Assert.Throws<ArgumentException>(() => fs.CreateFile($"/##/mnt/hello".Replace("#", char.ConvertFromUtf32(0xad))));

        // Try to create a folder on an unauthorized location
        fs.CreateDirectory("/");
        Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt2"));
        Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt"));
        Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt/yoyo"));
        Assert.Throws<UnauthorizedAccessException>(() => fs.CreateDirectory("/mnt/c"));

        var drives = fs.EnumerateDirectories("/mnt").ToList();
        Assert.IsTrue(drives.Count > 0);

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

    [TestMethod]
    public void TestWindowsDirectoryAttributes()
    {
        Skip.IfNot(IsWindows, "Root attributes are only set on the Windows");

        var fs = new PhysicalFileSystem();
        var sysAttr = FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly;

        Assert.IsTrue((fs.GetAttributes("/") & (sysAttr)) == sysAttr);
        Assert.IsTrue((fs.GetAttributes("/mnt") & (sysAttr)) == sysAttr);
    }

    [TestMethod]
    public void TestFile()
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
            var fileStream = fs.CreateFile(filePath);
            var buffer = Encoding.UTF8.GetBytes("This is a test");
            fileStream.Write(buffer, 0, buffer.Length);
            fileStream.Dispose();

            // FileLength
            AssertEx.AreEqual(buffer.Length, fs.GetFileLength(filePath));

            // LastAccessTime
            // LastWriteTime
            // CreationTime
            AssertEx.AreEqual(File.GetLastWriteTime(systemFilePath), fs.GetLastWriteTime(filePath));
            AssertEx.AreEqual(File.GetLastAccessTime(systemFilePath), fs.GetLastAccessTime(filePath));
            AssertEx.AreEqual(File.GetCreationTime(systemFilePath), fs.GetCreationTime(filePath));

            if (IsWindows)
            {
                var creationTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local);
                fs.SetCreationTime(filePath, creationTime);
                AssertEx.AreEqual(creationTime, fs.GetCreationTime(filePath));
            }

            var lastWriteTime = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Local);
            fs.SetLastWriteTime(filePath, lastWriteTime);
            AssertEx.AreEqual(lastWriteTime, fs.GetLastWriteTime(filePath));

            var lastAccessTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local);
            fs.SetLastAccessTime(filePath, lastAccessTime);
            AssertEx.AreEqual(lastAccessTime, fs.GetLastAccessTime(filePath));

            // FileAttributes
            AssertEx.AreEqual(File.GetAttributes(systemFilePath), fs.GetAttributes(filePath));

            var attributes = fs.GetAttributes(filePath);
            attributes |= FileAttributes.ReadOnly;
            fs.SetAttributes(filePath, attributes);

            AssertEx.AreEqual(File.GetAttributes(systemFilePath), fs.GetAttributes(filePath));

            attributes &= ~FileAttributes.ReadOnly;
            fs.SetAttributes(filePath, attributes);
            AssertEx.AreEqual(File.GetAttributes(systemFilePath), fs.GetAttributes(filePath));

            // FileExists
            Assert.IsTrue(File.Exists(systemFilePath));
            Assert.IsTrue(fs.FileExists(filePath));

            // CopyFile
            fs.CopyFile(filePath, filePathDest, true);
            Assert.IsTrue(File.Exists(systemFilePathDest));
            Assert.IsTrue(fs.FileExists(filePathDest));

            // DeleteFile
            fs.DeleteFile(filePath);
            Assert.IsFalse(File.Exists(systemFilePath));
            Assert.IsFalse(fs.FileExists(filePath));

            // MoveFile
            fs.MoveFile(filePathDest, filePath);
            Assert.IsFalse(File.Exists(systemFilePathDest));
            Assert.IsFalse(fs.FileExists(filePathDest));
            Assert.IsTrue(File.Exists(systemFilePath));
            Assert.IsTrue(fs.FileExists(filePath));

            // ReplaceFile

            // copy file to filePathDest
            fs.CopyFile(filePath, filePathDest, true);

            // Change src file
            var filestream2 = fs.OpenFile(filePath, FileMode.Open, FileAccess.ReadWrite);
            var buffer2 = Encoding.UTF8.GetBytes("This is a test 123");
            filestream2.Write(buffer2, 0, buffer2.Length);
            filestream2.Dispose();
            AssertEx.AreEqual(buffer2.Length, fs.GetFileLength(filePath));

            // Perform ReplaceFile
            fs.ReplaceFile(filePath, filePathDest, filePathBack, true);
            Assert.IsFalse(fs.FileExists(filePath));
            Assert.IsTrue(fs.FileExists(filePathDest));
            Assert.IsTrue(fs.FileExists(filePathBack));

            AssertEx.AreEqual(buffer2.Length, fs.GetFileLength(filePathDest));
            AssertEx.AreEqual(buffer.Length, fs.GetFileLength(filePathBack));

            if (IsWindows)
            {
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
            }
        }
        finally
        {
            SafeDeleteFile(systemFilePath);
            SafeDeleteFile(systemFilePathDest);
            SafeDeleteFile(systemFilePathBack);
        }
    }

    [TestMethod]
    public void TestEnumerate()
    {
        var fs = new PhysicalFileSystem();
        var path = fs.ConvertPathFromInternal(SystemPath);

        var files = fs.EnumerateFiles(path).Select(p => fs.ConvertPathToInternal(p)).ToList();
        var expectedfiles = Directory.EnumerateFiles(SystemPath).ToList();
        AssertEx.AreEqual(expectedfiles, files);

        var dirs = fs.EnumerateDirectories(path / "../../..").Select(p => fs.ConvertPathToInternal(p)).ToList();
        var expecteddirs = Directory.EnumerateDirectories(Path.GetFullPath(Path.Combine(SystemPath, "../../.."))).ToList();
        AssertEx.AreEqual(expecteddirs, dirs);

        var paths = fs.EnumeratePaths(path / "../..").Select(p => fs.ConvertPathToInternal(p)).ToList();
        var expectedPaths = Directory.EnumerateFileSystemEntries(Path.GetFullPath(Path.Combine(SystemPath, "../.."))).ToList();
        AssertEx.AreEqual(expectedPaths, paths);
    }

    [TestMethod]
    public void TestFileWindowsExceptions()
    {
        Skip.IfNot(IsWindows, "Exceptions are only thrown on Windows");

        var fs = new PhysicalFileSystem();
        var path = fs.ConvertPathFromInternal(SystemPath);
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

            // ConvertPathFromInternal / ConvertPathToInternal
            Assert.Throws<NotSupportedException>(() => fs.ConvertPathFromInternal(@"\\network\toto.txt"));
            Assert.Throws<NotSupportedException>(() => fs.ConvertPathFromInternal(@"zx:\toto.txt"));

            Assert.Throws<ArgumentException>(() => fs.ConvertPathToInternal(@"/toto.txt"));
            Assert.Throws<ArgumentException>(() => fs.ConvertPathToInternal(@"/mnt/yo/toto.txt"));

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

    [TestMethod]
    public void TestDirectorySymlink()
    {
#if NETCOREAPP
        if (OperatingSystem.IsWindows())
#else
        if (IsWindows)
#endif
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);

            Skip.IfNot(principal.IsInRole(WindowsBuiltInRole.Administrator), "This test requires to be run as an administrator on Windows");
        }

        var fs = new PhysicalFileSystem();
        var pathInfo = fs.ConvertPathFromInternal(SystemPath);
        var pathSource = pathInfo / "Source";
        var filePathSource = pathSource / "test.txt";
        var systemPathSource = fs.ConvertPathToInternal(pathSource);
        var pathDest = pathInfo / "Dest";
        var filePathDest = pathDest / "test.txt";
        var systemPathDest = fs.ConvertPathToInternal(pathDest);
        try
        {
            // CreateDirectory
            Assert.IsFalse(Directory.Exists(systemPathSource));
            fs.CreateDirectory(pathSource);
            Assert.IsTrue(Directory.Exists(systemPathSource));

            // CreateFile / OpenFile
            var fileStream = fs.CreateFile(filePathSource);
            var buffer = Encoding.UTF8.GetBytes("This is a test");
            fileStream.Write(buffer, 0, buffer.Length);
            fileStream.Dispose();
            AssertEx.AreEqual(buffer.Length, fs.GetFileLength(filePathSource));

            // CreateSymbolicLink
            fs.CreateSymbolicLink(pathDest, pathSource);

            // ResolveSymbolicLink
            Assert.IsTrue(fs.TryResolveLinkTarget(pathDest, out var resolvedPath));
            AssertEx.AreEqual(pathSource, resolvedPath);

            // FileExists
            Assert.IsTrue(fs.FileExists(filePathDest));
            AssertEx.AreEqual(buffer.Length, fs.GetFileLength(filePathDest));

            // RemoveDirectory
            fs.DeleteDirectory(pathDest, false);
            Assert.IsFalse(Directory.Exists(systemPathDest));
            Assert.IsTrue(Directory.Exists(systemPathSource));
        }
        finally
        {
            SafeDeleteDirectory(systemPathSource);
            SafeDeleteDirectory(systemPathDest);
        }
    }

    [TestMethod]
    public void TestFileSymlink()
    {
#if NETCOREAPP
        if (OperatingSystem.IsWindows())
#else
        if (IsWindows)
#endif
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);

            Skip.IfNot(principal.IsInRole(WindowsBuiltInRole.Administrator), "This test requires to be run as an administrator on Windows");
        }

        var fs = new PhysicalFileSystem();
        var pathInfo = fs.ConvertPathFromInternal(SystemPath);
        var pathSource = pathInfo / "source.txt";
        var systemPathSource = fs.ConvertPathToInternal(pathSource);
        var pathDest = pathInfo / "dest.txt";
        var systemPathDest = fs.ConvertPathToInternal(pathDest);
        try
        {
            // CreateEmptyFile
            fs.CreateFile(pathSource).Dispose();

            // CreateSymbolicLink
            fs.CreateSymbolicLink(pathDest, pathSource);

            // ResolveSymbolicLink
            Assert.IsTrue(fs.TryResolveLinkTarget(pathDest, out var resolvedPath));
            AssertEx.AreEqual(pathSource, resolvedPath);

            // FileExists
            Assert.IsTrue(fs.FileExists(pathDest));

            // CreateFile / OpenFile
            var fileStream = fs.OpenFile(pathSource, FileMode.Open, FileAccess.ReadWrite);
            var buffer = Encoding.UTF8.GetBytes("This is a test");
            fileStream.Write(buffer, 0, buffer.Length);
            fileStream.Dispose();
            AssertEx.AreEqual(buffer.Length, fs.GetFileLength(pathSource));

            // ReadAllBytes
            // Note: we can't check the length, since on Windows the symlink length is 0
            var symlinkBuffer = fs.ReadAllBytes(pathDest);
            AssertEx.AreEqual(buffer, symlinkBuffer);

            // FileEntry
            var entry = fs.GetFileSystemEntry(pathDest);
            Assert.IsTrue(entry.Attributes.HasFlag(FileAttributes.ReparsePoint));

            // DeleteFile
            fs.DeleteFile(pathDest);
            Assert.IsFalse(File.Exists(systemPathDest));
            Assert.IsTrue(File.Exists(systemPathSource));
        }
        finally
        {
            SafeDeleteFile(systemPathSource);
            SafeDeleteFile(systemPathDest);
        }
    }

    [TestMethod]
    public void TestResolvePath()
    {
        var fs = new PhysicalFileSystem();
        var testPath = fs.ConvertPathFromInternal(SystemPath);
        var (resFs, resPath) = fs.ResolvePath(testPath);
        AssertEx.AreEqual(testPath, resPath);
        AssertEx.AreEqual(fs, resFs);
    }
}



