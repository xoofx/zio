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
            _rootDirectory = new DirectoryNode(null);
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        protected override void CreateDirectoryImpl(PathInfo path)
        {
            CreateDirectoryNode(path);
        }

        protected override bool DirectoryExistsImpl(PathInfo path)
        {
            return FindDirectoryNode(path) != null;
        }

        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            var srcDirectory = FindDirectoryNode(srcPath);
            if (srcDirectory is FileNode)
            {
                throw new IOException($"The source directory `{srcPath}` is a file");
            }
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
            parentDestDirectory.EnterWrite(destPath);
            try
            {
                using (var locks = new ListFileSystemNodes())
                {
                    srcDirectory.TryLockWrite(locks, srcDirectory.Parent != parentDestDirectory, true, srcPath);

                    FileSystemNode node;
                    if (parentDestDirectory.Children.TryGetValue(destDirectoryName, out node))
                    {
                        if (node is DirectoryNode)
                        {
                            throw new IOException($"The destination directory `{destPath}` already exists");
                        }
                        throw new IOException($"The destination path `{destPath}` is a file");
                    }

                    srcDirectory.DetachFromParent(srcPath.GetName());
                    srcDirectory.AttachToParent(parentDestDirectory, destDirectoryName);
                }
            }
            finally
            {
                parentDestDirectory.ExitWrite();
            }
        }

        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            var directory = FindDirectoryNode(path);
            if (directory is FileNode)
            {
                throw new IOException($"The path `{path}` is a file");
            }
            if (directory == null)
            {
                throw new DirectoryNotFoundException($"The directory `{path}` was not found");
            }

            using (var locks = new ListFileSystemNodes())
            {
                directory.TryLockWrite(locks, true, isRecursive, path);

                // We remove up to the parent but not the parent
                for (var i = locks.Count - 1; i >= 1; i--)
                {
                    var node = locks[i];
                    locks.RemoveAt(i);
                    node.Value.DetachFromParent(node.Key);
                    node.Value.Dispose();
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
            if (srcNode is DirectoryNode)
            {
                throw new ArgumentException($"Cannot copy file. The path `{srcPath}` is a directory", nameof(srcPath));
            }
            if (srcNode == null)
            {
                throw new FileNotFoundException($"The file `{srcPath}` was not found");
            }

            // The dest file may exist
            DirectoryNode destDirectory;
            string destFileName;
            var destNode = FindNode(destPath, true, false, out destDirectory, out destFileName);
            if (destDirectory == null)
            {
                throw new DirectoryNotFoundException($"The directory from the path `{destPath}` was not found");
            }
            if (destNode is DirectoryNode)
            {
                throw new ArgumentException($"Cannot copy file. The path `{destPath}` is a directory", nameof(destPath));
            }

            // This whole region is reading <srcPath>
            srcNode.EnterRead(srcPath);
            try
            {
                // If the destination is empty, we need to create it
                if (destNode == null)
                {
                    destDirectory.EnterWrite(destPath);
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
                            destNode = new FileNode(destDirectory, (FileNode) srcNode);
                            destDirectory.Children.Add(destFileName, destNode);
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
                    destFileNode.EnterWrite(destPath);
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

            srcNode.EnterRead(srcPath);
            var parentFolder = srcNode.Parent;
            srcNode.ExitRead();

            parentFolder.EnterWrite(srcPath);
            try
            {
                srcNode.EnterWrite(srcPath);
                if (destDirectory != parentFolder)
                {
                    destDirectory.EnterWrite(destPath);
                }
                try
                {
                    var isDestBackupLocked = destBackupDirectory != null && destBackupDirectory != destDirectory && destBackupDirectory != parentFolder;
                    if (isDestBackupLocked)
                    {
                        Debug.Assert(destBackupfileName != null);
                        destBackupDirectory.EnterWrite(destBackupPath);
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

                        destNode.EnterWrite(destPath);
                        try
                        {
                            // Remove the dest and attach it to the backup if necessary
                            if (destBackupDirectory != null)
                            {
                                if (destBackupDirectory.Children.ContainsKey(destBackupfileName))
                                {
                                    throw new IOException($"The destination backup file `{destBackupPath}` already exist");
                                }
                                destNode.DetachFromParent(destPath.GetName());
                                destNode.AttachToParent(destBackupDirectory, destBackupfileName);
                            }
                            else
                            {
                                destNode.DetachFromParent(destPath.GetName());
                            }

                            // Move file from src to dest
                            srcNode.DetachFromParent(srcPath.GetName());
                            srcNode.AttachToParent(destDirectory, destfileName);
                        }
                        finally
                        {
                            destNode.ExitWrite();
                        }
                    }
                    finally
                    {
                        if (isDestBackupLocked)
                        {
                            destBackupDirectory.ExitWrite();
                        }
                    }
                }
                finally
                {
                    if (parentFolder != destDirectory)
                    {
                        destDirectory.ExitWrite();
                    }
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
            return ((FileNode)FindNodeSafe(path, true)).Content.Length;
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


            srcNode.EnterRead(srcPath);
            var parentFolder = srcNode.Parent;
            srcNode.ExitRead();

            parentFolder.EnterWrite(srcPath);
            srcNode.EnterWrite(srcPath);
            if (parentFolder != destDirectory)
            {
                destDirectory.EnterWrite(destPath);
            }
            try
            {
                if (destDirectory.Children.ContainsKey(destfileName))
                {
                    throw new IOException($"The destination path `{destPath}` already exist");
                }

                srcNode.DetachFromParent(srcPath.GetName());
                srcNode.AttachToParent(destDirectory, destfileName);
            }
            finally
            {
                if (parentFolder != destDirectory)
                {
                    destDirectory.ExitWrite();
                }
                srcNode.ExitWrite();
                parentFolder.ExitWrite();
            }
        }

        protected override void DeleteFileImpl(PathInfo path)
        {
            var srcNode = FindNode(path);
            if (srcNode == null)
            {
                throw new FileNotFoundException($"The file `{path}` was not found");
            }
            if (srcNode is DirectoryNode)
            {
                throw new IOException($"The path `{path}` is a directory");
            }

            using (var locks = new ListFileSystemNodes())
            {
                srcNode.TryLockWrite(locks, true, false, path);
                srcNode.DetachFromParent(path.GetName());
            }
        }

        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (share != FileShare.None)
            {
                throw new NotSupportedException($"The share `{share}` is not supported. Only none is supported");
            }

            DirectoryNode parentDirectory;
            string filename;
            var srcNode = FindNode(path, true, false, out parentDirectory, out filename);
            if (srcNode is DirectoryNode)
            {
                throw new IOException($"The path `{path}` is a directory");
            }
            if (parentDirectory == null)
            {
                throw new DirectoryNotFoundException($"The directory from the path `{path}` was not found");
            }
            var fileNode = (FileNode) srcNode;

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

            if (mode == FileMode.Truncate)
            {
                if (fileNode != null)
                {
                    mode = FileMode.Open;
                    shouldTruncate = true;
                }
                else
                {
                    throw new FileNotFoundException($"The file `{path}` was not found");
                }
            }

            // Here we should only have Open or CreateNew
            Debug.Assert(mode == FileMode.Open || mode == FileMode.CreateNew);

            if (mode == FileMode.CreateNew)
            {
                parentDirectory.EnterWrite(path);
                try
                {
                    // This is not completely accurate to throw an exception (as we have been called with an option to OpenOrCreate)
                    // But we assume that between the beginning of the method and here, the filesystem is not changing, and 
                    // if it is, it is an unfortunate conrurrency
                    if (parentDirectory.Children.ContainsKey(filename))
                    {
                        throw new IOException($"The file `{path}` already exist");
                    }

                    fileNode = new FileNode(parentDirectory);
                    parentDirectory.Children.Add(filename, fileNode);

                    OpenFile(fileNode, access, path);
                }
                finally
                {
                    parentDirectory.ExitWrite();
                }
            }
            else
            {
                if (fileNode == null)
                {
                    throw new FileNotFoundException($"The file `{path}` was not found");
                }
                OpenFile(fileNode, access, path);
            }

            // Create a memory file stream
            var stream = new MemoryFileStream(fileNode);
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
            var node = FindNodeSafe(path, false);
            var attributes = node.Attributes;
            if (node is DirectoryNode)
            {
                attributes |= FileAttributes.Directory;
            }
            else if (attributes == 0)
            {
                // If this is a file and there is no attributes, return Normal
                attributes = FileAttributes.Normal;
            }
            return attributes;
        }

        protected override void SetAttributesImpl(PathInfo path, FileAttributes attributes)
        {
            // We don't store the attributes Normal or directory
            // As they are returned by GetAttributes and we don't want
            // to duplicate the information with the type inheritance (FileNode or DirectoryNode)
            attributes = attributes & ~FileAttributes.Normal;
            attributes = attributes & ~FileAttributes.Directory;

            var node = FindNodeSafe(path, false);
            node.Attributes = attributes;
        }

        protected override DateTime GetCreationTimeImpl(PathInfo path)
        {
            return FindNodeSafe(path, false).CreationTime;
        }

        protected override void SetCreationTimeImpl(PathInfo path, DateTime time)
        {
            FindNodeSafe(path, false).CreationTime = time;
        }

        protected override DateTime GetLastAccessTimeImpl(PathInfo path)
        {
            return FindNodeSafe(path, false).LastAccessTime;
        }

        protected override void SetLastAccessTimeImpl(PathInfo path, DateTime time)
        {
            FindNodeSafe(path, false).LastAccessTime = time;
        }

        protected override DateTime GetLastWriteTimeImpl(PathInfo path)
        {
            return FindNodeSafe(path, false).LastWriteTime;
        }

        protected override void SetLastWriteTimeImpl(PathInfo path, DateTime time)
        {
            FindNodeSafe(path, false).LastWriteTime = time;
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        protected override IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            var node = FindDirectoryNode(path);
            if (node == null)
            {
                throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
            }

            var directory = node as DirectoryNode;
            if (directory == null)
            {
                throw new IOException($"The path `{path}` is a file and not a folder");
            }

            var foldersToProcess = new Queue<KeyValuePair<PathInfo, DirectoryNode>>();
            foldersToProcess.Enqueue(new KeyValuePair<PathInfo, DirectoryNode>(path, directory));

            var entries = new List<KeyValuePair<string, FileSystemNode>>();

            while (foldersToProcess.Count > 0)
            {
                var dirPair = foldersToProcess.Dequeue();
                var dirPath = dirPair.Key;
                directory = dirPair.Value;

                // If the directory seems to not be attached anymore, don't try to list it
                if (!directory.Exists)
                {
                    continue;
                }

                // Preread all entries by locking very shortly the folder
                // We optimistically expect that the folder won't change while we are iterating it
                directory.EnterRead(dirPath);
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
                        var fullPath = dirPath / nodePair.Key;
                        yield return fullPath;
                        if (searchOption == SearchOption.AllDirectories && nodePair.Value is DirectoryNode)
                        {
                            foldersToProcess.Enqueue(new KeyValuePair<PathInfo, DirectoryNode>(fullPath, (DirectoryNode)nodePair.Value));
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

        private FileSystemNode FindNodeSafe(PathInfo path, bool expectFileOnly)
        {
            var node = FindNode(path);
            if (node == null)
            {
                if (expectFileOnly)
                {
                    throw new FileNotFoundException($"The file `{path}` was not found");
                }
                throw new IOException($"The file or directory `{path}` was not found");
            }
            if (node is DirectoryNode)
            {
                if (expectFileOnly)
                {
                    throw new IOException($"Unexpected directory `{path}` not supported for the operation");
                }
            }
            return node;
        }

        private FileSystemNode FindNode(PathInfo path)
        {
            DirectoryNode parentNode;
            string filename;
            return FindNode(path, true, false, out parentNode, out filename);
        }

        private void CreateDirectoryNode(PathInfo path)
        {
            FindOrCreateDirectoryNode(path, true);
        }

        private FileSystemNode FindDirectoryNode(PathInfo path)
        {
            return FindOrCreateDirectoryNode(path, false);
        }

        private FileSystemNode FindOrCreateDirectoryNode(PathInfo path, bool createIfNotExist)
        {
            DirectoryNode parentNode;
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
                var nextDirectory = parentNode.GetFolder(subPath, createIfNotExist, path);
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
                return parentNode.GetFolder(filename, true, path);
            }

            parentNode.EnterRead(path);
            FileSystemNode node;
            parentNode.Children.TryGetValue(filename, out node);
            parentNode.ExitRead();

            return node;
        }

        private void OpenFile(FileNode fileNode, FileAccess access, PathInfo context)
        {
            if ((access & FileAccess.Write) != 0)
            {
                fileNode.EnterWrite(context);
            }
            else
            {
                fileNode.EnterRead(context);
            }
        }

        // Locking strategy is based on https://www.kernel.org/doc/Documentation/filesystems/directory-locking

        private abstract class FileSystemNode : ReaderWriterLockSlim
        {
            protected FileSystemNode(DirectoryNode parent, FileSystemNode copyNode) : base(LockRecursionPolicy.SupportsRecursion)
            {
                Parent = parent;
                CreationTime = copyNode?.CreationTime ?? DateTime.Now;
                LastWriteTime = copyNode?.LastWriteTime ?? CreationTime;
                LastAccessTime = copyNode?.LastAccessTime ?? CreationTime;
            }

            public DirectoryNode Parent { get; private set; }

            public FileAttributes Attributes { get; set; }

            public DateTime CreationTime { get; set; }

            public DateTime LastWriteTime { get; set; }

            public DateTime LastAccessTime { get; set; }

            public void EnterRead(PathInfo context)
            {
                CheckAlive(context);
                var result = !IsWriteLockHeld && !IsReadLockHeld && TryEnterReadLock(0);
                if (result)
                {
                    CheckAlive(context);
                }
                else
                {
                    throw new IOException($"Cannot read the file `{context}` as it is being used by another thread");
                }
            }

            public void ExitRead()
            {
                AssertLockRead();
                ExitReadLock();
            }

            public void EnterWrite(PathInfo context)
            {
                CheckAlive(context);
                var result = !IsWriteLockHeld && !IsReadLockHeld && TryEnterWriteLock(0);
                if (result)
                {
                    CheckAlive(context);
                }
                else
                {
                    throw new IOException($"Cannot write to the path `{context}` as it is already locked by another thread");
                }
            }

            public void ExitWrite()
            {
                if (!IsDisposed)
                {
                    AssertLockWrite();
                    ExitWriteLock();
                }
            }

            private bool IsDisposed { get; set; }

            public void DetachFromParent(string name)
            {
                AssertLockWrite();
                var parent = Parent;
                if (parent == null)
                {
                    return;
                }
                parent.AssertLockWrite();

                parent.Children.Remove(name);
                Parent = null;
            }

            public void AttachToParent(DirectoryNode parentNode, string name)
            {
                if (parentNode == null) throw new ArgumentNullException(nameof(parentNode));
                parentNode.AssertLockWrite();
                AssertLockWrite();
                Debug.Assert(Parent == null);

                Parent = parentNode;
                Parent.Children.Add(name, this);
            }

            public void TryLockWrite(ListFileSystemNodes locks, bool lockParent, bool recursive, PathInfo context)
            {
                if (locks == null) throw new ArgumentNullException(nameof(locks));
                DirectoryNode parent = Parent;

                // We read the parent, take a lock
                if (lockParent && parent != null)
                {
                    parent.EnterWrite(context);
                    var parentDir = context.GetDirectory();
                    var parentDirName = parentDir.IsNull ? null : parentDir.GetName();
                    locks.Add(new KeyValuePair<string, FileSystemNode>(parentDirName, parent));
                }

                // If we have a file, we can't wait on the lock (because there is potentially long running locks, like OpenFile)
                EnterWrite(context);
                locks.Add(new KeyValuePair<string, FileSystemNode>(context.GetName(), this));

                if ((Attributes & FileAttributes.ReadOnly) != 0)
                {
                    throw new IOException($"The path `{context}` is readonly");
                }

                if (recursive && this is DirectoryNode)
                {
                    var directory = (DirectoryNode) this;
                    foreach (var child in directory.Children)
                    {
                        child.Value.TryLockWrite(locks, false, true, context / child.Key);
                    }
                }
            }

            public new void Dispose()
            {
                // In order to issue a Dispose, we need to have control on this node
                AssertLockWrite();
                IsDisposed = true;
                ExitWriteLock();
                base.Dispose();
            }

            private void CheckAlive(PathInfo context)
            {
                if (IsDisposed)
                {
                    throw new InvalidOperationException($"The path `{context}` does not exist anymore");
                }
            }

            protected void AssertLockRead()
            {
                Debug.Assert(IsReadLockHeld);
            }

            protected void AssertLockReadOrWrite()
            {
                Debug.Assert(IsReadLockHeld || IsWriteLockHeld);
            }

            protected void AssertLockWrite()
            {
                Debug.Assert(IsWriteLockHeld);
            }

            protected void AssertNoLock()
            {
                Debug.Assert(!IsReadLockHeld && !IsWriteLockHeld);
            }
        }

        private class ListFileSystemNodes : List<KeyValuePair<string, FileSystemNode>>, IDisposable
        {
            public void Dispose()
            {
                for (var i = this.Count - 1; i >= 0; i--)
                {
                    var node = this[i];
                    node.Value.ExitWrite();
                }
                Clear();
            }
        }

        private class DirectoryNode : FileSystemNode
        {
            private readonly Dictionary<string, FileSystemNode> _children;

            public DirectoryNode(DirectoryNode parent) : base(parent, null)
            {
                IsRoot = parent == null;
                _children = new Dictionary<string, FileSystemNode>();
                Attributes = FileAttributes.Directory;
            }

            public bool IsRoot { get; }

            public bool Exists => IsRoot || Parent != null;

            public Dictionary<string, FileSystemNode> Children
            {
                get
                {
                    AssertLockReadOrWrite();
                    return _children;
                }
            }

            public DirectoryNode GetFolder(string subFolder, bool createIfNotExist, PathInfo context)
            {
                AssertNoLock();

                if (createIfNotExist)
                {
                    EnterWrite(context);
                }
                else
                {
                    EnterRead(context);
                }

                try
                {
                    FileSystemNode node;
                    if (Children.TryGetValue(subFolder, out node))
                    {
                        if (node is FileNode)
                        {
                            throw new IOException($"Can't create the directory `{context}` as it is already a file");
                        }
                    }
                    else if (createIfNotExist)
                    {
                        node = new DirectoryNode(this);
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

            public FileNode(DirectoryNode parent) : base(parent, null)
            {
                _content = new FileContent();
            }

            public FileNode(DirectoryNode parent, FileNode copyNode) : base(parent, copyNode)
            {
                if (copyNode != null)
                {
                    _content = new FileContent(copyNode.Content);
                }
            }

            public FileContent Content
            {
                get => _content;
                set
                {
                    Debug.Assert(value != null);
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
                Buffer = (byte[])copy.Buffer.Clone();
                Length = copy.Length;
            }

            public byte[] Buffer { get; set; }

            public long Length { get; set; }
        }


        private sealed class MemoryFileStream : Stream
        {
            private readonly MemoryStream _stream;
            private readonly FileNode _fileNode;
            private readonly bool _canRead;
            private readonly bool _canWrite;

            public MemoryFileStream(FileNode fileNode)
            {
                if (fileNode == null) throw new ArgumentNullException(nameof(fileNode));
                Debug.Assert(fileNode.IsReadLockHeld || fileNode.IsWriteLockHeld);
                _fileNode = fileNode;
                _canWrite = fileNode.IsWriteLockHeld;
                _canRead = true;
                _stream = _canWrite ? new MemoryStream() : new MemoryStream(fileNode.Content.Buffer, false);

                if (_canWrite && fileNode.Content.Length > 0)
                {
                    // TODO: Not supporting length > int.MaxValue
                    Write(fileNode.Content.Buffer, 0, (int) fileNode.Content.Length);
                    // Restore the position, as the previous write might have move it
                    Position = 0;
                }
            }

            public override bool CanRead => _canRead;

            public override bool CanSeek => true;

            public override bool CanWrite => _canWrite;

            public override long Length => _stream.Length;

            public override long Position
            {
                get => _stream.Position;
                set => _stream.Position = value;
            }

            ~MemoryFileStream()
            {
                Dispose(true);
            }

            protected override void Dispose(bool disposing)
            {
                _fileNode.LastAccessTime = DateTime.Now;
                if (_canWrite)
                {
                    _fileNode.LastWriteTime = DateTime.Now;
                    _fileNode.Content.Buffer = _stream.ToArray();
                    _fileNode.Content.Length = this.Length;

                    _fileNode.ExitWrite();
                }
                else
                {
                    _fileNode.ExitRead();
                }
                base.Dispose(disposing);
            }

            public override void Flush()
            {
                _stream.Flush();

                // If we flush on a writeable stream, update the node
                if (_canWrite)
                {
                    _fileNode.LastAccessTime = DateTime.Now;
                    _fileNode.LastWriteTime = DateTime.Now;
                    _fileNode.Content.Buffer = _stream.ToArray();
                    _fileNode.Content.Length = this.Length;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _stream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _stream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _stream.SetLength(value);

                _fileNode.LastAccessTime = DateTime.Now;
                _fileNode.LastWriteTime = DateTime.Now;
                _fileNode.Content.Buffer = _stream.ToArray();
                _fileNode.Content.Length = this.Length;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _stream.Write(buffer, offset, count);
            }
        }
    }
}