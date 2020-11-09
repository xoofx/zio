// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

            lock (_mounts)
            {
                lock (_watchers)
                {
                    foreach (var watcher in _watchers)
                    {
                        watcher.Dispose();
                    }

                    _watchers.Clear();
                }

                if (Owned)
                {
                    foreach (var kvp in _mounts)
                    {
                        kvp.Value.Dispose();
                    }
                }

                _mounts.Clear();
            }
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

            lock (_mounts)
            {
                if (_mounts.ContainsKey(name))
                {
                    throw new ArgumentException($"There is already a mount with the same name: `{name}`", nameof(name));
                }
                _mounts.Add(name, fileSystem);

                lock (_watchers)
                {
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

            lock (_mounts)
            {
                return _mounts.ContainsKey(name);
            }
        }

        /// <summary>
        /// Gets all the mounts currently mounted
        /// </summary>
        /// <returns>A dictionary of mounted filesystems.</returns>
        public Dictionary<UPath, IFileSystem> GetMounts()
        {
            var dict = new Dictionary<UPath, IFileSystem>();
            lock (_mounts)
            {
                foreach (var mount in _mounts)
                {
                    dict.Add(mount.Key, mount.Value);
                }
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

            IFileSystem mountFileSystem;

            lock (_mounts)
            {
                if (!_mounts.TryGetValue(name, out mountFileSystem))
                {
                    throw new ArgumentException($"The mount with the name `{name}` was not found");
                }

                lock (_watchers)
                {
                    foreach (var watcher in _watchers)
                    {
                        watcher.RemoveFrom(mountFileSystem);
                    }
                }

                _mounts.Remove(name);
            }

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

            lock (_mounts)
            {
                foreach (var mount in _mounts)
                {
                    if (mount.Value != fileSystem)
                        continue;

                    name = mount.Key;
                    return true;
                }
            }

            name = UPath.Null;
            return false;
        }

        /// <inheritdoc />
        protected override void CreateDirectoryImpl(UPath path)
        {
            var originalSrcPath = path;
            var fs = TryGetMountOrNext(ref path);
            if (fs != null && path != UPath.Root)
            {
                fs.CreateDirectory(path);
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{originalSrcPath}` is denied");
            }
        }

        /// <inheritdoc />
        protected override bool DirectoryExistsImpl(UPath path)
        {
            if (path == UPath.Root)
            {
                return true;
            }
            var fs = TryGetMountOrNext(ref path);
            if (fs != null)
            {
                return path == UPath.Root || fs.DirectoryExists(path);
            }

            // Check if the path is part of a mount name
            lock (_mounts)
            {
                foreach (var kvp in _mounts)
                {
                    var remainingPath = GetRemaining(path, kvp.Key);
                    if (!remainingPath.IsNull)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
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
                srcfs.MoveDirectory(srcPath, destPath);
            }
            else
            {
                // TODO: Add support for Copy + Delete ?
                throw new NotSupportedException($"Cannot move directory between mount `{originalSrcPath}` and `{originalDestPath}`");
            }
        }

        /// <inheritdoc />
        protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);

            if (mountfs != null && path == UPath.Root)
            {
                throw new UnauthorizedAccessException($"Cannot delete mount directory `{originalSrcPath}`. Use Unmount() instead");
            }

            if (mountfs != null)
            {
                mountfs.DeleteDirectory(path, isRecursive);
            }
            else
            {
                throw NewDirectoryNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            var originalSrcPath = srcPath;
            var originalDestPath = destPath;

            var srcfs = TryGetMountOrNext(ref srcPath);
            var destfs = TryGetMountOrNext(ref destPath);

            if (srcfs != null && destfs != null)
            {
                if (srcfs == destfs)
                {
                    srcfs.CopyFile(srcPath, destPath, overwrite);
                }
                else
                {
                    // Otherwise, perform a copy between filesystem
                    srcfs.CopyFileCross(srcPath, destfs, destPath, overwrite);
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
        protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            var originalSrcPath = srcPath;
            var originalDestPath = destPath;
            var originalDestBackupPath = destBackupPath;

            if (!FileExistsImpl(srcPath))
            {
                throw NewFileNotFoundException(srcPath);
            }

            if (!FileExistsImpl(destPath))
            {
                throw NewFileNotFoundException(destPath);
            }

            var srcfs = TryGetMountOrNext(ref srcPath);
            var destfs = TryGetMountOrNext(ref destPath);
            var backupfs = TryGetMountOrNext(ref destBackupPath);

            if (srcfs != null && srcfs == destfs && (destBackupPath.IsNull || srcfs == backupfs))
            {
                srcfs.ReplaceFile(srcPath, destPath, destBackupPath, ignoreMetadataErrors);
            }
            else
            {
                // TODO: Add support for moving file between filesystems (Copy+Delete) ?
                throw new NotSupportedException($"Cannot replace file between mount `{originalSrcPath}`, `{originalDestPath}` and `{originalDestBackupPath}`");
            }
        }

        /// <inheritdoc />
        protected override long GetFileLengthImpl(UPath path)
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
        protected override bool FileExistsImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);
            return mountfs?.FileExists(path) ?? false;
        }

        /// <inheritdoc />
        protected override void MoveFileImpl(UPath srcPath, UPath destPath)
        {
            var originalSrcPath = srcPath;
            var originalDestPath = destPath;
            if (!FileExistsImpl(srcPath))
            {
                throw NewFileNotFoundException(srcPath);
            }

            var destDirectory = destPath.GetDirectory();
            if (!DirectoryExistsImpl(destDirectory))
            {
                throw NewDirectoryNotFoundException(destDirectory);
            }

            if (FileExistsImpl(destPath))
            {
                throw new IOException($"The destination path `{destPath}` already exists");
            }

            var srcfs = TryGetMountOrNext(ref srcPath);
            var destfs = TryGetMountOrNext(ref destPath);

            if (srcfs != null && srcfs == destfs)
            {
                srcfs.MoveFile(srcPath, destPath);
            }
            else if (srcfs != null && destfs != null)
            {
                srcfs.MoveFileCross(srcPath, destfs, destPath);
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
        protected override void DeleteFileImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);
            mountfs?.DeleteFile(path);
        }

        /// <inheritdoc />
        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
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
        protected override FileAttributes GetAttributesImpl(UPath path)
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
        protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                mountfs.SetAttributes(path, attributes);
            }
            else
            {
                throw NewFileNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            return TryGetMountOrNext(ref path)?.GetCreationTime(path) ?? DefaultFileTime;
        }

        /// <inheritdoc />
        protected override void SetCreationTimeImpl(UPath path, DateTime time)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                mountfs.SetCreationTime(path, time);
            }
            else
            {
                throw NewFileNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            return TryGetMountOrNext(ref path)?.GetLastAccessTime(path) ?? DefaultFileTime;
        }

        /// <inheritdoc />
        protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                mountfs.SetLastAccessTime(path, time);
            }
            else
            {
                throw NewFileNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            return TryGetMountOrNext(ref path)?.GetLastWriteTime(path) ?? DefaultFileTime;
        }

        /// <inheritdoc />
        protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            var originalSrcPath = path;
            var mountfs = TryGetMountOrNext(ref path);
            if (mountfs != null)
            {
                mountfs.SetLastWriteTime(path, time);
            }
            else
            {
                throw NewFileNotFoundException(originalSrcPath);
            }
        }

        /// <inheritdoc />
        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            // Use the search pattern to normalize the path/search pattern
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            // Query all mounts just once
            List<KeyValuePair<UPath, IFileSystem>> mounts;
            lock (_mounts)
            {
                mounts = _mounts.ToList();
            }

            // Internal method used to retrieve the list of search locations
            List<SearchLocation> GetSearchLocations(UPath basePath)
            {
                var locations = new List<SearchLocation>();
                var matchedMount = false;

                foreach (var kvp in mounts)
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

                            if (kvp.Value.DirectoryExists(remainingPath))
                            {
                                locations.Add(new SearchLocation(kvp.Value, kvp.Key, remainingPath));
                            }
                        }
                    }
                }

                if (!matchedMount && Fallback != null && Fallback.DirectoryExists(basePath))
                {
                    locations.Add(new SearchLocation(Fallback, UPath.Null, basePath));
                }

                return locations;
            }
            
            var directoryToVisit = new List<UPath>();
            directoryToVisit.Add(path);

            var entries = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);
            var sortedDirectories = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);

            var first = true;

            while (directoryToVisit.Count > 0)
            {
                var pathToVisit = directoryToVisit[0];
                directoryToVisit.RemoveAt(0);
                var dirIndex = 0;
                entries.Clear();
                sortedDirectories.Clear();

                var locations = GetSearchLocations(pathToVisit);
                
                // Only need to search within one filesystem, no need to sort or do other work
                if (locations.Count == 1 && locations[0].FileSystem != this && (!first || searchOption == SearchOption.AllDirectories))
                {
                    var last = locations[0];
                    foreach (var item in last.FileSystem.EnumeratePaths(last.Path, searchPattern, searchOption, searchTarget))
                    {
                        yield return CombinePrefix(last.Prefix, item);
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
                            foreach (var item in fileSystem.EnumeratePaths(searchPath, "*", SearchOption.TopDirectoryOnly, SearchTarget.Both))
                            {
                                var publicName = CombinePrefix(location.Prefix, item);
                                if (entries.Contains(publicName))
                                {
                                    continue;
                                }

                                var isFile = fileSystem.FileExists(item);
                                var isDirectory = fileSystem.DirectoryExists(item);
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
                    yield return entry;
                }
            }
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

            lock (_mounts)
            lock (_watchers)
            {
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
            }

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
                    lock (_fileSystem._watchers)
                    {
                        _fileSystem._watchers.Remove(this);
                    }
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
            lock (_mounts)
            {
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
