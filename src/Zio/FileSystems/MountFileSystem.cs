// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

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

        public void Mount(PathInfo name, IFileSystem fileSystem)
        {
            name.AssertAbsolute();
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));

            if (fileSystem == this)
            {
                throw new ArgumentException("Cannot recursively mount the filesystem to self", nameof(fileSystem));
            }

            if (name == PathInfo.Root)
            {
                throw new ArgumentException("The mount name cannot be a `/` root filesystem", nameof(name));
            }

            if (name.GetDirectory() != PathInfo.Root)
            {
                throw new ArgumentException("The mount name cannot contain subpath and must contain only a root path e.g `/mount`", nameof(name));
            }

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

        public void Unmount(PathInfo name)
        {
            name.AssertAbsolute();
            if (name == PathInfo.Root)
            {
                throw new ArgumentException("The mount name cannot be a `/` root filesystem", nameof(name));
            }

            if (name.GetDirectory() != PathInfo.Root)
            {
                throw new ArgumentException("The mount name cannot contain subpath and must contain only a root path e.g `/mount`", nameof(name));
            }

            var mountName = name.GetName();

            lock (_mounts)
            {
                if (!_mounts.Remove(mountName))
                {
                    throw new ArgumentException("The mount with the name `{mountName}` was not found");
                }
            }
        }

        protected override void CreateDirectoryImpl(PathInfo path)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                mountfs.CreateDirectory(mountSubPath);
            }
            else if (NextFileSystem != null)
            {
                NextFileSystem.CreateDirectory(path);
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
            }
        }

        protected override bool DirectoryExistsImpl(PathInfo path)
        {
            if (path == PathInfo.Root)
            {
                return true;
            }

            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                return mountfs.DirectoryExists(mountSubPath);
            }

            return NextFileSystem != null && NextFileSystem.DirectoryExists(path);
        }

        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            if (!DirectoryExistsImpl(srcPath))
            {
                throw new DirectoryNotFoundException($"The directory `{srcPath}` was not found");
            }

            var destDirectory = destPath.GetDirectory();
            if (!DirectoryExistsImpl(destDirectory))
            {
                throw new DirectoryNotFoundException($"The directory `{destDirectory}` was not found");
            }

            string srcMountName;
            PathInfo srcMountSubPath;
            var srcfs = TryGetMount(srcPath, out srcMountName, out srcMountSubPath);

            string destMountName;
            PathInfo destMountSubPath;
            var destfs = TryGetMount(destPath, out destMountName, out destMountSubPath);

            if (srcfs != null &&  srcMountSubPath == "")
            {
                throw new UnauthorizedAccessException($"Cannot move a mount directory `{srcPath}`");
            }

            if (destfs != null && destMountSubPath == "")
            {
                throw new UnauthorizedAccessException($"Cannot move a mount directory `{destPath}`");
            }

            if (srcfs != null && srcfs == destfs)
            {
                srcfs.MoveDirectory(srcMountSubPath, destMountSubPath);
            }
            else if (srcfs == null && destfs == null && NextFileSystem != null)
            {
                NextFileSystem.MoveDirectory(srcPath, destPath);
            }
            else
            {
                throw new UnauthorizedAccessException($"Cannot move directory between mount");
            }
        }

        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null && mountSubPath == "")
            {
                throw new UnauthorizedAccessException($"Cannot delete mount directory `{path}`. Use Unmount() instead");
            }

            if (mountfs != null)
            {
                mountfs.DeleteDirectory(mountSubPath, isRecursive);
            }
            else if (NextFileSystem != null)
            {
                NextFileSystem.DeleteDirectory(path, isRecursive);
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
            }
        }

        protected override void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            if (!FileExistsImpl(srcPath))
            {
                throw new FileNotFoundException($"The file `{srcPath}` was not found");
            }

            var destDirectory = destPath.GetDirectory();
            if (!DirectoryExistsImpl(destDirectory))
            {
                throw new DirectoryNotFoundException($"The directory `{destDirectory}` was not found");
            }

            string srcMountName;
            PathInfo srcMountSubPath;
            var srcfs = TryGetMount(srcPath, out srcMountName, out srcMountSubPath);

            string destMountName;
            PathInfo destMountSubPath;
            var destfs = TryGetMount(destPath, out destMountName, out destMountSubPath);

            if (srcfs == null)
            {
                srcfs = NextFileSystem;
                srcMountSubPath = srcPath;
            }

            if (destfs == null)
            {
                destfs = NextFileSystem;
                destMountSubPath = destPath;
            }

            if (srcfs != null)
            {
                if (srcfs == destfs)
                {
                    srcfs.CopyFile(srcMountSubPath, destMountSubPath, overwrite);
                }
                else
                {
                    // Otherwise, perform a copy between filesystem
                    srcfs.CopyFileTo(destfs, srcMountSubPath, destMountSubPath, overwrite);
                }
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{srcPath}` or `{destPath}` is denied");
            }
        }

        protected override void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors)
        {
            throw new NotImplementedException();
        }

        protected override long GetFileLengthImpl(PathInfo path)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                return mountfs.GetFileLength(mountSubPath);
            }

            if (NextFileSystem != null)
            {
                return NextFileSystem.GetFileLength(path);
            }

            throw new FileNotFoundException($"The file path `{path}` does not exist");
        }

        protected override bool FileExistsImpl(PathInfo path)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                return mountfs.FileExists(mountSubPath);
            }

            return NextFileSystem != null && NextFileSystem.FileExists(path);
        }

        protected override void MoveFileImpl(PathInfo srcPath, PathInfo destPath)
        {
            if (!FileExistsImpl(srcPath))
            {
                throw new FileNotFoundException($"The file `{srcPath}` was not found");
            }

            var destDirectory = destPath.GetDirectory();
            if (!DirectoryExistsImpl(destDirectory))
            {
                throw new DirectoryNotFoundException($"The directory `{destDirectory}` was not found");
            }

            if (FileExistsImpl(destPath))
            {
                throw new IOException($"The destination path `{destPath}` already exists");
            }

            string srcMountName;
            PathInfo srcMountSubPath;
            var srcfs = TryGetMount(srcPath, out srcMountName, out srcMountSubPath);

            string destMountName;
            PathInfo destMountSubPath;
            var destfs = TryGetMount(destPath, out destMountName, out destMountSubPath);

            if (srcfs != null && srcfs == destfs)
            {
                srcfs.MoveFile(srcMountSubPath, destMountSubPath);
            }
            else if (srcfs == null && destfs == null && NextFileSystem != null)
            {
                NextFileSystem.MoveFile(srcPath, destPath);
            }
            else
            {
                throw new UnauthorizedAccessException($"Cannot move file between mount");
            }
        }

        protected override void DeleteFileImpl(PathInfo path)
        {
            if (!FileExistsImpl(path))
            {
                throw new FileNotFoundException($"The file `{path}` was not found");
            }

            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                mountfs.DeleteFile(mountSubPath);
            }
            else if (NextFileSystem != null)
            {
                NextFileSystem.DeleteFile(path);
            }
        }

        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                return mountfs.OpenFile(mountSubPath, mode, access, share);
            }
            else if (NextFileSystem != null)
            {
                return NextFileSystem.OpenFile(path, mode, access, share);
            }

            if (mode == FileMode.Open || mode == FileMode.Truncate)
            {
                throw new FileNotFoundException($"The file `{path}` was not found");
            }

            throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
        }

        protected override FileAttributes GetAttributesImpl(PathInfo path)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                return mountfs.GetAttributes(mountSubPath);
            }

            if (NextFileSystem != null)
            {
                return NextFileSystem.GetAttributes(path);
            }

            throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
        }

        protected override void SetAttributesImpl(PathInfo path, FileAttributes attributes)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                mountfs.SetAttributes(mountSubPath, attributes);
            }
            else if (NextFileSystem != null)
            {
                NextFileSystem.SetAttributes(path, attributes);
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
            }
        }

        protected override DateTime GetCreationTimeImpl(PathInfo path)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                return mountfs.GetCreationTime(mountSubPath);
            }

            if (NextFileSystem != null)
            {
                return NextFileSystem.GetCreationTime(path);
            }

            return DefaultFileTime;
        }

        protected override void SetCreationTimeImpl(PathInfo path, DateTime time)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                mountfs.SetCreationTime(mountSubPath, time);
            }
            else if (NextFileSystem != null)
            {
                NextFileSystem.SetCreationTime(path, time);
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
            }
        }

        protected override DateTime GetLastAccessTimeImpl(PathInfo path)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                return mountfs.GetLastAccessTime(mountSubPath);
            }

            if (NextFileSystem != null)
            {
                return NextFileSystem.GetLastAccessTime(path);
            }

            return DefaultFileTime;

        }

        protected override void SetLastAccessTimeImpl(PathInfo path, DateTime time)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                mountfs.SetLastAccessTime(mountSubPath, time);
            }
            else if (NextFileSystem != null)
            {
                NextFileSystem.SetLastAccessTime(path, time);
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
            }
        }

        protected override DateTime GetLastWriteTimeImpl(PathInfo path)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                return mountfs.GetLastWriteTime(mountSubPath);
            }

            if (NextFileSystem != null)
            {
                return NextFileSystem.GetLastWriteTime(path);
            }

            return DefaultFileTime;

        }

        protected override void SetLastWriteTimeImpl(PathInfo path, DateTime time)
        {
            string mountName;
            PathInfo mountSubPath;
            var mountfs = TryGetMount(path, out mountName, out mountSubPath);

            if (mountfs != null)
            {
                mountfs.SetLastWriteTime(mountSubPath, time);
            }
            else if (NextFileSystem != null)
            {
                NextFileSystem.SetLastWriteTime(path, time);
            }
            else
            {
                throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
            }
        }

        protected override IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            // Use the search pattern to normalize the path/search pattern
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            if (path == PathInfo.Root)
            {
                throw new NotImplementedException();
            }
            else
            {
                string mountName;
                PathInfo mountSubPath;
                var mountfs = TryGetMount(path, out mountName, out mountSubPath);

                if (mountfs != null)
                {
                    return mountfs.EnumeratePaths(mountSubPath, searchPattern, searchOption, searchTarget);
                }

                if (NextFileSystem != null)
                {
                    return NextFileSystem.EnumeratePaths(path, searchPattern, searchOption, searchTarget);
                }

                throw new UnauthorizedAccessException($"The access to path `{path}` is denied");
            }
        }
        protected override PathInfo ConvertPathToDelegate(PathInfo path)
        {
            return path;
        }

        protected override PathInfo ConvertPathFromDelegate(PathInfo path)
        {
            return path;
        }

        private IFileSystem TryGetMount(PathInfo path, out string mountName, out PathInfo mountSubPath)
        {
            path.ExtractFirstDirectory(out mountName, out mountSubPath);
            IFileSystem mountfs;
            lock (_mounts)
            {
                _mounts.TryGetValue(mountName, out mountfs);
            }
            return mountfs;
        }
    }
}