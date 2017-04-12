// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;

namespace Zio.FileSystems
{
    /// <summary>
    /// Base class for <see cref="IFileSystem"/>. Provides default arguments safety checking and redirecting to safe implementation.
    /// </summary>
    public abstract class FileSystemBase : IFileSystem
    {
        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        public void CreateDirectory(PathInfo path)
        {
            path.AssertAbsolute();
            CreateDirectoryImpl(path);
        }
        protected abstract void CreateDirectoryImpl(PathInfo path);

        /// <inheritdoc />
        public bool DirectoryExists(PathInfo path)
        {
            path.AssertAbsolute();
            return DirectoryExistsImpl(path);
        }
        protected abstract bool DirectoryExistsImpl(PathInfo path);

        /// <inheritdoc />
        public void MoveDirectory(PathInfo srcPath, PathInfo destPath)
        {
            srcPath.AssertAbsolute(nameof(srcPath));
            destPath.AssertAbsolute(nameof(destPath));
            MoveDirectoryImpl(srcPath, destPath);
        }
        protected abstract void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath);

        /// <inheritdoc />
        public void DeleteDirectory(PathInfo path, bool isRecursive)
        {
            path.AssertAbsolute();
            DeleteDirectoryImpl(path, isRecursive);
        }
        protected abstract void DeleteDirectoryImpl(PathInfo path, bool isRecursive);

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        public void CopyFile(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            srcPath.AssertAbsolute(nameof(srcPath));
            destPath.AssertAbsolute(nameof(destPath));
            CopyFileImpl(srcPath, destPath, overwrite);
        }
        protected abstract void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite);

        /// <inheritdoc />
        public void ReplaceFile(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors)
        {
            srcPath.AssertAbsolute(nameof(srcPath));
            destPath.AssertAbsolute(nameof(destPath));
            destBackupPath.AssertAbsolute(nameof(destBackupPath));
            ReplaceFileImpl(srcPath, destPath, destBackupPath, ignoreMetadataErrors);
        }
        protected abstract void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors);

        /// <inheritdoc />
        public long GetFileLength(PathInfo path)
        {
            path.AssertAbsolute();
            return GetFileLengthImpl(path);
        }
        protected abstract long GetFileLengthImpl(PathInfo path);

        /// <inheritdoc />
        public bool FileExists(PathInfo path)
        {
            path.AssertAbsolute();
            return FileExistsImpl(path);
        }
        protected abstract bool FileExistsImpl(PathInfo path);

        /// <inheritdoc />
        public void MoveFile(PathInfo srcPath, PathInfo destPath)
        {
            srcPath.AssertAbsolute(nameof(srcPath));
            destPath.AssertAbsolute(nameof(destPath));
            MoveFileImpl(srcPath, destPath);
        }
        protected abstract void MoveFileImpl(PathInfo srcPath, PathInfo destPath);

        /// <inheritdoc />
        public void DeleteFile(PathInfo path)
        {
            path.AssertAbsolute();
            DeleteFileImpl(path);
        }
        protected abstract void DeleteFileImpl(PathInfo path);

        /// <inheritdoc />
        public Stream OpenFile(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            path.AssertAbsolute();
            return OpenFileImpl(path, mode, access, share);
        }
        protected abstract Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None);

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        public FileAttributes GetAttributes(PathInfo path)
        {
            path.AssertAbsolute();
            return GetAttributesImpl(path);
        }

        protected abstract FileAttributes GetAttributesImpl(PathInfo path);

        /// <inheritdoc />
        public void SetAttributes(PathInfo path, FileAttributes attributes)
        {
            path.AssertAbsolute();
            SetAttributesImpl(path, attributes);
        }
        protected abstract void SetAttributesImpl(PathInfo path, FileAttributes attributes);

        /// <inheritdoc />
        public DateTime GetCreationTime(PathInfo path)
        {
            path.AssertAbsolute();
            return GetCreationTimeImpl(path);
        }

        protected abstract DateTime GetCreationTimeImpl(PathInfo path);

        /// <inheritdoc />
        public void SetCreationTime(PathInfo path, DateTime time)
        {
            path.AssertAbsolute();
            SetCreationTimeImpl(path, time);
        }
        protected abstract void SetCreationTimeImpl(PathInfo path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastAccessTime(PathInfo path)
        {
            path.AssertAbsolute();
            return GetLastAccessTimeImpl(path);
        }
        protected abstract DateTime GetLastAccessTimeImpl(PathInfo path);

        /// <inheritdoc />
        public void SetLastAccessTime(PathInfo path, DateTime time)
        {
            path.AssertAbsolute();
            SetLastAccessTimeImpl(path, time);
        }
        protected abstract void SetLastAccessTimeImpl(PathInfo path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastWriteTime(PathInfo path)
        {
            path.AssertAbsolute();
            return GetLastWriteTimeImpl(path);
        }
        protected abstract DateTime GetLastWriteTimeImpl(PathInfo path);

        /// <inheritdoc />
        public void SetLastWriteTime(PathInfo path, DateTime time)
        {
            path.AssertAbsolute();
            SetLastWriteTimeImpl(path, time);
        }
        protected abstract void SetLastWriteTimeImpl(PathInfo path, DateTime time);

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        public IEnumerable<PathInfo> EnumeratePaths(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            path.AssertAbsolute();
            return EnumeratePathsImpl(path, searchPattern, searchOption, searchTarget);
        }

        protected abstract IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget);

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        public string ConvertToSystem(PathInfo path)
        {
            path.AssertAbsolute();
            return ConvertToSystemImpl(path);
        }
        protected abstract string ConvertToSystemImpl(PathInfo path);


        /// <inheritdoc />
        public PathInfo ConvertFromSystem(string systemPath)
        {
            if (systemPath == null) throw new ArgumentNullException(nameof(systemPath));
            return ConvertFromSystemImpl(systemPath);
        }
        protected abstract PathInfo ConvertFromSystemImpl(string systemPath);
    }
}