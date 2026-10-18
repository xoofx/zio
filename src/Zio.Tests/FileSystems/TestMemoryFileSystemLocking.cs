// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestMemoryFileSystemLocking
{
    private static readonly TimeSpan BlockedWait = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void TestWaitingExclusiveLockDoesNotBlockSharedLocks()
    {
        var fs = new MemoryFileSystem();
        fs.WriteAllText("/a.txt", "a");

        using (var holder = new SharedLockHolder(fs, "/"))
        {
            // Creating a directory in / requires an exclusive lock on /, which is held shared
            var writer = new BackgroundAction(() => fs.CreateDirectory("/x"));
            Assert.IsFalse(writer.Wait(BlockedWait), "CreateDirectory should wait for the shared lock on / to be released");

            // A waiting exclusive lock doesn't prevent new shared locks
            var reader = new BackgroundAction(() => Assert.IsTrue(fs.FileExists("/a.txt")));
            reader.AssertCompleted("FileExists should not wait for a pending exclusive lock");
            Assert.IsFalse(writer.Wait(TimeSpan.Zero), "CreateDirectory should still wait for the shared lock on / to be released");

            holder.Release();
            writer.AssertCompleted("CreateDirectory should complete once the shared lock on / is released");
        }

        Assert.IsTrue(fs.DirectoryExists("/x"));
    }

    [TestMethod]
    public void TestBlockedLocksAreWokenUpWhenReleased()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/d");
        fs.WriteAllText("/d/b.txt", "b");
        fs.WriteAllText("/a.txt", "a");

        using (var holder = new SharedLockHolder(fs, "/d"))
        {
            // Locks / exclusively and then waits for an exclusive lock on /d
            var writer = new BackgroundAction(() => fs.CreateDirectory("/d/sub"));
            Assert.IsFalse(writer.Wait(BlockedWait), "CreateDirectory should wait for the shared lock on /d to be released");

            // Waits for the exclusive lock on / to be released
            var reader = new BackgroundAction(() => Assert.IsTrue(fs.FileExists("/a.txt")));
            Assert.IsFalse(reader.Wait(BlockedWait), "FileExists should wait for the exclusive lock on / to be released");

            holder.Release();
            writer.AssertCompleted("CreateDirectory should complete once the shared lock on /d is released");
            reader.AssertCompleted("FileExists should complete once the exclusive lock on / is released");
        }

        Assert.IsTrue(fs.DirectoryExists("/d/sub"));
    }

    [TestMethod]
    public void TestCloneWaitsForPendingOperations()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/d");
        fs.WriteAllText("/d/b.txt", "b");

        MemoryFileSystem clone = null;
        using (var holder = new SharedLockHolder(fs, "/d"))
        {
            // Cloning requires an exclusive lock on the whole filesystem
            var cloner = new BackgroundAction(() => clone = fs.Clone());
            Assert.IsFalse(cloner.Wait(BlockedWait), "Clone should wait for the pending enumeration to release its locks");

            var reader = new BackgroundAction(() => Assert.IsTrue(fs.FileExists("/d/b.txt")));
            reader.AssertCompleted("FileExists should not wait for a pending exclusive lock on the filesystem");

            holder.Release();
            cloner.AssertCompleted("Clone should complete once the enumeration has released its locks");
        }

        Assert.AreEqual("b", clone.ReadAllText("/d/b.txt"));
    }

    [TestMethod]
    public void TestFileShareModes()
    {
        var fs = new MemoryFileSystem();
        fs.WriteAllText("/file.txt", "content");

        using (fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.Read))
        using (fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // The requested share must be a subset of the share of the current readers
            Assert.Throws<IOException>(() => fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            Assert.Throws<IOException>(() => fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.None));
        }

        // Once all the readers are closed, the share of the next reader is used
        using (fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.None));
        }

        using (fs.OpenFile("/file.txt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.Read));
            Assert.Throws<IOException>(() => fs.OpenFile("/file.txt", FileMode.Open, FileAccess.Read, FileShare.None));
        }

        // All the locks have been released
        using (fs.OpenFile("/file.txt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        Assert.AreEqual("content", fs.ReadAllText("/file.txt"));
    }

    [TestMethod]
    public void TestConcurrentOperationsDoNotDeadlockOrLeakLocks()
    {
        const int ThreadCount = 8;
        const int Iterations = 300;
        const int SharedDirectoryCount = 4;

        var fs = new MemoryFileSystem();
        for (var i = 0; i < SharedDirectoryCount; i++)
        {
            fs.CreateDirectory($"/shared/dir{i}");
            fs.WriteAllText($"/shared/dir{i}/file.txt", "shared");
        }
        fs.CreateDirectory("/own");
        fs.CreateDirectory("/moved");

        var workers = new List<BackgroundAction>();
        for (var t = 0; t < ThreadCount; t++)
        {
            var threadIndex = t;
            workers.Add(new BackgroundAction(() =>
            {
                var own = $"/own/thread{threadIndex}";
                fs.CreateDirectory(own);
                for (var i = 0; i < Iterations; i++)
                {
                    // Readers on directories and files shared by all the threads
                    var shared = $"/shared/dir{i % SharedDirectoryCount}";
                    Assert.IsTrue(fs.DirectoryExists(shared));
                    Assert.IsTrue(fs.FileExists($"{shared}/file.txt"));
                    Assert.AreEqual("shared", fs.ReadAllText($"{shared}/file.txt"));
                    Assert.AreEqual(SharedDirectoryCount, fs.EnumerateDirectories("/shared").Count());
                    Assert.AreEqual(SharedDirectoryCount, fs.EnumerateItems("/shared", SearchOption.AllDirectories).Count(item => !item.IsDirectory));

                    // Writers in a directory owned by this thread, while other threads are walking through the same parents
                    var file = $"{own}/file{i % 3}.txt";
                    fs.WriteAllText(file, "content");
                    Assert.AreEqual(7L, fs.GetFileLength(file));
                    fs.CreateDirectory($"{own}/sub{i % 2}/child");
                    fs.EnumerateFiles("/own", "*", SearchOption.AllDirectories).Count();

                    if (i % 5 == 0)
                    {
                        fs.DeleteDirectory($"{own}/sub{i % 2}", true);
                    }

                    if (i % 7 == 0)
                    {
                        fs.DeleteFile(file);
                    }
                    else if (i % 11 == 0)
                    {
                        // Moving across directories takes an exclusive lock on the whole filesystem
                        var moved = $"/moved/thread{threadIndex}-{i}.txt";
                        fs.MoveFile(file, moved);
                        fs.DeleteFile(moved);
                    }
                }
            }));
        }

        for (var t = 0; t < workers.Count; t++)
        {
            workers[t].AssertCompleted($"Worker thread {t} did not complete");
        }

        // Every node can be locked exclusively again: no lock has been leaked
        var check = new BackgroundAction(() =>
        {
            fs.Clone().Dispose();

            foreach (var file in fs.EnumerateFiles("/", "*", SearchOption.AllDirectories).ToList())
            {
                using (fs.OpenFile(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }

            fs.DeleteDirectory("/own", true);
            fs.DeleteDirectory("/shared", true);
            fs.DeleteDirectory("/moved", true);
        });
        check.AssertCompleted("Locks were not all released");
        Assert.AreEqual(0, fs.EnumerateItems("/", SearchOption.AllDirectories).Count());
    }

    /// <summary>
    /// Holds a shared lock on a directory (and on the filesystem) by blocking in the predicate of an enumeration of this directory.
    /// </summary>
    private sealed class SharedLockHolder : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);
        private readonly BackgroundAction _enumeration;
        private bool _released;

        public SharedLockHolder(MemoryFileSystem fs, UPath directory)
        {
            _enumeration = new BackgroundAction(() =>
            {
                fs.EnumerateItems(directory, SearchOption.TopDirectoryOnly, (ref FileSystemItem item) =>
                {
                    _entered.Set();
                    _release.Wait(CompletionTimeout);
                    return true;
                }).ToList();
            });

            Assert.IsTrue(_entered.Wait(CompletionTimeout), $"The enumeration of `{directory}` did not start");
        }

        public void Release()
        {
            _released = true;
            _release.Set();
            _enumeration.AssertCompleted("The enumeration holding the shared lock did not complete");
        }

        public void Dispose()
        {
            if (!_released)
            {
                _release.Set();
            }
        }
    }

    private sealed class BackgroundAction
    {
        private readonly Thread _thread;
        private Exception _exception;

        public BackgroundAction(Action action)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _exception = ex;
                }
            })
            {
                IsBackground = true
            };
            _thread.Start();
        }

        public bool Wait(TimeSpan timeout) => _thread.Join(timeout);

        public void AssertCompleted(string message)
        {
            Assert.IsTrue(_thread.Join(CompletionTimeout), message);
            if (_exception != null)
            {
                Assert.Fail($"{message}: {_exception}");
            }
        }
    }
}
