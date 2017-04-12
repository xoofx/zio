// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a readonly filesystem on top of another <see cref="IFileSystem"/>.
    /// </summary>
    /// <seealso cref="Zio.FileSystems.DelegateFileSystem" />
    public class ReadOnlyFileSystem : DelegateFileSystem
    {
        private const string FileSystemIsReadOnly = "This filesystem is read-only";

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlyFileSystem"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system.</param>
        public ReadOnlyFileSystem(IFileSystem fileSystem) : base(fileSystem)
        {
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        protected override void CreateDirectoryImpl(PathInfo path)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        protected override void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override void MoveFileImpl(PathInfo srcPath, PathInfo destPath)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override void DeleteFileImpl(PathInfo path)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (mode != FileMode.Open)
            {
                throw new InvalidOperationException(FileSystemIsReadOnly);
            }

            return base.OpenFileImpl(path, mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        protected override void SetAttributesImpl(PathInfo path, FileAttributes attributes)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override void SetCreationTimeImpl(PathInfo path, DateTime time)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override void SetLastAccessTimeImpl(PathInfo path, DateTime time)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        protected override void SetLastWriteTimeImpl(PathInfo path, DateTime time)
        {
            throw new InvalidOperationException(FileSystemIsReadOnly);
        }

        // ----------------------------------------------
        // Path
        // ----------------------------------------------

        protected override PathInfo ConvertPathToDelegate(PathInfo path, string name)
        {
            // A readonly filesystem doesn't change the path to the delegated filesystem
            return path;
        }

        protected override PathInfo ConvertPathFromDelegate(PathInfo path)
        {
            // A readonly filesystem doesn't change the path from the delegated filesystem
            return path;
        }
    }
}