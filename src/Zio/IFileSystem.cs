// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Zio
{
    public interface IFileSystem
    {
        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        void CreateDirectory(PathInfo path);

        bool DirectoryExists(PathInfo path);

        void MoveDirectory(PathInfo srcPath, PathInfo destPath);

        void DeleteDirectory(PathInfo path, bool isRecursive);

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        void CopyFile(PathInfo srcPath, PathInfo destPath, bool overwrite);

        void ReplaceFile(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors);

        long GetFileLength(PathInfo path);

        bool FileExists(PathInfo path);

        void MoveFile(PathInfo srcPath, PathInfo destPath);

        void DeleteFile(PathInfo path);

        Stream OpenFile(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None);

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        FileAttributes GetAttributes(PathInfo path);

        void SetAttributes(PathInfo path, FileAttributes attributes);

        DateTime GetCreationTime(PathInfo path);

        void SetCreationTime(PathInfo path, DateTime time);

        DateTime GetLastAccessTime(PathInfo path);

        void SetLastAccessTime(PathInfo path, DateTime time);

        DateTime GetLastWriteTime(PathInfo path);

        void SetLastWriteTime(PathInfo path, DateTime time);

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        IEnumerable<PathInfo> EnumeratePaths(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget);

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        string ConvertToSystem(PathInfo path);

        PathInfo ConvertFromSystem(string systemPath);
    }
}