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
    /// Abstract class for a <see cref="IFileSystem"/>. Provides default arguments safety checking and redirecting to safe implementation.
    /// Implements also the <see cref="IDisposable"/> pattern.
    /// </summary>
    public abstract class FileSystem : IFileSystem
    {
        /// <summary>
        /// The default file time if the file described in a path parameter does not exist.
        /// The default file time is 12:00 midnight, January 1, 1601 A.D. (C.E.) Coordinated Universal Time (UTC), adjusted to local time.
        /// </summary>
        public static readonly DateTime DefaultFileTime = new DateTime(1601, 01, 01, 0, 0, 0, DateTimeKind.Utc).ToLocalTime();


        /// <summary>
        /// Finalizes an instance of the <see cref="FileSystem"/> class.
        /// </summary>
        ~FileSystem()
        {
            DisposeInternal(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DisposeInternal(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// <c>true</c> if this instance if being disposed.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        protected bool IsDisposing { get; private set; }

        /// <summary>
        /// <c>true</c> if this instance if being disposed.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        protected bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets or sets a name associated with this filesystem.
        /// </summary>
        /// <remarks>
        /// This is can be used for debugging purpose or to identify different filesystems of a same kind.
        /// </remarks>
        public string? Name { get; set; }

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

        /// <summary>
        /// Implementation for <see cref="CreateDirectory"/>, paths is guaranteed to be absolute and not the root path `/`
        /// and validated through <see cref="ValidatePath"/>.
        /// Creates all directories and subdirectories in the specified path unless they already exist.
        /// </summary>
        /// <param name="path">The directory to create.</param>
        protected abstract void CreateDirectoryImpl(UPath path);

        /// <inheritdoc />
        public bool DirectoryExists(UPath path)
        {
            AssertNotDisposed();

            // With FileExists, case where a null path is allowed
            if (path.IsNull)
            {
                return false;
            }

            return DirectoryExistsImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="DirectoryExists"/>, paths is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Determines whether the given path refers to an existing directory on disk.
        /// </summary>
        /// <param name="path">The path to test.</param>
        /// <returns><c>true</c> if the given path refers to an existing directory on disk, <c>false</c> otherwise.</returns>
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

        /// <summary>
        /// Implementation for <see cref="MoveDirectory"/>, <paramref name="srcPath"/> and <paramref name="destPath"/>
        /// are guaranteed to be absolute, not equal and different from root `/`, and validated through <see cref="ValidatePath"/>.
        /// Moves a directory and its contents to a new location.
        /// </summary>
        /// <param name="srcPath">The path of the directory to move.</param>
        /// <param name="destPath">The path to the new location for <paramref name="srcPath"/></param>
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

        /// <summary>
        /// Implementation for <see cref="DeleteDirectory"/>, <paramref name="path"/> is guaranteed to be absolute and different from root path `/` and validated through <see cref="ValidatePath"/>.
        /// Deletes the specified directory and, if indicated, any subdirectories and files in the directory.
        /// </summary>
        /// <param name="path">The path of the directory to remove.</param>
        /// <param name="isRecursive"><c>true</c> to remove directories, subdirectories, and files in path; otherwise, <c>false</c>.</param>
        protected abstract void DeleteDirectoryImpl(UPath path, bool isRecursive);

        internal string DebuggerDisplayInternal()
        {
            return DebuggerDisplay();
        }

        internal string DebuggerKindName()
        {
            var typeName = this.GetType().Name.Replace("FileSystem", "fs").ToLowerInvariant();
            return Name != null ? $"{typeName}-{Name}" : typeName;
        }

        protected virtual string DebuggerDisplay() => DebuggerKindName();

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        public void CopyFile(UPath srcPath, UPath destPath, bool overwrite)
        {
            AssertNotDisposed();
            CopyFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)), overwrite);
        }

        /// <summary>
        /// Implementation for <see cref="CopyFile"/>, <paramref name="srcPath"/> and <paramref name="destPath"/>
        /// are guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Copies an existing file to a new file. Overwriting a file of the same name is allowed.
        /// </summary>
        /// <param name="srcPath">The path of the file to copy.</param>
        /// <param name="destPath">The path of the destination file. This cannot be a directory.</param>
        /// <param name="overwrite"><c>true</c> if the destination file can be overwritten; otherwise, <c>false</c>.</param>
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

        /// <summary>
        /// Implementation for <see cref="ReplaceFile"/>, <paramref name="srcPath"/>, <paramref name="destPath"/> and <paramref name="destBackupPath"/>
        /// are guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Replaces the contents of a specified file with the contents of another file, deleting the original file, and creating a backup of the replaced file and optionally ignores merge errors.
        /// </summary>
        /// <param name="srcPath">The path of a file that replaces the file specified by <paramref name="destPath"/>.</param>
        /// <param name="destPath">The path of the file being replaced.</param>
        /// <param name="destBackupPath">The path of the backup file (maybe null, in that case, it doesn't create any backup)</param>
        /// <param name="ignoreMetadataErrors"><c>true</c> to ignore merge errors (such as attributes and access control lists (ACLs)) from the replaced file to the replacement file; otherwise, <c>false</c>.</param>
        protected abstract void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors);

        /// <inheritdoc />
        public long GetFileLength(UPath path)
        {
            AssertNotDisposed();
            return GetFileLengthImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetFileLength"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Gets the size, in bytes, of a file.
        /// </summary>
        /// <param name="path">The path of a file.</param>
        /// <returns>The size, in bytes, of the file</returns>
        protected abstract long GetFileLengthImpl(UPath path);

        /// <inheritdoc />
        public bool FileExists(UPath path)
        {
            AssertNotDisposed();

            // Only case where a null path is allowed
            if (path.IsNull)
            {
                return false;
            }

            return FileExistsImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="FileExists"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Determines whether the specified file exists.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if the caller has the required permissions and path contains the name of an existing file;
        /// otherwise, <c>false</c>. This method also returns false if path is null, an invalid path, or a zero-length string.
        /// If the caller does not have sufficient permissions to read the specified file,
        /// no exception is thrown and the method returns false regardless of the existence of path.</returns>
        protected abstract bool FileExistsImpl(UPath path);

        /// <inheritdoc />
        public void MoveFile(UPath srcPath, UPath destPath)
        {
            AssertNotDisposed();
            MoveFileImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)));
        }

        /// <summary>
        /// Implementation for <see cref="CopyFile"/>, <paramref name="srcPath"/> and <paramref name="destPath"/>
        /// are guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Moves a specified file to a new location, providing the option to specify a new file name.
        /// </summary>
        /// <param name="srcPath">The path of the file to move.</param>
        /// <param name="destPath">The new path and name for the file.</param>
        protected abstract void MoveFileImpl(UPath srcPath, UPath destPath);

        /// <inheritdoc />
        public void DeleteFile(UPath path)
        {
            AssertNotDisposed();
            DeleteFileImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="FileExists"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Deletes the specified file.
        /// </summary>
        /// <param name="path">The path of the file to be deleted.</param>
        protected abstract void DeleteFileImpl(UPath path);

        /// <inheritdoc />
        public Stream OpenFile(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            AssertNotDisposed();
            return OpenFileImpl(ValidatePath(path), mode, access, share);
        }

        /// <summary>
        /// Implementation for <see cref="OpenFile"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Opens a file <see cref="Stream"/> on the specified path, having the specified mode with read, write, or read/write access and the specified sharing option.
        /// </summary>
        /// <param name="path">The path to the file to open.</param>
        /// <param name="mode">A <see cref="FileMode"/> value that specifies whether a file is created if one does not exist, and determines whether the contents of existing files are retained or overwritten.</param>
        /// <param name="access">A <see cref="FileAccess"/> value that specifies the operations that can be performed on the file.</param>
        /// <param name="share">A <see cref="FileShare"/> value specifying the type of access other threads have to the file.</param>
        /// <returns>A file <see cref="Stream"/> on the specified path, having the specified mode with read, write, or read/write access and the specified sharing option.</returns>
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

        /// <summary>
        /// Implementation for <see cref="GetAttributes"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Gets the <see cref="FileAttributes"/> of the file or directory on the path.
        /// </summary>
        /// <param name="path">The path to the file or directory.</param>
        /// <returns>The <see cref="FileAttributes"/> of the file or directory on the path.</returns>
        protected abstract FileAttributes GetAttributesImpl(UPath path);

        /// <inheritdoc />
        public void SetAttributes(UPath path, FileAttributes attributes)
        {
            AssertNotDisposed();
            SetAttributesImpl(ValidatePath(path), attributes);
        }

        /// <summary>
        /// Implementation for <see cref="SetAttributes"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Sets the specified <see cref="FileAttributes"/> of the file or directory on the specified path.
        /// </summary>
        /// <param name="path">The path to the file or directory.</param>
        /// <param name="attributes">A bitwise combination of the enumeration values.</param>
        protected abstract void SetAttributesImpl(UPath path, FileAttributes attributes);

        /// <inheritdoc />
        public DateTime GetCreationTime(UPath path)
        {
            AssertNotDisposed();
            return GetCreationTimeImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetCreationTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Returns the creation date and time of the specified file or directory.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to obtain creation date and time information.</param>
        /// <returns>A <see cref="DateTime"/> structure set to the creation date and time for the specified file or directory. This value is expressed in local time.</returns>
        protected abstract DateTime GetCreationTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetCreationTime(UPath path, DateTime time)
        {
            AssertNotDisposed();
            SetCreationTimeImpl(ValidatePath(path), time);
        }

        /// <summary>
        /// Implementation for <see cref="SetCreationTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Sets the date and time the file was created.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to set the creation date and time.</param>
        /// <param name="time">A <see cref="DateTime"/> containing the value to set for the creation date and time of path. This value is expressed in local time.</param>
        protected abstract void SetCreationTimeImpl(UPath path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastAccessTime(UPath path)
        {
            AssertNotDisposed();
            return GetLastAccessTimeImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetLastAccessTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Returns the last access date and time of the specified file or directory.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to obtain creation date and time information.</param>
        /// <returns>A <see cref="DateTime"/> structure set to the last access date and time for the specified file or directory. This value is expressed in local time.</returns>
        protected abstract DateTime GetLastAccessTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetLastAccessTime(UPath path, DateTime time)
        {
            AssertNotDisposed();
            SetLastAccessTimeImpl(ValidatePath(path), time);
        }

        /// <summary>
        /// Implementation for <see cref="SetLastAccessTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Sets the date and time the file was last accessed.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to set the last access date and time.</param>
        /// <param name="time">A <see cref="DateTime"/> containing the value to set for the last access date and time of path. This value is expressed in local time.</param>
        protected abstract void SetLastAccessTimeImpl(UPath path, DateTime time);

        /// <inheritdoc />
        public DateTime GetLastWriteTime(UPath path)
        {
            AssertNotDisposed();
            return GetLastWriteTimeImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetLastWriteTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Returns the last write date and time of the specified file or directory.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to obtain creation date and time information.</param>
        /// <returns>A <see cref="DateTime"/> structure set to the last write date and time for the specified file or directory. This value is expressed in local time.</returns>
        protected abstract DateTime GetLastWriteTimeImpl(UPath path);

        /// <inheritdoc />
        public void SetLastWriteTime(UPath path, DateTime time)
        {
            AssertNotDisposed();
            SetLastWriteTimeImpl(ValidatePath(path), time);
        }

        /// <summary>
        /// Implementation for <see cref="SetLastWriteTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Sets the date and time that the specified file was last written to.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to set the last write date and time.</param>
        /// <param name="time">A <see cref="DateTime"/> containing the value to set for the last write date and time of path. This value is expressed in local time.</param>
        protected abstract void SetLastWriteTimeImpl(UPath path, DateTime time);

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        public IEnumerable<UPath> EnumeratePaths(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            AssertNotDisposed();
            if (searchPattern is null) throw new ArgumentNullException(nameof(searchPattern));
            return EnumeratePathsImpl(ValidatePath(path), searchPattern, searchOption, searchTarget);
        }

        /// <summary>
        /// Implementation for <see cref="EnumeratePaths"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Returns an enumerable collection of file names and/or directory names that match a search pattern in a specified path, and optionally searches subdirectories.
        /// </summary>
        /// <param name="path">The path to the directory to search.</param>
        /// <param name="searchPattern">The search string to match against file-system entries in path. This parameter can contain a combination of valid literal path and wildcard (* and ?) characters (see Remarks), but doesn't support regular expressions.</param>
        /// <param name="searchOption">One of the enumeration values that specifies whether the search operation should include only the current directory or should include all subdirectories.</param>
        /// <param name="searchTarget">The search target either <see cref="SearchTarget.Both"/> or only <see cref="SearchTarget.Directory"/> or <see cref="SearchTarget.File"/>.</param>
        /// <returns>An enumerable collection of file-system paths in the directory specified by path and that match the specified search pattern, option and target.</returns>
        protected abstract IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget);

        // ----------------------------------------------
        // Watch API
        // ----------------------------------------------

        /// <inheritdoc />
        public bool CanWatch(UPath path)
        {
            AssertNotDisposed();
            return CanWatchImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="CanWatch"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Checks if the file system and <paramref name="path"/> can be watched with <see cref="Watch"/>.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the the path can be watched on this file system.</returns>
        protected virtual bool CanWatchImpl(UPath path)
        {
            return true;
        }

        /// <inheritdoc />
        public IFileSystemWatcher Watch(UPath path)
        {
            AssertNotDisposed();

            var validatedPath = ValidatePath(path);

            if (!CanWatchImpl(validatedPath))
            {
                throw new NotSupportedException($"The file system or path `{validatedPath}` does not support watching");
            }

            return WatchImpl(validatedPath);
        }

        /// <summary>
        /// Implementation for <see cref="Watch"/>, <paramref name="path"/> is guaranteed to be absolute and valudated through <see cref="ValidatePath"/>.
        /// Returns an <see cref="IFileSystemWatcher"/> instance that can be used to watch for changes to files and directories in the given path. The instance must be
        /// configured before events are raised.
        /// </summary>
        /// <param name="path">The path to watch for changes.</param>
        /// <returns>An <see cref="IFileSystemWatcher"/> instance that watches the given path.</returns>
        protected abstract IFileSystemWatcher WatchImpl(UPath path);

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        public string ConvertPathToInternal(UPath path)
        {
            AssertNotDisposed();
            return ConvertPathToInternalImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="ConvertPathToInternal"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Converts the specified path to the underlying path used by this <see cref="IFileSystem"/>. In case of a <see cref="Zio.FileSystems.PhysicalFileSystem"/>, it
        /// would represent the actual path on the disk.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The converted system path according to the specified path.</returns>
        protected abstract string ConvertPathToInternalImpl(UPath path);

        /// <inheritdoc />
        public UPath ConvertPathFromInternal(string systemPath)
        {
            AssertNotDisposed();
            if (systemPath is null) throw new ArgumentNullException(nameof(systemPath));
            return ValidatePath(ConvertPathFromInternalImpl(systemPath));
        }
        /// <summary>
        /// Implementation for <see cref="ConvertPathToInternal"/>, <paramref name="innerPath"/> is guaranteed to be not null and return path to be validated through <see cref="ValidatePath"/>.
        /// Converts the specified system path to a <see cref="IFileSystem"/> path.
        /// </summary>
        /// <param name="innerPath">The system path.</param>
        /// <returns>The converted path according to the system path.</returns>
        protected abstract UPath ConvertPathFromInternalImpl(string innerPath);

        /// <summary>
        /// User overridable implementation for <see cref="ValidatePath"/> to validate the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="name">The name.</param>
        /// <returns>The path validated</returns>
        /// <exception cref="System.NotSupportedException">The path cannot contain the `:` character</exception>
        protected virtual UPath ValidatePathImpl(UPath path, string name = "path")
        {
            if (path.FullName.IndexOf(':') >= 0)
            {
                throw new NotSupportedException($"The path `{path}` cannot contain the `:` character");
            }
            return path;
        }

        /// <summary>
        /// Validates the specified path (Check that it is absolute by default)
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="name">The name.</param>
        /// <param name="allowNull">if set to <c>true</c> the path is allowed to be null. <c>false</c> otherwise.</param>
        /// <returns>The path validated</returns>
        protected UPath ValidatePath(UPath path, string name = "path", bool allowNull = false)
        {
            if (allowNull && path.IsNull)
            {
                return path;
            }
            path.AssertAbsolute(name);

            return ValidatePathImpl(path, name);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
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
            if (!IsDisposed)
            {
                AssertNotDisposed();
                IsDisposing = true;
                Dispose(disposing);
                IsDisposed = true;
            }
        }
    }
}