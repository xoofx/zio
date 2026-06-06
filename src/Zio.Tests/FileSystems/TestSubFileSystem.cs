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
    [DataRow("/addons", "/ADDONS/foo.txt", "/foo.txt")]  // flat:   sfs /addons  <-  pfs /ADDONS
    [DataRow("/a/b", "/A/B/foo.txt", "/foo.txt")]         // nested:  sfs /a/b     <-  pfs /A/B
    public void TestBasicCaseInsensitive(string sfsSubPath, string pfsDelegatePath, string expected)
    {
        // mirrors TestBasic but it explicitly requests a case-insensitive comparison and checks that the overload
        // is working as intended
        var fs = new DifferentlyCasedEnumerateFileSystem(pfsDelegatePath);
        fs.CreateDirectory(sfsSubPath);

        // Through the public EnumeratePaths surface, a differently-cased delegate (pfs) prefix maps into the sfs view.
        var subfs = new SubFileSystem(fs, sfsSubPath, owned: true, isCaseSensitive: false);
        AssertEx.AreEqual((UPath)expected, subfs.EnumeratePaths("/").Single());

        // Without the flag the pfs-cased prefix is unrooted, so the same enumeration throws.
        var sensitive = new SubFileSystem(fs, sfsSubPath);
        Assert.Throws<InvalidOperationException>(() => sensitive.EnumeratePaths("/").ToList());
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
    [DataRow("/sandbox", "/sandbox/foo.txt", "/foo.txt", "/SANDBOX/foo.txt", true)]   // flat, sensitive
    [DataRow("/sandbox", "/sandbox/foo.txt", "/foo.txt", "/SANDBOX/foo.txt", false)]  // flat, insensitive
    [DataRow("/a/b", "/a/b/c.txt", "/c.txt", "/A/B/c.txt", true)]                     // nested, sensitive
    [DataRow("/a/b", "/a/b/c.txt", "/c.txt", "/A/B/c.txt", false)]                    // nested, insensitive
    [DataRow("/A/B", "/A/B/c.txt", "/c.txt", "/a/b/c.txt", false)]                    // upper-cased sub path
    [DataRow("/a/b", "/a/b/C/d.txt", "/C/d.txt", "/A/B/C/d.txt", false)]              // deeper, child casing preserved
    public void TestConvertPathFromDelegateRespectsCaseSensitivity(string subPath, string delegatePath, string expected, string differentlyCasedPath, bool isCaseSensitive)
    {
        // this is ConvertPathFromDelegate overload test to make sure it behaves as expected
        var fs = new MemoryFileSystem();
        fs.CreateDirectory(subPath);

        var subFs = new TestableSubFileSystem(fs, subPath, isCaseSensitive);

        // Matching casing always strips the sub path prefix.
        AssertEx.AreEqual((UPath)expected, subFs.ConvertPathFromDelegateForTest(delegatePath));

        // A differently-cased prefix strips only when case-insensitive, otherwise it is unrooted and throws.
        if (isCaseSensitive)
        {
            Assert.Throws<InvalidOperationException>(() => subFs.ConvertPathFromDelegateForTest(differentlyCasedPath));
        }
        else
        {
            AssertEx.AreEqual((UPath)expected, subFs.ConvertPathFromDelegateForTest(differentlyCasedPath));
        }
    }

    [TestMethod]
    [DataRow("/sandbox", "/SANDBOX/foo.txt")]  // flat
    [DataRow("/a/b", "/A/B/c.txt")]            // nested
    public void TestConvertPathFromDelegateIsCaseSensitiveByDefault(string subPath, string differentlyCasedPath)
    {
        // this is the base ConvertPathFromDelegate that defaults to case-sensitive without overload argument
        var fs = new MemoryFileSystem();
        fs.CreateDirectory(subPath);

        // The flag-less constructor defaults to case-sensitive, so a differently-cased prefix is unrooted and throws.
        var subFs = new TestableSubFileSystem(fs, subPath);
        Assert.Throws<InvalidOperationException>(() => subFs.ConvertPathFromDelegateForTest(differentlyCasedPath));
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
    [DataRow("/test", "/test", "/foo.txt")]
    [DataRow("/test", "/test", "/~foo.txt")]
    [DataRow("/test", "/TEST", "/foo.txt")]
    [DataRow("/test", "/TEST", "/~foo.txt")]
    [DataRow("/verylongname", "/VERYLONGNAME", "/foo.txt")]
    [DataRow("/verylongname", "/VERYLONGNAME", "/~foo.txt")]
    public void TestWatcherCaseInsensitive(string eventDir, string subDir, string filePath)
    {
        // This mirrors TestWatcherCaseSensitive, but checks that the watcher can also make
        // a case-insensitive comparison as well, when constructor initializes case-insensitive
        var fs = new TriggerableWatchFileSystem();
        fs.CreateDirectory(subDir);

        // Case-insensitive: a differently-cased sub-path prefix on the delegate event still matches.
        var subFs = new SubFileSystem(fs, subDir, owned: true, isCaseSensitive: false);
        var watcher = subFs.Watch("/");
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        var waitHandle = new ManualResetEvent(false);
        watcher.Created += (sender, args) =>
        {
            if (args.FullPath == filePath)
            {
                waitHandle.Set();
            }
        };

        // The delegate emits an event whose prefix casing may differ from the sub path.
        fs.LastWatcher.TriggerCreated($"{eventDir}{filePath}");

        Assert.IsTrue(waitHandle.WaitOne(100));
    }

    [TestMethod]
    public void TestWatcherCaseSensitiveDropsDifferentCasing()
    {
        // Since we now have an override for ShouldRaiseEventImpl, we want to make sure a
        // non-overloaded SubFileSystem will still behave case-sensitively
        var fs = new TriggerableWatchFileSystem();
        fs.CreateDirectory("/sandbox");

        // Default constructor is ordinal (case-sensitive): a differently-cased event prefix is out of
        // scope, so the watcher drops it (guards the filtering / negative branch that the raise tests don't).
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

        public TestableSubFileSystem(IFileSystem fileSystem, UPath subPath, bool isCaseSensitive)
            : base(fileSystem, subPath, owned: false, isCaseSensitive: isCaseSensitive)
        {
        }

        public UPath ConvertPathToDelegateForTest(UPath path) => base.ConvertPathToDelegate(path);

        public UPath ConvertPathFromDelegateForTest(UPath path) => base.ConvertPathFromDelegate(path);
    }

    // Fake delegate that always enumerates one caller-supplied path, so SubFileSystem's enumerate conversion sees a differently-cased prefix.
    private sealed class DifferentlyCasedEnumerateFileSystem : MemoryFileSystem
    {
        private readonly UPath _delegatePath;

        public DifferentlyCasedEnumerateFileSystem(UPath delegatePath) => _delegatePath = delegatePath;

        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            yield return _delegatePath;
        }
    }

    // Fake delegate that exposes its watcher via LastWatcher, so a test can fire an arbitrary (differently-cased) event into SubFileSystem.
    private sealed class TriggerableWatchFileSystem : MemoryFileSystem
    {
        public TriggerableWatcher LastWatcher { get; private set; }

        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            LastWatcher = new TriggerableWatcher(this, path);
            return LastWatcher;
        }
    }

    // Pokeable watcher: TriggerCreated exposes the protected RaiseCreated, and it skips its own filtering so events reach the SubFileSystem watcher under test.
    private sealed class TriggerableWatcher : Zio.FileSystems.FileSystemWatcher
    {
        public TriggerableWatcher(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
        {
        }

        protected override bool ShouldRaiseEventImpl(FileChangedEventArgs args) => true;

        public void TriggerCreated(UPath fullPath)
            => RaiseCreated(new FileChangedEventArgs(FileSystem, WatcherChangeTypes.Created, fullPath));
    }
}



