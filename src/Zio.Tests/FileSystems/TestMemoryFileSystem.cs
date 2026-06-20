// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;

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
    public void TestWatcherCallbackCanReenterFileSystemWhenDispatchQueueIsBackedUp()
    {
        const int PreviousDispatchQueueCapacity = 16;
        const int QueuedDirectoryCount = PreviousDispatchQueueCapacity + 4;

        var fs = new MemoryFileSystem();
        var watcher = fs.Watch("/");
        var firstEventEntered = new ManualResetEventSlim(false);
        var releaseFirstEvent = new ManualResetEventSlim(false);
        var handlerCompleted = new ManualResetEventSlim(false);
        var startedOverflowCreate = new ManualResetEventSlim(false);

        Exception producerException = null;
        Exception cloneException = null;
        Thread cloneThread = null;
        var completedCreates = 0;
        var firstCallback = 0;
        var cloneTimedOut = false;

        watcher.Created += (_, _) =>
        {
            if (Interlocked.Exchange(ref firstCallback, 1) != 0)
            {
                return;
            }

            firstEventEntered.Set();
            releaseFirstEvent.Wait();

            var currentCloneThread = new Thread(() =>
            {
                try
                {
                    fs.Clone().Dispose();
                }
                catch (Exception ex)
                {
                    cloneException = ex;
                }
            })
            {
                IsBackground = true
            };

            cloneThread = currentCloneThread;
            currentCloneThread.Start();

            if (!currentCloneThread.Join(TimeSpan.FromSeconds(1)))
            {
                cloneTimedOut = true;
            }

            handlerCompleted.Set();
        };

        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        fs.CreateDirectory("/initial");
        Assert.IsTrue(firstEventEntered.Wait(TimeSpan.FromSeconds(5)), "The watcher callback was not invoked for the initial event.");

        var producer = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < QueuedDirectoryCount; i++)
                {
                    if (i == PreviousDispatchQueueCapacity)
                    {
                        startedOverflowCreate.Set();
                    }

                    fs.CreateDirectory($"/queued-{i}");
                    Interlocked.Increment(ref completedCreates);
                }
            }
            catch (Exception ex)
            {
                producerException = ex;
            }
        })
        {
            IsBackground = true
        };

        producer.Start();
        Assert.IsTrue(startedOverflowCreate.Wait(TimeSpan.FromSeconds(5)), "The producer did not reach the dispatch queue backpressure point.");

        SpinWait.SpinUntil(
            () => Volatile.Read(ref completedCreates) > PreviousDispatchQueueCapacity || !producer.IsAlive,
            TimeSpan.FromMilliseconds(500));

        releaseFirstEvent.Set();

        Assert.IsTrue(handlerCompleted.Wait(TimeSpan.FromSeconds(5)), "The watcher callback did not complete.");
        Assert.IsTrue(producer.Join(TimeSpan.FromSeconds(5)), "The event producer did not complete.");
        Assert.IsTrue(cloneThread?.Join(TimeSpan.FromSeconds(5)) ?? true, "The re-entrant clone did not complete after the producer finished.");

        watcher.Dispose();
        fs.Dispose();

        if (producerException != null)
        {
            Assert.Fail($"The event producer failed: {producerException}");
        }

        if (cloneException != null)
        {
            Assert.Fail($"The re-entrant clone failed: {cloneException}");
        }

        Assert.IsFalse(cloneTimedOut, "A watcher callback re-entering MemoryFileSystem blocked while the event producer held a filesystem lock.");
    }

    [TestMethod]
    public void TestWatcherCoalescesChangeEventsForMultipleWritesBeforeFlush()
    {
        var fs = new MemoryFileSystem();
        var watcher = fs.Watch("/");
        var changedReceived = new ManualResetEventSlim(false);
        var changedCount = 0;

        watcher.Changed += (_, args) =>
        {
            if (args.FullPath == "/watched.txt")
            {
                Interlocked.Increment(ref changedCount);
                changedReceived.Set();
            }
        };

        watcher.EnableRaisingEvents = true;

        using (var stream = fs.OpenFile("/watched.txt", FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var buffer = new byte[1];
            for (var i = 0; i < 32; i++)
            {
                buffer[0] = (byte)i;
                stream.Write(buffer, 0, buffer.Length);
            }

            stream.Flush();
        }

        Assert.IsTrue(changedReceived.Wait(TimeSpan.FromSeconds(5)), "The write did not dispatch a change event.");
        var sawDuplicateChange = SpinWait.SpinUntil(() => Volatile.Read(ref changedCount) > 1, TimeSpan.FromSeconds(1));

        watcher.Dispose();
        fs.Dispose();

        Assert.IsFalse(sawDuplicateChange, "Multiple writes before one flush should dispatch one coalesced change event.");
        Assert.AreEqual(1, Volatile.Read(ref changedCount));
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



