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
            CreateDirectoryImpl(ValidatePath(path));
        }
        protected abstract void CreateDirectoryImpl(PathInfo path);

        /// <inheritdoc />
        public bool DirectoryExists(PathInfo path)
        {
            return DirectoryExistsImpl(ValidatePath(path));
        }
        protected abstract bool DirectoryExistsImpl(PathInfo path);

        /// <inheritdoc />
        public void MoveDirectory(PathInfo srcPath, PathInfo destPath)
        {
            MoveDirectoryImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)));
        }
        protected abstract void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath);

        /// <inheritdoc />
        public void DeleteDirectory(PathInfo path, bool isRecursive)
        {
            DeleteDirectoryImpl(ValidatePath(path), isRecursive);
        }
        protected abstract void DeleteDirectoryImpl(PathInfo path, bool isRecursive);

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        public void CopyFile(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            CopyFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)), overwrite);
        }
        protected abstract void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite);

        /// <inheritdoc />
        public void ReplaceFile(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors)
        {
            ReplaceFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)), ValidatePath(destBackupPath, nameof(destBackupPath)), ignoreMetadataErrors);
        }
        protected abstract void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors);

        /// <inheritdoc />
        public long GetFileLength(PathInfo path)
        {
            return GetFileLengthImpl(ValidatePath(path));
        }
        protected abstract long GetFileLengthImpl(PathInfo path);

        /// <inheritdoc />
        public bool FileExists(PathInfo path)
        {
            return FileExistsImpl(ValidatePath(path));
        }
        protected abstract bool FileExistsImpl(PathInfo path);

        /// <inheritdoc />
        public void MoveFile(PathInfo srcPath, PathInfo destPath)
        {
            MoveFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)));
        }
        protected abstract void MoveFileImpl(PathInfo srcPath, PathInfo destPath);

        /// <inheritdoc />
        public void DeleteFile(PathInfo path)
        {
            DeleteFileImpl(ValidatePath(path));
        }
        protected abstract void DeleteFileImpl(PathInfo path);

        /// <inheritdoc />
        public Stream OpenFile(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            return OpenFileImpl(ValidatePath(path), mode, access, share);
        }
        protected abstract Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None);

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        public FileAttributes GetAttributes(PathInfo path)
        {
            return GetAttributesImpl(ValidatePath(path));
        }

        protected abstract FileAttributes GetAttributesImpl(PathInfo path);

        /// <inheritdoc />
        public void SetAttributes(PathInfo path, FileAttributes attributes)
        {
            SetAttributesImpl(ValidatePath(path), attributes);
        }
        protected abstract void SetAttributesImpl(PathInfo path, FileAttributes attributes);

        /// <inheritdoc />
        public DateTime GetCreationTime(PathInfo path)
        {
            return GetCreationTimeImpl(ValidatePath(path));
        }

        protected abstract DateTime GetCreationTimeImpl(PathInfo path);

        /// <inheritdoc />
        public void SetCreationTime(PathInfo path, DateTime time)
        {
            SetCreationTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetCreationTimeImpl(PathInfo path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastAccessTime(PathInfo path)
        {
            return GetLastAccessTimeImpl(ValidatePath(path));
        }
        protected abstract DateTime GetLastAccessTimeImpl(PathInfo path);

        /// <inheritdoc />
        public void SetLastAccessTime(PathInfo path, DateTime time)
        {
            SetLastAccessTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetLastAccessTimeImpl(PathInfo path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastWriteTime(PathInfo path)
        {
            return GetLastWriteTimeImpl(ValidatePath(path));
        }
        protected abstract DateTime GetLastWriteTimeImpl(PathInfo path);

        /// <inheritdoc />
        public void SetLastWriteTime(PathInfo path, DateTime time)
        {
            SetLastWriteTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetLastWriteTimeImpl(PathInfo path, DateTime time);

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        public IEnumerable<PathInfo> EnumeratePaths(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            return EnumeratePathsImpl(ValidatePath(path), searchPattern, searchOption, searchTarget);
        }

        protected abstract IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget);

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        public string ConvertToSystem(PathInfo path)
        {
            return ConvertToSystemImpl(ValidatePath(path));
        }
        protected abstract string ConvertToSystemImpl(PathInfo path);

        /// <inheritdoc />
        public PathInfo ConvertFromSystem(string systemPath)
        {
            if (systemPath == null) throw new ArgumentNullException(nameof(systemPath));
            return ValidatePath(ConvertFromSystemImpl(systemPath));
        }
        protected abstract PathInfo ConvertFromSystemImpl(string systemPath);

        protected virtual void ValidatePathImpl(PathInfo path, string name = "path")
        {
        }

        protected PathInfo ValidatePath(PathInfo path, string name = "path")
        {
            path.AssertAbsolute(name);
            ValidatePathImpl(path, name);
            return path;
        }
    }
}