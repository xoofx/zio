// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestSubFileSystem : TestFileSystemBase
{
    [TestMethod]
    public void TestBasic()
    {
        var fs = new PhysicalFileSystem();
        var path = fs.ConvertPathFromInternal(SystemPath);

        // Create a filesystem / on the current folder of this assembly
        var subfs = new SubFileSystem(fs, path);

        // This test is basically testing the two methods (ConvertPathToDelegate and ConvertPathFromDelegate) in SubFileSystem

        var files = subfs.EnumeratePaths("/").Select(info => info.GetName()).ToList();
        var expectedFiles = fs.EnumeratePaths(path).Select(info => info.GetName()).ToList();
        Assert.IsTrue(files.Count > 0);
        AssertEx.AreEqual(expectedFiles, files);

        // Check that SubFileSystem is actually checking that the directory exists in the delegate filesystem
        Assert.Throws<DirectoryNotFoundException>(() => new SubFileSystem(fs, path / "does_not_exist"));

        if (IsWindows)
        {
            Assert.Throws<InvalidOperationException>(() => subfs.ConvertPathFromInternal(@"C:\"));
        }

        // TODO: We could add another test just to make sure that files can be created...etc. But the test above should already cover the code provided in SubFileSystem
    }

    [TestMethod]
    public void TestGetOrCreateFileSystem()
    {
        var fs = new MemoryFileSystem();
        const string subFolder = "/sub";
        var subFileSystem = fs.GetOrCreateSubFileSystem(subFolder);
        Assert.IsTrue(fs.DirectoryExists(subFolder));
        subFileSystem.WriteAllText("/test.txt", "yo");
        var text = fs.ReadAllText(subFolder + "/test.txt");
        AssertEx.AreEqual("yo", text);
    }

    [TestMethod]
    public void TestConvertPathToDelegateRejectsEscapes()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/sandbox");

        var subFs = new TestableSubFileSystem(fs, "/sandbox");
        var unsafePath = CreateUnsafePath("/..");

        var exception = Assert.Throws<UnauthorizedAccessException>(() => subFs.ConvertPathToDelegateForTest(unsafePath));
        StringAssert.Contains(exception.Message, "/sandbox");
    }

    [TestMethod]
    public void TestConvertPathFromDelegateIsOrdinalByDefault()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/sandbox");

        var subFs = new TestableSubFileSystem(fs, "/sandbox");

        // Matching casing strips the sub path prefix.
        AssertEx.AreEqual((UPath)"/foo.txt", subFs.ConvertPathFromDelegateForTest("/sandbox/foo.txt"));

        // A differently-cased prefix is not rooted under the default ordinal matching.
        Assert.Throws<InvalidOperationException>(() => subFs.ConvertPathFromDelegateForTest("/SANDBOX/foo.txt"));
    }

    [TestMethod]
    public void TestConvertPathFromDelegateOrdinalIgnoreCase()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/sandbox");

        var subFs = new TestableSubFileSystem(fs, "/sandbox", StringComparison.OrdinalIgnoreCase);

        // With case-insensitive matching, a differently-cased delegate prefix is stripped correctly.
        AssertEx.AreEqual((UPath)"/foo.txt", subFs.ConvertPathFromDelegateForTest("/SANDBOX/foo.txt"));
    }

    [TestMethod]
    // Section 4: case-insensitive prefix strip. Assumes the delegate already resolved on-disk casing,
    // so SubFileSystem is doing string-compare only.
    [DataRow("/a/b", "/A/B/c.txt", "/c.txt")]
    [DataRow("/A/B", "/a/b/c.txt", "/c.txt")]
    [DataRow("/a/b", "/A/B/C/d.txt", "/C/d.txt")]
    public void TestConvertPathFromDelegateOrdinalIgnoreCasePartialPaths(string subPath, string delegatePath, string expected)
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory(subPath);

        var subFs = new TestableSubFileSystem(fs, subPath, StringComparison.OrdinalIgnoreCase);
        AssertEx.AreEqual((UPath)expected, subFs.ConvertPathFromDelegateForTest(delegatePath));
    }

    [TestMethod]
    public void TestOrdinalIgnoreCaseConstructorValidatesDirectoryExists()
    {
        var fs = new MemoryFileSystem();

        // The comparisonType overload still requires the sub path to exist in the delegate;
        // the comparison only affects prefix matching, not the existence check.
        Assert.Throws<DirectoryNotFoundException>(
            () => new SubFileSystem(fs, "/missing", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
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
        Assert.IsTrue(waitHandle.WaitOne(100));
    }

    [TestMethod]
    [DataRow("/test", "/test", "/foo.txt")]
    [DataRow("/test", "/test", "/~foo.txt")]
    [DataRow("/test", "/TEST", "/foo.txt")]
    [DataRow("/test", "/TEST", "/~foo.txt")]
    [DataRow("/verylongname", "/VERYLONGNAME", "/foo.txt")]
    [DataRow("/verylongname", "/VERYLONGNAME", "/~foo.txt")]
    public void TestWatcherCaseSensitive(string physicalDir, string subDir, string filePath)
    {
        Skip.IfNot(IsWindows, "This test involves case insensitivity on Windows");

        var physicalFs = GetCommonPhysicalFileSystem();
        physicalFs.CreateDirectory(physicalDir);

        Assert.IsTrue(physicalFs.DirectoryExists(physicalDir));
        Assert.IsTrue(physicalFs.DirectoryExists(subDir));

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

        Assert.IsTrue(waitHandle.WaitOne(100));
    }

    [TestMethod]
    [DataRow(StringComparison.OrdinalIgnoreCase, true)]
    [DataRow(StringComparison.Ordinal, false)]
    public void TestWatcherOrdinalIgnoreCaseConversion(StringComparison comparisonType, bool shouldRaise)
    {
        var fs = new TriggerableWatchFileSystem();
        fs.CreateDirectory("/sandbox");

        var subFs = new SubFileSystem(fs, "/sandbox", comparisonType);
        var watcher = subFs.Watch("/");
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        var waitHandle = new ManualResetEvent(false);
        watcher.Created += (sender, args) =>
        {
            if (args.FullPath == "/foo.txt")
            {
                waitHandle.Set();
            }
        };

        // The delegate emits an event with a differently-cased sub-path prefix.
        fs.LastWatcher.TriggerCreated("/SANDBOX/foo.txt");

        Assert.AreEqual(shouldRaise, waitHandle.WaitOne(100));
    }

    [TestMethod]
    public void TestWatcherOrdinalByDefault()
    {
        var fs = new TriggerableWatchFileSystem();
        fs.CreateDirectory("/sandbox");

        // Default constructor - no comparisonType, so matching is ordinal (case-sensitive).
        var subFs = new SubFileSystem(fs, "/sandbox");
        var watcher = subFs.Watch("/");
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        var waitHandle = new ManualResetEvent(false);
        watcher.Created += (sender, args) =>
        {
            if (args.FullPath == "/foo.txt")
            {
                waitHandle.Set();
            }
        };

        // A differently-cased prefix is not rooted under the default ordinal matching, so it is dropped.
        fs.LastWatcher.TriggerCreated("/SANDBOX/foo.txt");

        Assert.IsFalse(waitHandle.WaitOne(100));
    }

    [TestMethod]
    public void TestResolvePath()
    {
        var fs = GetCommonMemoryFileSystem();
        var subFs = fs.GetOrCreateSubFileSystem("/a/b");
        var (resFs, resPath) = subFs.ResolvePath("/c");
        AssertEx.AreEqual("/a/b/c", resPath);
        AssertEx.AreNotEqual(subFs, resFs);
        AssertEx.AreEqual(fs, resFs);
        (resFs, resPath) = subFs.ResolvePath("/c/d");
        AssertEx.AreEqual("/a/b/c/d", resPath);
        AssertEx.AreNotEqual(subFs, resFs);
        AssertEx.AreEqual(fs, resFs);

        var subFs2 = subFs.GetOrCreateSubFileSystem("/q");
        (resFs, resPath) = subFs2.ResolvePath("/c");
        AssertEx.AreEqual("/a/b/q/c", resPath);
        AssertEx.AreNotEqual(subFs2, resFs);
        AssertEx.AreEqual(fs, resFs);
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

    private static UPath CreateUnsafePath(string path)
    {
        var ctor = typeof(UPath).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, binder: null, new[] { typeof(string), typeof(bool) }, modifiers: null);
        Assert.IsNotNull(ctor);
        return (UPath)ctor.Invoke(new object[] { path, true });
    }

    private sealed class TestableSubFileSystem : SubFileSystem
    {
        public TestableSubFileSystem(IFileSystem fileSystem, UPath subPath)
            : base(fileSystem, subPath, owned: false)
        {
        }

        public TestableSubFileSystem(IFileSystem fileSystem, UPath subPath, StringComparison comparisonType)
            : base(fileSystem, subPath, comparisonType, owned: false)
        {
        }

        public UPath ConvertPathToDelegateForTest(UPath path) => base.ConvertPathToDelegate(path);

        public UPath ConvertPathFromDelegateForTest(UPath path) => base.ConvertPathFromDelegate(path);
    }

    /// <summary>
    /// A memory filesystem whose watcher can be triggered manually with an arbitrary (e.g. differently-cased)
    /// path, used to drive the <see cref="SubFileSystem"/> watcher conversion in isolation.
    /// </summary>
    private sealed class TriggerableWatchFileSystem : MemoryFileSystem
    {
        public TriggerableWatcher LastWatcher { get; private set; }

        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            LastWatcher = new TriggerableWatcher(this, path);
            return LastWatcher;
        }
    }

    private sealed class TriggerableWatcher : Zio.FileSystems.FileSystemWatcher
    {
        public TriggerableWatcher(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
        {
        }

        // Skip the delegate watcher's own event filtering so the triggered event reaches the SubFileSystem watcher under test.
        protected override bool ShouldRaiseEventImpl(FileChangedEventArgs args) => true;

        public void TriggerCreated(UPath fullPath)
            => RaiseCreated(new FileChangedEventArgs(FileSystem, WatcherChangeTypes.Created, fullPath));
    }
}



