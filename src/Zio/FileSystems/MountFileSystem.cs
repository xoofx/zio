// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Zio.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// A <see cref="IFileSystem"/> that can mount other filesystems on a root name. 
    /// This mount filesystem supports also an optionnal fallback delegate FileSystem if a path was not found through a mount
    /// </summary>
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "(),nq} Count={_mounts.Count}")]
    [DebuggerTypeProxy(typeof(DebuggerProxy))]
    public class MountFileSystem : ComposeFileSystem
    {
        private readonly SortedList<UPath, IFileSystem> _mounts;
        private readonly List<AggregateWatcher> _watchers;

        /// <summary>
        /// Initializes a new instance of the <see cref="MountFileSystem"/> class.
        /// </summary>
        /// <param name="owned">True if mounted filesystems should be disposed when this instance is disposed.</param>
        public MountFileSystem(bool owned = true) : this(null, owned)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MountFileSystem"/> class with a default backup filesystem.
        /// </summary>
        /// <param name="defaultBackupFileSystem">The default backup file system.</param>
        /// <param name="owned">True if <paramref name="defaultBackupFileSystem"/> and mounted filesytems should be disposed when this instance is disposed.</param>
        public MountFileSystem(IFileSystem? defaultBackupFileSystem, bool owned = true) : base(defaultBackupFileSystem, owned)
        {
            _mounts = new SortedList<UPath, IFileSystem>(new UPathLengthComparer());
            _watchers = new List<AggregateWatcher>();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();

            if (Owned)
            {
                foreach (var kvp in _mounts)
                {
                    kvp.Value.Dispose();
                }
            }

            _mounts.Clear();
        }

        /// <summary>
        /// Mounts a filesystem for the specified mount name.
        /// </summary>
        /// <param name="name">The mount name.</param>
        /// <param name="fileSystem">The file system.</param>
        /// <exception cref="System.ArgumentNullException">fileSystem</exception>
        /// <exception cref="System.ArgumentException">
        /// Cannot recursively mount the filesystem to self - <paramref name="fileSystem"/>
        /// or
        /// There is already a mount with the same name: `{name}` - <paramref name="name"/>
        /// </exception>
        public void Mount(UPath name, IFileSystem fileSystem)
        {
            if (fileSystem is null) throw new ArgumentNullException(nameof(fileSystem));
            if (fileSystem == this)
            {
                throw new ArgumentException("Cannot recursively mount the filesystem to self", nameof(fileSystem));
            }
            ValidateMountName(name);

            if (_mounts.ContainsKey(name))
            {
                throw new ArgumentException($"There is already a mount with the same name: `{name}`", nameof(name));
            }
            _mounts.Add(name, fileSystem);

            foreach (var watcher in _watchers)
            {
                if (!IsMountIncludedInWatch(name, watcher.Path, out var remainingPath))
                {
                    continue;
                }

                if (fileSystem.CanWatch(remainingPath))
                {
                    var internalWatcher = fileSystem.Watch(remainingPath);
                    watcher.Add(new WrapWatcher(fileSystem, name, remainingPath, internalWatcher));
                }
            }
        }

        /// <summary>
        /// Determines whether the specified mount name is mounted.
        /// </summary>
        /// <param name="name">The mount name.</param>
        /// <returns><c>true</c> if the specified name is mounted; otherwise, <c>false</c>.</returns>
        public bool IsMounted(UPath name)
        {
            ValidateMountName(name);

            return _mounts.ContainsKey(name);
        }

        /// <summary>
        /// Gets all the mounts currently mounted
        /// </summary>
        /// <returns>A dictionary of mounted filesystems.</returns>
        public Dictionary<UPath, IFileSystem> GetMounts()
        {
            var dict = new Dictionary<UPath, IFileSystem>();
            foreach (var mount in _mounts)
            {
                dict.Add(mount.Key, mount.Value);
            }
            return dict;
        }

        /// <summary>
        /// Unmounts the specified mount name and its attached filesystem.
        /// </summary>
        /// <param name="name">The mount name.</param>
        /// <returns>The filesystem that was unmounted.</returns>
        /// <exception cref="System.ArgumentException">The mount with the name <paramref name="name"/> was not found</exception>
        public IFileSystem Unmount(UPath name)
        {
            ValidateMountName(name);

            IFileSystem? mountFileSystem;

            if (!_mounts.TryGetValue(name, out mountFileSystem))
            {
                throw new ArgumentException($"The mount with the name `{name}` was not found");
            }

            foreach (var watcher in _watchers)
            {
                watcher.RemoveFrom(mountFileSystem);
            }

            _mounts.Remove(name);

            return mountFileSystem;
        }

        /// <summary>
        /// Attempts to find information about the mount that a given path maps to.
        /// </summary>
        /// <param name="path">The path to search for.</param>
        /// <param name="name">The mount name that the <paramref name="path"/> belongs to.</param>
        /// <param name="fileSystem">The mounted filesystem that the <paramref name="path"/> is located in.</param>
        /// <param name="fileSystemPath">The path inside of <paramref name="fileSystem"/> that refers to the file at <paramref name="path"/>.</param>
        /// <returns>True if the <paramref name="path"/> was found in a mounted filesystem.</returns>
        /// <exception cref="System.ArgumentNullException">The <paramref name="path"/> must not be null.</exception>
        /// <exception cref="System.ArgumentException">The <paramref name="path"/> must be absolute.</exception>
        public bool TryGetMount(UPath path, out UPath name, out IFileSystem? fileSystem, out UPath? fileSystemPath)
        {
            path.AssertNotNull();
            path.AssertAbsolute();

            var fs = TryGetMountOrNext(ref path, out name);

            if (fs is null || name.IsNull)
            {
                name = UPath.Null;
                fileSystem = null;
                fileSystemPath = null;
                return false;
            }

            fileSystem = fs;
            fileSystemPath = path;
            return true;
        }

        /// <summary>
        /// Attempts to find the mount name that a filesystem has been mounted to
        /// </summary>
        /// <param name="fileSystem">The mounted filesystem to search for.</param>
        /// <param name="name">The mount name that the <paramref name="fileSystem"/> is mounted with.</param>
        /// <returns>True if the <paramref name="fileSystem"/> is mounted.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="fileSystem"/> must not be null.</exception>
        public bool TryGetMountName(IFileSystem fileSystem, out UPath name)
        {
            if (fileSystem is null)
                throw new ArgumentNullException(nameof(fileSystem));

            foreach (var mount in _mounts)
            {
                if (mount.Value != fileSystem)
                    continue;

                name = mount.Key;
                return true;
            }

            name = UPath.Null;
            return false;
        }

        /// <inheritdoc />
        protected override ValueTask CreateDirectoryImpl(UPath path)
        {
            var originalSrcPath = path;
            var fs = TryGetMountOrNext(ref path);
            if (fs != null && path != UPath.Root)
            {
                return fs.CreateDirectory(path);
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{originalSrcPath}` is denied");
            }
        }

        /// <inheritdoc />
        protected override async ValueTask<bool> DirectoryExistsImpl(UPath path)
        {
            if (path == UPath.Root)
            {
                return true;
            }
            var fs = TryGetMountOrNext(ref path);
            if (fs != null)
            {
                return path == UPath.Root || await fs.DirectoryExists(path);
            }

            // Check if the path is part of a mount name
            foreach (var kvp in _mounts)
            {
                var remainingPath = GetRemaining(path, kvp.Key);
                if (!remainingPath.IsNull)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        protected override ValueTask MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            var originalSrcPath = srcPath;
            var originalDestPath = destPath;

            var srcfs = TryGetMountOrNext(ref srcPath);
            var destfs = TryGetMountOrNext(ref destPath);

            if (srcfs != null && srcPath == UPath.Root)
            {
                throw new UnauthorizedAccessException($"Cannot move a mount directory `{originalSrcPath}`");
            }

            if (destfs != null && destPath == UPath.Root)
            {
                throw new UnauthorizedAccessException($"Cannot move a mount directory `{originalDestPath}`");
            }

            if (srcfs != null && srcfs == destfs)
            {
                return srcfs.MoveDirectory(srcPath, destPath);
            }
            else
            {
                // TODO: Add support for Copy + Delete ?
                throw new NotSupportedException($"Cannot move directory between mount `{originalSrcPath}` and `{originalDestPath}`");
            }
        }

        /// <inheritdoc />
        protected override ValueTask DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);

            if (mountfs != null && path == UPath.Root)
            {
                throw new UnauthorizedAccessException($"Cannot delete mount directory `{originalSrcPath}`. Use Unmount() instead");
            }

            if (mountfs != null)
            {
                return mountfs.DeleteDirectory(path, isRecursive);
            }
            else
            {
                throw NewDirectoryNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override async ValueTask CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            var originalSrcPath = srcPath;
            var originalDestPath = destPath;

            var srcfs = TryGetMountOrNext(ref srcPath);
            var destfs = TryGetMountOrNext(ref destPath);

            if (srcfs != null && destfs != null)
            {
                if (srcfs == destfs)
                {
                    await srcfs.CopyFile(srcPath, destPath, overwrite);
                }
                else
                {
                    // Otherwise, perform a copy between filesystem
                    await srcfs.CopyFileCross(srcPath, destfs, destPath, overwrite);
                }
            }
            else
            {
                if (srcfs is null)
                {
                    throw NewFileNotFoundException(originalSrcPath);
                }

                throw NewDirectoryNotFoundException(originalDestPath);
            }
        }

        /// <inheritdoc />
        protected override async ValueTask ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            var originalSrcPath = srcPath;
            var originalDestPath = destPath;
            var originalDestBackupPath = destBackupPath;

            if (!await FileExistsImpl(srcPath))
            {
                throw NewFileNotFoundException(srcPath);
            }

            if (!await FileExistsImpl(destPath))
            {
                throw NewFileNotFoundException(destPath);
            }

            var srcfs = TryGetMountOrNext(ref srcPath);
            var destfs = TryGetMountOrNext(ref destPath);
            var backupfs = TryGetMountOrNext(ref destBackupPath);

            if (srcfs != null && srcfs == destfs && (destBackupPath.IsNull || srcfs == backupfs))
            {
                await srcfs.ReplaceFile(srcPath, destPath, destBackupPath, ignoreMetadataErrors);
            }
            else
            {
                // TODO: Add support for moving file between filesystems (Copy+Delete) ?
                throw new NotSupportedException($"Cannot replace file between mount `{originalSrcPath}`, `{originalDestPath}` and `{originalDestBackupPath}`");
            }
        }

        /// <inheritdoc />
        protected override ValueTask<long> GetFileLengthImpl(UPath path)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);

            if (mountfs != null)
            {
                return mountfs.GetFileLength(path);
            }
            throw NewFileNotFoundException(originalSrcPath);
        }

        /// <inheritdoc />
        protected override ValueTask<bool> FileExistsImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                return mountfs.FileExists(path);
            }

            return new (false);
        }

        /// <inheritdoc />
        protected override async ValueTask MoveFileImpl(UPath srcPath, UPath destPath)
        {
            var originalSrcPath = srcPath;
            var originalDestPath = destPath;
            if (!await FileExistsImpl(srcPath))
            {
                throw NewFileNotFoundException(srcPath);
            }

            var destDirectory = destPath.GetDirectory();
            if (!await DirectoryExistsImpl(destDirectory))
            {
                throw NewDirectoryNotFoundException(destDirectory);
            }

            if (await FileExistsImpl(destPath))
            {
                throw new IOException($"The destination path `{destPath}` already exists");
            }

            var srcfs = TryGetMountOrNext(ref srcPath);
            var destfs = TryGetMountOrNext(ref destPath);

            if (srcfs != null && srcfs == destfs)
            {
                await srcfs.MoveFile(srcPath, destPath);
            }
            else if (srcfs != null && destfs != null)
            {
                await srcfs.MoveFileCross(srcPath, destfs, destPath);
            }
            else
            {
                if (srcfs is null)
                {
                    throw NewFileNotFoundException(originalSrcPath);
                }
                throw NewDirectoryNotFoundException(originalDestPath);
            }
        }

        /// <inheritdoc />
        protected override ValueTask DeleteFileImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);

            if (mountfs != null)
            {
                return mountfs.DeleteFile(path);
            }

            return new();
        }

        /// <inheritdoc />
        protected override ValueTask<Stream> OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);

            if (mountfs != null)
            {
                return mountfs.OpenFile(path, mode, access, share);
            }

            if (mode == FileMode.Open || mode == FileMode.Truncate)
            {
                throw NewFileNotFoundException(originalSrcPath);
            }

            throw new UnauthorizedAccessException($"The access to path `{originalSrcPath}` is denied");
        }

        /// <inheritdoc />
        protected override ValueTask<FileAttributes> GetAttributesImpl(UPath path)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                return mountfs.GetAttributes(path);
            }
            throw NewFileNotFoundException(originalSrcPath);
        }

        /// <inheritdoc />
        protected override ValueTask SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                return mountfs.SetAttributes(path, attributes);
            }
            else
            {
                throw NewFileNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override ValueTask<DateTime> GetCreationTimeImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);

            if (mountfs != null)
            {
                return mountfs.GetCreationTime(path);
            }
             
            return new (DefaultFileTime);
        }

        /// <inheritdoc />
        protected override ValueTask SetCreationTimeImpl(UPath path, DateTime time)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                return mountfs.SetCreationTime(path, time);
            }
            else
            {
                throw NewFileNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override ValueTask<DateTime> GetLastAccessTimeImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);

            if (mountfs != null)
            {
                return mountfs.GetLastAccessTime(path);
            }

            return new(DefaultFileTime);
        }

        /// <inheritdoc />
        protected override ValueTask SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                return mountfs.SetLastAccessTime(path, time);
            }
            else
            {
                throw NewFileNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override ValueTask<DateTime> GetLastWriteTimeImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);

            if (mountfs != null)
            {
                return mountfs.GetLastWriteTime(path);
            }

            return new(DefaultFileTime);
        }

        /// <inheritdoc />
        protected override ValueTask SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                return mountfs.SetLastWriteTime(path, time);
            }
            else
            {
                throw NewFileNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override async ValueTask<IEnumerable<UPath>> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            // Use the search pattern to normalize the path/search pattern
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            // Internal method used to retrieve the list of search locations
            async ValueTask<List<SearchLocation>> GetSearchLocations(UPath basePath)
            {
                var locations = new List<SearchLocation>();
                var matchedMount = false;

                foreach (var kvp in _mounts)
                {
                    // Check if path partially matches a mount name
                    var remainingPath = GetRemaining(basePath, kvp.Key);
                    if (!remainingPath.IsNull && remainingPath != UPath.Root)
                    {
                        locations.Add(new SearchLocation(this, basePath, remainingPath));
                        continue;
                    }

                    if (!matchedMount)
                    {
                        // Check if path fully matches a mount name
                        remainingPath = GetRemaining(kvp.Key, basePath);
                        if (!remainingPath.IsNull)
                        {
                            matchedMount = true; // don't check other mounts, we don't want to merge them together

                            if (await kvp.Value.DirectoryExists(remainingPath))
                            {
                                locations.Add(new SearchLocation(kvp.Value, kvp.Key, remainingPath));
                            }
                        }
                    }
                }

                if (!matchedMount && Fallback != null && await Fallback.DirectoryExists(basePath))
                {
                    locations.Add(new SearchLocation(Fallback, UPath.Null, basePath));
                }

                return locations;
            }
            
            var directoryToVisit = new List<UPath>();
            directoryToVisit.Add(path);

            var entries = new SortedSet<UPath>();
            var sortedDirectories = new SortedSet<UPath>();

            var first = true;

            var results = new List<UPath>();

            while (directoryToVisit.Count > 0)
            {
                var pathToVisit = directoryToVisit[0];
                directoryToVisit.RemoveAt(0);
                var dirIndex = 0;
                entries.Clear();
                sortedDirectories.Clear();

                var locations = await GetSearchLocations(pathToVisit);

                // Only need to search within one filesystem, no need to sort or do other work
                if (locations.Count == 1 && locations[0].FileSystem != this && (!first || searchOption == SearchOption.AllDirectories))
                {
                    var last = locations[0];
                    foreach (var item in await last.FileSystem.EnumeratePaths(last.Path, searchPattern, searchOption, searchTarget))
                    {
                        results.Add(CombinePrefix(last.Prefix, item));
                    }
                }
                else
                {
                    for (var i = locations.Count - 1; i >= 0; i--)
                    {
                        var location = locations[i];
                        var fileSystem = location.FileSystem;
                        var searchPath = location.Path;

                        if (fileSystem == this)
                        {
                            // List a single part of a mount name, queue it to be visited if needed
                            var mountPart = new UPath(searchPath.GetFirstDirectory(out _)).ToRelative();
                            var mountPath = location.Prefix / mountPart;

                            var isMatching = search.Match(mountPath);
                            if (isMatching && searchTarget != SearchTarget.File)
                            {
                                entries.Add(mountPath);
                            }

                            if (searchOption == SearchOption.AllDirectories)
                            {
                                sortedDirectories.Add(mountPath);
                            }
                        }
                        else
                        {
                            // List files in the mounted filesystems, merged and sorted into one list
                            foreach (var item in await fileSystem.EnumeratePaths(searchPath, "*", SearchOption.TopDirectoryOnly, SearchTarget.Both))
                            {
                                var publicName = CombinePrefix(location.Prefix, item);
                                if (entries.Contains(publicName))
                                {
                                    continue;
                                }

                                var isFile = await fileSystem.FileExists(item);
                                var isDirectory = await fileSystem.DirectoryExists(item);
                                var isMatching = search.Match(publicName);

                                if (isMatching && ((isFile && searchTarget != SearchTarget.Directory) || (isDirectory && searchTarget != SearchTarget.File)))
                                {
                                    entries.Add(publicName);
                                }

                                if (searchOption == SearchOption.AllDirectories && isDirectory)
                                {
                                    sortedDirectories.Add(publicName);
                                }
                            }
                        }
                    }
                }

                if (first)
                {
                    if (locations.Count == 0 && path != UPath.Root)
                        throw NewDirectoryNotFoundException(path);

                    first = false;
                }

                // Enqueue directories and respect order
                foreach (var nextDir in sortedDirectories)
                {
                    directoryToVisit.Insert(dirIndex++, nextDir);
                }

                // Return entries
                foreach (var entry in entries)
                {
                    results.Add(entry);
                }
            }

            return results;

        }

        /// <inheritdoc/>
        protected override async ValueTask<IEnumerable<FileSystemItem>> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
        {
            // Internal method used to retrieve the list of search locations
            async ValueTask<List<SearchLocation>> GetSearchLocations(UPath basePath)
            {
                var locations = new List<SearchLocation>();
                var matchedMount = false;

                foreach (var kvp in _mounts)
                {
                    // Check if path partially matches a mount name
                    var remainingPath = GetRemaining(basePath, kvp.Key);
                    if (!remainingPath.IsNull && remainingPath != UPath.Root)
                    {
                        locations.Add(new SearchLocation(this, basePath, remainingPath));
                        continue;
                    }

                    if (!matchedMount)
                    {
                        // Check if path fully matches a mount name
                        remainingPath = GetRemaining(kvp.Key, basePath);
                        if (!remainingPath.IsNull)
                        {
                            matchedMount = true; // don't check other mounts, we don't want to merge them together

                            if (await kvp.Value.DirectoryExists(remainingPath))
                            {
                                locations.Add(new SearchLocation(kvp.Value, kvp.Key, remainingPath));
                            }
                        }
                    }
                }

                if (!matchedMount && Fallback != null && await Fallback.DirectoryExists(basePath))
                {
                    locations.Add(new SearchLocation(Fallback, UPath.Null, basePath));
                }

                return locations;
            }

            var directoryToVisit = new List<UPath> {path};
            var results = new List<FileSystemItem>();

            var entries = new HashSet<UPath>();
            var sortedDirectories = new SortedSet<UPath>();

            var first = true;

            while (directoryToVisit.Count > 0)
            {
                var pathToVisit = directoryToVisit[0];
                directoryToVisit.RemoveAt(0);
                var dirIndex = 0;
                entries.Clear();
                sortedDirectories.Clear();

                var locations = await GetSearchLocations(pathToVisit);

                // Only need to search within one filesystem, no need to sort or do other work
                if (locations.Count == 1 && locations[0].FileSystem != this && (!first || searchOption == SearchOption.AllDirectories))
                {
                    var last = locations[0];
                    foreach (var item in await last.FileSystem.EnumerateItems(last.Path, searchOption, searchPredicate))
                    {
                        var localItem = item;
                        localItem.Path = CombinePrefix(last.Prefix, item.Path);
                        if (entries.Add(localItem.Path))
                        {
                            results.Add(localItem);
                        }
                    }
                }
                else
                {
                    for (var i = locations.Count - 1; i >= 0; i--)
                    {
                        var location = locations[i];
                        var fileSystem = location.FileSystem;
                        var searchPath = location.Path;

                        if (fileSystem == this)
                        {
                            // List a single part of a mount name, queue it to be visited if needed
                            var mountPart = new UPath(searchPath.GetFirstDirectory(out _)).ToRelative();
                            var mountPath = location.Prefix / mountPart;

                            var item = new FileSystemItem(this, mountPath, true);
                            if (searchPredicate == null || searchPredicate(ref item))
                            {
                                if (entries.Add(item.Path))
                                {
                                    results.Add(item);
                                }
                            }

                            if (searchOption == SearchOption.AllDirectories)
                            {
                                sortedDirectories.Add(mountPath);
                            }
                        }
                        else
                        {
                            // List files in the mounted filesystems, merged and sorted into one list
                            foreach (var item in await fileSystem.EnumerateItems(searchPath, SearchOption.TopDirectoryOnly, searchPredicate))
                            {
                                var publicName = CombinePrefix(location.Prefix, item.Path);
                                if (entries.Add(publicName))
                                {
                                    var localItem = item;
                                    localItem.Path = publicName;
                                    results.Add(localItem);

                                    if (searchOption == SearchOption.AllDirectories && item.IsDirectory)
                                    {
                                        sortedDirectories.Add(publicName);
                                    }
                                }

                            }
                        }
                    }
                }

                if (first)
                {
                    if (locations.Count == 0 && path != UPath.Root)
                        throw NewDirectoryNotFoundException(path);

                    first = false;
                }

                // Enqueue directories and respect order
                foreach (var nextDir in sortedDirectories)
                {
                    directoryToVisit.Insert(dirIndex++, nextDir);
                }
            }

            return results;
        }

        /// <inheritdoc />
        protected override bool CanWatchImpl(UPath path)
        {
            // Always allow watching because a future filesystem can be added that matches this path.
            return true;
        }

        /// <inheritdoc />
        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            // TODO: create/delete events when mounts are added/removed
            
            var watcher = new AggregateWatcher(this, path);

            foreach (var kvp in _mounts)
            {
                if (!IsMountIncludedInWatch(kvp.Key, path, out var remainingPath))
                {
                    continue;
                }

                if (kvp.Value.CanWatch(remainingPath))
                {
                    var internalWatcher = kvp.Value.Watch(remainingPath);
                    watcher.Add(new WrapWatcher(kvp.Value, kvp.Key, remainingPath, internalWatcher));
                }
            }

            if (Fallback != null && Fallback.CanWatch(path))
            {
                var internalWatcher = Fallback.Watch(path);
                watcher.Add(new WrapWatcher(Fallback, UPath.Null, path, internalWatcher));
            }

            _watchers.Add(watcher);

            return watcher;
        }

        private class AggregateWatcher : AggregateFileSystemWatcher
        {
            private readonly MountFileSystem _fileSystem;

            public AggregateWatcher(MountFileSystem fileSystem, UPath path)
                : base(fileSystem, path)
            {
                _fileSystem = fileSystem;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_fileSystem.IsDisposing)
                {
                    _fileSystem._watchers.Remove(this);
                }
            }
        }

        private class WrapWatcher : WrapFileSystemWatcher
        {
            private readonly UPath _mountPath;

            public WrapWatcher(IFileSystem fileSystem, UPath mountPath, UPath path, IFileSystemWatcher watcher)
                : base(fileSystem, path, watcher)
            {
                _mountPath = mountPath;
            }

            protected override UPath? TryConvertPath(UPath pathFromEvent)
            {
                if (!_mountPath.IsNull)
                {
                    return _mountPath / pathFromEvent.ToRelative();
                }
                else
                {
                    return pathFromEvent;
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

        private IFileSystem? TryGetMountOrNext(ref UPath path)
        {
            return TryGetMountOrNext(ref path, out var _);
        }

        private IFileSystem? TryGetMountOrNext(ref UPath path, out UPath mountPath)
        {
            mountPath = UPath.Null;
            if (path.IsNull)
            {
                return null;
            }

            IFileSystem? mountfs = null;
            foreach (var kvp in _mounts)
            {
                var remainingPath = GetRemaining(kvp.Key, path);
                if (remainingPath.IsNull)
                {
                    continue;
                }

                mountPath = kvp.Key;
                mountfs = kvp.Value;
                path = remainingPath;
                break;
            }

            if (mountfs != null)
            {
                return mountfs;
            }
            
            mountPath = UPath.Null;
            return Fallback;
        }

        /// <summary>
        /// Checks if a mount path would be included in the given watch path. Also provides the path to watch on the mounted
        /// filesystem in <paramref name="remainingPath"/>.
        /// </summary>
        private static bool IsMountIncludedInWatch(UPath mountPrefix, UPath watchPath, out UPath remainingPath)
        {
            if (watchPath == UPath.Root)
            {
                remainingPath = UPath.Root;
                return true;
            }
            
            remainingPath = GetRemaining(mountPrefix, watchPath);
            return !remainingPath.IsNull;
        }

        /// <summary>
        /// Gets the remaining path after the <see cref="prefix"/>.
        /// </summary>
        /// <param name="prefix">The prefix of the path.</param>
        /// <param name="path">The path to search.</param>
        /// <returns>The path after the prefix, or a <c>null</c> path if <see cref="path"/> does not have the correct prefix.</returns>
        private static UPath GetRemaining(UPath prefix, UPath path)
        {
            if (!path.IsInDirectory(prefix, true))
            {
                return null!;
            }

            var remaining = path.FullName.Substring(prefix.FullName.Length);
            var remainingPath = new UPath(remaining).ToAbsolute();

            return remainingPath;
        }

        private static UPath CombinePrefix(UPath prefix, UPath remaining)
        {
            return prefix.IsNull ? remaining
                : prefix / remaining.ToRelative();
        }

        private void ValidateMountName(UPath name)
        {
            name.AssertAbsolute(nameof(name));
            if (name == UPath.Root)
            {
                throw new ArgumentException("The mount name cannot be a `/` root filesystem", nameof(name));
            }
        }

        private class UPathLengthComparer : IComparer<UPath>
        {
            public int Compare(UPath x, UPath y)
            {
                // longest UPath first
                var lengthCompare = y.FullName.Length.CompareTo(x.FullName.Length);
                if (lengthCompare != 0)
                {
                    return lengthCompare;
                }

                // then compare name if equal length (otherwise we get exceptions about duplicates)
                return string.CompareOrdinal(x.FullName, y.FullName);
            }
        }

        private readonly struct SearchLocation
        {
            public IFileSystem FileSystem { get; }
            public UPath Prefix { get; }
            public UPath Path { get; }

            public SearchLocation(IFileSystem fileSystem, UPath prefix, UPath path)
            {
                FileSystem = fileSystem;
                Prefix = prefix;
                Path = path;
            }
        }

        private sealed class DebuggerProxy
        {
            private readonly MountFileSystem _fs;

            public DebuggerProxy(MountFileSystem fs)
            {
                _fs = fs;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public KeyValuePair<string, IFileSystem>[] Mounts => _fs._mounts.Select(x => new KeyValuePair<string, IFileSystem>(x.Key.ToString(), x.Value)).ToArray();

            public IFileSystem? Fallback => _fs.Fallback;
        }
    }
}
