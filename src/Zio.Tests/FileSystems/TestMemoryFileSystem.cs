// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestMemoryFileSystem : TestFileSystemBase
{
    [TestMethod]
    public void TestCommonRead()
    {
        var fs = GetCommonMemoryFileSystem();
        AssertCommonRead(fs);
    }

    [TestMethod]
    public void TestCopyFileSystem()
    {
        var fs = GetCommonMemoryFileSystem();

        var dest = new MemoryFileSystem();
        fs.CopyTo(dest, UPath.Root, true);

        AssertFileSystemEqual(fs, dest);
    }

    [TestMethod]
    public void TestCopyFileSystemSubFolder()
    {
        var fs = GetCommonMemoryFileSystem();

        var dest = new MemoryFileSystem();
        var subFolder = UPath.Root / "subfolder";
        fs.CopyTo(dest, subFolder, true);

        var destSubFileSystem = dest.GetOrCreateSubFileSystem(subFolder);

        AssertFileSystemEqual(fs, destSubFileSystem);
    }


    [TestMethod]
    public void TestWatcher()
    {
        var fs = GetCommonMemoryFileSystem();
        AssertFileCreatedEventDispatched(fs, "/a", "/a/watched.txt");
    }

    [TestMethod]
    public void TestCreatingTopFile()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/");
    }

    [TestMethod]
    public void TestDispose()
    {
        var memfs = new MemoryFileSystem();

        memfs.Dispose();
        Assert.Throws<ObjectDisposedException>(() => memfs.DirectoryExists("/"));
    }

    [TestMethod]
    public void TestCopyFileCross()
    {
        var fs = new TriggerMemoryFileSystem();
        fs.CreateDirectory("/sub1");
        fs.CreateDirectory("/sub2");
        var sub1 = new SubFileSystem(fs, "/sub1");
        var sub2 = new SubFileSystem(fs, "/sub2");
        sub1.WriteAllText("/file.txt", "test");
        sub1.CopyFileCross("/file.txt", sub2, "/file.txt", overwrite: false);
        AssertEx.AreEqual("test", sub2.ReadAllText("/file.txt"));
        AssertEx.AreEqual(TriggerMemoryFileSystem.TriggerType.Copy, fs.Triggered);
    }

    [TestMethod]
    public void TestMoveFileCross()
    {
        var fs = new TriggerMemoryFileSystem();
        fs.CreateDirectory("/sub1");
        fs.CreateDirectory("/sub2");
        var sub1 = new SubFileSystem(fs, "/sub1");
        var sub2 = new SubFileSystem(fs, "/sub2");
        sub1.WriteAllText("/file.txt", "test");
        sub1.MoveFileCross("/file.txt", sub2, "/file.txt");
        AssertEx.AreEqual("test", sub2.ReadAllText("/file.txt"));
        Assert.IsFalse(sub1.FileExists("/file.txt"));
        AssertEx.AreEqual(TriggerMemoryFileSystem.TriggerType.Move, fs.Triggered);
    }

    [TestMethod]
    public void TestMoveFileCrossMount()
    {
        var fs = new TriggerMemoryFileSystem();
        fs.CreateDirectory("/sub1");
        fs.CreateDirectory("/sub2");
        var mount = new MountFileSystem();
        var sub1 = new SubFileSystem(fs, "/sub1");
        var sub2 = new SubFileSystem(fs, "/sub2");
        mount.Mount("/sub2-mount", sub2);
        sub1.WriteAllText("/file.txt", "test");
        sub1.MoveFileCross("/file.txt", mount, "/sub2-mount/file.txt");
        AssertEx.AreEqual("test", sub2.ReadAllText("/file.txt"));
        Assert.IsFalse(sub1.FileExists("/file.txt"));
        AssertEx.AreEqual(TriggerMemoryFileSystem.TriggerType.Move, fs.Triggered);
    }

    private sealed class TriggerMemoryFileSystem : MemoryFileSystem
    {
        public enum TriggerType
        {
            None,
            Copy,
            Move
        }

        public TriggerType Triggered { get; private set; } = TriggerType.None;

        protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            Triggered = TriggerType.Copy;
            base.CopyFileImpl(srcPath, destPath, overwrite);
        }

        protected override void MoveFileImpl(UPath srcPath, UPath destPath)
        {
            Triggered = TriggerType.Move;
            base.MoveFileImpl(srcPath, destPath);
        }
    }
}



