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
        // For GetCreationTime...etc. If the file described in a path parameter does not exist
        // the default file time is 12:00 midnight, January 1, 1601 A.D. (C.E.) Coordinated Universal Time (UTC), adjusted to local time.
        public static readonly DateTime DefaultFileTime = new DateTime(1601, 01, 01, 0, 0, 0, DateTimeKind.Utc).ToLocalTime();

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        public void CreateDirectory(UPath path)
        {
            if (path == UPath.Root)
            {
                throw new UnauthorizedAccessException("Cannot create root directory `/`");
            }

            CreateDirectoryImpl(ValidatePath(path));
        }
        protected abstract void CreateDirectoryImpl(UPath path);

        /// <inheritdoc />
        public bool DirectoryExists(UPath path)
        {
            return DirectoryExistsImpl(ValidatePath(path));
        }
        protected abstract bool DirectoryExistsImpl(UPath path);

        /// <inheritdoc />
        public void MoveDirectory(UPath srcPath, UPath destPath)
        {
            if (srcPath == UPath.Root)
            {
                throw new UnauthorizedAccessException("Cannot move from the source root directory `/`");
            }
            if (destPath == UPath.Root)
            {
                throw new UnauthorizedAccessException("Cannot move to the root directory `/`");
            }

            if (srcPath == destPath)
            {
                throw new IOException($"The source and destination path are the same `{srcPath}`");
            }

            MoveDirectoryImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)));
        }
        protected abstract void MoveDirectoryImpl(UPath srcPath, UPath destPath);

        /// <inheritdoc />
        public void DeleteDirectory(UPath path, bool isRecursive)
        {
            if (path == UPath.Root)
            {
                throw new UnauthorizedAccessException("Cannot delete root directory `/`");
            }

            DeleteDirectoryImpl(ValidatePath(path), isRecursive);
        }
        protected abstract void DeleteDirectoryImpl(UPath path, bool isRecursive);

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        public void CopyFile(UPath srcPath, UPath destPath, bool overwrite)
        {
            CopyFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)), overwrite);
        }
        protected abstract void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite);

        /// <inheritdoc />
        public void ReplaceFile(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            srcPath = ValidatePath(srcPath, nameof(srcPath));
            destPath = ValidatePath(destPath, nameof(destPath));
            destBackupPath = ValidatePath(destBackupPath, nameof(destBackupPath), true);

            if (!FileExistsImpl(srcPath))
            {
                throw new FileNotFoundException($"Unable to find the source file `{srcPath}`.");
            }

            if (!FileExistsImpl(destPath))
            {
                throw new FileNotFoundException($"Unable to find the source file `{srcPath}`.");
            }

            if (destBackupPath == srcPath)
            {
                throw new IOException($"The source and backup cannot have the same path `{srcPath}`");
            }

            ReplaceFileImpl(srcPath, destPath, destBackupPath, ignoreMetadataErrors);
        }
        protected abstract void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors);

        /// <inheritdoc />
        public long GetFileLength(UPath path)
        {
            return GetFileLengthImpl(ValidatePath(path));
        }
        protected abstract long GetFileLengthImpl(UPath path);

        /// <inheritdoc />
        public bool FileExists(UPath path)
        {
            return FileExistsImpl(ValidatePath(path));
        }
        protected abstract bool FileExistsImpl(UPath path);

        /// <inheritdoc />
        public void MoveFile(UPath srcPath, UPath destPath)
        {
            MoveFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)));
        }
        protected abstract void MoveFileImpl(UPath srcPath, UPath destPath);

        /// <inheritdoc />
        public void DeleteFile(UPath path)
        {
            DeleteFileImpl(ValidatePath(path));
        }
        protected abstract void DeleteFileImpl(UPath path);

        /// <inheritdoc />
        public Stream OpenFile(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            return OpenFileImpl(ValidatePath(path), mode, access, share);
        }
        protected abstract Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share);

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        public FileAttributes GetAttributes(UPath path)
        {
            return GetAttributesImpl(ValidatePath(path));
        }

        protected abstract FileAttributes GetAttributesImpl(UPath path);

        /// <inheritdoc />
        public void SetAttributes(UPath path, FileAttributes attributes)
        {
            SetAttributesImpl(ValidatePath(path), attributes);
        }
        protected abstract void SetAttributesImpl(UPath path, FileAttributes attributes);

        /// <inheritdoc />
        public DateTime GetCreationTime(UPath path)
        {
            return GetCreationTimeImpl(ValidatePath(path));
        }

        protected abstract DateTime GetCreationTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetCreationTime(UPath path, DateTime time)
        {
            SetCreationTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetCreationTimeImpl(UPath path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastAccessTime(UPath path)
        {
            return GetLastAccessTimeImpl(ValidatePath(path));
        }
        protected abstract DateTime GetLastAccessTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetLastAccessTime(UPath path, DateTime time)
        {
            SetLastAccessTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetLastAccessTimeImpl(UPath path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastWriteTime(UPath path)
        {
            return GetLastWriteTimeImpl(ValidatePath(path));
        }
        protected abstract DateTime GetLastWriteTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetLastWriteTime(UPath path, DateTime time)
        {
            SetLastWriteTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetLastWriteTimeImpl(UPath path, DateTime time);

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        public IEnumerable<UPath> EnumeratePaths(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            return EnumeratePathsImpl(ValidatePath(path), searchPattern, searchOption, searchTarget);
        }

        protected abstract IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget);

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        public string ConvertToSystem(UPath path)
        {
            return ConvertToSystemImpl(ValidatePath(path));
        }
        protected abstract string ConvertToSystemImpl(UPath path);

        /// <inheritdoc />
        public UPath ConvertFromSystem(string systemPath)
        {
            if (systemPath == null) throw new ArgumentNullException(nameof(systemPath));
            return ValidatePath(ConvertFromSystemImpl(systemPath));
        }
        protected abstract UPath ConvertFromSystemImpl(string systemPath);

        protected virtual void ValidatePathImpl(UPath path, string name = "path")
        {
        }

        protected UPath ValidatePath(UPath path, string name = "path", bool allowNull = false)
        {
            if (allowNull && path.IsNull)
            {
                return path;
            }
            path.AssertAbsolute(name);
            ValidatePathImpl(path, name);
            return path;
        }
    }
}