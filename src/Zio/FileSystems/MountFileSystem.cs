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
    /// A <see cref="IFileSystem"/> that can mount other filesystems on a root name. 
    /// This mount filesystem supports also an optionnal fallback delegate FileSystem if a path was not found through a mount
    /// </summary>
    public class MountFileSystem : ComposeFileSystem
    {
        private readonly SortedList<UPath, IFileSystem> _mounts;
        private readonly List<AggregateFileSystemWatcher> _aggregateWatchers;
        private readonly List<Watcher> _watchers;

        /// <summary>
        /// Initializes a new instance of the <see cref="MountFileSystem"/> class.
        /// </summary>
        public MountFileSystem() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MountFileSystem"/> class with a default backup filesystem.
        /// </summary>
        /// <param name="defaultBackupFileSystem">The default backup file system.</param>
        public MountFileSystem(IFileSystem defaultBackupFileSystem) : base(defaultBackupFileSystem)
        {
            _mounts = new SortedList<UPath, IFileSystem>(new UPathLengthComparer());
            _aggregateWatchers = new List<AggregateFileSystemWatcher>();
            _watchers = new List<Watcher>();
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
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            if (fileSystem == this)
            {
                throw new ArgumentException("Cannot recursively mount the filesystem to self", nameof(fileSystem));
            }
            AssertMountName(name);
            name = NormalizeMountName(name);

            lock (_mounts)
            {
                if (_mounts.ContainsKey(name))
                {
                    throw new ArgumentException($"There is already a mount with the same name: `{name}`", nameof(name));
                }
                _mounts.Add(name, fileSystem);

                lock (_aggregateWatchers)
                {
                    foreach (var watcher in _aggregateWatchers)
                    {
                        var internalWatcher = fileSystem.Watch(UPath.Root);
                        watcher.Add(new Watcher(this, name, UPath.Root, internalWatcher));
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
            AssertMountName(name);
            name = NormalizeMountName(name);

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
        /// <exception cref="System.ArgumentException">The mount with the name <paramref name="name"/> was not found</exception>
        public void Unmount(UPath name)
        {
            AssertMountName(name);
            name = NormalizeMountName(name);

            IFileSystem mountFileSystem;

            lock (_mounts)
            {
                if (!_mounts.TryGetValue(name, out mountFileSystem))
                {
                    throw new ArgumentException($"The mount with the name `{name}` was not found");
                }

                _mounts.Remove(name);
            }

            lock (_aggregateWatchers)
            {
                foreach (var watcher in _aggregateWatchers)
                {
                    watcher.RemoveFrom(mountFileSystem);
                }
            }

            lock (_watchers)
            {
                for (var i = _watchers.Count - 1; i >= 0; i--)
                {
                    var watcher = _watchers[i];

                    if (watcher.MountFileSystem == mountFileSystem)
                    {
                        _watchers.RemoveAt(i);
                    }
                }
            }
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
                    srcfs.CopyFileCross(destfs, srcPath, destPath, overwrite);
                }
            }
            else
            {
                if (srcfs == null)
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
                srcfs.MoveFileCross(destfs, srcPath, destPath);
            }
            else
            {
                if (srcfs == null)
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
            var originalSrcPath = path;

            // Internal method used to retrieve the list of root directories
            SortedSet<UPath> GetRootDirectories()
            {
                var directories = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);
                lock (_mounts)
                {
                    foreach (var mountName in _mounts.Keys)
                    {
                        directories.Add(mountName);
                    }
                }

                if (NextFileSystem != null)
                {
                    foreach (var dir in NextFileSystem.EnumeratePaths(path, "*", SearchOption.TopDirectoryOnly, SearchTarget.Directory))
                    {
                        if (!directories.Contains(dir))
                        {
                            directories.Add(dir);
                        }
                    }
                }

                return directories;
            }

            IEnumerable<UPath> EnumeratePathFromFileSystem(UPath subPath, bool failOnInvalidPath)
            {
                var fs = TryGetMountOrNext(ref subPath, out var mountPath);

                if (fs == null)
                {
                    if (failOnInvalidPath)
                    {
                        throw NewDirectoryNotFoundException(originalSrcPath);
                    }
                    yield break;
                }

                if (fs != NextFileSystem)
                {
                    // In the case of a mount, we need to return the full path
                    Debug.Assert(!mountPath.IsNull);

                    foreach (var entry in fs.EnumeratePaths(subPath, searchPattern, searchOption, searchTarget))
                    {
                        yield return mountPath / entry.ToRelative();
                    }
                }
                else
                {
                    foreach (var entry in fs.EnumeratePaths(subPath, searchPattern, searchOption, searchTarget))
                    {
                        yield return entry;
                    }
                }
            }

            // Special case for the root as we have to return the list of mount directories
            // and merge them with the underlying FileSystem
            if (path == UPath.Root)
            {
                var entries = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);

                // Return the list of dircetories
                var directories = GetRootDirectories();

                // Process the files first
                if (NextFileSystem != null && (searchTarget == SearchTarget.File || searchTarget == SearchTarget.Both))
                {
                    foreach (var file in NextFileSystem.EnumeratePaths(path, searchPattern, SearchOption.TopDirectoryOnly, SearchTarget.File))
                    {
                        entries.Add(file);
                    }
                }

                if (searchTarget != SearchTarget.File)
                {
                    foreach (var dir in directories)
                    {
                        if (search.Match(dir))
                        {
                            entries.Add(dir);
                        }
                    }
                }

                // Return all entries sorted
                foreach (var entry in entries)
                {
                    yield return entry;
                }

                if (searchOption == SearchOption.AllDirectories)
                {
                    foreach (var dir in directories)
                    {
                        foreach (var entry in EnumeratePathFromFileSystem(dir, false))
                        {
                            yield return entry;
                        }
                    }
                }
            }
            else
            {
                foreach (var entry in EnumeratePathFromFileSystem(path, true))
                {
                    yield return entry;
                }
            }
        }

        /// <inheritdoc />
        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            if (path == UPath.Root)
            {
                // watch on all
                // TODO: create/delete events when mounts are added/removed

                var watcher = new AggregateFileSystemWatcher(this, path);

                lock (_mounts)
                lock (_aggregateWatchers)
                {
                    foreach (var kvp in _mounts)
                    {
                        var internalWatcher = kvp.Value.Watch(UPath.Root);
                        watcher.Add(new Watcher(this, kvp.Key, UPath.Root, internalWatcher));
                    }

                    if (NextFileSystem != null)
                    {
                        var internalWatcher = NextFileSystem.Watch(UPath.Root);
                        watcher.Add(new Watcher(this, null, UPath.Root, internalWatcher));
                    }

                    _aggregateWatchers.Add(watcher);
                }

                return watcher;
            }
            else
            {
                // watch only one mount point
                var internalPath = path;
                var fs = TryGetMountOrNext(ref internalPath, out var mountPath);
                if (fs == null)
                {
                    throw NewFileNotFoundException(path);
                }

                var internalWatcher = fs.Watch(internalPath);
                var watcher = new Watcher(this, mountPath, path, internalWatcher);

                lock (_watchers)
                {
                    _watchers.Add(watcher);
                }

                return watcher;
            }
        }

        private class Watcher : WrapFileSystemWatcher
        {
            private readonly UPath _mountPath;

            public IFileSystem MountFileSystem { get; }

            public Watcher(MountFileSystem fileSystem, UPath mountPath, UPath path, IFileSystemWatcher watcher)
                : base(fileSystem, path, watcher)
            {
                _mountPath = mountPath;

                MountFileSystem = watcher.FileSystem;
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

        private IFileSystem TryGetMountOrNext(ref UPath path)
        {
            return TryGetMountOrNext(ref path, out var _);
        }

        private IFileSystem TryGetMountOrNext(ref UPath path, out UPath mountPath)
        {
            mountPath = null;
            if (path.IsNull)
            {
                return null;
            }

            IFileSystem mountfs = null;
            lock (_mounts)
            {
                foreach (var kvp in _mounts)
                {
                    if (path.IsInDirectory(kvp.Key, true))
                    {
                        mountPath = kvp.Key;
                        mountfs = kvp.Value;
                        path = new UPath(path.FullName.Substring(mountPath.FullName.Length)).ToAbsolute();
                        break;
                    }
                }
            }

            if (mountfs != null)
            {
                return mountfs;
            }
            
            mountPath = null;
            return NextFileSystem;
        }

        private static UPath NormalizeMountName(UPath name)
        {
            if (name.FullName[name.FullName.Length - 1] == UPath.DirectorySeparator)
                return name;

            return name.FullName + "/";
        }

        private void AssertMountName(UPath name)
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
    }
}
