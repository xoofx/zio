#if NET6_0_OR_GREATER
// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static Zio.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// Abstract class for a <see cref="IAsyncFileSystem"/>. Provides default arguments safety checking and redirecting to safe implementation.
    /// Implements also the <see cref="IDisposable"/> pattern.
    /// </summary>
    public abstract partial class FileSystem : IAsyncFileSystem
    {
        /// <inheritdoc />
        public ValueTask CreateDirectoryAsync(UPath path)
        {
            AssertNotDisposed();
            if (path == UPath.Root) return ValueTask.CompletedTask; // nop
            return CreateDirectoryAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="CreateDirectoryAsync"/>, paths is guaranteed to be absolute and not the root path `/`
        /// and validated through <see cref="ValidatePath"/>.
        /// Creates all directories and subdirectories in the specified path unless they already exist.
        /// </summary>
        /// <param name="path">The directory to create.</param>
        protected virtual ValueTask CreateDirectoryAsyncImpl(UPath path)
        {
            CreateDirectoryImpl(path);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<bool> DirectoryExistsAsync(UPath path)
        {
            AssertNotDisposed();

            // With FileExists, case where a null path is allowed
            if (path.IsNull)
            {
                return new ValueTask<bool>(true);
            }

            return DirectoryExistsAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="DirectoryExistsAsync"/>, paths is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Determines whether the given path refers to an existing directory on disk.
        /// </summary>
        /// <param name="path">The path to test.</param>
        /// <returns><c>true</c> if the given path refers to an existing directory on disk, <c>false</c> otherwise.</returns>
        protected virtual ValueTask<bool> DirectoryExistsAsyncImpl(UPath path)
        {
            return new ValueTask<bool>(DirectoryExistsImpl(path));
        }
        
        /// <inheritdoc />
        public ValueTask MoveDirectoryAsync(UPath srcPath, UPath destPath)
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

            return MoveDirectoryAsyncImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)));
        }

        /// <summary>
        /// Implementation for <see cref="MoveDirectory"/>, <paramref name="srcPath"/> and <paramref name="destPath"/>
        /// are guaranteed to be absolute, not equal and different from root `/`, and validated through <see cref="ValidatePath"/>.
        /// Moves a directory and its contents to a new location.
        /// </summary>
        /// <param name="srcPath">The path of the directory to move.</param>
        /// <param name="destPath">The path to the new location for <paramref name="srcPath"/></param>
        protected virtual ValueTask MoveDirectoryAsyncImpl(UPath srcPath, UPath destPath)
        {
            MoveDirectoryImpl(srcPath, destPath);
            return ValueTask.CompletedTask;
        }
        
        /// <inheritdoc />
        public ValueTask DeleteDirectoryAsync(UPath path, bool isRecursive)
        {
            AssertNotDisposed();
            if (path == UPath.Root)
            {
                throw new UnauthorizedAccessException("Cannot delete root directory `/`");
            }

            return DeleteDirectoryAsyncImpl(ValidatePath(path), isRecursive);
        }

        /// <summary>
        /// Implementation for <see cref="DeleteDirectoryAsync"/>, <paramref name="path"/> is guaranteed to be absolute and different from root path `/` and validated through <see cref="ValidatePath"/>.
        /// Deletes the specified directory and, if indicated, any subdirectories and files in the directory.
        /// </summary>
        /// <param name="path">The path of the directory to remove.</param>
        /// <param name="isRecursive"><c>true</c> to remove directories, subdirectories, and files in path; otherwise, <c>false</c>.</param>
        protected virtual ValueTask DeleteDirectoryAsyncImpl(UPath path, bool isRecursive)
        {
            DeleteDirectoryImpl(path, isRecursive);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask CopyFileAsync(UPath srcPath, UPath destPath, bool overwrite)
        {
            AssertNotDisposed();
            return CopyFileAsyncImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)), overwrite);
        }

        /// <summary>
        /// Implementation for <see cref="CopyFileAsync"/>, <paramref name="srcPath"/> and <paramref name="destPath"/>
        /// are guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Copies an existing file to a new file. Overwriting a file of the same name is allowed.
        /// </summary>
        /// <param name="srcPath">The path of the file to copy.</param>
        /// <param name="destPath">The path of the destination file. This cannot be a directory.</param>
        /// <param name="overwrite"><c>true</c> if the destination file can be overwritten; otherwise, <c>false</c>.</param>
        protected virtual ValueTask CopyFileAsyncImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            CopyFileImpl(srcPath, destPath, overwrite);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask ReplaceFileAsync(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
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

            return ReplaceFileAsyncImpl(srcPath, destPath, destBackupPath, ignoreMetadataErrors);
        }

        /// <summary>
        /// Implementation for <see cref="ReplaceFileAsync"/>, <paramref name="srcPath"/>, <paramref name="destPath"/> and <paramref name="destBackupPath"/>
        /// are guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Replaces the contents of a specified file with the contents of another file, deleting the original file, and creating a backup of the replaced file and optionally ignores merge errors.
        /// </summary>
        /// <param name="srcPath">The path of a file that replaces the file specified by <paramref name="destPath"/>.</param>
        /// <param name="destPath">The path of the file being replaced.</param>
        /// <param name="destBackupPath">The path of the backup file (maybe null, in that case, it doesn't create any backup)</param>
        /// <param name="ignoreMetadataErrors"><c>true</c> to ignore merge errors (such as attributes and access control lists (ACLs)) from the replaced file to the replacement file; otherwise, <c>false</c>.</param>
        protected virtual ValueTask ReplaceFileAsyncImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            ReplaceFileImpl(srcPath, destPath, destBackupPath, ignoreMetadataErrors);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<long> GetFileLengthAsync(UPath path)
        {
            AssertNotDisposed();
            return GetFileLengthAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetFileLengthAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Gets the size, in bytes, of a file.
        /// </summary>
        /// <param name="path">The path of a file.</param>
        /// <returns>The size, in bytes, of the file</returns>
        protected virtual ValueTask<long> GetFileLengthAsyncImpl(UPath path)
        {
            return new ValueTask<long>(GetFileLengthImpl(path));
        }

        /// <inheritdoc />
        public ValueTask<bool> FileExistsAsync(UPath path)
        {
            AssertNotDisposed();

            // Only case where a null path is allowed
            if (path.IsNull)
            {
                return new ValueTask<bool>(false);
            }

            return FileExistsAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="FileExistsAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Determines whether the specified file exists.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if the caller has the required permissions and path contains the name of an existing file;
        /// otherwise, <c>false</c>. This method also returns false if path is null, an invalid path, or a zero-length string.
        /// If the caller does not have sufficient permissions to read the specified file,
        /// no exception is thrown and the method returns false regardless of the existence of path.</returns>
        protected virtual ValueTask<bool> FileExistsAsyncImpl(UPath path)
        {
            return new ValueTask<bool>(FileExistsImpl(path));
        }

        /// <inheritdoc />
        public ValueTask MoveFileAsync(UPath srcPath, UPath destPath)
        {
            AssertNotDisposed();
            return MoveFileAsyncImpl(ValidatePath(srcPath, nameof(srcPath)), ValidatePath(destPath, nameof(destPath)));
        }

        /// <summary>
        /// Implementation for <see cref="MoveFileAsync"/>, <paramref name="srcPath"/> and <paramref name="destPath"/>
        /// are guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Moves a specified file to a new location, providing the option to specify a new file name.
        /// </summary>
        /// <param name="srcPath">The path of the file to move.</param>
        /// <param name="destPath">The new path and name for the file.</param>
        protected virtual ValueTask MoveFileAsyncImpl(UPath srcPath, UPath destPath)
        {
            MoveFileImpl(srcPath, destPath);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DeleteFileAsync(UPath path)
        {
            AssertNotDisposed();
            return DeleteFileAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="DeleteFileAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Deletes the specified file.
        /// </summary>
        /// <param name="path">The path of the file to be deleted.</param>
        protected virtual ValueTask DeleteFileAsyncImpl(UPath path)
        {
            DeleteFileImpl(path);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<Stream> OpenFileAsync(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            AssertNotDisposed();
            return OpenFileAsyncImpl(ValidatePath(path), mode, access, share);
        }

        /// <summary>
        /// Implementation for <see cref="OpenFileAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Opens a file <see cref="Stream"/> on the specified path, having the specified mode with read, write, or read/write access and the specified sharing option.
        /// </summary>
        /// <param name="path">The path to the file to open.</param>
        /// <param name="mode">A <see cref="FileMode"/> value that specifies whether a file is created if one does not exist, and determines whether the contents of existing files are retained or overwritten.</param>
        /// <param name="access">A <see cref="FileAccess"/> value that specifies the operations that can be performed on the file.</param>
        /// <param name="share">A <see cref="FileShare"/> value specifying the type of access other threads have to the file.</param>
        /// <returns>A file <see cref="Stream"/> on the specified path, having the specified mode with read, write, or read/write access and the specified sharing option.</returns>
        protected virtual ValueTask<Stream> OpenFileAsyncImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
        {
            return new ValueTask<Stream>(OpenFile(path, mode, access, share));
        }

        /// <inheritdoc />
        public ValueTask<FileAttributes> GetAttributesAsync(UPath path)
        {
            AssertNotDisposed();
            return GetAttributesAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetAttributesAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Gets the <see cref="FileAttributes"/> of the file or directory on the path.
        /// </summary>
        /// <param name="path">The path to the file or directory.</param>
        /// <returns>The <see cref="FileAttributes"/> of the file or directory on the path.</returns>
        protected virtual ValueTask<FileAttributes> GetAttributesAsyncImpl(UPath path)
        {
            return new ValueTask<FileAttributes>(GetAttributesImpl(path));
        }

        /// <inheritdoc />
        public ValueTask SetAttributesAsync(UPath path, FileAttributes attributes)
        {
            AssertNotDisposed();
            return SetAttributesAsyncImpl(ValidatePath(path), attributes);
        }

        /// <summary>
        /// Implementation for <see cref="SetAttributesAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Sets the specified <see cref="FileAttributes"/> of the file or directory on the specified path.
        /// </summary>
        /// <param name="path">The path to the file or directory.</param>
        /// <param name="attributes">A bitwise combination of the enumeration values.</param>
        protected virtual ValueTask SetAttributesAsyncImpl(UPath path, FileAttributes attributes)
        {
            SetAttributesImpl(path, attributes);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<DateTime> GetCreationTimeAsync(UPath path)
        {
            AssertNotDisposed();
            return GetCreationTimeAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetCreationTimeAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Returns the creation date and time of the specified file or directory.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to obtain creation date and time information.</param>
        /// <returns>A <see cref="DateTime"/> structure set to the creation date and time for the specified file or directory. This value is expressed in local time.</returns>
        protected virtual ValueTask<DateTime> GetCreationTimeAsyncImpl(UPath path)
        {
            return new ValueTask<DateTime>(GetCreationTimeImpl(path));
        }

        /// <inheritdoc />
        public ValueTask SetCreationTimeAsync(UPath path, DateTime time)
        {
            AssertNotDisposed();
            return SetCreationTimeAsyncImpl(ValidatePath(path), time);
        }

        /// <summary>
        /// Implementation for <see cref="SetCreationTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Sets the date and time the file was created.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to set the creation date and time.</param>
        /// <param name="time">A <see cref="DateTime"/> containing the value to set for the creation date and time of path. This value is expressed in local time.</param>
        protected virtual ValueTask SetCreationTimeAsyncImpl(UPath path, DateTime time)
        {
            SetCreationTimeImpl(path, time);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<DateTime> GetLastAccessTimeAsync(UPath path)
        {
            AssertNotDisposed();
            return GetLastAccessTimeAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetLastAccessTimeAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Returns the last access date and time of the specified file or directory.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to obtain creation date and time information.</param>
        /// <returns>A <see cref="DateTime"/> structure set to the last access date and time for the specified file or directory. This value is expressed in local time.</returns>
        protected virtual ValueTask<DateTime> GetLastAccessTimeAsyncImpl(UPath path)
        {
            return new ValueTask<DateTime>(GetLastAccessTimeImpl(path));
        }

        /// <inheritdoc />
        public ValueTask SetLastAccessTimeAsync(UPath path, DateTime time)
        {
            AssertNotDisposed();
            return SetLastAccessTimeAsyncImpl(ValidatePath(path), time);
        }

        /// <summary>
        /// Implementation for <see cref="SetLastAccessTimeAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Sets the date and time the file was last accessed.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to set the last access date and time.</param>
        /// <param name="time">A <see cref="DateTime"/> containing the value to set for the last access date and time of path. This value is expressed in local time.</param>
        protected virtual ValueTask SetLastAccessTimeAsyncImpl(UPath path, DateTime time)
        {
            SetLastAccessTimeImpl(path, time);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<DateTime> GetLastWriteTimeAsync(UPath path)
        {
            AssertNotDisposed();
            return GetLastWriteTimeAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="GetLastWriteTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Returns the last write date and time of the specified file or directory.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to obtain creation date and time information.</param>
        /// <returns>A <see cref="DateTime"/> structure set to the last write date and time for the specified file or directory. This value is expressed in local time.</returns>
        protected virtual ValueTask<DateTime> GetLastWriteTimeAsyncImpl(UPath path)
        {
            return new ValueTask<DateTime>(GetLastWriteTimeImpl(path));
        }

        /// <inheritdoc />
        public ValueTask SetLastWriteTimeAsync(UPath path, DateTime time)
        {
            AssertNotDisposed();
            return SetLastWriteTimeAsyncImpl(ValidatePath(path), time);
        }

        /// <summary>
        /// Implementation for <see cref="SetLastWriteTime"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Sets the date and time that the specified file was last written to.
        /// </summary>
        /// <param name="path">The path to a file or directory for which to set the last write date and time.</param>
        /// <param name="time">A <see cref="DateTime"/> containing the value to set for the last write date and time of path. This value is expressed in local time.</param>
        protected virtual ValueTask SetLastWriteTimeAsyncImpl(UPath path, DateTime time)
        {
            SetLastWriteTimeImpl(path, time);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public IAsyncEnumerable<UPath> EnumeratePathsAsync(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            AssertNotDisposed();
            if (searchPattern is null) throw new ArgumentNullException(nameof(searchPattern));
            return EnumeratePathsAsyncImpl(ValidatePath(path), searchPattern, searchOption, searchTarget);
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
        protected virtual async IAsyncEnumerable<UPath> EnumeratePathsAsyncImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            foreach (var item in EnumeratePathsImpl(path, searchPattern, searchOption, searchTarget))
            {
                yield return item;
            }
        }

        /// <inheritdoc />
        public IAsyncEnumerable<FileSystemItem> EnumerateItemsAsync(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate = null)
        {
            AssertNotDisposed();
            return EnumerateItemsAsyncImpl(ValidatePath(path), searchOption, searchPredicate);
        }

        /// <summary>
        /// Implementation for <see cref="EnumeratePaths"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Returns an enumerable collection of <see cref="FileSystemItem"/> that match a search pattern in a specified path, and optionally searches subdirectories.
        /// </summary>
        /// <param name="path">The path to the directory to search.</param>
        /// <param name="searchOption">One of the enumeration values that specifies whether the search operation should include only the current directory or should include all subdirectories.</param>
        /// <param name="searchPredicate">The search string to match against file-system entries in path. This parameter can contain a combination of valid literal path and wildcard (* and ?) characters (see Remarks), but doesn't support regular expressions.</param>
        /// <returns>An enumerable collection of <see cref="FileSystemItem"/> in the directory specified by path and that match the specified search pattern, option and target.</returns>
        protected virtual async IAsyncEnumerable<FileSystemItem> EnumerateItemsAsyncImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
        {
            foreach (var item in EnumerateItemsImpl(path, searchOption, searchPredicate))
            {
                yield return item;
            }
        }

        /// <inheritdoc />
        public ValueTask<bool> CanWatchAsync(UPath path)
        {
            AssertNotDisposed();
            return CanWatchAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="CanWatchAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Checks if the file system and <paramref name="path"/> can be watched with <see cref="Watch"/>.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the the path can be watched on this file system.</returns>
        protected virtual ValueTask<bool> CanWatchAsyncImpl(UPath path)
        {
            return new ValueTask<bool>(CanWatchImpl(path));
        }

        /// <inheritdoc />
        public ValueTask<IFileSystemWatcher> WatchAsync(UPath path)
        {
            AssertNotDisposed();

            var validatedPath = ValidatePath(path);

            if (!CanWatchImpl(validatedPath))
            {
                throw new NotSupportedException($"The file system or path `{validatedPath}` does not support watching");
            }

            return WatchAsyncImpl(validatedPath);
        }

        /// <summary>
        /// Implementation for <see cref="Watch"/>, <paramref name="path"/> is guaranteed to be absolute and valudated through <see cref="ValidatePath"/>.
        /// Returns an <see cref="IFileSystemWatcher"/> instance that can be used to watch for changes to files and directories in the given path. The instance must be
        /// configured before events are raised.
        /// </summary>
        /// <param name="path">The path to watch for changes.</param>
        /// <returns>An <see cref="IFileSystemWatcher"/> instance that watches the given path.</returns>
        protected virtual ValueTask<IFileSystemWatcher> WatchAsyncImpl(UPath path)
        {
            return new ValueTask<IFileSystemWatcher>(WatchImpl(path));
        }

        /// <inheritdoc />
        public ValueTask<string> ConvertPathToInternalAsync(UPath path)
        {
            AssertNotDisposed();
            return ConvertPathToInternalAsyncImpl(ValidatePath(path));
        }

        /// <summary>
        /// Implementation for <see cref="ConvertPathToInternalAsync"/>, <paramref name="path"/> is guaranteed to be absolute and validated through <see cref="ValidatePath"/>.
        /// Converts the specified path to the underlying path used by this <see cref="IAsyncFileSystem"/>. In case of a <see cref="Zio.FileSystems.PhysicalFileSystem"/>, it
        /// would represent the actual path on the disk.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The converted system path according to the specified path.</returns>
        protected virtual ValueTask<string> ConvertPathToInternalAsyncImpl(UPath path)
        {
            return new ValueTask<string>(ConvertPathToInternalImpl(path));
        }

        /// <inheritdoc />
        public async ValueTask<UPath> ConvertPathFromInternalAsync(string systemPath)
        {
            AssertNotDisposed();
            if (systemPath is null) throw new ArgumentNullException(nameof(systemPath));
            return ValidatePath(await ConvertPathFromInternalAsyncImpl(systemPath));
        }

        /// <summary>
        /// Implementation for <see cref="ConvertPathToInternal"/>, <paramref name="innerPath"/> is guaranteed to be not null and return path to be validated through <see cref="ValidatePath"/>.
        /// Converts the specified system path to a <see cref="IFileSystem"/> path.
        /// </summary>
        /// <param name="innerPath">The system path.</param>
        /// <returns>The converted path according to the system path.</returns>
        protected virtual ValueTask<UPath> ConvertPathFromInternalAsyncImpl(string innerPath)
        {
            return new ValueTask<UPath>(ConvertPathFromInternalImpl(innerPath));
        }
    }
}
#endif