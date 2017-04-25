// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides an merged readonly view filesystem over multiple filesystems (overriding files/directory in order)
    /// </summary>
    public class AggregateFileSystem : FileSystemBase
    {
        protected override void CreateDirectoryImpl(UPath path)
        {
            throw new NotImplementedException();
        }

        protected override bool DirectoryExistsImpl(UPath path)
        {
            throw new NotImplementedException();
        }

        protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            throw new NotImplementedException();
        }

        protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            throw new NotImplementedException();
        }

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
            throw new NotImplementedException();
        }

        protected override bool FileExistsImpl(UPath path)
        {
            throw new NotImplementedException();
        }

        protected override void MoveFileImpl(UPath srcPath, UPath destPath)
        {
            throw new NotImplementedException();
        }

        protected override void DeleteFileImpl(UPath path)
        {
            throw new NotImplementedException();
        }

        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            throw new NotImplementedException();
        }

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
            throw new NotImplementedException();
        }

        protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            throw new NotImplementedException();
        }

        protected override string ConvertToSystemImpl(UPath path)
        {
            throw new NotImplementedException();
        }

        protected override UPath ConvertFromSystemImpl(string systemPath)
        {
            throw new NotImplementedException();
        }
    }
}