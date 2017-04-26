// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using static Zio.FileSystems.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides an in-memory <see cref="IFileSystem"/>
    /// </summary>
    public class MemoryFileSystem : FileSystemBase
    {
        // The locking strategy is based on https://www.kernel.org/doc/Documentation/filesystems/directory-locking

        private readonly DirectoryNode _rootDirectory;
        private readonly FileSystemNodeReadWriteLock _globalLock;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryFileSystem"/> class.
        /// </summary>
        public MemoryFileSystem()
        {
            _rootDirectory = new DirectoryNode();
            _globalLock = new FileSystemNodeReadWriteLock();
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        protected override void CreateDirectoryImpl(UPath path)
        {
            EnterFileSystemShared();
            try
            {
                CreateDirectoryNode(path);
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        protected override bool DirectoryExistsImpl(UPath path)
        {
            if (path == UPath.Root)
            {
                return true;
            }

            EnterFileSystemShared();
            try
            {
                var result = EnterFindNode(path, FindNodeFlags.None);
                try
                {
                    return result.Node is DirectoryNode;
                }
                finally
                {
                    ExitFindNode(result);
                }
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
        {
            MoveFileOrDirectory(srcPath, destPath, true);
        }

        protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
        {
            EnterFileSystemShared();
            try
            {
                var result = EnterFindNode(path, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive);

                bool deleteRootDirectory = false;
                try
                {
                    AssertDirectory(result.Node, path);

                    if (result.Node.IsReadOnly)
                    {
                        throw new IOException($"Access to the path `{path}` is denied");
                    }

                    using (var locks = new ListFileSystemNodes(this))
                    {
                        TryLockExclusive(result.Node, locks, isRecursive, path);

                        // Check that files are not readonly
                        foreach (var lockFile in locks)
                        {
                            var node = lockFile.Value;

                            if (node.IsReadOnly)
                            {
                                throw new UnauthorizedAccessException($"Access to path `{path}` is denied.");
                            }
                        }

                        // We remove all elements
                        for (var i = locks.Count - 1; i >= 0; i--)
                        {
                            var node = locks[i];
                            locks.RemoveAt(i);
                            node.Value.DetachFromParent(node.Key);
                            node.Value.Dispose();

                            ExitExclusive(node.Value);
                        }
                    }
                    deleteRootDirectory = true;
                }
                finally
                {
                    if (deleteRootDirectory)
                    {
                        result.Node.DetachFromParent(result.Name);
                        result.Node.Dispose();
                    }

                    ExitFindNode(result);
                }
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
        {
            EnterFileSystemShared();
            try
            {
                var srcResult = EnterFindNode(srcPath, FindNodeFlags.None);
                try
                {
                    // The source file must exist
                    var srcNode = srcResult.Node;
                    if (srcNode is DirectoryNode)
                    {
                        throw new UnauthorizedAccessException($"Cannot copy file. The path `{srcPath}` is a directory");
                    }
                    if (srcNode == null)
                    {
                        throw NewFileNotFoundException(srcPath);
                    }

                    var destResult = EnterFindNode(destPath, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive);
                    var destFileName = destResult.Name;
                    var destDirectory = destResult.Directory;
                    var destNode = destResult.Node;
                    try
                    {
                        // The dest file may exist
                        if (destDirectory == null)
                        {
                            throw NewDirectoryNotFoundException(destPath);
                        }
                        if (destNode is DirectoryNode)
                        {
                            throw new IOException($"The target file `{destPath}` is a directory, not a file.");
                        }

                        // If the destination is empty, we need to create it
                        if (destNode == null)
                        {
                            var newFileNode = new FileNode(destDirectory, (FileNode)srcNode);
                            destDirectory.Children.Add(destFileName, newFileNode);
                        }
                        else if (overwrite)
                        {
                            if (destNode.IsReadOnly)
                            {
                                throw new UnauthorizedAccessException($"Access to path `{destPath}` is denied.");
                            }
                            var destFileNode = (FileNode)destNode;
                            destFileNode.Content.CopyFrom(((FileNode)srcNode).Content);
                        }
                        else
                        {
                            throw new IOException($"The destination file path `{destPath}` already exist and overwrite is false");
                        }
                    }
                    finally
                    {
                        if (destNode != null)
                        {
                            ExitExclusive(destNode);
                        }

                        if (destDirectory != null)
                        {
                            ExitExclusive(destDirectory);
                        }
                    }
                }
                finally
                {
                    ExitFindNode(srcResult);
                }
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
        {
            // Get the directories of src/dest/backup
            var parentSrcPath = srcPath.GetDirectory();
            var parentDestPath = destPath.GetDirectory();
            var parentDestBackupPath = destBackupPath.IsNull ? new UPath() : destBackupPath.GetDirectory();

            // Simple case: src/dest/backup in the same folder
            var isSameFolder = parentSrcPath == parentDestPath && (destBackupPath.IsNull || (parentDestBackupPath == parentSrcPath));
            // Else at least one folder is different. This is a rename semantic (as per the locking guidelines)

            var paths = new List<KeyValuePair<UPath, int>>
            {
                new KeyValuePair<UPath, int>(srcPath, 0),
                new KeyValuePair<UPath, int>(destPath, 1)
            };

            if (!destBackupPath.IsNull)
            {
                paths.Add(new KeyValuePair<UPath, int>(destBackupPath, 2));
            }
            paths.Sort((p1, p2) => string.Compare(p1.Key.FullName, p2.Key.FullName, StringComparison.Ordinal));

            // We need to take the lock on the folders in the correct order to avoid deadlocks
            // So we sort the srcPath and destPath in alphabetical order
            // (if srcPath is a subFolder of destPath, we will lock first destPath parent Folder, and then srcFolder)

            if (isSameFolder)
            {
                EnterFileSystemShared();
            }
            else
            {
                EnterFileSystemExclusive();
            }

            try
            {
                var results = new NodeResult[destBackupPath.IsNull ? 2 : 3];
                try
                {
                    for (int i = 0; i < paths.Count; i++)
                    {
                        var pathPair = paths[i];
                        var flags = FindNodeFlags.KeepParentNodeExclusive;
                        if (pathPair.Value != 2)
                        {
                            flags |= FindNodeFlags.NodeExclusive;
                        }
                        results[pathPair.Value] = EnterFindNode(pathPair.Key, flags, results);
                    }

                    var srcResult = results[0];
                    var destResult = results[1];

                    AssertFile(srcResult.Node, srcPath);
                    AssertFile(destResult.Node, destPath);

                    if (!destBackupPath.IsNull)
                    {
                        var backupResult = results[2];
                        AssertDirectory(backupResult.Directory, destPath);

                        if (backupResult.Node != null)
                        {
                            AssertFile(backupResult.Node, destBackupPath);
                            backupResult.Node.DetachFromParent(backupResult.Name);
                            backupResult.Node.Dispose();
                        }

                        destResult.Node.DetachFromParent(destResult.Name);
                        destResult.Node.AttachToParent(backupResult.Directory, backupResult.Name);
                    }
                    else
                    {
                        destResult.Node.DetachFromParent(destResult.Name);
                        destResult.Node.Dispose();
                    }

                    srcResult.Node.DetachFromParent(srcResult.Name);
                    srcResult.Node.AttachToParent(destResult.Directory, destResult.Name);
                }
                finally
                {
                    for (int i = results.Length - 1; i >= 0; i--)
                    {
                        ExitFindNode(results[i]);
                    }
                }
            }
            finally
            {
                if (isSameFolder)
                {
                    ExitFileSystemShared();
                }
                else
                {
                    ExitFileSystemExclusive();
                }
            }
        }

        protected override long GetFileLengthImpl(UPath path)
        {
            EnterFileSystemShared();
            try
            {
                return ((FileNode)FindNodeSafe(path, true)).Content.Length;
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        protected override bool FileExistsImpl(UPath path)
        {
            EnterFileSystemShared();
            try
            {
                var result = EnterFindNode(path, FindNodeFlags.None);
                ExitFindNode(result);
                return result.Node is FileNode;
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        protected override void MoveFileImpl(UPath srcPath, UPath destPath)
        {
            MoveFileOrDirectory(srcPath, destPath, false);
        }

        protected override void DeleteFileImpl(UPath path)
        {
            EnterFileSystemShared();
            try
            {
                var result = EnterFindNode(path, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive);
                try
                {
                    var srcNode = result.Node;
                    if (srcNode == null)
                    {
                        // If the file to be deleted does not exist, no exception is thrown.
                        return;
                    }
                    if (srcNode is DirectoryNode || srcNode.IsReadOnly)
                    {
                        throw new UnauthorizedAccessException($"Access to path `{path}` is denied.");
                    }

                    srcNode.DetachFromParent(result.Name);
                    srcNode.Dispose();
                }
                finally
                {
                    ExitFindNode(result);
                }
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
        {
            if (mode == FileMode.Append && (access & FileAccess.Read) != 0)
            {
                throw new ArgumentException("Combining FileMode: Append with FileAccess: Read is invalid.", nameof(access));
            }

            var isWriting = (access & FileAccess.Write) != 0;
            var isExclusive = share == FileShare.None;

            EnterFileSystemShared();
            DirectoryNode parentDirectory = null;
            FileNode fileNodeToRelease = null;
            try
            {
                var result = EnterFindNode(path, (isExclusive ? FindNodeFlags.NodeExclusive : FindNodeFlags.None) | FindNodeFlags.KeepParentNodeExclusive, share);
                if (result.Directory == null)
                {
                    ExitFindNode(result);
                    throw NewDirectoryNotFoundException(path);
                }

                if (result.Node is DirectoryNode || (isWriting && result.Node != null && result.Node.IsReadOnly))
                {
                    ExitFindNode(result);
                    throw new UnauthorizedAccessException($"Access to the path `{path}` is denied.");
                }

                var filename = result.Name;
                parentDirectory = result.Directory;
                var srcNode = result.Node;

                var fileNode = (FileNode)srcNode;

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
                        throw NewFileNotFoundException(path);
                    }
                }

                // Here we should only have Open or CreateNew
                Debug.Assert(mode == FileMode.Open || mode == FileMode.CreateNew);

                if (mode == FileMode.CreateNew)
                {
                    // This is not completely accurate to throw an exception (as we have been called with an option to OpenOrCreate)
                    // But we assume that between the beginning of the method and here, the filesystem is not changing, and 
                    // if it is, it is an unfortunate conrurrency
                    if (fileNode != null)
                    {
                        fileNodeToRelease = fileNode;
                        throw new IOException($"The file `{path}` already exist");
                    }

                    fileNode = new FileNode(parentDirectory);
                    parentDirectory.Children.Add(filename, fileNode);
                    if (isExclusive)
                    {
                        EnterExclusive(fileNode, path);
                    }
                    else
                    {
                        EnterShared(fileNode, path, share);
                    }
                }
                else
                {
                    if (fileNode == null)
                    {
                        throw NewFileNotFoundException(path);
                    }

                    ExitExclusive(parentDirectory);
                    parentDirectory = null;
                }

                // TODO: Add checks between mode and access

                // Create a memory file stream
                var stream = new MemoryFileStream(this, fileNode, isWriting, isExclusive);
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
            finally
            {
                if (fileNodeToRelease != null)
                {
                    if (isExclusive)
                    {
                        ExitExclusive(fileNodeToRelease);
                    }
                    else
                    {
                        ExitShared(fileNodeToRelease);
                    }
                }
                if (parentDirectory != null)
                {
                    ExitExclusive(parentDirectory);
                }
                ExitFileSystemShared();
            }
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        protected override FileAttributes GetAttributesImpl(UPath path)
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

        protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
        {
            // We don't store the attributes Normal or directory
            // As they are returned by GetAttributes and we don't want
            // to duplicate the information with the type inheritance (FileNode or DirectoryNode)
            attributes = attributes & ~FileAttributes.Normal;
            attributes = attributes & ~FileAttributes.Directory;

            var node = FindNodeSafe(path, false);
            node.Attributes = attributes;
        }

        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            return TryFindNodeSafe(path)?.CreationTime ?? DefaultFileTime;
        }

        protected override void SetCreationTimeImpl(UPath path, DateTime time)
        {
            FindNodeSafe(path, false).CreationTime = time;
        }

        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            return TryFindNodeSafe(path)?.LastAccessTime ?? DefaultFileTime;
        }

        protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
        {
            FindNodeSafe(path, false).LastAccessTime = time;
        }

        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            return TryFindNodeSafe(path)?.LastWriteTime ?? DefaultFileTime;
        }

        protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
        {
            FindNodeSafe(path, false).LastWriteTime = time;
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            var foldersToProcess = new Queue<UPath>();
            foldersToProcess.Enqueue(path);

            var entries = new List<UPath>();
            while (foldersToProcess.Count > 0)
            {
                var directoryPath = foldersToProcess.Dequeue();
                entries.Clear();

                // This is important that here we don't lock the FileSystemShared
                // or the visited folder while returning a yield otherwise the finally
                // may never be executed if the caller of this method decide to not
                // Dispose the IEnumerable (because the generated IEnumerable
                // doesn't have a finalizer calling Dispose)
                // This is why the yield is performed outside this block
                EnterFileSystemShared();
                try
                {
                    var result = EnterFindNode(directoryPath, FindNodeFlags.None);
                    try
                    {
                        if (directoryPath == path)
                        {
                            // The first folder must be a directory, if it is not, throw an error
                            AssertDirectory(result.Node, directoryPath);
                        }
                        else
                        {
                            // Might happen during the time a DirectoryNode is enqueued into foldersToProcess
                            // and the time we are going to actually visit it, it might have been
                            // removed in the meantime, so we make sure here that we have a folder
                            // and we don't throw an error if it is not
                            if (!(result.Node is DirectoryNode))
                            {
                                continue;
                            }
                        }

                        var directory = (DirectoryNode)result.Node;
                        foreach (var nodePair in directory.Children)
                        {
                            if (nodePair.Value is FileNode && searchTarget == SearchTarget.Directory)
                            {
                                continue;
                            }

                            var isEntryMatching = search.Match(nodePair.Key);

                            var canFollowFolder = searchOption == SearchOption.AllDirectories && nodePair.Value is DirectoryNode && (searchTarget == SearchTarget.File || isEntryMatching);

                            var addEntry = (nodePair.Value is FileNode && searchTarget != SearchTarget.Directory && isEntryMatching)
                                           || (nodePair.Value is DirectoryNode && searchTarget != SearchTarget.File && isEntryMatching);

                            var fullPath = directoryPath / nodePair.Key;

                            if (canFollowFolder)
                            {
                                foldersToProcess.Enqueue(fullPath);
                            }

                            if (addEntry)
                            {
                                entries.Add(fullPath);
                            }
                        }
                    }
                    finally
                    {
                        ExitFindNode(result);
                    }
                }
                finally
                {
                    ExitFileSystemShared();
                }

                // We return all the elements of visited directory in one shot, outside the previous lock block
                foreach (var entry in entries)
                {
                    yield return entry;
                }
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
            return new UPath(systemPath);
        }

        // ----------------------------------------------
        // Internals
        // ----------------------------------------------

        private void MoveFileOrDirectory(UPath srcPath, UPath destPath, bool expectDirectory)
        {
            var parentSrcPath = srcPath.GetDirectory();
            var parentDestPath = destPath.GetDirectory();

            void CheckDestination(FileSystemNode node)
            {
                if (expectDirectory)
                {
                    if (node is FileNode)
                    {
                        throw new IOException($"The destination path `{destPath}` is an existing file");
                    }
                }
                else
                {
                    if (node is DirectoryNode)
                    {
                        throw new IOException($"The destination path `{destPath}` is an existing directory");
                    }
                }

                if (node != null)
                {
                    throw new IOException($"The destination path `{destPath}` already exists");
                }
            }

            // Same directory move
            var isSamefolder = parentSrcPath == parentDestPath;
            // Check that Destination folder is not a subfolder of source directory
            if (!isSamefolder && expectDirectory)
            {
                var checkParentDestDirectory = destPath.GetDirectory();
                while (checkParentDestDirectory != null)
                {
                    if (checkParentDestDirectory == srcPath)
                    {
                        throw new IOException($"Cannot move the source directory `{srcPath}` to a a sub-folder of itself `{destPath}`");
                    }

                    checkParentDestDirectory = checkParentDestDirectory.GetDirectory();
                }
            }

            // We need to take the lock on the folders in the correct order to avoid deadlocks
            // So we sort the srcPath and destPath in alphabetical order
            // (if srcPath is a subFolder of destPath, we will lock first destPath parent Folder, and then srcFolder)

            bool isLockInverted = !isSamefolder && string.Compare(srcPath.FullName, destPath.FullName, StringComparison.Ordinal) > 0;

            if (isSamefolder)
            {
                EnterFileSystemShared();
            }
            else
            {
                EnterFileSystemExclusive();
            }
            try
            {
                var srcResult = new NodeResult();
                var destResult = new NodeResult();
                try
                {
                    if (isLockInverted)
                    {
                        destResult = EnterFindNode(destPath, FindNodeFlags.KeepParentNodeExclusive);
                        srcResult = EnterFindNode(srcPath, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive, destResult);
                    }
                    else
                    {
                        srcResult = EnterFindNode(srcPath, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive);
                        destResult = EnterFindNode(destPath, FindNodeFlags.KeepParentNodeExclusive, srcResult);
                    }

                    if (expectDirectory)
                    {
                        AssertDirectory(srcResult.Node, srcPath);
                    }
                    else
                    {
                        AssertFile(srcResult.Node, srcPath);
                    }
                    AssertDirectory(destResult.Directory, destPath);

                    CheckDestination(destResult.Node);

                    srcResult.Node.DetachFromParent(srcResult.Name);
                    srcResult.Node.AttachToParent(destResult.Directory, destResult.Name);
                }
                finally
                {
                    if (isLockInverted)
                    {
                        ExitFindNode(srcResult);
                        ExitFindNode(destResult);
                    }
                    else
                    {
                        ExitFindNode(destResult);
                        ExitFindNode(srcResult);
                    }
                }
            }
            finally
            {
                if (isSamefolder)
                {
                    ExitFileSystemShared();
                }
                else
                {
                    ExitFileSystemExclusive();
                }
            }
        }


        private void AssertDirectory(FileSystemNode node, UPath srcPath)
        {
            if (node is FileNode)
            {
                throw new IOException($"The source directory `{srcPath}` is a file");
            }
            if (node == null)
            {
                throw NewDirectoryNotFoundException(srcPath);
            }
        }

        private void AssertFile(FileSystemNode node, UPath srcPath)
        {
            if (node == null)
            {
                throw NewFileNotFoundException(srcPath);
            }
        }

        private FileSystemNode TryFindNodeSafe(UPath path)
        {
            EnterFileSystemShared();
            try
            {
                var result = EnterFindNode(path, FindNodeFlags.None);
                try
                {
                    var node = result.Node;
                    return node;
                }
                finally
                {
                    ExitFindNode(result);
                }
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        private FileSystemNode FindNodeSafe(UPath path, bool expectFileOnly)
        {
            var node = TryFindNodeSafe(path);

            if (node == null)
            {
                if (expectFileOnly)
                {
                    throw NewFileNotFoundException(path);
                }
                throw new IOException($"The file or directory `{path}` was not found");
            }

            if (node is DirectoryNode)
            {
                if (expectFileOnly)
                {
                    throw NewFileNotFoundException(path);
                }
            }

            return node;
        }

        private void CreateDirectoryNode(UPath path)
        {
            ExitFindNode(EnterFindNode(path, FindNodeFlags.CreatePathIfNotExist));
        }

        private struct NodeResult
        {
            public NodeResult(DirectoryNode directory, FileSystemNode node, string name, FindNodeFlags flags)
            {
                Directory = directory;
                Node = node;
                Name = name;
                Flags = flags;
            }

            public readonly DirectoryNode Directory;

            public readonly FileSystemNode Node;

            public readonly string Name;

            public readonly FindNodeFlags Flags;
        }

        [Flags]
        private enum FindNodeFlags
        {
            None = 0,

            CreatePathIfNotExist = 1 << 1,

            NodeExclusive = 1 << 2,

            KeepParentNodeExclusive = 1 << 3,

            KeepParentNodeShared = 1 << 4,
        }

        private void ExitFindNode(NodeResult nodeResult)
        {
            var flags = nodeResult.Flags;

            // Unlock first the node
            if (nodeResult.Node != null)
            {
                if ((flags & FindNodeFlags.NodeExclusive) != 0)
                {
                    ExitExclusive(nodeResult.Node);
                }
                else
                {
                    ExitShared(nodeResult.Node);
                }
            }

            if (nodeResult.Directory == null)
            {
                return;
            }

            // Unlock the parent directory if necessary
            if ((flags & FindNodeFlags.KeepParentNodeExclusive) != 0)
            {
                ExitExclusive(nodeResult.Directory);
            }
            else if ((flags & FindNodeFlags.KeepParentNodeShared) != 0)
            {
                ExitShared(nodeResult.Directory);
            }
        }

    
        private NodeResult EnterFindNode(UPath path, FindNodeFlags flags, params NodeResult[] existingNodes)
        {
            return EnterFindNode(path, flags, null, existingNodes);
        }

        private NodeResult EnterFindNode(UPath path, FindNodeFlags flags, FileShare? share, params NodeResult[] existingNodes)
        {
            var result = new NodeResult();

            var sharePath = share ?? FileShare.Read;

            if (path == UPath.Root)
            {
                if ((flags & FindNodeFlags.NodeExclusive) != 0)
                {
                    EnterExclusive(_rootDirectory, path);
                }
                else
                {
                    EnterShared(_rootDirectory, path, sharePath);
                }
                result = new NodeResult(null, _rootDirectory, null, flags);
                return result;
            }

            var isParentWriting = (flags & (FindNodeFlags.CreatePathIfNotExist | FindNodeFlags.KeepParentNodeExclusive)) != 0;

            var parentNode = _rootDirectory;
            var names = path.Split().ToList();

            for (var i = 0; i < names.Count && parentNode != null; i++)
            {
                var name = names[i];
                bool isLast = i + 1 == names.Count;

                bool isParentAlreadylocked = false;

                if (existingNodes.Length > 0)
                {
                    foreach (var existingNode in existingNodes)
                    {
                        if (existingNode.Directory == parentNode || existingNode.Node == parentNode)
                        {
                            // The parent is already locked, so clear the flags
                            if (isLast)
                            {
                                flags = flags & ~(FindNodeFlags.KeepParentNodeShared | FindNodeFlags.KeepParentNodeExclusive);
                            }
                            isParentAlreadylocked = true;
                            break;
                        }
                    }
                }

                if (!isParentAlreadylocked)
                {
                    // TODO: Make it Read lock until last one instead of write lock on all path
                    if (isParentWriting)
                    {
                        EnterExclusiveDirectoryOrBlock(parentNode, path);
                    }
                    else
                    {
                        EnterSharedDirectoryOrBlock(parentNode, path);
                    }
                }

                FileSystemNode subNode;
                bool releaseParent = !isParentAlreadylocked;
                try
                {
                    if (!parentNode.Children.TryGetValue(name, out subNode))
                    {
                        if ((flags & FindNodeFlags.CreatePathIfNotExist) != 0)
                        {
                            subNode = new DirectoryNode(parentNode);
                            parentNode.Children.Add(name, subNode);
                        }
                    }
                    else
                    {
                        if ((flags & FindNodeFlags.CreatePathIfNotExist) != 0 && subNode is FileNode)
                        {
                            throw new IOException($"Cannot create directory `{path}` on an existing file");
                        }
                    }

                    if (isLast)
                    {
                        result = new NodeResult(parentNode, subNode, name, flags);
                        if (subNode != null)
                        {
                            if ((flags & FindNodeFlags.NodeExclusive) != 0)
                            {
                                EnterExclusive(subNode, path);
                            }
                            else
                            {
                                EnterShared(subNode, path, sharePath);
                            }
                        }

                        if ((flags & (FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.KeepParentNodeShared)) != 0)
                        {
                            releaseParent = false;
                            break;
                        }
                    }
                }
                finally
                {
                    if (releaseParent)
                    {
                        if (isParentWriting)
                        {
                            ExitExclusive(parentNode);
                        }
                        else
                        {
                            ExitShared(parentNode);
                        }
                    }
                }

                parentNode = subNode as DirectoryNode;
            }

            return result;
        }

        // ----------------------------------------------
        // Locks internals
        // ----------------------------------------------

        private void EnterFileSystemShared()
        {
            _globalLock.EnterShared(UPath.Root);
        }

        private void ExitFileSystemShared()
        {
            _globalLock.ExitShared();
        }

        private void EnterFileSystemExclusive()
        {
            _globalLock.EnterExclusive();
        }

        private void ExitFileSystemExclusive()
        {
            _globalLock.ExitExclusive();
        }

        private void EnterSharedDirectoryOrBlock(DirectoryNode node, UPath context)
        {
            EnterShared(node, context, true, FileShare.Read);
        }

        private void EnterExclusiveDirectoryOrBlock(DirectoryNode node, UPath context)
        {
            EnterExclusive(node, context, true);
        }

        private void EnterExclusive(FileSystemNode node, UPath context)
        {
            EnterExclusive(node, context, node is DirectoryNode);
        }

        private void EnterShared(FileSystemNode node, UPath context, FileShare share)
        {
            EnterShared(node, context, node is DirectoryNode, share);
        }

        private void EnterShared(FileSystemNode node, UPath context, bool block, FileShare share)
        {
            if (block)
            {
                node.EnterShared(share, context);
            }
            else if (!node.TryEnterShared(share))
            {
                var pathType = node is FileNode ? "file" : "directory";
                throw new IOException($"The {pathType} `{context}` is already used for writing by another thread");
            }
        }

        private void ExitShared(FileSystemNode node)
        {
            node.ExitShared();
        }

        private void EnterExclusive(FileSystemNode node, UPath context, bool block)
        {
            if (block)
            {
                node.EnterExclusive();
            }
            else if(!node.TryEnterExclusive())
            {
                var pathType = node is FileNode ? "file" : "directory";
                throw new IOException($"The {pathType} `{context}` is already locked");
            }
        }

        private void ExitExclusive(FileSystemNode node)
        {
            node.ExitExclusive();
        }

        private void TryLockExclusive(FileSystemNode node, ListFileSystemNodes locks, bool recursive, UPath context)
        {
            if (locks == null) throw new ArgumentNullException(nameof(locks));

            if (node is DirectoryNode)
            {
                var directory = (DirectoryNode)node;
                if (recursive)
                {
                    foreach (var child in directory.Children)
                    {
                        EnterExclusive(child.Value, context);
                        locks.Add(new KeyValuePair<string, FileSystemNode>(child.Key, child.Value));

                        TryLockExclusive(child.Value, locks, true, context / child.Key);
                    }
                }
                else
                {
                    if (directory.Children.Count > 0)
                    {
                        throw new IOException($"The directory `{context}` is not empty");
                    }
                }
            }
        }

        private abstract class FileSystemNode : FileSystemNodeReadWriteLock
        {
            protected FileSystemNode(DirectoryNode parent, FileSystemNode copyNode)
            {
                Parent = parent;
                if (copyNode != null && copyNode.Attributes != 0)
                {
                    Attributes = copyNode.Attributes;
                }
                CreationTime = DateTime.Now;
                LastWriteTime = copyNode?.LastWriteTime ?? CreationTime;
                LastAccessTime = copyNode?.LastAccessTime ?? CreationTime;
            }

            public DirectoryNode Parent { get; private set; }

            public FileAttributes Attributes { get; set; }

            public DateTime CreationTime { get; set; }

            public DateTime LastWriteTime { get; set; }

            public DateTime LastAccessTime { get; set; }

            public bool IsDisposed { get; set; }

            public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;

            public void DetachFromParent(string name)
            {
                Debug.Assert(IsLocked);
                var parent = Parent;
                Debug.Assert(parent.IsLocked);

                parent.Children.Remove(name);
                Parent = null;
            }

            public void AttachToParent(DirectoryNode parentNode, string name)
            {
                if (parentNode == null) throw new ArgumentNullException(nameof(parentNode));
                Debug.Assert(parentNode.IsLocked);
                Debug.Assert(IsLocked);
                Debug.Assert(Parent == null);

                Parent = parentNode;
                Parent.Children.Add(name, this);
            }

            public void Dispose()
            {
                Debug.Assert(IsLocked);
                // In order to issue a Dispose, we need to have control on this node
                IsDisposed = true;
            }
        }

        private class ListFileSystemNodes : List<KeyValuePair<string, FileSystemNode>>, IDisposable
        {
            private readonly MemoryFileSystem _fs;


            public ListFileSystemNodes(MemoryFileSystem fs)
            {
                Debug.Assert(fs != null);
                _fs = fs;
            }

            public void Dispose()
            {
                for (var i = this.Count - 1; i >= 0; i--)
                {
                    var node = this[i];
                    _fs.ExitExclusive(node.Value);
                }
                Clear();
            }
        }

        private class DirectoryNode : FileSystemNode
        {
            private readonly Dictionary<string, FileSystemNode> _children;

            public DirectoryNode() : base(null, null)
            {
                _children = new Dictionary<string, FileSystemNode>();
            }

            public DirectoryNode(DirectoryNode parent) : base(parent, null)
            {
                Debug.Assert(parent != null);
                _children = new Dictionary<string, FileSystemNode>();
            }

            public Dictionary<string, FileSystemNode> Children
            {
                get
                {
                    Debug.Assert(IsLocked);
                    return _children;
                }
            }
        }

        private class FileNode : FileSystemNode
        {
            public FileNode(DirectoryNode parent) : base(parent, null)
            {
                Content = new FileContent();
                Attributes = FileAttributes.Archive;
            }

            public FileNode(DirectoryNode parent, FileNode copyNode) : base(parent, copyNode)
            {
                Content = copyNode != null ? new FileContent(copyNode.Content) : new FileContent();
            }

            public FileContent Content { get; }
        }

        private class FileContent
        {
            private readonly MemoryStream _stream;

            public FileContent()
            {
                _stream = new MemoryStream();
            }

            public FileContent(FileContent copy)
            {
                var length = copy.Length;
                _stream = new MemoryStream(length <= Int32.MaxValue ? (int)length : Int32.MaxValue);
                CopyFrom(copy);
            }

            public byte[] ToArray()
            {
                lock (this)
                {
                    return _stream.ToArray();
                }
            }

            public void CopyFrom(FileContent copy)
            {
                lock (this)
                {
                    var length = copy.Length;
                    var buffer = copy.ToArray();
                    _stream.Position = 0;
                    _stream.Write(buffer, 0, buffer.Length);
                    _stream.Position = 0;
                    _stream.SetLength(length);
                }
            }

            public int Read(long position, byte[] buffer, int offset, int count)
            {
                lock (this)
                {
                    _stream.Position = position;
                    return _stream.Read(buffer, offset, count);
                }
            }
            public void Write(long position, byte[] buffer, int offset, int count)
            {
                lock (this)
                {
                    _stream.Position = position;
                    _stream.Write(buffer, offset, count);
                }
            }

            public void SetPosition(long position)
            {
                lock (this)
                {
                    _stream.Position = position;
                }
            }

            public long Length
            {
                get
                {
                    lock (this)
                    {
                        return _stream.Length;
                    }
                }
                set
                {
                    lock (this)
                    {
                        _stream.SetLength(value);
                    }
                }
            }
        }

        private sealed class MemoryFileStream : Stream
        {
            private readonly MemoryFileSystem _fs;
            private readonly FileNode _fileNode;
            private readonly bool _canRead;
            private readonly bool _canWrite;
            private readonly bool _isExclusive;
            private int _isDisposed;
            private long _position;

            public MemoryFileStream(MemoryFileSystem fs, FileNode fileNode, bool canWrite, bool isExclusive)
            {
                Debug.Assert(fs != null);
                Debug.Assert(fileNode != null);
                Debug.Assert(fileNode.IsLocked);
                _fs = fs;
                _fileNode = fileNode;
                _canWrite = canWrite;
                _canRead = true;
                _isExclusive = isExclusive;
                _position = 0;
            }

            public override bool CanRead => _isDisposed == 0 && _canRead;

            public override bool CanSeek => _isDisposed == 0;

            public override bool CanWrite => _isDisposed == 0 && _canWrite;

            public override long Length
            {
                get
                {
                    CheckNotDisposed();
                    return _fileNode.Content.Length;
                }
            }

            public override long Position
            {
                get
                {
                    CheckNotDisposed();
                    return _position;
                }

                set
                {
                    CheckNotDisposed();
                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException("The position cannot be negative");
                    }
                    _position = value;
                    _fileNode.Content.SetPosition(_position);
                }
            }

            ~MemoryFileStream()
            {
                Dispose(false);
            }

            protected override void Dispose(bool disposing)
            {
                if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
                {
                    return;
                }

                if (_isExclusive)
                {
                    _fs.ExitExclusive(_fileNode);
                }
                else
                {
                    _fs.ExitShared(_fileNode);
                }

                base.Dispose(disposing);
            }

            public override void Flush()
            {
                CheckNotDisposed();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                CheckNotDisposed();
                int readCount = _fileNode.Content.Read(_position, buffer, offset, count);
                _position += readCount;
                _fileNode.LastAccessTime = DateTime.Now;
                return readCount;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                CheckNotDisposed();
                var newPosition = offset;

                switch (origin)
                {
                    case SeekOrigin.Current:
                        newPosition += _position;
                        break;

                    case SeekOrigin.End:
                        newPosition += _fileNode.Content.Length;
                        break;
                }

                if (newPosition < 0)
                {
                    throw new IOException("An attempt was made to move the file pointer before the beginning of the file");
                }

                return _position = newPosition;
            }

            public override void SetLength(long value)
            {
                CheckNotDisposed();
                _fileNode.Content.Length = value;

                var time = DateTime.Now;
                _fileNode.LastAccessTime = time;
                _fileNode.LastWriteTime = time;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                CheckNotDisposed();
                _fileNode.Content.Write(_position, buffer, offset, count);
                _position += count;

                var time = DateTime.Now;
                _fileNode.LastAccessTime = time;
                _fileNode.LastWriteTime = time;
            }


            private void CheckNotDisposed()
            {
                if (_isDisposed > 0)
                {
                    throw new ObjectDisposedException("Cannot access a closed file.");
                }
            }
        }

        /// <summary>
        /// Internal class used to synchronize shared-exclusive access to a <see cref="FileSystemNode"/>
        /// </summary>
        private class FileSystemNodeReadWriteLock
        {
            // _sharedCount  < 0 => This is an exclusive lock (_sharedCount == -1)
            // _sharedCount == 0 => No lock
            // _sharedCount  > 0 => This is a shared lock
            private int _sharedCount;

            private FileShare? _shared;

            internal bool IsLocked => _sharedCount != 0;

            public void EnterShared(UPath context)
            {
                EnterShared(FileShare.Read, context);
            }

            public void EnterShared(FileShare share, UPath context)
            {
                Monitor.Enter(this);
                try
                {
                    while (_sharedCount < 0)
                    {
                        Monitor.Wait(this);
                    }

                    if (_shared.HasValue)
                    {
                        var currentShare = _shared.Value;
                        // The previous share must be a superset of the shared being asked
                        if ((share & currentShare) != share)
                        {
                            throw new UnauthorizedAccessException($"Cannot access shared resource path `{context}` with shared access`{share}` while current is `{currentShare}`");
                        }
                    }
                    else
                    {
                        _shared = share;
                    }

                    _sharedCount++;
                    Monitor.PulseAll(this);
                }
                finally
                {
                    Monitor.Exit(this);
                }
            }

            public void ExitShared()
            {
                Monitor.Enter(this);
                try
                {
                    Debug.Assert(_sharedCount > 0);
                    _sharedCount--;
                    if (_sharedCount == 0)
                    {
                        _shared = null;
                    }
                    Monitor.PulseAll(this);
                }
                finally
                {
                    Monitor.Exit(this);
                }
            }

            public void EnterExclusive()
            {
                Monitor.Enter(this);
                try
                {
                    while (_sharedCount != 0)
                    {
                        Monitor.Wait(this);
                    }
                    _sharedCount  = -1;
                    Monitor.PulseAll(this);
                }
                finally
                {
                    Monitor.Exit(this);
                }
            }

            public bool TryEnterShared(FileShare share)
            {
                Monitor.Enter(this);
                try
                {
                    if (_sharedCount < 0)
                    {
                        return false;
                    }

                    if (_shared.HasValue)
                    {
                        var currentShare = _shared.Value;
                        if ((share & currentShare) != share)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        _shared = share;
                    }

                    _sharedCount++;
                    Monitor.PulseAll(this);
                }
                finally
                {
                    Monitor.Exit(this);
                }
                return true;
            }

            public bool TryEnterExclusive()
            {
                Monitor.Enter(this);
                try
                {
                    if (_sharedCount != 0)
                    {
                        return false;
                    }
                    _sharedCount = -1;
                    Monitor.PulseAll(this);
                }
                finally
                {
                    Monitor.Exit(this);
                }
                return true;
            }
            public void ExitExclusive()
            {
                Monitor.Enter(this);
                try
                {
                    Debug.Assert(_sharedCount < 0);
                    _sharedCount = 0;
                    Monitor.PulseAll(this);
                }
                finally
                {
                    Monitor.Exit(this);
                }
            }

            public override string ToString()
            {
                return _sharedCount < 0 ? "exclusive lock" : _sharedCount > 0 ? $"shared lock ({_sharedCount})" : "no lock";
            }
        }
    }
}