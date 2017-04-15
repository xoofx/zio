// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides an in-memory <see cref="IFileSystem"/>
    /// </summary>
    public class MemoryFileSystem : FileSystemBase
    {
        private readonly DirectoryNode _rootDirectory;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryFileSystem"/> class.
        /// </summary>
        public MemoryFileSystem()
        {
            _rootDirectory = new DirectoryNode(null, null);
        }

        ~MemoryFileSystem()
        {
            _rootDirectory.Dispose();
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        protected override void CreateDirectoryImpl(PathInfo path)
        {
            if (path == PathInfo.Root)
            {
                throw new UnauthorizedAccessException("Cannot create the root folder `/` that already exists");
            }

            FindDirectoryNode(path, true);
        }

        protected override bool DirectoryExistsImpl(PathInfo path)
        {
            return FindDirectoryNode(path, false) != null;
        }

        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            if (srcPath == PathInfo.Root)
            {
                throw new UnauthorizedAccessException("Cannot move from the source root directory `/`");
            }
            if (destPath == PathInfo.Root)
            {
                throw new UnauthorizedAccessException("Cannot move to the root directory `/`");
            }

            if (srcPath == destPath)
            {
                throw new IOException($"The source and destination path are the same `{srcPath}`");
            }

            var srcDirectory = FindDirectoryNode(srcPath, false) as DirectoryNode;
            if (srcDirectory == null)
            {
                throw new DirectoryNotFoundException($"The source directory `{srcPath}` was not found");
            }

            string destDirectoryName;
            DirectoryNode parentDestDirectory;
            if (FindNode(destPath, false, false, out parentDestDirectory, out destDirectoryName) != null)
            {
                throw new IOException($"The destination directory `{destPath}` already exists");
            }

            // We are going to move the source directory
            // so we need to lock its parent and then the directory itself
            parentDestDirectory.EnterWrite();
            try
            {
                using (var locks = new ListFileSystemNodes())
                {
                    srcDirectory.TryLockWrite(locks, true, true);

                    FileSystemNode node;
                    if (parentDestDirectory.Children.TryGetValue(destDirectoryName, out node))
                    {
                        if (node is DirectoryNode)
                        {
                            throw new IOException($"The destination directory `{destPath}` already exists");
                        }
                        throw new IOException($"The destination path `{destPath}` is a file");
                    }

                    srcDirectory.DetachFromParent();
                    // Change the directory name
                    srcDirectory.Name = destDirectoryName;
                    srcDirectory.AttachToParent(srcDirectory);
                }
            }
            finally
            {
                parentDestDirectory.ExitWrite();
            }
        }

        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            if (path == PathInfo.Root)
            {
                throw new UnauthorizedAccessException("Cannot delete root directory `/`");
            }

            var directory = FindDirectoryNode(path, false) as DirectoryNode;
            if (directory == null)
            {
                throw new DirectoryNotFoundException($"The directory `{path}` was not found");
            }

            using (var locks = new ListFileSystemNodes())
            {
                directory.TryLockWrite(locks, true, isRecursive);

                // We remove up to the parent but not the parent
                for (var i = locks.Count - 1; i >= 1; i--)
                {
                    var node = locks[i];
                    locks.RemoveAt(i);
                    node.DetachFromParent();
                    node.Dispose();
                }
            }
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        protected override void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            // The source file must exist
            var srcNode = FindNode(srcPath);
            if (srcNode == null)
            {
                throw new FileNotFoundException($"The file `{srcPath}` was not found");
            }

            // The dest file may exist
            DirectoryNode destDirectory;
            string destFileName;
            var destNode = FindNode(destPath, true, false, out destDirectory, out destFileName);

            if (srcNode is DirectoryNode)
            {
                throw new ArgumentException($"Cannot copy file. The path `{srcPath}` is a directory", nameof(srcPath));
            }

            if (destNode is DirectoryNode)
            {
                throw new ArgumentException($"Cannot copy file. The path `{destPath}` is a directory", nameof(destPath));
            }


            // This whole region is reading <srcPath>
            if (srcNode.TryEnterRead())
            {
                throw new IOException($"Cannot read the file `{srcPath}` as it is being used by another thread");
            }
            try
            {
                // If the destination is empty, we need to create it
                if (destNode == null)
                {
                    destDirectory.EnterWrite();
                    try
                    {
                        // After entering in write mode, we need to make sure that the file was not added in the meantime
                        if (destDirectory.Children.TryGetValue(destFileName, out destNode))
                        {
                            if (destNode is DirectoryNode)
                            {
                                throw new ArgumentException($"Cannot copy file. The path `{destPath}` is a directory", nameof(destPath));
                            }
                        }
                        else
                        {
                            destNode = new FileNode(destDirectory, (FileNode) srcNode, destFileName);
                            destDirectory.Children.Add(destNode.Name, destNode);
                            return;
                        }
                    }
                    finally
                    {
                        destDirectory.ExitWrite();
                    }
                }

                if (overwrite)
                {
                    var destFileNode = (FileNode) destNode;
                    if (!destFileNode.TryEnterWrite())
                    {
                        throw new IOException($"Cannot write to the file `{destPath}` as it is being used by another thread");
                    }
                    try
                    {
                        destFileNode.Content = new FileContent(((FileNode) srcNode).Content);
                    }
                    finally
                    {
                        destFileNode.ExitWrite();
                    }
                }
                else
                {
                    throw new IOException($"The destination file path `{destPath}` already exist and overwrite is false");
                }
            }
            finally
            {
                srcNode.ExitRead();
            }
        }

        protected override void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors)
        {
            if (srcPath == destPath)
            {
                throw new IOException($"The source and destination path are the same `{srcPath}`");
            }
            if (destBackupPath == destPath || destBackupPath == srcPath)
            {
                throw new IOException($"The backup is the same as the source or destination path `{destBackupPath}`");
            }

            var srcNode = FindNode(srcPath) as FileNode;
            if (srcNode == null)
            {
                throw new FileNotFoundException($"The file `{srcPath}` was not found");
            }

            DirectoryNode destDirectory;
            string destfileName;
            var destNode = FindNode(destPath, true, false, out destDirectory, out destfileName);
            if (destDirectory == null)
            {
                throw new DirectoryNotFoundException($"The destination directory `{destPath.GetDirectory()}` does not exist");
            }
            if (destNode == null)
            {
                throw new FileNotFoundException($"The file `{destPath}` was not found");
            }

            DirectoryNode destBackupDirectory = null;
            string destBackupfileName = null;

            if (!destBackupPath.IsNull)
            {
                destBackupPath.AssertAbsolute(nameof(destBackupPath));
                var destBackupNode = FindNode(destBackupPath, true, false, out destBackupDirectory, out destBackupfileName);
                if (destBackupDirectory == null)
                {
                    throw new DirectoryNotFoundException($"The destination directory `{destBackupPath.GetDirectory()}` does not exist");
                }
                if (destBackupNode != null)
                {
                    throw new IOException($"The destination path `{destBackupPath}` already exist");
                }               
            }

            if (!srcNode.TryEnterRead())
            {
                throw new IOException($"The file `{srcPath}` is already used by another thread");
            }
            var parentFolder = srcNode.Parent;
            srcNode.ExitRead();

            parentFolder.EnterWrite();
            try
            {
                if (!srcNode.TryEnterWrite())
                {
                    throw new IOException($"The file `{srcPath}` is already used by another thread");
                }
                destDirectory.EnterWrite();
                try
                {
                    if (destBackupDirectory != null)
                    {
                        Debug.Assert(destBackupfileName != null);
                        destBackupDirectory.EnterWrite();
                    }
                    try
                    {
                        // TODO: Check what is the behavior of File.Replace when the destination file does not exist?
                        if (!destDirectory.Children.TryGetValue(destfileName, out destNode))
                        {
                            throw new FileNotFoundException($"The destination file `{destPath}` was not found");
                        }
                        if (destNode is DirectoryNode)
                        {
                            throw new DirectoryNotFoundException($"The destination path `{destPath}` is a directory");
                        }

                        if (!destNode.TryEnterWrite())
                        {
                            throw new IOException($"The file `{destPath}` is already used by another thread");
                        }
                        try
                        {
                            // Remove the dest and attach it to the backup if necessary
                            var destNodeName = destNode.Name;
                            if (destBackupDirectory != null)
                            {
                                if (destBackupDirectory.Children.ContainsKey(destBackupfileName))
                                {
                                    throw new IOException($"The destination backup file `{destBackupPath}` already exist");
                                }
                                destNode.DetachFromParent();
                                destNode.Name = destBackupfileName;
                                destNode.AttachToParent(destBackupDirectory);
                            }
                            else
                            {
                                destNode.DetachFromParent();
                            }

                            // Move file from src to dest
                            srcNode.DetachFromParent();
                            srcNode.Name = destNodeName;
                            srcNode.AttachToParent(destDirectory);
                        }
                        finally
                        {
                            destNode.ExitWrite();
                        }
                    }
                    finally
                    {
                        if (destBackupDirectory != null)
                        {
                            destBackupDirectory.ExitWrite();
                        }
                    }
                }
                finally
                {
                    destDirectory.ExitWrite();
                    srcNode.ExitWrite();
                }
            }
            finally
            {
                parentFolder.ExitWrite();
            }

        }

        protected override long GetFileLengthImpl(PathInfo path)
        {
            using (var locker = GetLocker(path, expectFileOnly: true, isWriting: false))
            {
                return locker.File.Content.Length;
            }
        }

        protected override bool FileExistsImpl(PathInfo path)
        {
            var srcNode = FindNode(path) as FileNode;
            return srcNode != null;
        }

        protected override void MoveFileImpl(PathInfo srcPath, PathInfo destPath)
        {
            var srcNode = FindNode(srcPath) as FileNode;
            if (srcNode == null)
            {
                throw new FileNotFoundException($"The file `{srcPath}` was not found");
            }

            DirectoryNode destDirectory;
            string destfileName;
            var node = FindNode(destPath, true, false, out destDirectory, out destfileName);
            if (destDirectory == null)
            {
                throw new DirectoryNotFoundException($"The destination directory `{destPath.GetDirectory()}` does not exist");
            }
            if (node != null)
            {
                throw new IOException($"The destination path `{destPath}` already exist");
            }


            srcNode.EnterRead();
            var parentFolder = srcNode.Parent;
            srcNode.ExitRead();

            parentFolder.EnterWrite();
            srcNode.EnterWrite();
            destDirectory.EnterWrite();
            try
            {
                if (destDirectory.Children.ContainsKey(destfileName))
                {
                    throw new IOException($"The destination path `{destPath}` already exist");
                }

                srcNode.DetachFromParent();
                srcNode.AttachToParent(destDirectory);
            }
            finally
            {
                destDirectory.ExitWrite();
                srcNode.ExitWrite();
                parentFolder.ExitWrite();
            }
        }

        protected override void DeleteFileImpl(PathInfo path)
        {
            var srcNode = FindNode(path) as FileNode;
            if (srcNode == null)
            {
                throw new FileNotFoundException($"The file `{path}` was not found");
            }

            using (var locks = new ListFileSystemNodes())
            {
                srcNode.TryLockWrite(locks, true, false);
                srcNode.DetachFromParent();
            }
        }

        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            DirectoryNode parentDirectory;
            string filename;
            var srcNode = FindNode(path, true, false, out parentDirectory, out filename);
            if (srcNode is DirectoryNode)
            {
                throw new IOException($"Cannot `{mode}` the path `{path}` is a directory");
            }
            var fileNode = (FileNode) srcNode;

            if (share != FileShare.None)
            {
                throw new NotSupportedException($"The share `{share}` is not supported. Only none is supported");
            }

            // Append: Opens the file if it exists and seeks to the end of the file, or creates a new file. 
            //         This requires FileIOPermissionAccess.Append permission. FileMode.Append can be used only in 
            //         conjunction with FileAccess.Write. Trying to seek to a position before the end of the file 
            //         throws an IOException exception, and any attempt to read fails and throws a 
            //         NotSupportedException exception.
            //
            //
            // CreateNew: Specifies that the operating system should create a new file.This requires 
            //            FileIOPermissionAccess.Write permission. If the file already exists, an IOException 
            //            exception is thrown.
            //
            // Open: Specifies that the operating system should open an existing file. The ability to open 
            //       the file is dependent on the value specified by the FileAccess enumeration. 
            //       A System.IO.FileNotFoundException exception is thrown if the file does not exist.
            //
            // OpenOrCreate: Specifies that the operating system should open a file if it exists; 
            //               otherwise, a new file should be created. If the file is opened with 
            //               FileAccess.Read, FileIOPermissionAccess.Read permission is required. 
            //               If the file access is FileAccess.Write, FileIOPermissionAccess.Write permission 
            //               is required. If the file is opened with FileAccess.ReadWrite, both 
            //               FileIOPermissionAccess.Read and FileIOPermissionAccess.Write permissions 
            //               are required. 
            //
            // Truncate: Specifies that the operating system should open an existing file. 
            //           When the file is opened, it should be truncated so that its size is zero bytes. 
            //           This requires FileIOPermissionAccess.Write permission. Attempts to read from a file 
            //           opened with FileMode.Truncate cause an ArgumentException exception.

            // Create: Specifies that the operating system should create a new file.If the file already exists, 
            //         it will be overwritten.This requires FileIOPermissionAccess.Write permission. 
            //         FileMode.Create is equivalent to requesting that if the file does not exist, use CreateNew; 
            //         otherwise, use Truncate. If the file already exists but is a hidden file, 
            //         an UnauthorizedAccessException exception is thrown.

            bool shouldTruncate = false;
            bool shouldAppend = false;

            if (mode == FileMode.Create)
            {
                if (fileNode != null)
                {
                    mode = FileMode.Open;
                    shouldTruncate = true;
                }
                else
                {
                    mode = FileMode.CreateNew;
                }
            }

            if (mode == FileMode.OpenOrCreate)
            {
                mode = fileNode != null ? FileMode.Open : FileMode.CreateNew;
            }

            if (mode == FileMode.Append)
            {
                if (fileNode != null)
                {
                    mode = FileMode.Open;
                    shouldAppend = true;
                }
                else
                {
                    mode = FileMode.CreateNew;
                }
            }

            // Here we should only have Open or CreateNew
            Debug.Assert(mode == FileMode.Open || mode == FileMode.CreateNew);

            if (mode == FileMode.CreateNew)
            {
                parentDirectory.EnterWrite();
                try
                {
                    // This is not completely accurate to throw an exception (as we have been called with an option to OpenOrCreate)
                    // But we assume that between the beginning of the method and here, the filesystem is not changing, and 
                    // if it is, it is an unfortunate conrurrency
                    if (!parentDirectory.Children.ContainsKey(filename))
                    {
                        throw new IOException($"The file `{path}` already exist");
                    }

                    fileNode = new FileNode(parentDirectory, filename);
                    parentDirectory.Children.Add(filename, fileNode);

                    OpenFile(fileNode, access);
                }
                finally
                {
                    parentDirectory.ExitWrite();
                }
            }
            else
            {
                Debug.Assert(fileNode != null);
                OpenFile(fileNode, access);
            }

            // Create a memory file stream
            var stream = new MemoryFileStream(fileNode, fileNode.Locker);
            if (shouldAppend)
            {
                stream.Position = stream.Length;
            }
            else if (shouldTruncate)
            {
                stream.SetLength(0);
            }
            return stream;
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        protected override FileAttributes GetAttributesImpl(PathInfo path)
        {
            using (var locker = GetLocker(path, expectFileOnly: false, isWriting: false))
            {
                return locker.Node.Attributes;
            }
        }

        protected override void SetAttributesImpl(PathInfo path, FileAttributes attributes)
        {
            using (var locker = GetLocker(path, expectFileOnly: false, isWriting: true))
            {
                if (locker.Node is DirectoryNode)
                {
                    if ((attributes & FileAttributes.Directory) == 0)
                    {
                        throw new UnauthorizedAccessException($"The path `{path}` cannot have attributes `{attributes}`");
                    }
                }
                else
                {
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        throw new UnauthorizedAccessException($"The path `{path}` cannot have attributes `{attributes}`");
                    }
                }
                locker.Node.Attributes = attributes;
            }
        }

        protected override DateTime GetCreationTimeImpl(PathInfo path)
        {
            using (var locker = GetLocker(path, expectFileOnly: false, isWriting: false))
            {
                return locker.Node.CreationTime;
            }
        }

        protected override void SetCreationTimeImpl(PathInfo path, DateTime time)
        {
            using (var locker = GetLocker(path, expectFileOnly: false, isWriting: true))
            {
                locker.Node.CreationTime = time;
            }
        }

        protected override DateTime GetLastAccessTimeImpl(PathInfo path)
        {
            using (var locker = GetLocker(path, expectFileOnly: false, isWriting: false))
            {
                return locker.Node.LastAccessTime;
            }
        }

        protected override void SetLastAccessTimeImpl(PathInfo path, DateTime time)
        {
            using (var locker = GetLocker(path, expectFileOnly: false, isWriting: true))
            {
                locker.Node.LastAccessTime = time;
            }
        }

        protected override DateTime GetLastWriteTimeImpl(PathInfo path)
        {
            using (var locker = GetLocker(path, expectFileOnly: false, isWriting: false))
            {
                return locker.Node.LastWriteTime;
            }
        }

        protected override void SetLastWriteTimeImpl(PathInfo path, DateTime time)
        {
            using (var locker = GetLocker(path, expectFileOnly: false, isWriting: true))
            {
                locker.Node.LastWriteTime = time;
            }
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        protected override IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            var node = FindDirectoryNode(path, false);
            if (node == null)
            {
                throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
            }

            var directory = node as DirectoryNode;
            if (directory == null)
            {
                throw new IOException($"The path `{path}` is a file and not a folder");
            }

            var foldersToProcess = new Queue<DirectoryNode>();
            foldersToProcess.Enqueue(directory);

            var entries = new List<KeyValuePair<string, FileSystemNode>>();

            while (foldersToProcess.Count > 0)
            {
                directory = foldersToProcess.Dequeue();

                // If the directory seems to not be attached anymore, don't try to list it
                if (!directory.Exists)
                {
                    continue;
                }

                // Preread all entries by locking very shortly the folder
                // We optimistically expect that the folder won't change while we are iterating it
                directory.EnterRead();
                entries.Clear();
                entries.AddRange(directory.Children);
                directory.ExitRead();

                foreach (var nodePair in entries)
                {
                    if (nodePair.Value is DirectoryNode && searchTarget == SearchTarget.File)
                    {
                        continue;
                    }
                    if (nodePair.Value is FileNode && searchTarget == SearchTarget.Directory)
                    {
                        continue;
                    }

                    if (search.Match(nodePair.Key))
                    {
                        var fullPath = directory.GetFullPath() / nodePair.Key;
                        yield return fullPath;
                        if (searchOption == SearchOption.AllDirectories && nodePair.Value is DirectoryNode)
                        {
                            foldersToProcess.Enqueue((DirectoryNode)nodePair.Value);
                        }
                    }
                }
            }
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

        protected override void ValidatePathImpl(PathInfo path, string name = "path")
        {
            // TODO: Performing the same check as a PhysicalFileSystem on Windows for now
            if (path.FullName.IndexOf(':') >= 0)
            {
                throw new NotSupportedException($"The path `{path}` cannot contain the `:` character");
            }
        }

        // ----------------------------------------------
        // Internals
        // ----------------------------------------------

        private void EnsureNotReadOnly(PathInfo path)
        {
            if ((GetAttributesImpl(path) & FileAttributes.ReadOnly) != 0)
            {
                throw new IOException($"The file `{path}` is readonly");
            }
        }

        private FileSystemNodeLocker GetLocker(PathInfo path, bool expectFileOnly, bool isWriting)
        {
            // The source file must exist
            var node = FindNode(path);
            if (node == null)
            {
                var expect = expectFileOnly ? "file" : "file or directory";
                throw new FileNotFoundException($"The {expect} `{path}` was not found");
            }
            if (node is DirectoryNode)
            {
                if (expectFileOnly)
                {
                    throw new IOException($"Unexpected directory `{path}` not supported for the operation");
                }

                if (isWriting)
                {
                    node.EnterWrite();                    
                }
                else
                {
                    node.EnterRead();
                }
            }
            else
            {
                // We don't try to enter, as we always want to be able to get the length
                if (isWriting)
                {
                    if (!node.TryEnterWrite())
                    {
                        throw new IOException($"Cannot write to the file `{path}` as it is being used by another thread");
                    }
                }
                else if (!node.TryEnterRead())
                {
                    throw new IOException($"Cannot read the file `{path}` as it is being used by another thread");
                }
            }
            return new FileSystemNodeLocker(node, isWriting);
        }

        private FileSystemNode FindNode(PathInfo path)
        {
            DirectoryNode parentNode = null;
            string filename;
            return FindNode(path, true, false, out parentNode, out filename);
        }

        private FileSystemNode FindDirectoryNode(PathInfo path, bool createIfNotExist)
        {
            DirectoryNode parentNode = null;
            string filename;
            return FindNode(path, false, createIfNotExist, out parentNode, out filename);
        }

        private FileSystemNode FindNode(PathInfo path, bool pathCanBeAFile, bool createIfNotExist, out DirectoryNode parentNode, out string filename)
        {
            filename = null;
            parentNode = null;
            if (path == PathInfo.Root)
            {
                return _rootDirectory;
            }

            parentNode = _rootDirectory;
            var names = path.Split().ToList();
            filename = names[names.Count - 1];
            for (var i = 0; i < names.Count - 1; i++)
            {
                var subPath = names[i];
                var nextDirectory = parentNode.GetFolder(subPath, createIfNotExist);
                if (nextDirectory == null)
                {
                    parentNode = null;
                    return null;
                }
                parentNode = nextDirectory;
            }

            // If we don't expect a file and we are looking to create the folder, we can create the last part as a folder
            if (!pathCanBeAFile && createIfNotExist)
            {
                return parentNode.GetFolder(filename, true);
            }

            parentNode.EnterRead();
            FileSystemNode node;
            parentNode.Children.TryGetValue(filename, out node);
            parentNode.ExitRead();

            return node;
        }

        private void OpenFile(FileNode fileNode, FileAccess access)
        {
            if (fileNode == null) throw new ArgumentNullException(nameof(fileNode));
            if ((access & FileAccess.Write) != 0)
            {
                if (!fileNode.TryEnterWrite())
                {
                    throw new IOException($"Cannot open file `{fileNode}` for write as it is already used by another thread");
                }
            }
            else
            {
                if (!fileNode.TryEnterRead())
                {
                    throw new IOException($"Cannot open file `{fileNode}` for read as it is already used by another thread");
                }
            }
        }

        // Locking strategy is based on https://www.kernel.org/doc/Documentation/filesystems/directory-locking

        private abstract class FileSystemNode : IDisposable
        {
            protected DirectoryNode _parent;
            private string _name;
            protected FileAttributes _attributes;
            private DateTime _creationTime;
            private DateTime _lastWriteTime;
            private DateTime _lastAccessTime;

            protected FileSystemNode(DirectoryNode parent, FileSystemNode copyNode, string name)
            {
                if (parent != null && name == null) throw new ArgumentNullException(nameof(name));
                Locker = new ReaderWriterLockSlim();
                _parent = parent;
                _creationTime = copyNode?.CreationTime ?? DateTime.Now;
                _lastWriteTime = copyNode?.LastWriteTime ?? _creationTime;
                _lastAccessTime = copyNode?.LastAccessTime ?? _creationTime;
                _name = name;
            }

            public ReaderWriterLockSlim Locker { get; }

            public DirectoryNode Parent
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _parent;
                }
                set
                {
                    AssertLockWrite();
                    _parent = value;
                }
            }

            public string Name
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _name;
                }
                set
                {
                    AssertLockWrite();
                    _name = value;
                }
            }

            public FileAttributes Attributes
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _attributes;
                }
                set
                {
                    AssertLockWrite();
                    _attributes = value;
                }
            }

            public DateTime CreationTime
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _creationTime;
                }
                set
                {
                    AssertLockWrite();
                    _creationTime = value;
                }
            }

            public DateTime LastWriteTime
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _lastWriteTime;
                }
                set
                {
                    AssertLockWrite();
                    _lastWriteTime = value;
                }
            }

            public DateTime LastAccessTime
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _lastAccessTime;
                }
                set
                {
                    AssertLockWrite();
                    _lastAccessTime = value;
                }
            }

            public void EnterRead()
            {
                CheckAlive();
                AssertNoLock();
                Locker.EnterReadLock();
                CheckAlive();
            }

            public bool TryEnterRead()
            {
                CheckAlive();
                AssertNoLock();
                var result = Locker.TryEnterReadLock(0);
                if (result)
                {
                    CheckAlive();
                }
                return result;
            }

            public void ExitRead()
            {
                AssertLockRead();
                Locker.ExitReadLock();
            }

            public void EnterWrite()
            {
                CheckAlive();
                AssertNoLock();
                Locker.EnterWriteLock();
                CheckAlive();
            }

            public bool TryEnterWrite()
            {
                CheckAlive();
                AssertNoLock();
                var result = Locker.TryEnterWriteLock(0);
                if (result)
                {
                    CheckAlive();
                }
                return result;
            }

            public void ExitWrite()
            {
                if (!IsDisposed)
                {
                    AssertLockWrite();
                    Locker.ExitWriteLock();
                }
            }

            public PathInfo GetFullPath()
            {
                var parent = _parent;
                if (parent == null)
                {
                    return PathInfo.Root;
                }
                return parent.GetFullPath() / Name;
            }

            public bool IsDisposed { get; private set; }

            public void DetachFromParent()
            {
                AssertLockWrite();
                var parent = _parent;
                if (parent == null)
                {
                    return;
                }
                parent.AssertLockWrite();

                parent.Children.Remove(Name);
                _parent = null;
            }

            public void AttachToParent(DirectoryNode parentNode)
            {
                if (parentNode == null) throw new ArgumentNullException(nameof(parentNode));
                parentNode.AssertLockWrite();
                AssertLockWrite();
                Debug.Assert(Parent == null);

                Parent = parentNode;
                Parent.Children.Add(Name, this);
            }

            public void TryLockWrite(ListFileSystemNodes locks, bool lockParent, bool recursive)
            {
                if (locks == null) throw new ArgumentNullException(nameof(locks));
                AssertNoLock();

                EnterRead();
                DirectoryNode parent = null;
                try
                {
                    parent = Parent;
                }
                finally
                {
                    ExitRead();
                }

                // We read the parent, take a lock
                if (lockParent && parent != null)
                {
                    parent.EnterWrite();
                    locks.Add(parent);
                }

                // If we have a file, we can't wait on the lock (because there is potentially long running locks, like OpenFile)
                if (this is FileNode)
                {
                    if (!TryEnterWrite())
                    {
                        throw new IOException($"Cannot lock the file `{GetFullPath()}` as it is already locked by another thread");
                    }
                }
                else
                {
                    // Otherwise for a directory, we expect to block
                    EnterWrite();
                }
                locks.Add(this);

                if ((Attributes & FileAttributes.ReadOnly) != 0)
                {
                    throw new IOException($"The path {GetFullPath()} is readonly");
                }

                if (recursive && this is DirectoryNode)
                {
                    var directory = (DirectoryNode) this;
                    foreach (var child in directory.Children)
                    {
                        child.Value.TryLockWrite(locks, false, true);
                    }
                }
            }

            public void Dispose()
            {
                // In order to issue a Dispose, we need to have control on this node
                AssertLockWrite();
                IsDisposed = true;
                Locker.ExitWriteLock();
                Locker.Dispose();
            }

            private void CheckAlive()
            {
                if (IsDisposed)
                {
                    throw new InvalidOperationException($"The path `{GetFullPath()}` does not exist anymore");
                }
            }

            protected void AssertLockRead()
            {
                Debug.Assert(Locker.IsReadLockHeld);
            }

            protected void AssertLockReadOrWrite()
            {
                Debug.Assert(Locker.IsReadLockHeld || Locker.IsWriteLockHeld);
            }

            protected void AssertLockWrite()
            {
                Debug.Assert(Locker.IsWriteLockHeld);
            }

            protected void AssertNoLock()
            {
                Debug.Assert(!Locker.IsReadLockHeld && !Locker.IsWriteLockHeld);
            }

            public override string ToString()
            {
                return GetFullPath().ToString();
            }
        }

        private class ListFileSystemNodes : List<FileSystemNode>, IDisposable
        {
            public void Dispose()
            {
                for (var i = this.Count - 1; i >= 0; i--)
                {
                    var node = this[i];
                    node.ExitWrite();
                }
                Clear();
            }
        }

        private struct FileSystemNodeLocker : IDisposable
        {
            private readonly FileSystemNode _fileNode;
            private readonly bool _isWriting;

            public FileSystemNodeLocker(FileSystemNode fileNode, bool isWriting)
            {
                _fileNode = fileNode;
                _isWriting = isWriting;
            }

            public FileSystemNode Node => _fileNode;

            public FileNode File
            {
                get
                {
                    Debug.Assert(_fileNode is FileNode);
                    return (FileNode) _fileNode;
                }
            }

            public DirectoryNode Directory
            {
                get
                {
                    Debug.Assert(_fileNode is DirectoryNode);
                    return (DirectoryNode)_fileNode;
                }
            }

            public void Dispose()
            {
                if (_isWriting)
                {
                    _fileNode.ExitWrite();
                }
                else
                {
                    _fileNode.ExitRead();
                }
            }
        }

        private class DirectoryNode : FileSystemNode
        {
            private readonly Dictionary<string, FileSystemNode> _children;

            public DirectoryNode(DirectoryNode parent, string name) : base(parent, null, name)
            {
                IsRoot = parent == null;
                _children = new Dictionary<string, FileSystemNode>();
                _attributes = FileAttributes.Directory;
            }

            public bool IsRoot { get; }

            public bool Exists
            {
                get
                {
                    return IsRoot || _parent != null;
                }
            }

            public Dictionary<string, FileSystemNode> Children
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _children;
                }
            }

            public DirectoryNode GetFolder(string subFolder, bool createIfNotExist)
            {
                if (string.IsNullOrWhiteSpace(subFolder))
                {
                    throw new ArgumentException($"Cannot create a sub folder with only whitespace for the name `{GetFullPath()}/{subFolder}`");
                }
                AssertNoLock();

                if (createIfNotExist)
                {
                    EnterWrite();
                }
                else
                {
                    EnterRead();
                }

                try
                {
                    FileSystemNode node;
                    if (Children.TryGetValue(subFolder, out node))
                    {
                        if (node is FileNode)
                        {
                            throw new IOException($"Can't create the directory `{GetFullPath()}/{subFolder}` as it is already a file");
                        }
                    }
                    else if (createIfNotExist)
                    {
                        node = new DirectoryNode(this, subFolder);
                        Children.Add(subFolder, node);
                    }
                    return (DirectoryNode) node;
                }
                finally
                {
                    if (createIfNotExist)
                    {
                        ExitWrite();
                    }
                    else
                    {
                        ExitRead();
                    }
                }
            }
        }

        private class FileNode : FileSystemNode
        {
            private FileContent _content;

            public FileNode(DirectoryNode parent, string name) : base(parent, null, name)
            {
                Content = new FileContent();
            }

            public FileNode(DirectoryNode parent, FileNode copyNode, string name) : base(parent, copyNode, name)
            {
                if (copyNode != null)
                {
                    Content = new FileContent(copyNode.Content);
                }
            }

            public FileContent Content
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _content;
                }
                set
                {
                    AssertLockWrite();
                    _content = value;
                }
            }
        }

        private class FileContent
        {
            public FileContent()
            {
                Buffer = Array.Empty<byte>();
            }

            public FileContent(FileContent copy)
            {
                if (copy == null) throw new ArgumentNullException(nameof(copy));
                Buffer = (byte[])copy.Buffer.Clone();
                Length = copy.Length;
            }

            public byte[] Buffer { get; set; }

            public long Length { get; set; }
        }


        private sealed class MemoryFileStream : MemoryStream
        {
            private readonly FileNode _fileNode;
            private readonly ReaderWriterLockSlim _locker;
            private bool _isWritable;

            public MemoryFileStream(FileNode fileNode, ReaderWriterLockSlim locker) : base(fileNode.Content.Buffer, true)
            {
                Debug.Assert(_locker.IsReadLockHeld || _locker.IsWriteLockHeld);
                _fileNode = fileNode;
                _locker = locker;
                _isWritable = _locker.IsWriteLockHeld;
                SetLength(fileNode.Content.Length);
            }

            public override bool CanWrite => _isWritable;

            ~MemoryFileStream()
            {
                Dispose(true);
            }

            protected override void Dispose(bool disposing)
            {
                if (_isWritable)
                {
                    _isWritable = false;
                    _fileNode.Content.Buffer = this.ToArray();
                    _fileNode.Content.Length = this.Length;

                    _locker.ExitWriteLock();
                }
                else
                {
                    _locker.ExitReadLock();
                }
                base.Dispose(disposing);
            }
        }
    }
}