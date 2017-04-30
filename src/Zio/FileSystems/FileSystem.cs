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
    /// Abstract class for a <see cref="IFileSystem"/>. Provides default arguments safety checking and redirecting to safe implementation.
    /// Implements also the <see cref="IDisposable"/> pattern.
    /// </summary>
    public abstract class FileSystem : IFileSystem
    {
        // For GetCreationTime...etc. If the file described in a path parameter does not exist
        // the default file time is 12:00 midnight, January 1, 1601 A.D. (C.E.) Coordinated Universal Time (UTC), adjusted to local time.
        public static readonly DateTime DefaultFileTime = new DateTime(1601, 01, 01, 0, 0, 0, DateTimeKind.Utc).ToLocalTime();

        ~FileSystem()
        {
            DisposeInternal(false);
        }

        public void Dispose()
        {
            DisposeInternal(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// <c>true</c> if this instance if being disposed.
        /// </summary>
        protected bool IsDisposing { get; private set; }

        /// <summary>
        /// <c>true</c> if this instance if being disposed.
        /// </summary>
        protected bool IsDisposed { get; private set; }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        public void CreateDirectory(UPath path)
        {
            AssertNotDisposed();
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
            AssertNotDisposed();
            return DirectoryExistsImpl(ValidatePath(path));
        }
        protected abstract bool DirectoryExistsImpl(UPath path);

        /// <inheritdoc />
        public void MoveDirectory(UPath srcPath, UPath destPath)
        {
            AssertNotDisposed();
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
            AssertNotDisposed();
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
            AssertNotDisposed();
            CopyFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)), overwrite);
        }
        protected abstract void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite);

        /// <inheritdoc />
        public void ReplaceFile(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            AssertNotDisposed();
            srcPath = ValidatePath(srcPath, nameof(srcPath));
            destPath = ValidatePath(destPath, nameof(destPath));
            destBackupPath = ValidatePath(destBackupPath, nameof(destBackupPath), true);

            if (!FileExistsImpl(srcPath))
            {
                throw NewFileNotFoundException(srcPath);
            }

            if (!FileExistsImpl(destPath))
            {
                throw NewFileNotFoundException(srcPath);
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
            AssertNotDisposed();
            return GetFileLengthImpl(ValidatePath(path));
        }
        protected abstract long GetFileLengthImpl(UPath path);

        /// <inheritdoc />
        public bool FileExists(UPath path)
        {
            // Only case where a null path is allowed
            if (path.IsNull)
            {
                return false;
            }

            AssertNotDisposed();
            return FileExistsImpl(ValidatePath(path));
        }
        protected abstract bool FileExistsImpl(UPath path);

        /// <inheritdoc />
        public void MoveFile(UPath srcPath, UPath destPath)
        {
            AssertNotDisposed();
            MoveFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)));
        }
        protected abstract void MoveFileImpl(UPath srcPath, UPath destPath);

        /// <inheritdoc />
        public void DeleteFile(UPath path)
        {
            AssertNotDisposed();
            DeleteFileImpl(ValidatePath(path));
        }
        protected abstract void DeleteFileImpl(UPath path);

        /// <inheritdoc />
        public Stream OpenFile(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            AssertNotDisposed();
            return OpenFileImpl(ValidatePath(path), mode, access, share);
        }
        protected abstract Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share);

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        public FileAttributes GetAttributes(UPath path)
        {
            AssertNotDisposed();
            return GetAttributesImpl(ValidatePath(path));
        }

        protected abstract FileAttributes GetAttributesImpl(UPath path);

        /// <inheritdoc />
        public void SetAttributes(UPath path, FileAttributes attributes)
        {
            AssertNotDisposed();
            SetAttributesImpl(ValidatePath(path), attributes);
        }
        protected abstract void SetAttributesImpl(UPath path, FileAttributes attributes);

        /// <inheritdoc />
        public DateTime GetCreationTime(UPath path)
        {
            AssertNotDisposed();
            return GetCreationTimeImpl(ValidatePath(path));
        }

        protected abstract DateTime GetCreationTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetCreationTime(UPath path, DateTime time)
        {
            AssertNotDisposed();
            SetCreationTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetCreationTimeImpl(UPath path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastAccessTime(UPath path)
        {
            AssertNotDisposed();
            return GetLastAccessTimeImpl(ValidatePath(path));
        }
        protected abstract DateTime GetLastAccessTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetLastAccessTime(UPath path, DateTime time)
        {
            AssertNotDisposed();
            SetLastAccessTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetLastAccessTimeImpl(UPath path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastWriteTime(UPath path)
        {
            AssertNotDisposed();
            return GetLastWriteTimeImpl(ValidatePath(path));
        }
        protected abstract DateTime GetLastWriteTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetLastWriteTime(UPath path, DateTime time)
        {
            AssertNotDisposed();
            SetLastWriteTimeImpl(ValidatePath(path), time);
        }
        protected abstract void SetLastWriteTimeImpl(UPath path, DateTime time);

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        public IEnumerable<UPath> EnumeratePaths(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            AssertNotDisposed();
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            return EnumeratePathsImpl(ValidatePath(path), searchPattern, searchOption, searchTarget);
        }

        protected abstract IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget);

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        public string ConvertPathToInner(UPath path)
        {
            AssertNotDisposed();
            return ConvertPathToInnerImpl(ValidatePath(path));
        }
        protected abstract string ConvertPathToInnerImpl(UPath path);

        /// <inheritdoc />
        public UPath ConvertPathFromInner(string systemPath)
        {
            AssertNotDisposed();
            if (systemPath == null) throw new ArgumentNullException(nameof(systemPath));
            return ValidatePath(ConvertPathFromInnerImpl(systemPath));
        }
        protected abstract UPath ConvertPathFromInnerImpl(string systemPath);

        protected virtual void ValidatePathImpl(UPath path, string name = "path")
        {
            if (path.FullName.IndexOf(':') >= 0)
            {
                throw new NotSupportedException($"The path `{path}` cannot contain the `:` character");
            }
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

        protected virtual void Dispose(bool disposing)
        {
        }

        private void AssertNotDisposed()
        {
            if (IsDisposing || IsDisposed)
            {
                throw new ObjectDisposedException($"This instance `{GetType()}` is already disposed.");
            }
        }

        private void DisposeInternal(bool disposing)
        {
            AssertNotDisposed();
            IsDisposing = true;
            Dispose(disposing);
            IsDisposed = true;
        }
    }
}