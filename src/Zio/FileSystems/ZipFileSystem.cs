#if HAS_ZIPARCHIVE
// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

using static Zio.FileSystems.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a FileSystem on top of a <see cref="ZipArchive"/>.
    /// </summary>
    internal class ZipFileSystem : FileSystemBase
    {
        // TODO: The implementation is not finished

        private readonly ZipArchive _zipArchive;

        public ZipFileSystem(ZipArchive zipArchive)
        {
            _zipArchive = zipArchive ?? throw new ArgumentNullException(nameof(zipArchive));
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        protected override void CreateDirectoryImpl(UPath path)
        {
            _zipArchive.CreateEntry(SafeDirectory(path));
        }

        protected override bool DirectoryExistsImpl(UPath path)
        {
            return _zipArchive.GetEntry(SafeDirectory(path)) != null;
        }

        protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            throw new NotImplementedException();
        }

        protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            var entry = _zipArchive.GetEntry(SafeDirectory(path));
            entry?.Delete();
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            throw new NotImplementedException();
        }

        protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            throw new NotImplementedException();
        }

        protected override long GetFileLengthImpl(UPath path)
        {
            var entry = _zipArchive.GetEntry(path.FullName);
            if (entry == null)
            {
                throw NewFileNotFoundException(path);
            }
            return entry.Length;
        }

        protected override bool FileExistsImpl(UPath path)
        {
            return _zipArchive.GetEntry(path.FullName) != null;
        }

        protected override void MoveFileImpl(UPath srcPath, UPath destPath)
        {
            throw new NotImplementedException();
        }

        protected override void DeleteFileImpl(UPath path)
        {
            var entry = _zipArchive.GetEntry(path.FullName);
            entry?.Delete();
        }

        private Stream CreateFile(UPath path)
        {
            return _zipArchive.CreateEntry(path.FullName).Open();
        }

        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            var abspath = path.FullName;
            var entry = _zipArchive.GetEntry(abspath);
            switch (mode)
            {
                case FileMode.Create:
                    return entry != null ? entry.Open() : CreateFile(path);

                case FileMode.CreateNew:
                    if (entry == null)
                    {
                        throw new IOException($"The file `{path}` already exists in this zip archive");
                    }
                    return CreateFile(path);

                case FileMode.Open:
                    if (entry == null)
                    {
                        throw NewFileNotFoundException(path);
                    }
                    return entry.Open();

                case FileMode.OpenOrCreate:
                    return entry != null ? entry.Open() : CreateFile(path);

                case FileMode.Append:
                    if (entry == null)
                    {
                        entry = _zipArchive.CreateEntry(abspath);
                    }
                    var appendStream = entry.Open();
                    appendStream.Seek(entry.Length, SeekOrigin.Begin);
                    return appendStream;

                case FileMode.Truncate:
                    if (entry == null)
                    {
                        throw new IOException($"The file `{path}` already exists in this zip archive");
                    }
                    var truncateStream = entry.Open();
                    truncateStream.Write(new byte[0], 0, 0);
                    return truncateStream;
            }
            throw new NotImplementedException();
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        protected override FileAttributes GetAttributesImpl(UPath path)
        {
            throw new NotImplementedException();
        }

        protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            throw new NotImplementedException();
        }

        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            throw new NotImplementedException();
        }

        protected override void SetCreationTimeImpl(UPath path, DateTime time)
        {
            throw new NotImplementedException();
        }

        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            throw new NotImplementedException();
        }

        protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            throw new NotImplementedException();
        }

        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            var entry = _zipArchive.GetEntry(path.FullName);
            if (entry == null)
            {
                throw NewFileNotFoundException(path);
            }
            return entry.LastWriteTime.DateTime;
        }

        protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            var entry = _zipArchive.GetEntry(path.FullName);
            if (entry == null)
            {
                throw NewFileNotFoundException(path);
            }
            entry.LastWriteTime = DateTimeOffset.FromFileTime(time.Ticks);
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var search = SearchPattern.Parse(ref path, ref searchPattern);
            switch (searchTarget)
            {
                case SearchTarget.File:
                    foreach (var entry in _zipArchive.Entries)
                    {
                        var pathInfo = new UPath(entry.FullName);
                        if (search.Match(pathInfo))
                        {
                            yield return pathInfo;
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        protected override string ConvertToSystemImpl(UPath path)
        {
            return path.FullName;
        }

        protected override UPath ConvertFromSystemImpl(string systemPath)
        {
            return new UPath(systemPath).AssertAbsolute();
        }

        private static string SafeDirectory(UPath path)
        {
            return path.ToRelative().FullName + "/";
        }
    }
}
#endif