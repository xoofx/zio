// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

            lock (_fileSystems)
            {
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
        }

        /// <summary>
        /// Gets an ordered list of the filesystems registered to this instance. The backup filesystem passed to the constructor
        /// is not part of the liset.
        /// </summary>
        public List<IFileSystem> GetFileSystems()
        {
            lock (_fileSystems)
            {
                return new List<IFileSystem>(_fileSystems);
            }
        }

        /// <summary>
        /// Clears the registered file systems.
        /// </summary>
        public void ClearFileSystems()
        {
            lock (_fileSystems)
            {
                _fileSystems.Clear();

                foreach (var watcher in _watchers)
                {
                    watcher.Clear(Fallback);
                }
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
            lock (_fileSystems)
            {
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

            lock (_fileSystems)
            {
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
            lock (_fileSystems)
            {
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
        }

        /// <summary>
        /// Finds the first <see cref="FileSystemEntry"/> from this aggregate system found at the specified path for each registered filesystems (in order).
        /// The type of the first entry (file or directory) dictates the type of the following entries in the list (e.g if a file is coming first, only files will be showned for the specified path).
        /// </summary>
        /// <param name="path">To check for an entry</param>
        /// <returns>A file system entry or null if it was not found.</returns>
        public FileSystemEntry? FindFirstFileSystemEntry(UPath path)
        {
            path.AssertAbsolute();
            var entry  = TryGetPath(path);
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
        public List<FileSystemEntry> FindFileSystemEntries(UPath path)
        {
            path.AssertAbsolute();
            var paths = new List<FileSystemPath>();
            FindPaths(path, SearchTarget.Both, paths);
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
        protected override bool DirectoryExistsImpl(UPath path)
        {
            var directory = TryGetDirectory(path);
            return directory.HasValue;
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override long GetFileLengthImpl(UPath path)
        {
            var entry = GetFile(path);
            return entry.FileSystem.GetFileLength(path);
        }

        /// <inheritdoc />
        protected override bool FileExistsImpl(UPath path)
        {
            var entry = TryGetFile(path);
            return entry.HasValue;
        }

        /// <inheritdoc />
        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (mode != FileMode.Open)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            if ((access & FileAccess.Write) != 0)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            var entry = GetFile(path);
            return entry.FileSystem.OpenFile(path, mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override FileAttributes GetAttributesImpl(UPath path)
        {
            var entry = GetPath(path);
            return entry.FileSystem.GetAttributes(path) | FileAttributes.ReadOnly;
        }

        /// <inheritdoc />
        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            var entry = TryGetPath(path);
            return entry.HasValue ? entry.Value.FileSystem.GetCreationTime(path) : DefaultFileTime;
        }

        /// <inheritdoc />
        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            var entry = TryGetPath(path);
            return entry.HasValue ? entry.Value.FileSystem.GetLastWriteTime(path) : DefaultFileTime;
        }

        /// <inheritdoc />
        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            var entry = TryGetPath(path);
            return entry.HasValue ? entry.Value.FileSystem.GetLastWriteTime(path) : DefaultFileTime;
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            var directoryToVisit = new List<UPath>();
            directoryToVisit.Add(path);

            var entries = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);
            var sortedDirectories = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);
            var fileSystems = new List<IFileSystem>();

            if (Fallback != null)
            {
                fileSystems.Add(Fallback);
            }

            // Query all filesystems just once
            lock (_fileSystems)
            {
                fileSystems.AddRange(_fileSystems);
            }

            while (directoryToVisit.Count > 0)
            {
                var pathToVisit = directoryToVisit[0];
                directoryToVisit.RemoveAt(0);
                int dirIndex = 0;
                entries.Clear();
                sortedDirectories.Clear();

                for (var i = fileSystems.Count - 1; i >= 0; i--)
                {
                    var fileSystem = fileSystems[i];

                    if (fileSystem.DirectoryExists(pathToVisit))
                    {
                        foreach (var item in fileSystem.EnumeratePaths(pathToVisit, "*", SearchOption.TopDirectoryOnly, SearchTarget.Both))
                        {
                            if (!entries.Contains(item))
                            {
                                var isFile = fileSystem.FileExists(item);
                                var isDirectory = fileSystem.DirectoryExists(item);
                                var isMatching = search.Match(item);

                                if (isMatching && ((isFile && searchTarget != SearchTarget.Directory) || (isDirectory && searchTarget != SearchTarget.File)))
                                {
                                    entries.Add(item);
                                }

                                if (searchOption == SearchOption.AllDirectories && isDirectory)
                                {
                                    sortedDirectories.Add(item);
                                }
                            }
                        }
                    }
                }

                // Enqueue directories and respect order
                foreach (var nextDir in sortedDirectories)
                {
                    directoryToVisit.Insert(dirIndex++, nextDir);
                }

                // Return entries
                foreach (var entry in entries)
                {
                    yield return entry;
                }
            }
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
            lock (_fileSystems)
            {
                var watcher = new Watcher(this, path);

                if (Fallback != null && Fallback.CanWatch(path) && Fallback.DirectoryExists(path))
                {
                    watcher.Add(Fallback.Watch(path));
                }

                foreach (var fs in _fileSystems)
                {
                    if (fs.CanWatch(path) && fs.DirectoryExists(path))
                    {
                        watcher.Add(fs.Watch(path));
                    }
                }

                _watchers.Add(watcher);
                return watcher;
            }
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
                    lock (_fileSystem._fileSystems)
                    {
                        _fileSystem._watchers.Remove(this);
                    }
                }
            }
        }

        // ----------------------------------------------
        // Internals API
        // Used to retrieve the correct paths
        // from the list of registered filesystem.
        // ----------------------------------------------

        private FileSystemPath GetFile(UPath path)
        {
            var entry = TryGetFile(path);
            if (!entry.HasValue)
            {
                throw NewFileNotFoundException(path);
            }
            return entry.Value;
        }

        private FileSystemPath? TryGetFile(UPath path)
        {
            return TryGetPath(path, SearchTarget.File);
        }

        private FileSystemPath? TryGetDirectory(UPath path)
        {
            return TryGetPath(path, SearchTarget.Directory);
        }

        private FileSystemPath GetPath(UPath path)
        {
            var entry = TryGetPath(path);
            if (!entry.HasValue)
            {
                throw NewFileNotFoundException(path);
            }
            return entry.Value;
        }

        /// <summary>
        /// Get a single path. Optimized version of <see cref="FindPaths"/>.
        /// </summary>
        private FileSystemPath? TryGetPath(UPath path, SearchTarget searchTarget = SearchTarget.Both)
        {
            bool queryDirectory = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.Directory;
            bool queryFile = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.File;

            lock (_fileSystems)
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
                        return aggregate.TryGetPath(path, searchTarget);
                    }
                    else
                    {
                        bool isFile = false;
                        if ((queryDirectory && fileSystem.DirectoryExists(path)) || (queryFile && (isFile = fileSystem.FileExists(path))))
                        {
                            return new FileSystemPath(fileSystem, path, isFile);
                        }
                    }
                }
            }

            return null;
        }

        private void FindPaths(UPath path, SearchTarget searchTarget, List<FileSystemPath> paths)
        {
            bool queryDirectory = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.Directory;
            bool queryFile = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.File;

            lock (_fileSystems)
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
                        aggregate.FindPaths(path, searchTarget, paths);
                    }
                    else
                    {
                        bool isFile = false;
                        if ((queryDirectory && fileSystem.DirectoryExists(path)) || (queryFile && (isFile = fileSystem.FileExists(path))))
                        {
                            paths.Add(new FileSystemPath(fileSystem, path, isFile));
                        }
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