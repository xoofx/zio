// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

#if NET10_0_OR_GREATER && !ZIO_NO_ASYNC
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

[TestClass]
public class TestAsyncFileSystem
{
    [TestMethod]
    public async Task TestAsyncReadWriteAndEnumeration()
    {
        await using var fs = new MemoryFileSystemAsync();
        await fs.CreateDirectoryAsync("/data");

        await fs.WriteAllTextAsync("/data/file.txt", "hello");
        await fs.AppendAllTextAsync("/data/file.txt", " world");
        Assert.AreEqual("hello world", await fs.ReadAllTextAsync("/data/file.txt"));

        await fs.WriteAllBytesAsync("/data/blob.bin", new byte[] { 1, 2, 3 });
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await fs.ReadAllBytesAsync("/data/blob.bin"));

        await using (var stream = await fs.CreateFileAsync("/data/created.bin"))
        {
            await stream.WriteAsync(new byte[] { 4, 5 });
        }

        var txtFiles = await ToListAsync(fs.EnumerateFilesAsync("/data", "*.txt"));
        CollectionAssert.AreEqual(new UPath[] { "/data/file.txt" }, txtFiles);

        var allPaths = await ToListAsync(fs.EnumeratePathsAsync("/data", "*", SearchOption.TopDirectoryOnly));
        CollectionAssert.AreEquivalent(new UPath[] { "/data/blob.bin", "/data/created.bin", "/data/file.txt" }, allPaths);
    }

    [TestMethod]
    public async Task TestAsyncEntryHelpers()
    {
        await using var fs = new MemoryFileSystemAsync();
        await fs.CreateDirectoryAsync("/data");
        await fs.WriteAllTextAsync("/data/file.txt", "content");

        var file = await fs.GetFileEntryAsync("/data/file.txt");
        Assert.IsInstanceOfType<FileEntryAsync>(file);
        Assert.AreSame(fs, file.FileSystem);
        Assert.AreEqual("/data/file.txt", file.FullName);

        var directory = await fs.GetDirectoryEntryAsync("/data");
        Assert.IsInstanceOfType<DirectoryEntryAsync>(directory);
        Assert.AreSame(fs, directory.FileSystem);
        Assert.AreEqual("/data", directory.FullName);

        var fileSystemEntry = await fs.GetFileSystemEntryAsync("/data/file.txt");
        Assert.IsInstanceOfType<FileEntryAsync>(fileSystemEntry);
        Assert.AreSame(fs, fileSystemEntry.FileSystem);

        Assert.IsNull(await fs.TryGetFileSystemEntryAsync("/missing"));

        var entries = await ToListAsync(fs.EnumerateFileSystemEntriesAsync("/data"));
        var entryNames = entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray();
        CollectionAssert.AreEqual(new[] { "/data/file.txt" }, entryNames);
    }

    [TestMethod]
    public void TestFileSystemAsyncDoesNotExposeSyncBridge()
    {
        Assert.IsFalse(typeof(FileSystemAsync).GetMethods().Any(method => method.Name == "AsSync"));
        Assert.IsFalse(typeof(FileSystemAsync).GetInterfaces().Contains(typeof(IFileSystem)));
        Assert.IsFalse(typeof(FileSystem).GetInterfaces().Contains(typeof(IFileSystemAsync)));
    }

    [TestMethod]
    public async Task TestAsyncWatcherUsesAsyncTypes()
    {
        await using var fs = new MemoryFileSystemAsync();
        await fs.CreateDirectoryAsync("/data");

        using var watcher = await fs.WatchAsync("/data");

        Assert.IsInstanceOfType<IFileSystemWatcherAsync>(watcher);
        Assert.IsFalse(watcher is IFileSystemWatcher);
        Assert.AreSame(fs, watcher.FileSystem);
        Assert.AreEqual(typeof(EventHandler<FileChangedEventArgsAsync>), typeof(IFileSystemWatcherAsync).GetEvent(nameof(IFileSystemWatcherAsync.Created))!.EventHandlerType);
        Assert.AreEqual(typeof(EventHandler<FileRenamedEventArgsAsync>), typeof(IFileSystemWatcherAsync).GetEvent(nameof(IFileSystemWatcherAsync.Renamed))!.EventHandlerType);
        Assert.AreEqual(typeof(EventHandler<FileSystemErrorEventArgsAsync>), typeof(IFileSystemWatcherAsync).GetEvent(nameof(IFileSystemWatcherAsync.Error))!.EventHandlerType);

        var args = new FileChangedEventArgsAsync(fs, WatcherChangeTypes.Created, "/data/file.txt");
        Assert.AreSame(fs, args.FileSystem);
    }

    [TestMethod]
    public async Task TestReadOnlyFileSystemAsyncGuardsWrites()
    {
        await using var inner = new MemoryFileSystemAsync();
        await inner.WriteAllTextAsync("/file.txt", "content");
        await using var fs = new ReadOnlyFileSystemAsync(inner, owned: false);

        Assert.AreEqual("content", await fs.ReadAllTextAsync("/file.txt"));
        await AssertThrowsAsync<IOException>(() => fs.WriteAllTextAsync("/file.txt", "updated"));
        await AssertThrowsAsync<IOException>(() => fs.DeleteFileAsync("/file.txt"));
        await AssertThrowsAsync<IOException>(() => fs.SetLastWriteTimeAsync("/file.txt", DateTime.UtcNow));
    }

    [TestMethod]
    public async Task TestTryResolveLinkTargetAsyncReturnsTuple()
    {
        await using var fs = new LinkMemoryFileSystem();
        await fs.CreateSymbolicLinkAsync("/link", "/target");

        var result = await fs.TryResolveLinkTargetAsync("/link");

        Assert.IsTrue(result.Success);
        Assert.AreEqual((UPath)"/target", result.ResolvedPath);

        var missing = await fs.TryResolveLinkTargetAsync("/missing");
        Assert.IsFalse(missing.Success);
    }

    [TestMethod]
    public async Task TestSubFileSystemAsyncTranslatesPaths()
    {
        await using var inner = new MemoryFileSystemAsync();
        await inner.CreateDirectoryAsync("/root");
        await using var fs = new SubFileSystemAsync(inner, "/root", owned: false);

        await fs.WriteAllTextAsync("/file.txt", "content");

        Assert.AreEqual("content", await inner.ReadAllTextAsync("/root/file.txt"));
        CollectionAssert.AreEqual(new UPath[] { "/file.txt" }, await ToListAsync(fs.EnumerateFilesAsync("/")));

        var resolved = await fs.ResolvePathAsync("/file.txt");
        Assert.AreSame(inner, resolved.FileSystem);
        Assert.AreEqual((UPath)"/root/file.txt", resolved.Path);
    }

    [TestMethod]
    public async Task TestMountFileSystemAsyncDispatchesToMountedFileSystem()
    {
        await using var inner = new MemoryFileSystemAsync();
        await using var fs = new MountFileSystemAsync(owned: false);
        await fs.MountAsync("/mounted", inner);

        await fs.WriteAllTextAsync("/mounted/file.txt", "mounted");

        Assert.AreEqual("mounted", await inner.ReadAllTextAsync("/file.txt"));
        CollectionAssert.AreEqual(new UPath[] { "/mounted/file.txt" }, await ToListAsync(fs.EnumerateFilesAsync("/mounted")));
    }

    [TestMethod]
    public async Task TestAggregateFileSystemAsyncReadsTopmostFileSystem()
    {
        await using var fallback = new MemoryFileSystemAsync();
        await fallback.WriteAllTextAsync("/shared.txt", "fallback");
        await fallback.WriteAllTextAsync("/fallback.txt", "fallback");

        await using var top = new MemoryFileSystemAsync();
        await top.WriteAllTextAsync("/shared.txt", "top");
        await top.WriteAllTextAsync("/top.txt", "top");

        await using var fs = new AggregateFileSystemAsync(fallback, owned: false);
        await fs.AddFileSystemAsync(top);

        Assert.AreEqual("top", await fs.ReadAllTextAsync("/shared.txt"));
        CollectionAssert.AreEquivalent(new UPath[] { "/fallback.txt", "/shared.txt", "/top.txt" }, await ToListAsync(fs.EnumerateFilesAsync("/")));
    }

    [TestMethod]
    public async Task TestAsyncCancellationIsObserved()
    {
        await using var fs = new MemoryFileSystemAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await AssertThrowsAsync<OperationCanceledException, bool>(() => fs.FileExistsAsync("/file.txt", cancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task TestPhysicalFileSystemAsyncSmoke()
    {
        await using var physical = new PhysicalFileSystemAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "zio-async-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            await using var fs = new SubFileSystemAsync(physical, await physical.ConvertPathFromInternalAsync(tempRoot), owned: false);
            await fs.WriteAllTextAsync("/file.txt", "physical");

            Assert.AreEqual("physical", await fs.ReadAllTextAsync("/file.txt"));
            await fs.CopyFileAsync("/file.txt", "/copy.txt", overwrite: false);
            await fs.MoveFileAsync("/copy.txt", "/moved.txt");
            Assert.IsTrue(File.Exists(Path.Combine(tempRoot, "moved.txt")));
            Assert.AreEqual((UPath)"/moved.txt", await fs.ConvertPathFromInternalAsync(Path.Combine(tempRoot, "moved.txt")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task TestZipArchiveFileSystemAsyncSmoke()
    {
        using var stream = new MemoryStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        await using var fs = new ZipArchiveFileSystemAsync(archive);

        await fs.CreateDirectoryAsync("/dir");
        await fs.WriteAllTextAsync("/file.txt", "zip");
        await fs.CopyFileAsync("/file.txt", "/dir/copy.txt", overwrite: false);
        await fs.MoveFileAsync("/dir/copy.txt", "/dir/moved.txt");

        Assert.AreEqual("zip", await fs.ReadAllTextAsync("/file.txt"));
        Assert.AreEqual("zip", await fs.ReadAllTextAsync("/dir/moved.txt"));
        CollectionAssert.AreEquivalent(new UPath[] { "/dir/moved.txt", "/file.txt" }, await ToListAsync(fs.EnumerateFilesAsync("/", "*", SearchOption.AllDirectories)));
    }

    [TestMethod]
    public async Task TestAsyncFileSystemTypesUseAsyncDisposableOnly()
    {
        using var stream = new MemoryStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        var fileSystems = new IFileSystemAsync[]
        {
            new MemoryFileSystemAsync(),
            new PhysicalFileSystemAsync(),
            new ReadOnlyFileSystemAsync(new MemoryFileSystemAsync()),
            new SubFileSystemAsync(new MemoryFileSystemAsync(), "/"),
            new AggregateFileSystemAsync(new MemoryFileSystemAsync()),
            new MountFileSystemAsync(new MemoryFileSystemAsync()),
            new IdentityComposeFileSystemAsync(new MemoryFileSystemAsync()),
            new ZipArchiveFileSystemAsync(archive),
        };

        try
        {
            foreach (var fileSystem in fileSystems)
            {
                var boxed = (object)fileSystem;
                Assert.IsTrue(boxed is IAsyncDisposable, fileSystem.GetType().FullName);
                Assert.IsFalse(boxed is IDisposable, fileSystem.GetType().FullName);
                await fileSystem.DisposeAsync();
            }
        }
        finally
        {
            await DisposeAllAsync(fileSystems);
        }
    }

    [TestMethod]
    public async Task TestMemoryFileSystemAsyncSupportsMutationMetadataAndWatch()
    {
        await using var fs = new MemoryFileSystemAsync();
        await fs.CreateDirectoryAsync("/data");
        await fs.WriteAllTextAsync("/data/source.txt", "source");
        Assert.AreEqual(6L, await fs.GetFileLengthAsync("/data/source.txt"));

        var lastWrite = new DateTime(2024, 04, 05, 06, 07, 08, DateTimeKind.Utc);
        await fs.SetLastWriteTimeAsync("/data/source.txt", lastWrite);
        Assert.AreEqual(lastWrite, await fs.GetLastWriteTimeAsync("/data/source.txt"));

        await fs.CopyFileAsync("/data/source.txt", "/data/copy.txt", overwrite: false);
        await fs.MoveFileAsync("/data/copy.txt", "/data/moved.txt");
        await fs.ReplaceFileAsync("/data/moved.txt", "/data/source.txt", "/data/backup.txt", ignoreMetadataErrors: true);

        Assert.AreEqual("source", await fs.ReadAllTextAsync("/data/source.txt"));
        Assert.IsTrue(await fs.FileExistsAsync("/data/backup.txt"));
        Assert.IsTrue(await fs.CanWatchAsync("/data"));
        using var watcher = await fs.WatchAsync("/data");
        Assert.IsNotNull(watcher);
    }

    [TestMethod]
    public async Task TestComposeFileSystemAsyncDelegatesOperationsAndDisposal()
    {
        var inner = new TrackingMemoryFileSystem();
        await inner.CreateDirectoryAsync("/data");

        await using (var fs = new IdentityComposeFileSystemAsync(inner))
        {
            await fs.WriteAllTextAsync("/data/file.txt", "compose");
            await fs.SetCreationTimeAsync("/data/file.txt", new DateTime(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc));

            Assert.AreEqual("compose", await inner.ReadAllTextAsync("/data/file.txt"));
            Assert.AreEqual("compose", await fs.ReadAllTextAsync("/data/file.txt"));
            var resolved = await fs.ResolvePathAsync("/data/file.txt");
            Assert.AreSame(inner, resolved.FileSystem);
            Assert.AreEqual((UPath)"/data/file.txt", resolved.Path);
            CollectionAssert.AreEqual(new UPath[] { "/data/file.txt" }, await ToListAsync(fs.EnumerateFilesAsync("/data")));
        }

        Assert.AreEqual(1, inner.DisposeCount);
    }

    [TestMethod]
    public async Task TestAsyncDisposalOwnershipAcrossCompositeFileSystems()
    {
        var readOnlyInner = new TrackingMemoryFileSystem();
        await using (var readOnly = new ReadOnlyFileSystemAsync(readOnlyInner, owned: true))
        {
            Assert.AreEqual(0, readOnlyInner.DisposeCount);
        }

        Assert.AreEqual(1, readOnlyInner.DisposeCount);

        var subInner = new TrackingMemoryFileSystem();
        await subInner.CreateDirectoryAsync("/root");
        await using (var sub = new SubFileSystemAsync(subInner, "/root", owned: true))
        {
            Assert.AreEqual(0, subInner.DisposeCount);
        }

        Assert.AreEqual(1, subInner.DisposeCount);

        var notOwnedInner = new TrackingMemoryFileSystem();
        await using (var readOnly = new ReadOnlyFileSystemAsync(notOwnedInner, owned: false))
        {
            Assert.AreEqual(0, notOwnedInner.DisposeCount);
        }

        Assert.AreEqual(0, notOwnedInner.DisposeCount);
        await notOwnedInner.DisposeAsync();
        Assert.AreEqual(1, notOwnedInner.DisposeCount);
    }

    [TestMethod]
    public async Task TestAggregateFileSystemAsyncSearchesAndDisposesOwnedFileSystems()
    {
        var fallback = new TrackingMemoryFileSystem();
        await fallback.WriteAllTextAsync("/fallback.txt", "fallback");
        await fallback.WriteAllTextAsync("/shared.txt", "fallback");
        var top = new TrackingMemoryFileSystem();
        await top.WriteAllTextAsync("/top.txt", "top");
        await top.WriteAllTextAsync("/shared.txt", "top");

        await using (var fs = new AggregateFileSystemAsync(fallback, owned: true))
        {
            await fs.AddFileSystemAsync(top);

            Assert.AreEqual("top", await fs.ReadAllTextAsync("/top.txt"));
            Assert.AreEqual((UPath)"/top.txt", (await fs.FindFirstFileSystemEntryAsync("/top.txt"))!.Path);
            var entries = await fs.FindFileSystemEntriesAsync("/shared.txt");
            Assert.AreEqual(2, entries.Count);
            CollectionAssert.AreEqual(new UPath[] { "/shared.txt", "/shared.txt" }, entries.Select(entry => entry.Path).ToArray());
        }

        Assert.AreEqual(1, fallback.DisposeCount);
        Assert.AreEqual(1, top.DisposeCount);
    }

    [TestMethod]
    public async Task TestMountFileSystemAsyncUsesFallbackMountsAndAsyncDisposal()
    {
        var fallback = new TrackingMemoryFileSystem();
        await fallback.WriteAllTextAsync("/fallback.txt", "fallback");
        var mounted = new TrackingMemoryFileSystem();

        await using (var fs = new MountFileSystemAsync(fallback, owned: true))
        {
            await fs.MountAsync("/mnt", mounted);
            await fs.WriteAllTextAsync("/mnt/file.txt", "mounted");

            Assert.AreEqual("fallback", await fs.ReadAllTextAsync("/fallback.txt"));
            Assert.AreEqual("mounted", await mounted.ReadAllTextAsync("/file.txt"));
            Assert.IsTrue(fs.TryGetMount("/mnt/file.txt", out var mountName, out var mountFileSystem, out var fileSystemPath));
            Assert.AreEqual((UPath)"/mnt", mountName);
            Assert.AreSame(mounted, mountFileSystem);
            Assert.AreEqual((UPath)"/file.txt", fileSystemPath);
        }

        Assert.AreEqual(1, fallback.DisposeCount);
        Assert.AreEqual(1, mounted.DisposeCount);
    }

    [TestMethod]
    public async Task TestAsyncCrossFileSystemCopyAndMove()
    {
        await using var source = new MemoryFileSystemAsync();
        await using var destination = new MemoryFileSystemAsync();
        await source.WriteAllTextAsync("/copy.txt", "copy");
        await source.WriteAllTextAsync("/move.txt", "move");

        await source.CopyFileCrossAsync("/copy.txt", destination, "/copied.txt", overwrite: false);
        await source.MoveFileCrossAsync("/move.txt", destination, "/moved.txt");

        Assert.AreEqual("copy", await destination.ReadAllTextAsync("/copied.txt"));
        Assert.AreEqual("move", await destination.ReadAllTextAsync("/moved.txt"));
        Assert.IsFalse(await source.FileExistsAsync("/move.txt"));
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (var value in values)
        {
            result.Add(value);
        }
        return result;
    }

    private static Task AssertThrowsAsync<TException>(Func<ValueTask> action)
        where TException : Exception
    {
        return AssertThrowsTaskAsync<TException>(async () => await action());
    }

    private static Task AssertThrowsAsync<TException, T>(Func<ValueTask<T>> action)
        where TException : Exception
    {
        return AssertThrowsTaskAsync<TException>(async () => await action());
    }

    private static async Task AssertThrowsTaskAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}: {ex.Message}");
        }

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private static async Task DisposeAllAsync(IEnumerable<IFileSystemAsync> fileSystems)
    {
        foreach (var fileSystem in fileSystems)
        {
            try
            {
                await fileSystem.DisposeAsync();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private sealed class IdentityComposeFileSystemAsync : ComposeFileSystemAsync
    {
        public IdentityComposeFileSystemAsync(IFileSystemAsync fileSystem, bool owned = true)
            : base(fileSystem, owned)
        {
        }

        protected override UPath ConvertPathToDelegate(UPath path)
        {
            return path;
        }

        protected override UPath ConvertPathFromDelegate(UPath path)
        {
            return path;
        }
    }

    private sealed class TrackingMemoryFileSystem : MemoryFileSystemAsync
    {
        public int DisposeCount { get; private set; }

        protected override async ValueTask DisposeAsync(bool disposing)
        {
            DisposeCount++;
            await base.DisposeAsync(disposing).ConfigureAwait(false);
        }
    }

    private sealed class LinkMemoryFileSystem : MemoryFileSystemAsync
    {
        private readonly Dictionary<UPath, UPath> _links = new Dictionary<UPath, UPath>();

        protected override ValueTask CreateSymbolicLinkAsyncImpl(UPath path, UPath pathToTarget, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _links[path] = pathToTarget;
            return default;
        }

        protected override ValueTask<(bool Success, UPath ResolvedPath)> TryResolveLinkTargetAsyncImpl(UPath linkPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _links.TryGetValue(linkPath, out var resolvedPath)
                ? new ValueTask<(bool Success, UPath ResolvedPath)>((true, resolvedPath))
                : new ValueTask<(bool Success, UPath ResolvedPath)>((false, default));
        }
    }
}
#endif
