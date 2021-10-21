// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a readonly filesystem on top of another <see cref="IFileSystem"/>.
    /// </summary>
    /// <seealso cref="ComposeFileSystem" />
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "(),nq}")]
    public class ReadOnlyFileSystem : ComposeFileSystem
    {
        /// <summary>
        /// The message "The filesystem is readonly" used to throw an <see cref="IOException"/>.
        /// </summary>
        protected const string FileSystemIsReadOnly = "This filesystem is read-only";

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlyFileSystem"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="owned">True if <paramref name="fileSystem"/> should be disposed when this instance is disposed.</param>
        public ReadOnlyFileSystem(IFileSystem? fileSystem, bool owned = true) : base(fileSystem, owned)
        {
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override ValueTask CreateDirectoryImpl(UPath path)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override ValueTask CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask MoveFileImpl(UPath srcPath, UPath destPath)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask DeleteFileImpl(UPath path)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask<Stream> OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (mode != FileMode.Open)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            if ((access & FileAccess.Write) != 0)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            return base.OpenFileImpl(path, mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override async ValueTask<FileAttributes> GetAttributesImpl(UPath path)
        {
            // All paths are readonly
            return await base.GetAttributesImpl(path) | FileAttributes.ReadOnly;
        }

        /// <inheritdoc />
        protected override ValueTask SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask SetCreationTimeImpl(UPath path, DateTime time)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        /// <inheritdoc />
        protected override ValueTask SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            throw new IOException(FileSystemIsReadOnly);
        }

        // ----------------------------------------------
        // Path
        // ----------------------------------------------

        /// <inheritdoc />
        protected override UPath ConvertPathToDelegate(UPath path)
        {
            // A readonly filesystem doesn't change the path to the delegated filesystem
            return path;
        }

        /// <inheritdoc />
        protected override UPath ConvertPathFromDelegate(UPath path)
        {
            // A readonly filesystem doesn't change the path from the delegated filesystem
            return path;
        }
    }
}