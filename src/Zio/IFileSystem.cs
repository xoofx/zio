// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Zio
{
    /// <summary>
    /// Base interface of a FileSystem.
    /// </summary>
    public interface IFileSystem : IDisposable
    {
        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        void CreateDirectory(UPath path);

        bool DirectoryExists(UPath path);

        void MoveDirectory(UPath srcPath, UPath destPath);

        void DeleteDirectory(UPath path, bool isRecursive);

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        void CopyFile(UPath srcPath, UPath destPath, bool overwrite);

        void ReplaceFile(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors);

        long GetFileLength(UPath path);

        bool FileExists(UPath path);

        void MoveFile(UPath srcPath, UPath destPath);

        void DeleteFile(UPath path);

        Stream OpenFile(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None);

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        FileAttributes GetAttributes(UPath path);

        void SetAttributes(UPath path, FileAttributes attributes);

        DateTime GetCreationTime(UPath path);

        void SetCreationTime(UPath path, DateTime time);

        DateTime GetLastAccessTime(UPath path);

        void SetLastAccessTime(UPath path, DateTime time);

        DateTime GetLastWriteTime(UPath path);

        void SetLastWriteTime(UPath path, DateTime time);

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        IEnumerable<UPath> EnumeratePaths(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget);

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        string ConvertToSystem(UPath path);

        UPath ConvertFromSystem(string systemPath);
    }
}