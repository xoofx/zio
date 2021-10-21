// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides an abstract base <see cref="IFileSystem"/> for composing a filesystem with another FileSystem. 
    /// This implementation delegates by default its implementation to the filesystem passed to the constructor.
    /// </summary>
    public abstract class ComposeFileSystem : FileSystem
    {
        protected bool Owned { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ComposeFileSystem"/> class.
        /// </summary>
        /// <param name="fileSystem">The delegated file system (can be null).</param>
        /// <param name="owned">True if <paramref name="fileSystem"/> should be disposed when this instance is disposed.</param>
        protected ComposeFileSystem(IFileSystem? fileSystem, bool owned = true)
        {
            Fallback = fileSystem;
            Owned = owned;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Owned)
            {
                Fallback?.Dispose();
            }
        }

        /// <summary>
        /// Gets the next delegated file system (may be null).
        /// </summary>
        protected IFileSystem? Fallback { get; }

        /// <summary>
        /// Gets the next delegated file system or throws an error if it is null.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        protected IFileSystem FallbackSafe
        {
            get
            {
                if (Fallback is null)
                {
                    throw new InvalidOperationException("The delegate filesystem for this instance is null");
                }
                return Fallback;
            }
        }

        protected override string DebuggerDisplay()
        {
            return $"{base.DebuggerDisplay()} (Fallback: {(Fallback is FileSystem fs ? fs.DebuggerKindName() : Fallback?.GetType().Name.Replace("FileSystem", "fs").ToLowerInvariant())})";
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override ValueTask CreateDirectoryImpl(UPath path)
        {
            return FallbackSafe.CreateDirectory(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask<bool> DirectoryExistsImpl(UPath path)
        {
            return FallbackSafe.DirectoryExists(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            return FallbackSafe.MoveDirectory(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath));
        }

        /// <inheritdoc />
        protected override ValueTask DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            return FallbackSafe.DeleteDirectory(ConvertPathToDelegate(path), isRecursive);
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override ValueTask CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            return FallbackSafe.CopyFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath), overwrite);
        }

        /// <inheritdoc />
        protected override ValueTask ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath,
            bool ignoreMetadataErrors)
        {
            return FallbackSafe.ReplaceFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath), destBackupPath.IsNull ? destBackupPath : ConvertPathToDelegate(destBackupPath), ignoreMetadataErrors);
        }

        /// <inheritdoc />
        protected override ValueTask<long> GetFileLengthImpl(UPath path)
        {
            return FallbackSafe.GetFileLength(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask<bool> FileExistsImpl(UPath path)
        {
            return FallbackSafe.FileExists(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask MoveFileImpl(UPath srcPath, UPath destPath)
        {
            return FallbackSafe.MoveFile(ConvertPathToDelegate(srcPath), ConvertPathToDelegate(destPath));
        }

        /// <inheritdoc />
        protected override ValueTask DeleteFileImpl(UPath path)
        {
            return FallbackSafe.DeleteFile(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask<Stream> OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            return FallbackSafe.OpenFile(ConvertPathToDelegate(path), mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override ValueTask<FileAttributes> GetAttributesImpl(UPath path)
        {
            return FallbackSafe.GetAttributes(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            return FallbackSafe.SetAttributes(ConvertPathToDelegate(path), attributes);
        }

        /// <inheritdoc />
        protected override ValueTask<DateTime> GetCreationTimeImpl(UPath path)
        {
            return FallbackSafe.GetCreationTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask SetCreationTimeImpl(UPath path, DateTime time)
        {
            return FallbackSafe.SetCreationTime(ConvertPathToDelegate(path), time);
        }

        /// <inheritdoc />
        protected override ValueTask<DateTime> GetLastAccessTimeImpl(UPath path)
        {
            return FallbackSafe.GetLastAccessTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            return FallbackSafe.SetLastAccessTime(ConvertPathToDelegate(path), time);
        }

        /// <inheritdoc />
        protected override ValueTask<DateTime> GetLastWriteTimeImpl(UPath path)
        {
            return FallbackSafe.GetLastWriteTime(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override ValueTask SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            return FallbackSafe.SetLastWriteTime(ConvertPathToDelegate(path), time);
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override async ValueTask<IEnumerable<UPath>> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var paths = await FallbackSafe.EnumeratePaths(ConvertPathToDelegate(path), searchPattern, searchOption, searchTarget);
            return paths.Select(ConvertPathFromDelegate).ToArray();
        }

        /// <inheritdoc />
        protected override async ValueTask<IEnumerable<FileSystemItem>> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
        {
            var items = await FallbackSafe.EnumerateItems(ConvertPathToDelegate(path), searchOption, searchPredicate);
            return items.Select(subItem =>
            {
                var localItem = subItem;
                localItem.Path = ConvertPathFromDelegate(localItem.Path);
                return localItem;
            }).ToArray();
        }

        // ----------------------------------------------
        // Watch API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override bool CanWatchImpl(UPath path)
        {
            return FallbackSafe.CanWatch(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            return FallbackSafe.Watch(ConvertPathToDelegate(path));
        }

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override string ConvertPathToInternalImpl(UPath path)
        {
            return FallbackSafe.ConvertPathToInternal(ConvertPathToDelegate(path));
        }

        /// <inheritdoc />
        protected override UPath ConvertPathFromInternalImpl(string innerPath)
        {
            return ConvertPathFromDelegate(FallbackSafe.ConvertPathFromInternal(innerPath));
        }

        /// <summary>
        /// Converts the specified path to the path supported by the underlying <see cref="Fallback"/>
        /// </summary>
        /// <param name="path">The path exposed by this filesystem</param>
        /// <returns>A new path translated to the delegate path</returns>
        protected abstract UPath ConvertPathToDelegate(UPath path);

        /// <summary>
        /// Converts the specified delegate path to the path exposed by this filesystem.
        /// </summary>
        /// <param name="path">The path used by the underlying <see cref="Fallback"/></param>
        /// <returns>A new path translated to this filesystem</returns>
        protected abstract UPath ConvertPathFromDelegate(UPath path);
    }
}