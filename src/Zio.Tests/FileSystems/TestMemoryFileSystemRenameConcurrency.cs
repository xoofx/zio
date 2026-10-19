// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

/// <summary>
/// A move or replace within a sub-directory keeps that directory locked while it looks up its other paths, so those
/// lookups must not take the locks of the ancestors again: a concurrent lookup in the same directory holds an
/// ancestor while it waits for the directory, and the two would block each other forever.
/// </summary>
[TestClass]
public class TestMemoryFileSystemRenameConcurrency
{
    private const int Iterations = 2000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void TestMoveFileInSubDirectoryWithConcurrentLookups()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/d");
        fs.WriteAllText("/d/a.txt", "a");
        fs.WriteAllText("/d/other.txt", "other");

        AssertRenamesCompleteWithConcurrentLookups(fs, () =>
        {
            fs.MoveFile("/d/a.txt", "/d/b.txt");
            fs.MoveFile("/d/b.txt", "/d/a.txt");
        });

        Assert.AreEqual("a", fs.ReadAllText("/d/a.txt"));
        Assert.IsFalse(fs.FileExists("/d/b.txt"));
    }

    [TestMethod]
    public void TestMoveDirectoryInSubDirectoryWithConcurrentLookups()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/d/x");
        fs.WriteAllText("/d/x/file.txt", "x");
        fs.WriteAllText("/d/other.txt", "other");

        AssertRenamesCompleteWithConcurrentLookups(fs, () =>
        {
            fs.MoveDirectory("/d/x", "/d/y");
            fs.MoveDirectory("/d/y", "/d/x");
        });

        Assert.AreEqual("x", fs.ReadAllText("/d/x/file.txt"));
        Assert.IsFalse(fs.DirectoryExists("/d/y"));
    }

    [TestMethod]
    public void TestReplaceFileInSubDirectoryWithConcurrentLookups()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/d");
        fs.WriteAllText("/d/dest.txt", "dest");
        fs.WriteAllText("/d/other.txt", "other");

        var i = 0;
        AssertRenamesCompleteWithConcurrentLookups(fs, () =>
        {
            fs.WriteAllText("/d/src.txt", $"src {i++}");
            fs.ReplaceFile("/d/src.txt", "/d/dest.txt", "/d/backup.txt", ignoreMetadataErrors: true);
        });

        Assert.AreEqual($"src {Iterations - 1}", fs.ReadAllText("/d/dest.txt"));
        Assert.AreEqual($"src {Iterations - 2}", fs.ReadAllText("/d/backup.txt"));
        Assert.IsFalse(fs.FileExists("/d/src.txt"));
    }

#if NET10_0_OR_GREATER && !ZIO_NO_ASYNC
    [TestMethod]
    public async Task TestMoveFileAsyncInSubDirectoryWithConcurrentLookups()
    {
        await using var fs = new MemoryFileSystemAsync();
        await fs.CreateDirectoryAsync("/d");
        await fs.WriteAllTextAsync("/d/a.txt", "a");
        await fs.WriteAllTextAsync("/d/other.txt", "other");

        var stop = 0;
        var lookups = Task.Run(async () =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                await fs.FileExistsAsync("/d/other.txt");
            }
        });

        var renames = Task.Run(async () =>
        {
            for (var i = 0; i < Iterations; i++)
            {
                await fs.MoveFileAsync("/d/a.txt", "/d/b.txt");
                await fs.MoveFileAsync("/d/b.txt", "/d/a.txt");
            }
        });

        var completed = await Task.WhenAny(renames, Task.Delay(Timeout)) == renames;
        Volatile.Write(ref stop, 1);

        Assert.IsTrue(completed, "The renames did not complete: they are deadlocked with the concurrent lookups");
        await renames;
        Assert.IsTrue(await Task.WhenAny(lookups, Task.Delay(Timeout)) == lookups, "The lookups did not complete");
        Assert.AreEqual("a", await fs.ReadAllTextAsync("/d/a.txt"));
    }
#endif

    private static void AssertRenamesCompleteWithConcurrentLookups(MemoryFileSystem fs, Action rename)
    {
        var stop = 0;
        var lookups = new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                fs.FileExists("/d/other.txt");
            }
        }) { IsBackground = true };

        Exception failure = null;
        var renames = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < Iterations; i++)
                {
                    rename();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }) { IsBackground = true };

        lookups.Start();
        renames.Start();
        var completed = renames.Join(Timeout);
        Volatile.Write(ref stop, 1);

        Assert.IsTrue(completed, "The renames did not complete: they are deadlocked with the concurrent lookups");
        Assert.IsNull(failure, failure?.ToString());
        Assert.IsTrue(lookups.Join(Timeout), "The lookups did not complete");
    }
}
