// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.IO;
using System.Security.Principal;
using System.Text;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

public class TestSubFileSystem : TestFileSystemBase
{
    [Fact]
    public void TestBasic()
    {
        var fs = new PhysicalFileSystem();
        var path = fs.ConvertPathFromInternal(SystemPath);

        // Create a filesystem / on the current folder of this assembly
        var subfs = new SubFileSystem(fs, path);

        // This test is basically testing the two methods (ConvertPathToDelegate and ConvertPathFromDelegate) in SubFileSystem

        var files = subfs.EnumeratePaths("/").Select(info => info.GetName()).ToList();
        var expectedFiles = fs.EnumeratePaths(path).Select(info => info.GetName()).ToList();
        Assert.True(files.Count > 0);
        Assert.Equal(expectedFiles, files);

        // Check that SubFileSystem is actually checking that the directory exists in the delegate filesystem
        Assert.Throws<DirectoryNotFoundException>(() => new SubFileSystem(fs, path / "does_not_exist"));

        if (IsWindows)
        {
            Assert.Throws<InvalidOperationException>(() => subfs.ConvertPathFromInternal(@"C:\"));
        }

        // TODO: We could add another test just to make sure that files can be created...etc. But the test above should already cover the code provided in SubFileSystem
    } 

    [Fact]
    public void TestGetOrCreateFileSystem()
    {
        var fs = new MemoryFileSystem();
        const string subFolder = "/sub";
        var subFileSystem = fs.GetOrCreateSubFileSystem(subFolder);
        Assert.True(fs.DirectoryExists(subFolder));
        subFileSystem.WriteAllText("/test.txt", "yo");
        var text = fs.ReadAllText(subFolder + "/test.txt");
        Assert.Equal("yo", text);
    }

    [Fact]
    public void TestWatcher()
    {
        var fs = GetCommonMemoryFileSystem();
        var subFs = fs.GetOrCreateSubFileSystem("/a/b");
        var watcher = subFs.Watch("/");

        var waitHandle = new ManualResetEvent(false);
        watcher.Created += (sender, args) =>
        {
            if (args.FullPath == "/watched.txt")
            {
                waitHandle.Set();
            }
        };

        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        fs.WriteAllText("/a/b/watched.txt", "test");
        Assert.True(waitHandle.WaitOne(100));
    }

    [SkippableTheory]
    [InlineData("/test", "/test", "/foo.txt")]
    [InlineData("/test", "/test", "/~foo.txt")]
    [InlineData("/test", "/TEST", "/foo.txt")]
    [InlineData("/test", "/TEST", "/~foo.txt")]
    [InlineData("/verylongname", "/VERYLONGNAME", "/foo.txt")]
    [InlineData("/verylongname", "/VERYLONGNAME", "/~foo.txt")]
    public void TestWatcherCaseSensitive(string physicalDir, string subDir, string filePath)
    {
        Skip.IfNot(IsWindows, "This test involves case insensitivity on Windows");

        var physicalFs = GetCommonPhysicalFileSystem();
        physicalFs.CreateDirectory(physicalDir);

        Assert.True(physicalFs.DirectoryExists(physicalDir));
        Assert.True(physicalFs.DirectoryExists(subDir));

        var subFs = new SubFileSystem(physicalFs, subDir);
        var watcher = subFs.Watch("/");
        var waitHandle = new ManualResetEvent(false);

        watcher.Created += (sender, args) =>
        {
            if (args.FullPath == filePath)
            {
                waitHandle.Set();
            }
        };

        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        physicalFs.WriteAllText($"{physicalDir}{filePath}", "test");

        Assert.True(waitHandle.WaitOne(100));
    }

    [SkippableFact]
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
        var fs = new SubFileSystem(physicalFs, physicalFs.ConvertPathFromInternal(SystemPath));

        UPath pathSource = "/Source";
        var filePathSource = pathSource / "test.txt";
        var systemPathSource = fs.ConvertPathToInternal(pathSource);
        UPath pathDest = "/Dest";
        var filePathDest = pathDest / "test.txt";
        var systemPathDest = fs.ConvertPathToInternal(pathDest);

        try
        {
            // CreateDirectory
            Assert.False(Directory.Exists(systemPathSource));
            fs.CreateDirectory(pathSource);
            Assert.True(Directory.Exists(systemPathSource));

            // CreateFile / OpenFile
            var fileStream = fs.CreateFile(filePathSource);
            var buffer = Encoding.UTF8.GetBytes("This is a test");
            fileStream.Write(buffer, 0, buffer.Length);
            fileStream.Dispose();
            Assert.Equal(buffer.Length, fs.GetFileLength(filePathSource));

            // CreateSymbolicLink
            fs.CreateSymbolicLink(pathDest, pathSource);

            // ResolveSymbolicLink
            Assert.True(fs.TryResolveLinkTarget(pathDest, out var resolvedPath));
            Assert.Equal(pathSource, resolvedPath);

            // FileExists
            Assert.True(fs.FileExists(filePathDest));
            Assert.Equal(buffer.Length, fs.GetFileLength(filePathDest));

            // RemoveDirectory
            fs.DeleteDirectory(pathDest, false);
            Assert.False(Directory.Exists(systemPathDest));
            Assert.True(Directory.Exists(systemPathSource));
        }
        finally
        {
            SafeDeleteDirectory(systemPathSource);
            SafeDeleteDirectory(systemPathDest);
        }
    }
}