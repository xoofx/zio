// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides an in-memory <see cref="IFileSystem"/>
    /// </summary>
    public class MemoryFileSystem : FileSystemBase
    {
        private readonly DirectoryNode _rootDirectory;
        private readonly Dictionary<PathInfo, FileSystemNode> _nodes;

        public MemoryFileSystem()
        {
            _rootDirectory = new DirectoryNode();
            _nodes = new Dictionary<PathInfo, FileSystemNode>();
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

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

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

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

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

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

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        protected override IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            throw new NotImplementedException();
        }

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        protected override string ConvertToSystemImpl(PathInfo path)
        {
            return path.FullName;
        }

        protected override PathInfo ConvertFromSystemImpl(string systemPath)
        {
            return new PathInfo(systemPath);
        }


        private class FileSystemNode
        {
            public FileAttributes Attributes { get; set; }
        }

        private class DirectoryNode : FileSystemNode
        {
            private readonly Dictionary<string, FileSystemNode> _children;
            public DirectoryNode()
            {
                _children = new Dictionary<string, FileSystemNode>();
                Attributes = FileAttributes.Directory;
            }
        }

        private class FileNode : FileSystemNode
        {
            public object Content { get; set; }
        }
    }
}