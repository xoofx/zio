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
        /// <exception cref="System.ArgumentNullException">fileSystem</exception>
        protected DelegateFileSystem(IFileSystem fileSystem)
        {
            NextFileSystem = fileSystem;
        }

        /// <summary>
        /// Gets the next delegated file system.
        /// </summary>
        protected IFileSystem NextFileSystem { get; }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override void CreateDirectoryImpl(UPath path)
        {
            NextFileSystem.CreateDirectory(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override bool DirectoryExistsImpl(UPath path)
        {
            return NextFileSystem.DirectoryExists(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            NextFileSystem.MoveDirectory(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath));
        }

        /// <inheritdoc />
        protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            NextFileSystem.DeleteDirectory(ConvertPathToDelegate(path), isRecursive);
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            NextFileSystem.CopyFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath), overwrite);
        }

        /// <inheritdoc />
        protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath,
            bool ignoreMetadataErrors)
        {
            NextFileSystem.ReplaceFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath), destBackupPath.IsNull ? destBackupPath : ConvertPathToDelegate(destBackupPath), ignoreMetadataErrors);
        }

        /// <inheritdoc />
        protected override long GetFileLengthImpl(UPath path)
        {
            return NextFileSystem.GetFileLength(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override bool FileExistsImpl(UPath path)
        {
            return NextFileSystem.FileExists(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void MoveFileImpl(UPath srcPath, UPath destPath)
        {
            NextFileSystem.MoveFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath));
        }

        /// <inheritdoc />
        protected override void DeleteFileImpl(UPath path)
        {
            NextFileSystem.DeleteFile(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            return NextFileSystem.OpenFile(ConvertPathToDelegate(path), mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override FileAttributes GetAttributesImpl(UPath path)
        {
            return NextFileSystem.GetAttributes(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            NextFileSystem.SetAttributes(ConvertPathToDelegate(path), attributes);
        }

        /// <inheritdoc />
        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            return NextFileSystem.GetCreationTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetCreationTimeImpl(UPath path, DateTime time)
        {
            NextFileSystem.SetCreationTime(ConvertPathToDelegate(path), time);
        }

        /// <inheritdoc />
        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            return NextFileSystem.GetLastAccessTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            NextFileSystem.SetLastAccessTime(ConvertPathToDelegate(path), time);
        }

        /// <inheritdoc />
        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            return NextFileSystem.GetLastWriteTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            NextFileSystem.SetLastWriteTime(ConvertPathToDelegate(path), time);
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            foreach (var subPath in NextFileSystem.EnumeratePaths(ConvertPathToDelegate(path), searchPattern, searchOption, searchTarget))
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
            return NextFileSystem.ConvertToSystem(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override UPath ConvertFromSystemImpl(string systemPath)
        {
            return ConvertPathFromDelegate(NextFileSystem.ConvertFromSystem(systemPath));
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