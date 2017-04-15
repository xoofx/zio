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
            NextFileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        /// <summary>
        /// Gets the next delegated file system.
        /// </summary>
        public IFileSystem NextFileSystem { get; }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override void CreateDirectoryImpl(PathInfo path)
        {
            NextFileSystem.CreateDirectory(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override bool DirectoryExistsImpl(PathInfo path)
        {
            return NextFileSystem.DirectoryExists(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            NextFileSystem.MoveDirectory(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath));
        }

        /// <inheritdoc />
        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            NextFileSystem.DeleteDirectory(ConvertPathToDelegate(path), isRecursive);
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            NextFileSystem.CopyFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath), overwrite);
        }

        /// <inheritdoc />
        protected override void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath,
            bool ignoreMetadataErrors)
        {
            NextFileSystem.ReplaceFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath), destBackupPath.IsNull ? destBackupPath : ConvertPathToDelegate(destPath), ignoreMetadataErrors);
        }

        /// <inheritdoc />
        protected override long GetFileLengthImpl(PathInfo path)
        {
            return NextFileSystem.GetFileLength(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override bool FileExistsImpl(PathInfo path)
        {
            return NextFileSystem.FileExists(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void MoveFileImpl(PathInfo srcPath, PathInfo destPath)
        {
            NextFileSystem.MoveFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath));
        }

        /// <inheritdoc />
        protected override void DeleteFileImpl(PathInfo path)
        {
            NextFileSystem.DeleteFile(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            return NextFileSystem.OpenFile(ConvertPathToDelegate(path), mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override FileAttributes GetAttributesImpl(PathInfo path)
        {
            return NextFileSystem.GetAttributes(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetAttributesImpl(PathInfo path, FileAttributes attributes)
        {
            NextFileSystem.SetAttributes(ConvertPathToDelegate(path), attributes);
        }

        /// <inheritdoc />
        protected override DateTime GetCreationTimeImpl(PathInfo path)
        {
            return NextFileSystem.GetCreationTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetCreationTimeImpl(PathInfo path, DateTime time)
        {
            NextFileSystem.SetCreationTime(ConvertPathToDelegate(path), time);
        }

        /// <inheritdoc />
        protected override DateTime GetLastAccessTimeImpl(PathInfo path)
        {
            return NextFileSystem.GetLastAccessTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetLastAccessTimeImpl(PathInfo path, DateTime time)
        {
            NextFileSystem.SetLastAccessTime(ConvertPathToDelegate(path), time);
        }

        /// <inheritdoc />
        protected override DateTime GetLastWriteTimeImpl(PathInfo path)
        {
            return NextFileSystem.GetLastWriteTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override void SetLastWriteTimeImpl(PathInfo path, DateTime time)
        {
            NextFileSystem.SetLastWriteTime(ConvertPathToDelegate(path), time);
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
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
        protected override string ConvertToSystemImpl(PathInfo path)
        {
            return NextFileSystem.ConvertToSystem(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override PathInfo ConvertFromSystemImpl(string systemPath)
        {
            return ConvertPathFromDelegate(NextFileSystem.ConvertFromSystem(systemPath));
        }

        /// <summary>
        /// Converts the specified path to the path supported by the underlying <see cref="NextFileSystem"/>
        /// </summary>
        /// <param name="path">The path exposed by this filesystem</param>
        /// <returns>A new path translated to the delegate path</returns>
        protected abstract PathInfo ConvertPathToDelegate(PathInfo path);

        /// <summary>
        /// Converts the specified delegate path to the path exposed by this filesystem.
        /// </summary>
        /// <param name="path">The path used by the underlying <see cref="NextFileSystem"/></param>
        /// <returns>A new path translated to this filesystem</returns>
        protected abstract PathInfo ConvertPathFromDelegate(PathInfo path);
    }
}