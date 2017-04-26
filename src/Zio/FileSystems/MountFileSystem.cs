// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using static Zio.FileSystems.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// A <see cref="IFileSystem"/> that can mount other filesystems on a root name. 
    /// This mount filesystem supports also an optionnal fallback delegate FileSystem if a path was not found through a mount
    /// </summary>
    public class MountFileSystem : DelegateFileSystem
    {
        private readonly Dictionary<string, IFileSystem> _mounts;

        public MountFileSystem() : this(null)
        {
        }

        public MountFileSystem(IFileSystem nextFileSystem) : base(nextFileSystem)
        {
            _mounts = new Dictionary<string, IFileSystem>();
        }

        public void Mount(UPath name, IFileSystem fileSystem)
        {
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            if (fileSystem == this)
            {
                throw new ArgumentException("Cannot recursively mount the filesystem to self", nameof(fileSystem));
            }
            AssertMountName(name);
            var mountName = name.GetName();

            lock (_mounts)
            {
                if (_mounts.ContainsKey(mountName))
                {                    
                    throw new ArgumentException("There is already a mount with the same name: `{mountName}`", nameof(name));
                }
                _mounts.Add(mountName, fileSystem);
            }
        }

        public bool IsMounted(UPath name)
        {
            AssertMountName(name);
            var mountName = name.GetName();

            lock (_mounts)
            {
                return _mounts.ContainsKey(mountName);
            }
        }

        public Dictionary<UPath, IFileSystem> GetMounts()
        {
            var dict = new Dictionary<UPath, IFileSystem>();
            lock (_mounts)
            {
                foreach (var mount in _mounts)
                {
                    dict.Add(UPath.Root / mount.Key, mount.Value);
                }
            }
            return dict;
        }

        public void Unmount(UPath name)
        {
            AssertMountName(name);
            var mountName = name.GetName();

            lock (_mounts)
            {
                if (!_mounts.Remove(mountName))
                {
                    throw new ArgumentException("The mount with the name `{mountName}` was not found");
                }
            }
        }
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

        protected override bool FileExistsImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);
            return mountfs?.FileExists(path) ?? false;
        }

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

        protected override void DeleteFileImpl(UPath path)
        {
            var mountfs = TryGetMountOrNext(ref path);
            mountfs?.DeleteFile(path);
        }

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

        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            return TryGetMountOrNext(ref path)?.GetCreationTime(path) ?? DefaultFileTime;
        }

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

        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            return TryGetMountOrNext(ref path)?.GetLastAccessTime(path) ?? DefaultFileTime;
        }

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

        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            return TryGetMountOrNext(ref path)?.GetLastWriteTime(path) ?? DefaultFileTime;
        }

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

        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            // Use the search pattern to normalize the path/search pattern
            var search = SearchPattern.Parse(ref path, ref searchPattern);
            var originalSrcPath = path;

            // Internal method used to retrieve the list of root directories
            List<UPath> GetRootDirectories()
            {
                var directories = new List<UPath>();
                lock (_mounts)
                {
                    foreach (var mountName in _mounts.Keys)
                    {
                        var mountPath = UPath.Root / mountName;
                        directories.Add(mountPath);
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

                directories.Sort();
                return directories;
            }

            IEnumerable<UPath> EnumeratePathFromFileSystem(UPath subPath, bool failOnInvalidPath)
            {
                string mountName;
                var fs = TryGetMountOrNext(ref subPath, out mountName);

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
                    Debug.Assert(mountName != null);
                    var pathPrefix = UPath.Root / mountName;
                    foreach (var entry in fs.EnumeratePaths(subPath, searchPattern, searchOption, searchTarget))
                    {
                        yield return pathPrefix / entry.ToRelative();
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
                if (searchOption == SearchOption.TopDirectoryOnly)
                {
                    // Process the files first
                    if (NextFileSystem != null && (searchTarget == SearchTarget.File || searchTarget == SearchTarget.Both))
                    {
                        foreach (var file in NextFileSystem.EnumeratePaths(path, searchPattern, SearchOption.TopDirectoryOnly, SearchTarget.File))
                        {
                            yield return file;
                        }
                    }

                    if (searchTarget == SearchTarget.File)
                    {
                        yield break;
                    }

                    foreach (var dir in GetRootDirectories())
                    {
                        if (searchTarget == SearchTarget.Both || search.Match(dir))
                        {
                            yield return dir;
                        }
                    }
                }
                else // Recursive
                {
                    // Process the files first
                    if (NextFileSystem != null && (searchTarget == SearchTarget.File || searchTarget == SearchTarget.Both))
                    {
                        foreach (var file in NextFileSystem.EnumeratePaths(path, searchPattern, SearchOption.TopDirectoryOnly, SearchTarget.File))
                        {
                            yield return file;
                        }
                    }

                    // Return the list of dircetories
                    var directories = GetRootDirectories();
                    if (searchTarget != SearchTarget.File)
                    {
                        foreach (var dir in directories)
                        {
                            if (searchTarget == SearchTarget.Both || search.Match(dir))
                            {
                                yield return dir;
                            }
                        }
                    }

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

        protected override UPath ConvertPathToDelegate(UPath path)
        {
            return path;
        }

        protected override UPath ConvertPathFromDelegate(UPath path)
        {
            return path;
        }

        private IFileSystem TryGetMountOrNext(ref UPath path)
        {
            string mountName;
            return TryGetMountOrNext(ref path, out mountName);
        }

        private IFileSystem TryGetMountOrNext(ref UPath path, out string mountName)
        {
            mountName = null;
            if (path.IsNull)
            {
                return null;
            }

            UPath mountSubPath;

            path.ExtractFirstDirectory(out mountName, out mountSubPath);
            IFileSystem mountfs;
            lock (_mounts)
            {
                _mounts.TryGetValue(mountName, out mountfs);
            }

            if (mountfs != null)
            {
                path = mountSubPath.IsNull ? UPath.Root : mountSubPath;
                Debug.Assert(path.IsAbsolute);
                return mountfs;
            }
            else if (NextFileSystem != null)
            {
                mountName = null;
                return NextFileSystem;
            }
            mountName = null;
            return null;
        }

        private void AssertMountName(UPath name)
        {
            name.AssertAbsolute();
            if (name == UPath.Root)
            {
                throw new ArgumentException("The mount name cannot be a `/` root filesystem", nameof(name));
            }

            if (name.GetDirectory() != UPath.Root)
            {
                throw new ArgumentException("The mount name cannot contain subpath and must contain only a root path e.g `/mount`", nameof(name));
            }
        }
    }
}