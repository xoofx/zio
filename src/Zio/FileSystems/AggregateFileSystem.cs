// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using static Zio.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a readonly merged view filesystem over multiple filesystems (overriding files/directory in order)
    /// </summary>
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "(),nq} Count={_fileSystems.Count}")]
    [DebuggerTypeProxy(typeof(DebuggerProxy))]
    public class AggregateFileSystem : ReadOnlyFileSystem
    {
        private readonly List<IFileSystem> _fileSystems;
        private readonly List<Watcher> _watchers;

        /// <summary>
        /// Initializes a new instance of the <see cref="AggregateFileSystem"/> class.
        /// </summary>
        /// <param name="owned">True if filesystems should be disposed when this instance is disposed.</param>
        public AggregateFileSystem(bool owned = true) : this(null, owned)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AggregateFileSystem"/> class with a default <see cref="IFileSystem"/>
        /// that will be used as a final filesystem while trying to resolve paths.
        /// </summary>
        /// <param name="fileSystem">The final backup filesystem (can be null).</param>
        /// <param name="owned">True if <paramref name="fileSystem"/> and other filesystems should be disposed when this instance is disposed.</param>
        public AggregateFileSystem(IFileSystem? fileSystem, bool owned = true) : base(fileSystem, owned)
        {
            _fileSystems = new List<IFileSystem>();
            _watchers = new List<Watcher>();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            if (Owned)
            {
                foreach (var fs in _fileSystems)
                {
                    fs.Dispose();
                }
            }

            _fileSystems.Clear();

            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        /// <summary>
        /// Gets an ordered list of the filesystems registered to this instance. The backup filesystem passed to the constructor
        /// is not part of the liset.
        /// </summary>
        public List<IFileSystem> GetFileSystems()
        {
            return new List<IFileSystem>(_fileSystems);
        }

        /// <summary>
        /// Clears the registered file systems.
        /// </summary>
        public void ClearFileSystems()
        {
            _fileSystems.Clear();

            foreach (var watcher in _watchers)
            {
                watcher.Clear(Fallback);
            }
        }

        /// <summary>
        /// Sets the filesystems by clearing all previously registered filesystems, from the lowest to highest priority filesystem.
        /// </summary>
        /// <param name="fileSystems">The file systems.</param>
        /// <exception cref="System.ArgumentNullException">fileSystems</exception>
        /// <exception cref="System.ArgumentException">
        /// A null filesystem is invalid
        /// or
        /// Cannot add this instance as an aggregate delegate of itself
        /// </exception>
        public void SetFileSystems(IEnumerable<IFileSystem> fileSystems)
        {
            if (fileSystems is null) throw new ArgumentNullException(nameof(fileSystems));
            _fileSystems.Clear();

            foreach (var watcher in _watchers)
            {
                watcher.Clear(Fallback);
            }

            foreach (var fileSystem in fileSystems)
            {
                if (fileSystem is null) throw new ArgumentException("A null filesystem is invalid");
                if (fileSystem == this) throw new ArgumentException("Cannot add this instance as an aggregate delegate of itself");
                _fileSystems.Add(fileSystem);

                foreach (var watcher in _watchers)
                {
                    if (fileSystem.CanWatch(watcher.Path))
                    {
                        var newWatcher = fileSystem.Watch(watcher.Path);
                        watcher.Add(newWatcher);
                    }
                }
            }
        }

        /// <summary>
        /// Adds a filesystem to this aggregate view. The Last filesystem as more priority than the previous one when an overrides
        /// of a file occurs. 
        /// </summary>
        /// <param name="fs">The filesystem to add to this aggregate.</param>
        /// <exception cref="System.ArgumentNullException">fs</exception>
        /// <exception cref="System.ArgumentException">Cannot add this instance as an aggregate delegate of itself</exception>
        /// <exception cref="System.ArgumentException">The filesystem is already added</exception>
        public virtual void AddFileSystem(IFileSystem fs)
        {
            if (fs is null) throw new ArgumentNullException(nameof(fs));
            if (fs == this) throw new ArgumentException("Cannot add this instance as an aggregate delegate of itself");

            if (!_fileSystems.Contains(fs))
            {
                _fileSystems.Add(fs);

                foreach (var watcher in _watchers)
                {
                    if (fs.CanWatch(watcher.Path))
                    {
                        var newWatcher = fs.Watch(watcher.Path);
                        watcher.Add(newWatcher);
                    }
                }
            }
            else
            {
                throw new ArgumentException("The filesystem is already added");
            }
        }

        /// <summary>
        /// Removes a filesystem from this aggregate view.
        /// </summary>
        /// <param name="fs">The filesystem to remove to this aggregate.</param>
        /// <exception cref="System.ArgumentNullException">fs</exception>
        /// <exception cref="System.ArgumentException">FileSystem was not found - fs</exception>
        public virtual void RemoveFileSystem(IFileSystem fs)
        {
            if (fs is null) throw new ArgumentNullException(nameof(fs));
            if (_fileSystems.Contains(fs))
            {
                _fileSystems.Remove(fs);

                foreach (var watcher in _watchers)
                {
                    watcher.RemoveFrom(fs);
                }
            }
            else
            {
                throw new ArgumentException("FileSystem was not found", nameof(fs));
            }
        }

        /// <summary>
        /// Finds the first <see cref="FileSystemEntry"/> from this aggregate system found at the specified path for each registered filesystems (in order).
        /// The type of the first entry (file or directory) dictates the type of the following entries in the list (e.g if a file is coming first, only files will be showned for the specified path).
        /// </summary>
        /// <param name="path">To check for an entry</param>
        /// <returns>A file system entry or null if it was not found.</returns>
        public async ValueTask<FileSystemEntry?> FindFirstFileSystemEntry(UPath path)
        {
            path.AssertAbsolute();
            var entry = await TryGetPath(path);
            if (!entry.HasValue) return null;

            var pathItem = entry.Value;
            return pathItem.IsFile ? (FileSystemEntry) new FileEntry(pathItem.FileSystem, pathItem.Path) : new DirectoryEntry(pathItem.FileSystem, pathItem.Path);
        }

        /// <summary>
        /// Finds the list of <see cref="FileSystemEntry"/> for each file or directory found at the specified path for each registered filesystems (in order).
        /// The type of the first entry (file or directory) dictates the type of the following entries in the list (e.g if a file is coming first, only files will be showned for the specified path).
        /// </summary>
        /// <param name="path">To check for an entry</param>
        /// <returns>A list of file entries for the specified path</returns>
        public async ValueTask<List<FileSystemEntry>> FindFileSystemEntries(UPath path)
        {
            path.AssertAbsolute();
            var paths = new List<FileSystemPath>();
            await FindPaths(path, SearchTarget.Both, paths);
            var result = new List<FileSystemEntry>(paths.Count);
            if (paths.Count == 0)
            {
                return result;
            }

            var isFile = paths[0].IsFile;

            foreach (var pathItem in paths)
            {
                if (pathItem.IsFile == isFile)
                {
                    if (isFile)
                    {
                        result.Add(new FileEntry(pathItem.FileSystem, pathItem.Path));
                    }
                    else
                    {
                        result.Add(new DirectoryEntry(pathItem.FileSystem, pathItem.Path));
                    }
                }
            }
            return result;
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override async ValueTask<bool> DirectoryExistsImpl(UPath path)
        {
            var directory = await TryGetDirectory(path);
            return directory.HasValue;
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override async ValueTask<long> GetFileLengthImpl(UPath path)
        {
            var entry = await GetFile(path);
            return await entry.FileSystem.GetFileLength(path);
        }

        /// <inheritdoc />
        protected override async ValueTask<bool> FileExistsImpl(UPath path)
        {
            var entry = await TryGetFile(path);
            return entry.HasValue;
        }

        /// <inheritdoc />
        protected override async ValueTask<Stream> OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (mode != FileMode.Open)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            if ((access & FileAccess.Write) != 0)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            var entry = await GetFile(path);
            return await entry.FileSystem.OpenFile(path, mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override async ValueTask<FileAttributes> GetAttributesImpl(UPath path)
        {
            var entry = await GetPath(path);
            return await entry.FileSystem.GetAttributes(path) | FileAttributes.ReadOnly;
        }

        /// <inheritdoc />
        protected override async ValueTask<DateTime> GetCreationTimeImpl(UPath path)
        {
            var entry = await TryGetPath(path);
            return entry.HasValue ? await entry.Value.FileSystem.GetCreationTime(path) : DefaultFileTime;
        }

        /// <inheritdoc />
        protected override async ValueTask<DateTime> GetLastAccessTimeImpl(UPath path)
        {
            var entry = await TryGetPath(path);
            return entry.HasValue ? await entry.Value.FileSystem.GetLastWriteTime(path) : DefaultFileTime;
        }

        /// <inheritdoc />
        protected override async ValueTask<DateTime> GetLastWriteTimeImpl(UPath path)
        {
            var entry = await TryGetPath(path);
            return entry.HasValue ? await entry.Value.FileSystem.GetLastWriteTime(path) : DefaultFileTime;
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override async ValueTask<IEnumerable<UPath>> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            SearchPattern.Parse( ref path, ref searchPattern );

            var entries = new SortedSet<UPath>();
            var fileSystems = new List<IFileSystem>();

            if (Fallback != null)
            {
                fileSystems.Add(Fallback);
            }

            // Query all filesystems just once
            fileSystems.AddRange(_fileSystems);

            for (var i = fileSystems.Count - 1; i >= 0; i--)
            {
                var fileSystem = fileSystems[i];

                if (!await fileSystem.DirectoryExists( path ))
                    continue;

                foreach (var item in await fileSystem.EnumeratePaths( path, searchPattern, searchOption, searchTarget ) )
                {
                    if (entries.Contains( item )) continue;
                    entries.Add(item);
                }
            }

            return entries;
        }


        /// <inheritdoc />
        protected override async ValueTask<IEnumerable<FileSystemItem>> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
        {
            var results = new List<FileSystemItem>();
            var entries = new HashSet<UPath>();
            for (var i = _fileSystems.Count - 1; i >= 0; i--)
            {
                var fileSystem = _fileSystems[i];
                foreach (var item in await fileSystem.EnumerateItems(path, searchOption, searchPredicate))
                {
                    if (entries.Add(item.Path))
                    {
                        results.Add(item);
                    }
                }
            }

            var fallback = Fallback;
            if (fallback != null)
            {
                foreach (var item in await fallback.EnumerateItems(path, searchOption, searchPredicate))
                {
                    if (entries.Add(item.Path))
                    {
                        results.Add(item);
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        protected override UPath ConvertPathToDelegate(UPath path)
        {
            return path;
        }

        /// <inheritdoc />
        protected override UPath ConvertPathFromDelegate(UPath path)
        {
            return path;
        }

        // ----------------------------------------------
        // Watch API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override bool CanWatchImpl(UPath path)
        {
            // Always allow watching because a future filesystem can be added that matches this path.
            return true;
        }

        /// <inheritdoc />
        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            var watcher = new Watcher(this, path);

            if (Fallback != null && Fallback.CanWatch(path))
            {
                watcher.Add(Fallback.Watch(path));
            }

            foreach (var fs in _fileSystems)
            {
                if (fs.CanWatch(path))
                {
                    watcher.Add(fs.Watch(path));
                }
            }

            _watchers.Add(watcher);
            return watcher;
        }

        private sealed class Watcher : AggregateFileSystemWatcher
        {
            private readonly AggregateFileSystem _fileSystem;

            public Watcher(AggregateFileSystem fileSystem, UPath path)
                : base(fileSystem, path)
            {
                _fileSystem = fileSystem;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);

                if (disposing && !_fileSystem.IsDisposing)
                {
                    _fileSystem._watchers.Remove(this);
                }
            }
        }

        // ----------------------------------------------
        // Internals API
        // Used to retrieve the correct paths
        // from the list of registered filesystem.
        // ----------------------------------------------

        private async ValueTask<FileSystemPath> GetFile(UPath path)
        {
            var entry = await TryGetFile(path);
            if (!entry.HasValue)
            {
                throw NewFileNotFoundException(path);
            }
            return entry.Value;
        }

        private async ValueTask<FileSystemPath?> TryGetFile(UPath path)
        {
            for (var i = _fileSystems.Count - 1; i >= -1; i--)
            {
                var fileSystem = i < 0 ? Fallback : _fileSystems[i];
                // Go through aggregates
                if (fileSystem is AggregateFileSystem aggregate)
                {
                    return await aggregate.TryGetFile(path);
                }

                if (fileSystem != null)
                {
                    if (await fileSystem.FileExists(path))
                    {
                        return new FileSystemPath(fileSystem, path, true);
                    }
                }
                else
                {
                    break;
                }
            }
            return null;
        }

        private async ValueTask<FileSystemPath?> TryGetDirectory(UPath path)
        {
            for (var i = _fileSystems.Count - 1; i >= -1; i--)
            {
                var fileSystem = i < 0 ? Fallback : _fileSystems[i];
                // Go through aggregates
                if (fileSystem is AggregateFileSystem aggregate)
                {
                    return await aggregate.TryGetDirectory(path);
                }

                if (fileSystem != null)
                {
                    if (await fileSystem.DirectoryExists(path))
                    {
                        return new FileSystemPath(fileSystem, path, false);
                    }
                }
                else
                {
                    break;
                }
            }

            return null;
        }

        private async ValueTask<FileSystemPath> GetPath(UPath path)
        {
            var entry = await TryGetPath(path);
            if (!entry.HasValue)
            {
                throw NewFileNotFoundException(path);
            }
            return entry.Value;
        }

        /// <summary>
        /// Get a single path. Optimized version of <see cref="FindPaths"/>.
        /// </summary>
        private async ValueTask<FileSystemPath?> TryGetPath(UPath path, SearchTarget searchTarget = SearchTarget.Both)
        {
            if (searchTarget == SearchTarget.File)
            {
                return await TryGetFile(path);
            }
            else if (searchTarget == SearchTarget.Directory)
            {
                return await TryGetDirectory(path);
            }
            else
            {
                for (var i = _fileSystems.Count - 1; i >= -1; i--)
                {
                    var fileSystem = i < 0 ? Fallback : _fileSystems[i];

                    if (fileSystem is null)
                    {
                        break;
                    }

                    // Go through aggregates
                    if (fileSystem is AggregateFileSystem aggregate)
                    {
                        return await aggregate.TryGetPath(path, searchTarget);
                    }

                    if (await fileSystem.DirectoryExists(path))
                    {
                        return new FileSystemPath(fileSystem, path, false);
                    }

                    if (await fileSystem.FileExists(path))
                    {
                        return new FileSystemPath(fileSystem, path, true);
                    }
                }
            }

            return null;
        }

        private async ValueTask FindPaths(UPath path, SearchTarget searchTarget, List<FileSystemPath> paths)
        {
            bool queryDirectory = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.Directory;
            bool queryFile = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.File;

            var fileSystems = _fileSystems;
            for (var i = fileSystems.Count - 1; i >= -1; i--)
            {
                var fileSystem = i < 0 ? Fallback : fileSystems[i];

                if (fileSystem is null)
                {
                    break;
                }

                // Go through aggregates
                if (fileSystem is AggregateFileSystem aggregate)
                {
                    await aggregate.FindPaths(path, searchTarget, paths);
                }
                else
                {
                    bool isFile = false;
                    if ((queryDirectory && await fileSystem.DirectoryExists(path)) || (queryFile && (isFile = await fileSystem.FileExists(path))))
                    {
                        paths.Add(new FileSystemPath(fileSystem, path, isFile));
                    }
                }
            }
        }

        private readonly struct FileSystemPath
        {
            public FileSystemPath(IFileSystem fileSystem, UPath path, bool isFile)
            {
                FileSystem = fileSystem;
                Path = path;
                IsFile = isFile;
            }

            public readonly IFileSystem FileSystem;

            public readonly UPath Path;

            public readonly bool IsFile;
        }

        private sealed class DebuggerProxy
        {
            private readonly AggregateFileSystem _fs;

            public DebuggerProxy(AggregateFileSystem fs)
            {
                _fs = fs;
            }
            
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public IFileSystem[] FileSystems => _fs._fileSystems.ToArray();

            public IFileSystem? Fallback => _fs.Fallback;
        }
    }
}