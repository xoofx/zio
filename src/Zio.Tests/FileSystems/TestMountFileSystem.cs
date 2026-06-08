// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestMountFileSystem : TestFileSystemBase
{
    [TestMethod]
    public void TestCommonReadWithOnlyBackup()
    {
        var fs = GetCommonMountFileSystemWithOnlyBackup();
        AssertCommonRead(fs);
    }

    [TestMethod]
    public void TestCommonReadWithMounts()
    {
        var fs = GetCommonMountFileSystemWithMounts();
        AssertCommonRead(fs);
    }
    
    [TestMethod]
    public void TestWatcherOnRoot()
    {
        var fs = GetCommonMountFileSystemWithMounts();
        AssertFileCreatedEventDispatched(fs, "/", "/b/watched.txt");
    }

    [TestMethod]
    public void TestWatcherOnMount()
    {
        var fs = GetCommonMountFileSystemWithMounts();
        AssertFileCreatedEventDispatched(fs, "/b", "/b/watched.txt");
    }

    [TestMethod]
    public void TestWatcherWithBackupOnRoot()
    {
        var fs = GetCommonMountFileSystemWithOnlyBackup();
        AssertFileCreatedEventDispatched(fs, "/", "/b/watched.txt");
    }

    [TestMethod]
    public void TestMount()
    {
        var fs = new MountFileSystem();
        var memfs = new MemoryFileSystem();

        Assert.Throws<ArgumentNullException>(() => fs.Mount(null, memfs));
        Assert.Throws<ArgumentNullException>(() => fs.Mount("/test", null));
        Assert.Throws<ArgumentException>(() => fs.Mount("test", memfs));
        Assert.Throws<ArgumentException>(() => fs.Mount("/", memfs));

        Assert.IsFalse(fs.IsMounted("/test"));
        fs.Mount("/test", memfs);
        Assert.IsTrue(fs.IsMounted("/test"));
        Assert.Throws<ArgumentException>(() => fs.Mount("/test", memfs));
        Assert.Throws<ArgumentException>(() => fs.Mount("/test", fs));

        Assert.Throws<ArgumentNullException>(() => fs.Unmount(null));
        Assert.Throws<ArgumentException>(() => fs.Unmount("test"));
        Assert.Throws<ArgumentException>(() => fs.Unmount("/"));
        Assert.Throws<ArgumentException>(() => fs.Unmount("/test/a"));
        Assert.Throws<ArgumentException>(() => fs.Unmount("/test/a/b"));
        Assert.Throws<ArgumentException>(() => fs.Unmount("/test2"));

        fs.Mount("/test2", memfs);
        Assert.IsTrue(fs.IsMounted("/test"));
        Assert.IsTrue(fs.IsMounted("/test2"));

        AssertEx.AreEqual(new Dictionary<UPath, IFileSystem>()
        {
            {"/test", memfs},
            {"/test2", memfs},
        }, fs.GetMounts());

        fs.Unmount("/test");
        Assert.IsFalse(fs.IsMounted("/test"));
        Assert.IsTrue(fs.IsMounted("/test2"));

        fs.Unmount("/test2");

        AssertEx.Empty(fs.GetMounts());

        var innerFs = GetCommonMemoryFileSystem();
        fs.Mount("/x/y", innerFs);
        fs.Mount("/x/y/b", innerFs);
        Assert.IsTrue(fs.FileExists("/x/y/A.txt"));
        Assert.IsTrue(fs.FileExists("/x/y/b/A.txt"));
    }

    [TestMethod]
    public void ConvertPathToInternalUsesMountedFileSystem()
    {
        using var physical = new PhysicalDirectoryHelper(SystemPath);
        physical.PhysicalFileSystem.WriteAllText("/file.txt", "content");

        var mountFs = new MountFileSystem();
        mountFs.Mount("/physical", physical.PhysicalFileSystem);

        var expected = physical.PhysicalFileSystem.ConvertPathToInternal("/file.txt");
        var actual = mountFs.ConvertPathToInternal("/physical/file.txt");

        AssertEx.AreEqual(expected, actual);
        Assert.IsTrue(File.Exists(actual));
    }

    [TestMethod]
    public void TestWatcherRemovedWhenDisposed()
    {
        var fs = GetCommonMountFileSystemWithMounts();

        var watcher = fs.Watch("/");
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;
        watcher.Dispose();

        System.Threading.Thread.Sleep(100);

        var watchersField = typeof(MountFileSystem).GetTypeInfo()
            .GetField("_watchers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(watchersField);

        var watchers = (IList)watchersField.GetValue(fs);
        Assert.IsNotNull(watchers);
        AssertEx.Empty(watchers);
    }

    [TestMethod]
    public void EnumerateDeepMount()
    {
        var fs = GetCommonMemoryFileSystem();
        var mountFs = new MountFileSystem();
        mountFs.Mount("/x/y/z", fs);
        Assert.IsTrue(mountFs.FileExists("/x/y/z/A.txt"));

        var expected = new List<UPath>
        {
            "/x",
            "/x/y",
            "/x/y/z",
            "/x/y/z/a",
            "/x/y/z/A.txt"
        };

        // only concerned with the first few because it should list the mount parts first
        var actual = mountFs.EnumeratePaths("/", "*", SearchOption.AllDirectories).Take(5).ToList();
        AssertEx.AreEqual(expected, actual);
    }

    [TestMethod]
    public void EnumerateDeepMountPartial()
    {
        var fs = GetCommonMemoryFileSystem();
        var mountFs = new MountFileSystem();
        mountFs.Mount("/x/y/z", fs);
        Assert.IsTrue(mountFs.FileExists("/x/y/z/A.txt"));

        var expected = new List<UPath>
        {
            "/x/y",
            "/x/y/z",
            "/x/y/z/a",
            "/x/y/z/A.txt"
        };

        // only concerned with the first few because it should list the mount parts first
        var actual = mountFs.EnumeratePaths("/x", "*", SearchOption.AllDirectories).Take(4).ToList();
        AssertEx.AreEqual(expected, actual);
    }

    [TestMethod]
    public void EnumerateMountsOverride()
    {
        var baseFs = new MemoryFileSystem();
        baseFs.CreateDirectory("/foo/bar");
        baseFs.WriteAllText("/base.txt", "test");
        baseFs.WriteAllText("/foo/base.txt", "test");
        baseFs.WriteAllText("/foo/bar/base.txt", "test");

        var mountedFs = new MemoryFileSystem();
        mountedFs.WriteAllText("/mounted.txt", "test");

        var deepMountedFs = new MemoryFileSystem();
        deepMountedFs.WriteAllText("/deep_mounted.txt", "test");

        var mountFs = new MountFileSystem(baseFs);
        mountFs.Mount("/foo", mountedFs);
        mountFs.Mount("/foo/bar", deepMountedFs);
        
        var expected = new List<UPath>
        {
            "/base.txt",
            "/foo",
            "/foo/bar",
            "/foo/mounted.txt",
            "/foo/bar/deep_mounted.txt"
        };

        var actual = mountFs.EnumeratePaths("/", "*", SearchOption.AllDirectories).ToList();
        AssertEx.AreEqual(expected, actual);
    }

    [TestMethod]
    public void EnumerateEmptyOnRoot()
    {
        var mountFs = new MountFileSystem();
        var expected = Array.Empty<UPath>();
        var actual = mountFs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Both).ToList();
        AssertEx.AreEqual(expected, actual);
    }

    [TestMethod]
    public void EnumerateDoesntExist()
    {
        var mountFs = new MountFileSystem();
        mountFs.Mount("/x", new MemoryFileSystem());
        Assert.Throws<DirectoryNotFoundException>(() => mountFs.EnumeratePaths("/y", "*", SearchOption.AllDirectories, SearchTarget.Both).ToList());
    }

    [TestMethod]
    public void EnumerateBackupDoesntExist()
    {
        var mountFs = new MountFileSystem(new MemoryFileSystem());            
        Assert.Throws<DirectoryNotFoundException>(() => mountFs.EnumeratePaths("/y", "*", SearchOption.AllDirectories, SearchTarget.Both).ToList());
    }

    [TestMethod]
    public void DirectoryExistsPartialMountName()
    {
        var fs = new MemoryFileSystem();
        var mountFs = new MountFileSystem();
        mountFs.Mount("/x/y/z", fs);

        Assert.IsTrue(mountFs.DirectoryExists("/x"));
        Assert.IsTrue(mountFs.DirectoryExists("/x/y"));
        Assert.IsTrue(mountFs.DirectoryExists("/x/y/z"));
        Assert.IsFalse(mountFs.DirectoryExists("/z"));
    }

    [TestMethod]
    public void DirectoryEntryPartialMountName()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/w");

        var mountFs = new MountFileSystem();
        mountFs.Mount("/x/y/z", fs);

        Assert.IsNotNull(mountFs.GetDirectoryEntry("/x"));
        Assert.IsNotNull(mountFs.GetDirectoryEntry("/x/y"));
        Assert.IsNotNull(mountFs.GetDirectoryEntry("/x/y/z"));
        Assert.IsNotNull(mountFs.GetDirectoryEntry("/x/y/z/w"));
    }

    [TestMethod]
    public void CreateDirectoryFail()
    {
        var mountfs = new MountFileSystem();
        Assert.Throws<UnauthorizedAccessException>(() => mountfs.CreateDirectory("/test"));
    }

    [TestMethod]
    public void MoveDirectoryFail()
    {
        var mountfs = new MountFileSystem();
        mountfs.Mount("/dir1", new MemoryFileSystem());
        mountfs.Mount("/dir2", new MemoryFileSystem());

        Assert.Throws<UnauthorizedAccessException>(() => mountfs.MoveDirectory("/dir1", "/dir2/yyy"));
        Assert.Throws<UnauthorizedAccessException>(() => mountfs.MoveDirectory("/dir1/xxx", "/dir2"));
        Assert.Throws<NotSupportedException>(() => mountfs.MoveDirectory("/dir1/xxx", "/dir2/yyy"));
    }

    [TestMethod]
    public void DeleteDirectoryFail()
    {
        var mountfs = new MountFileSystem();
        mountfs.Mount("/dir1", new MemoryFileSystem());

        Assert.Throws<UnauthorizedAccessException>(() => mountfs.DeleteDirectory("/dir1", true));
        Assert.Throws<UnauthorizedAccessException>(() => mountfs.DeleteDirectory("/dir1", false));
        Assert.Throws<DirectoryNotFoundException>(() => mountfs.DeleteDirectory("/dir2", false));
    }

    [TestMethod]
    public void CopyFileFail()
    {
        var mountfs = new MountFileSystem();
        mountfs.Mount("/dir1", new MemoryFileSystem());
        Assert.Throws<FileNotFoundException>(() => mountfs.CopyFile("/test", "/test2", true));
        Assert.Throws<DirectoryNotFoundException>(() => mountfs.CopyFile("/dir1/test.txt", "/test2", true));
    }

    [TestMethod]
    public void ReplaceFileFail()
    {
        var mountfs = new MountFileSystem();
        var memfs1 = new MemoryFileSystem();
        memfs1.WriteAllText("/file.txt", "content1");

        var memfs2 = new MemoryFileSystem();
        memfs2.WriteAllText("/file2.txt", "content1");

        mountfs.Mount("/dir1", memfs1);
        mountfs.Mount("/dir2", memfs2);
        Assert.Throws<FileNotFoundException>(() => mountfs.ReplaceFile("/dir1/file.txt", "/dir1/to.txt", "/dir1/to.bak", true));
        Assert.Throws<FileNotFoundException>(() => mountfs.ReplaceFile("/dir1/to.txt", "/dir1/file.txt", "/dir1/to.bak", true));
        Assert.Throws<NotSupportedException>(() => mountfs.ReplaceFile("/dir1/file.txt", "/dir2/file2.txt", null, true));
    }

    [TestMethod]
    public void GetFileLengthFail()
    {
        var mountfs = new MountFileSystem();
        Assert.Throws<FileNotFoundException>(() => mountfs.GetFileLength("/toto.txt"));
    }

    [TestMethod]
    public void MoveFileFail()
    {
        var mountfs = new MountFileSystem();
        var memfs1 = new MemoryFileSystem();
        memfs1.WriteAllText("/file.txt", "content1");

        var memfs2 = new MemoryFileSystem();
        memfs2.WriteAllText("/file2.txt", "content1");

        mountfs.Mount("/dir1", memfs1);
        mountfs.Mount("/dir2", memfs2);
        Assert.Throws<DirectoryNotFoundException>(() => mountfs.MoveFile("/dir1/file.txt", "/xxx/yyy.txt"));
        Assert.Throws<FileNotFoundException>(() => mountfs.MoveFile("/dir1/xxx", "/dir1/file1.txt"));
        Assert.Throws<FileNotFoundException>(() => mountfs.MoveFile("/xxx", "/dir1/file1.txt"));
    }


    [TestMethod]
    public void OpenFileFail()
    {
        var mountfs = new MountFileSystem();
        Assert.Throws<FileNotFoundException>(() => mountfs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Read));
        Assert.Throws<UnauthorizedAccessException>(() => mountfs.OpenFile("/toto.txt", FileMode.Create, FileAccess.Read));
    }

    [TestMethod]
    public void AttributesFail()
    {
        var mountfs = new MountFileSystem();
        Assert.Throws<FileNotFoundException>(() => mountfs.GetAttributes("/toto.txt"));
        Assert.Throws<FileNotFoundException>(() => mountfs.SetAttributes("/toto.txt", FileAttributes.Normal));
    }

    [TestMethod]
    public void TimesFail()
    {
        var mountfs = new MountFileSystem();
        Assert.Throws<FileNotFoundException>(() => mountfs.SetCreationTime("/toto.txt", DateTime.Now));
        Assert.Throws<FileNotFoundException>(() => mountfs.SetLastAccessTime("/toto.txt", DateTime.Now));
        Assert.Throws<FileNotFoundException>(() => mountfs.SetLastWriteTime("/toto.txt", DateTime.Now));
    }

    [TestMethod]
    public void EnumerateFail()
    {
        var mountfs = new MountFileSystem();
        Assert.Throws<DirectoryNotFoundException>(() => mountfs.EnumeratePaths("/dir").ToList());
    }

    [TestMethod]
    public void CopyAndMoveFileCross()
    {
        var mountfs = new MountFileSystem();
        var memfs1 = new MemoryFileSystem();
        memfs1.WriteAllText("/file1.txt", "content1");
        var memfs2 = new MemoryFileSystem();

        mountfs.Mount("/dir1", memfs1);
        mountfs.Mount("/dir2", memfs2);

        mountfs.CopyFile("/dir1/file1.txt", "/dir2/file2.txt", true);

        Assert.IsTrue(memfs2.FileExists("/file2.txt"));
        AssertEx.AreEqual("content1", memfs2.ReadAllText("/file2.txt"));

        mountfs.MoveFile("/dir1/file1.txt", "/dir2/file1.txt");

        Assert.IsFalse(memfs1.FileExists("/file1.txt"));
        Assert.IsTrue(memfs2.FileExists("/file1.txt"));
        AssertEx.AreEqual("content1", memfs2.ReadAllText("/file1.txt"));
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

        var physicalFs = new PhysicalFileSystem();
        var memoryFs = new MemoryFileSystem();
        var fs = new MountFileSystem();
        fs.Mount("/physical", physicalFs);
        fs.Mount("/memory", memoryFs);

        var pathInfo = physicalFs.ConvertPathFromInternal(SystemPath).ToRelative();
        var pathSource = "/physical" / pathInfo / "Source";
        var filePathSource = pathSource / "test.txt";
        var systemPathSource = Path.Combine(SystemPath, "Source");
        var pathDest = "/physical" / pathInfo / "Dest";
        var filePathDest = pathDest / "test.txt";
        var systemPathDest = Path.Combine(SystemPath, "Dest");

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
            Assert.Throws<InvalidOperationException>(() => fs.CreateSymbolicLink("/memory/invalid", pathSource));

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
}



