// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Zio.FileSystems
{
    /// <summary>
    /// A <see cref="IFileSystem"/> that can (auto-)mount other filesystems on a specified path.
    /// </summary>
    public class MountFileSystem : FileSystemBase
    {
        protected override void CreateDirectoryImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override bool DirectoryExistsImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            throw new NotImplementedException();
        }

        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            throw new NotImplementedException();
        }

        protected override void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            throw new NotImplementedException();
        }

        protected override void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors)
        {
            throw new NotImplementedException();
        }

        protected override long GetFileLengthImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override bool FileExistsImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override void MoveFileImpl(PathInfo srcPath, PathInfo destPath)
        {
            throw new NotImplementedException();
        }

        protected override void DeleteFileImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            throw new NotImplementedException();
        }

        protected override FileAttributes GetAttributesImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override void SetAttributesImpl(PathInfo path, FileAttributes attributes)
        {
            throw new NotImplementedException();
        }

        protected override DateTime GetCreationTimeImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override void SetCreationTimeImpl(PathInfo path, DateTime time)
        {
            throw new NotImplementedException();
        }

        protected override DateTime GetLastAccessTimeImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override void SetLastAccessTimeImpl(PathInfo path, DateTime time)
        {
            throw new NotImplementedException();
        }

        protected override DateTime GetLastWriteTimeImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override void SetLastWriteTimeImpl(PathInfo path, DateTime time)
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            throw new NotImplementedException();
        }

        protected override string ConvertToSystemImpl(PathInfo path)
        {
            throw new NotImplementedException();
        }

        protected override PathInfo ConvertFromSystemImpl(string systemPath)
        {
            throw new NotImplementedException();
        }
    }
}