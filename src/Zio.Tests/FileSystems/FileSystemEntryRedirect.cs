// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    /// <summary>
    /// Mocking FileSystem redierecting to <see cref="FileEntry"/> and <see cref="DirectoryEntry"/> to verify that these API are correctly working.
    /// </summary>
    public class FileSystemEntryRedirect : FileSystem
    {
        private readonly MemoryFileSystem _fs;

        public FileSystemEntryRedirect()
        {
            _fs = new MemoryFileSystem();
        }

        public FileSystemEntryRedirect(MemoryFileSystem fs)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        }

        protected override ValueTask CreateDirectoryImpl(UPath path)
        {
            new DirectoryEntry(_fs, path).Create();
            return new();
        }

        protected override ValueTask<bool> DirectoryExistsImpl(UPath path)
        {
            return new DirectoryEntry(_fs, path).Exists;
        }

        protected override ValueTask MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            return new DirectoryEntry(_fs, srcPath).MoveTo(destPath);
        }

        protected override ValueTask DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            return new DirectoryEntry(_fs, path).Delete(isRecursive);
        }

        protected override async ValueTask CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            await new FileEntry(_fs, srcPath).CopyTo(destPath, overwrite);
        }

        protected override async ValueTask ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            await new FileEntry(_fs, srcPath).ReplaceTo(destPath, destBackupPath, ignoreMetadataErrors);
        }

        protected override ValueTask<long> GetFileLengthImpl(UPath path)
        {
            return new FileEntry(_fs, path).Length;
        }

        protected override ValueTask<bool> FileExistsImpl(UPath path)
        {
            return new FileEntry(_fs, path).Exists;
        }

        protected override ValueTask MoveFileImpl(UPath srcPath, UPath destPath)
        {
            return new FileEntry(_fs, srcPath).MoveTo(destPath);
        }

        protected override ValueTask DeleteFileImpl(UPath path)
        {
            return new FileEntry(_fs, path).Delete();
        }

        protected override ValueTask<Stream> OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
        {
            return new FileEntry(_fs, path).Open(mode, access, share);
        }

        protected override async ValueTask<FileAttributes> GetAttributesImpl(UPath path)
        {
            return await (await _fs.GetFileSystemEntry(path)).GetAttributes();
        }

        protected override async ValueTask SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            await (await _fs.GetFileSystemEntry(path)).SetAttributes(attributes);
        }

        protected override async ValueTask<DateTime> GetCreationTimeImpl(UPath path)
        {
            var entry = await _fs.TryGetFileSystemEntry(path);
            if (entry == null)
            {
                return DefaultFileTime;
            }

            return await _fs.GetCreationTime(path);
        }

        protected override ValueTask SetCreationTimeImpl(UPath path, DateTime time)
        {
            return _fs.SetCreationTime(path, time);
        }

        protected override async ValueTask<DateTime> GetLastAccessTimeImpl(UPath path)
        {
            var entry = await _fs.TryGetFileSystemEntry(path);
            if (entry == null)
            {
                return DefaultFileTime;
            }


            return await _fs.GetLastAccessTime(path);
        }

        protected override ValueTask SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            return _fs.SetLastAccessTime(path, time);
        }

        protected override async ValueTask<DateTime> GetLastWriteTimeImpl(UPath path)
        {
            var entry = await _fs.TryGetFileSystemEntry(path);
            if (entry == null)
            {
                return DefaultFileTime;
            }


            return await _fs.GetLastWriteTime(path);
        }

        protected override ValueTask SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            return _fs.SetLastWriteTime(path, time);
        }

        protected override async ValueTask<IEnumerable<UPath>> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var directoryEntry = await _fs.GetDirectoryEntry(path);
            var entries = await directoryEntry.EnumerateEntries(searchPattern, searchOption, searchTarget);
            return entries.Select(e => e.Path).ToArray();
        }
        
        protected override async ValueTask<IEnumerable<FileSystemItem>> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate searchPredicate)
        {
            return await (await _fs.GetDirectoryEntry(path)).EnumerateItems(searchOption);
        }

        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            return _fs.Watch(path);
        }

        protected override string ConvertPathToInternalImpl(UPath path)
        {
            return _fs.ConvertPathToInternal(path);
        }

        protected override UPath ConvertPathFromInternalImpl(string innerPath)
        {
            return _fs.ConvertPathFromInternal(innerPath);
        }
    }
}