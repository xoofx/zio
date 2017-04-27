// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a base FileSystem for delegating to another FileSystem while allowing specific overrides.
    /// </summary>
    public abstract class DelegateFileSystem : FileSystemBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DelegateFileSystem"/> class.
        /// </summary>
        /// <param name="fileSystem">The delegated file system.</param>
        protected DelegateFileSystem(IFileSystem fileSystem)
        {
            NextFileSystem = fileSystem;
        }

        /// <summary>
        /// Gets the next delegated file system (may be null).
        /// </summary>
        protected IFileSystem NextFileSystem { get; }

        /// <summary>
        /// Gets the next delegated file system.
        /// </summary>
        protected IFileSystem NextFileSystemSafe
        {
            get
            {
                if (NextFileSystem == null)
                {
                    throw new InvalidOperationException("The delegate filesystem for this instance is null");
                }
                return NextFileSystem;
            }
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override void CreateDirectoryImpl(UPath path)
        {
            NextFileSystemSafe.CreateDirectory(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override bool DirectoryExistsImpl(UPath path)
        {
            return NextFileSystemSafe.DirectoryExists(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            NextFileSystemSafe.MoveDirectory(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath));
        }

        /// <inheritdoc />
        protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            NextFileSystemSafe.DeleteDirectory(ConvertPathToDelegate(path), isRecursive);
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            NextFileSystemSafe.CopyFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath), overwrite);
        }

        /// <inheritdoc />
        protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath,
            bool ignoreMetadataErrors)
        {
            NextFileSystemSafe.ReplaceFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath), destBackupPath.IsNull ? destBackupPath : ConvertPathToDelegate(destBackupPath), ignoreMetadataErrors);
        }

        /// <inheritdoc />
        protected override long GetFileLengthImpl(UPath path)
        {
            return NextFileSystemSafe.GetFileLength(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override bool FileExistsImpl(UPath path)
        {
            return NextFileSystemSafe.FileExists(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void MoveFileImpl(UPath srcPath, UPath destPath)
        {
            NextFileSystemSafe.MoveFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath));
        }

        /// <inheritdoc />
        protected override void DeleteFileImpl(UPath path)
        {
            NextFileSystemSafe.DeleteFile(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            return NextFileSystemSafe.OpenFile(ConvertPathToDelegate(path), mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override FileAttributes GetAttributesImpl(UPath path)
        {
            return NextFileSystemSafe.GetAttributes(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            NextFileSystemSafe.SetAttributes(ConvertPathToDelegate(path), attributes);
        }

        /// <inheritdoc />
        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            return NextFileSystemSafe.GetCreationTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetCreationTimeImpl(UPath path, DateTime time)
        {
            NextFileSystemSafe.SetCreationTime(ConvertPathToDelegate(path), time);
        }

        /// <inheritdoc />
        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            return NextFileSystemSafe.GetLastAccessTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            NextFileSystemSafe.SetLastAccessTime(ConvertPathToDelegate(path), time);
        }

        /// <inheritdoc />
        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            return NextFileSystemSafe.GetLastWriteTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            NextFileSystemSafe.SetLastWriteTime(ConvertPathToDelegate(path), time);
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            foreach (var subPath in NextFileSystemSafe.EnumeratePaths(ConvertPathToDelegate(path), searchPattern, searchOption, searchTarget))
            {
                yield return ConvertPathFromDelegate(subPath);
            }
        }

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override string ConvertToSystemImpl(UPath path)
        {
            return NextFileSystemSafe.ConvertToSystem(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override UPath ConvertFromSystemImpl(string systemPath)
        {
            return ConvertPathFromDelegate(NextFileSystemSafe.ConvertFromSystem(systemPath));
        }

        /// <summary>
        /// Converts the specified path to the path supported by the underlying <see cref="NextFileSystem"/>
        /// </summary>
        /// <param name="path">The path exposed by this filesystem</param>
        /// <returns>A new path translated to the delegate path</returns>
        protected abstract UPath ConvertPathToDelegate(UPath path);

        /// <summary>
        /// Converts the specified delegate path to the path exposed by this filesystem.
        /// </summary>
        /// <param name="path">The path used by the underlying <see cref="NextFileSystem"/></param>
        /// <returns>A new path translated to this filesystem</returns>
        protected abstract UPath ConvertPathFromDelegate(UPath path);
    }
}