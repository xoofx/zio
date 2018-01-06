// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using static Zio.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a readonly merged view filesystem over multiple filesystems (overriding files/directory in order)
    /// </summary>
    public class AggregateFileSystem : ReadOnlyFileSystem
    {
        private readonly List<IFileSystem> _fileSystems;
        private readonly List<AggregateFileSystemWatcher> _watchers;

        /// <summary>
        /// Initializes a new instance of the <see cref="AggregateFileSystem"/> class.
        /// </summary>
        public AggregateFileSystem() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AggregateFileSystem"/> class with a default <see cref="IFileSystem"/>
        /// that will be used as a final filesystem while trying to resolve paths.
        /// </summary>
        /// <param name="fileSystem">The final backup filesystem (can be null).</param>
        public AggregateFileSystem(IFileSystem fileSystem) : base(fileSystem)
        {
            _fileSystems = new List<IFileSystem>();
            _watchers = new List<AggregateFileSystemWatcher>();
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
                    watcher.Clear(NextFileSystem);
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
            if (fileSystems == null) throw new ArgumentNullException(nameof(fileSystems));
            lock (_fileSystems)
            {
                _fileSystems.Clear();

                foreach (var watcher in _watchers)
                {
                    watcher.Clear(NextFileSystem);
                }

                foreach (var fileSystem in fileSystems)
                {
                    if (fileSystem == null) throw new ArgumentException("A null filesystem is invalid");
                    if (fileSystem == this) throw new ArgumentException("Cannot add this instance as an aggregate delegate of itself");
                    _fileSystems.Add(fileSystem);

                    foreach (var watcher in _watchers)
                    {
                        var newWatcher = fileSystem.Watch(watcher.Path);
                        if (newWatcher != null)
                        {
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
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            if (fs == this) throw new ArgumentException("Cannot add this instance as an aggregate delegate of itself");

            lock (_fileSystems)
            {
                if (!_fileSystems.Contains(fs))
                {
                    _fileSystems.Add(fs);

                    foreach (var watcher in _watchers)
                    {
                        var newWatcher = fs.Watch(watcher.Path);
                        if (newWatcher != null)
                        {
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
            if (fs == null) throw new ArgumentNullException(nameof(fs));
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
        /// Finds the list of <see cref="FileSystemEntry"/> for each file or directory found at the specified path for each registered filesystems (in order).
        /// The type of the first entry (file or directory) dictates the type of the following entries in the list (e.g if a file is coming first, only files will be showned for the specified path).
        /// </summary>
        /// <param name="path">To check for an entry</param>
        /// <returns>A list of file entries for the specified path</returns>
        public List<FileSystemEntry> FindFileSystemEntries(UPath path)
        {
            path.AssertAbsolute();
            var paths = FindPaths(path, SearchTarget.Both);
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

            if (NextFileSystem != null)
            {
                fileSystems.Add(NextFileSystem);
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
        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            lock (_fileSystems)
            {
                var watcher = new AggregateFileSystemWatcher(this, path);

                if (NextFileSystem != null)
                {
                    var newWatcher = NextFileSystem.Watch(path);
                    if (newWatcher != null)
                    {
                        watcher.Add(newWatcher);
                    }
                }

                foreach (var fs in _fileSystems)
                {
                    var newWatcher = fs.Watch(path);
                    if (newWatcher != null)
                    {
                        watcher.Add(newWatcher);
                    }
                }

                _watchers.Add(watcher);
                return watcher;
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
            var entries = FindPaths(path, SearchTarget.File);
            return entries.Count > 0 ? (FileSystemPath?)entries[0] : null;
        }

        private FileSystemPath? TryGetDirectory(UPath path)
        {
            var entries = FindPaths(path, SearchTarget.Directory);
            return entries.Count > 0 ? (FileSystemPath?)entries[0] : null;
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

        private FileSystemPath? TryGetPath(UPath path)
        {
            var entries = FindPaths(path, SearchTarget.Both);
            return entries.Count > 0 ? (FileSystemPath?)entries[0] : null;
        }

        private List<FileSystemPath> FindPaths(UPath path, SearchTarget searchTarget)
        {
            var paths = new List<FileSystemPath>();

            bool queryDirectory = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.Directory;
            bool queryFile = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.File;

            lock (_fileSystems)
            {
                for (var i = _fileSystems.Count - 1; i >= -1; i--)
                {
                    var fileSystem = i < 0 ? NextFileSystem : _fileSystems[i];

                    if (fileSystem == null)
                    {
                        break;
                    }

                    bool isFile = false;
                    if ((queryDirectory && fileSystem.DirectoryExists(path)) || (queryFile && (isFile = fileSystem.FileExists(path))))
                    {
                        paths.Add(new FileSystemPath(fileSystem, path, isFile));
                    }
                }
            }

            return paths;
        }

        private struct FileSystemPath
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
    }
}