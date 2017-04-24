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
        // The locking strategy is based on https://www.kernel.org/doc/Documentation/filesystems/directory-locking

        private readonly DirectoryNode _rootDirectory;
        private readonly FileSystemNodeReadWriteLock _globalLock;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryFileSystem"/> class.
        /// </summary>
        public MemoryFileSystem()
        {
            _rootDirectory = new DirectoryNode(null);
            _globalLock = new FileSystemNodeReadWriteLock();
        }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        protected override void CreateDirectoryImpl(PathInfo path)
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

        protected override bool DirectoryExistsImpl(PathInfo path)
        {
            if (path == PathInfo.Root)
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

        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            MoveFileOrDirectory(srcPath, destPath, true);
        }

        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            EnterFileSystemShared();
            try
            {
                var result = EnterFindNode(path, FindNodeFlags.KeepParentNodeWrite | FindNodeFlags.NodeWrite);

                bool deleteRootDirectory = false;
                try
                {
                    AssertDirectory(result.Node, path);

                    if ((result.Node.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        throw new IOException($"The path `{path}` is readonly and cannot be deleted");
                    }

                    using (var locks = new ListFileSystemNodes(this))
                    {
                        TryLockWrite(result.Node, locks, isRecursive, path);

                        // Check that files are not readonly
                        foreach (var lockFile in locks)
                        {
                            var node = lockFile.Value;

                            if ((node.Attributes & FileAttributes.ReadOnly) != 0)
                            {
                                throw new IOException($"The path `{path}` contains readonly elements and cannot be deleted");
                            }
                        }

                        // We remove all elements
                        for (var i = locks.Count - 1; i >= 0; i--)
                        {
                            var node = locks[i];
                            locks.RemoveAt(i);
                            node.Value.DetachFromParent(node.Key);
                            node.Value.Dispose();

                            ExitWrite(node.Value);
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

        protected override void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite)
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
                        throw new ArgumentException($"Cannot copy file. The path `{srcPath}` is a directory", nameof(srcPath));
                    }
                    if (srcNode == null)
                    {
                        throw new FileNotFoundException($"The file `{srcPath}` was not found");
                    }

                    var destResult = EnterFindNode(destPath, FindNodeFlags.KeepParentNodeWrite | FindNodeFlags.NodeWrite);
                    var destFileName = destResult.Name;
                    var destDirectory = destResult.Directory;
                    var destNode = destResult.Node;
                    try
                    {
                        // The dest file may exist
                        if (destDirectory == null)
                        {
                            throw new DirectoryNotFoundException($"The directory from the path `{destPath}` was not found");
                        }
                        if (destNode is DirectoryNode)
                        {
                            throw new ArgumentException($"Cannot copy file. The path `{destPath}` is a directory", nameof(destPath));
                        }

                        // If the destination is empty, we need to create it
                        if (destNode == null)
                        {
                            var newFileNode = new FileNode(destDirectory, (FileNode) srcNode);
                            destDirectory.Children.Add(destFileName, newFileNode);
                        }
                        else if (overwrite)
                        {
                            var destFileNode = (FileNode) destNode;
                            destFileNode.Content = new FileContent(((FileNode) srcNode).Content);
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
                            ExitWrite(destNode);
                        }

                        if (destDirectory != null)
                        {
                            ExitWrite(destDirectory);
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

            // Get the directories of src/dest/backup
            var parentSrcPath = srcPath.GetDirectory();
            var parentDestPath = destPath.GetDirectory();
            var parentDestBackupPath = destBackupPath.IsNull ? new PathInfo() : destBackupPath.GetDirectory();

            // Simple case: src/dest/backup in the same folder
            var isSameFolder = parentSrcPath == parentDestPath && (destBackupPath.IsNull || (parentDestBackupPath == parentSrcPath));
            // Else at least one folder is different. This is a rename semantic (as per the locking guidelines)

            var paths = new List<KeyValuePair<PathInfo, int>>
            {
                new KeyValuePair<PathInfo, int>(srcPath, 0),
                new KeyValuePair<PathInfo, int>(destPath, 1)
            };
            if (!destBackupPath.IsNull)
            {
                paths.Add(new KeyValuePair<PathInfo, int>(destBackupPath, 2));
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
                        var flags = FindNodeFlags.KeepParentNodeWrite;
                        if (pathPair.Value != 2)
                        {
                            flags |= FindNodeFlags.NodeWrite;
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

        protected override long GetFileLengthImpl(PathInfo path)
        {
            EnterFileSystemShared();
            try
            {
                return ((FileNode) FindNodeSafe(path, true)).Content.Length;
            }
            finally
            {
                ExitFileSystemShared();
            }
        }

        protected override bool FileExistsImpl(PathInfo path)
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

        protected override void MoveFileImpl(PathInfo srcPath, PathInfo destPath)
        {
            MoveFileOrDirectory(srcPath, destPath, false);
        }

        protected override void DeleteFileImpl(PathInfo path)
        {
            EnterFileSystemShared();
            try
            {
                var result = EnterFindNode(path, FindNodeFlags.KeepParentNodeWrite | FindNodeFlags.NodeWrite);
                try
                {
                    var srcNode = result.Node;
                    if (srcNode == null)
                    {
                        throw new FileNotFoundException($"The file `{path}` was not found");
                    }
                    if (srcNode is DirectoryNode)
                    {
                        throw new IOException($"The path `{path}` is a directory");
                    }
                    if ((srcNode.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        throw new IOException($"The path `{path}` is a readonly file");
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

        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (share != FileShare.None)
            {
                throw new NotSupportedException($"The share `{share}` is not supported. Only none is supported");
            }
            var isWriting = (access & FileAccess.Write) != 0;


            EnterFileSystemShared();
            DirectoryNode parentDirectory = null;
            FileNode fileNodeToRelease = null;
            try
            {
                var result = EnterFindNode(path, (isWriting ? FindNodeFlags.NodeWrite : FindNodeFlags.None) | FindNodeFlags.KeepParentNodeWrite);
                if (result.Directory == null)
                {
                    ExitFindNode(result);
                    throw new DirectoryNotFoundException($"The directory from the path `{path}` was not found");
                }

                if (result.Node is DirectoryNode)
                {
                    ExitFindNode(result);
                    throw new IOException($"The path `{path}` is a directory");
                }

                var filename = result.Name;
                parentDirectory = result.Directory;
                var srcNode = result.Node;

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
                    if (isWriting)
                    {
                        EnterWrite(fileNode, path);
                    }
                    else
                    {
                        EnterRead(fileNode, path);
                    }
                }
                else
                {
                    if (fileNode == null)
                    {
                        throw new FileNotFoundException($"The file `{path}` was not found");
                    }

                    ExitWrite(parentDirectory);
                    parentDirectory = null;
                }

                // TODO: Add checks between mode and access

                // Create a memory file stream
                var stream = new MemoryFileStream(this, fileNode, isWriting);
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
                    if (isWriting)
                    {
                        ExitWrite(fileNodeToRelease);
                    }
                    else
                    {
                        ExitRead(fileNodeToRelease);
                    }
                }
                if (parentDirectory != null)
                {
                    ExitWrite(parentDirectory);
                }
                ExitFileSystemShared();
            }
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

            var foldersToProcess = new Queue<PathInfo>();
            foldersToProcess.Enqueue(path);

            var entries = new List<PathInfo>();
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

                        var directory = (DirectoryNode) result.Node;
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

        private void MoveFileOrDirectory(PathInfo srcPath, PathInfo destPath, bool expectDirectory)
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
                        destResult = EnterFindNode(destPath, FindNodeFlags.KeepParentNodeWrite);
                        srcResult = EnterFindNode(srcPath, FindNodeFlags.KeepParentNodeWrite | FindNodeFlags.NodeWrite, destResult);
                    }
                    else
                    {
                        srcResult = EnterFindNode(srcPath, FindNodeFlags.KeepParentNodeWrite | FindNodeFlags.NodeWrite);
                        destResult = EnterFindNode(destPath, FindNodeFlags.KeepParentNodeWrite, srcResult);
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


        private void AssertDirectory(FileSystemNode node, PathInfo srcPath)
        {
            if (node is FileNode)
            {
                throw new IOException($"The source directory `{srcPath}` is a file");
            }
            if (node == null)
            {
                throw new DirectoryNotFoundException($"The source directory `{srcPath}` was not found");
            }
        }

        private void AssertFile(FileSystemNode node, PathInfo srcPath)
        {
            if (node is DirectoryNode)
            {
                throw new IOException($"The source file `{srcPath}` is a directory");
            }
            if (node == null)
            {
                throw new FileNotFoundException($"The source file `{srcPath}` was not found");
            }
        }

        private FileSystemNode FindNodeSafe(PathInfo path, bool expectFileOnly)
        {
            EnterFileSystemShared();
            try
            {
                var result = EnterFindNode(path, FindNodeFlags.None);
                try
                {
                    var node = result.Node;

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

        private void CreateDirectoryNode(PathInfo path)
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

            NodeWrite = 1 << 2,

            KeepParentNodeWrite = 1 << 3,

            KeepParentNodeRead = 1 << 4,
        }

        private void ExitFindNode(NodeResult nodeResult)
        {
            var flags = nodeResult.Flags;

            // Unlock first the node
            if (nodeResult.Node != null)
            {
                if ((flags & FindNodeFlags.NodeWrite) != 0)
                {
                    ExitWrite(nodeResult.Node);
                }
                else
                {
                    ExitRead(nodeResult.Node);
                }
            }

            if (nodeResult.Directory == null)
            {
                return;
            }

            // Unlock the parent directory if necessary
            if ((flags & FindNodeFlags.KeepParentNodeWrite) != 0)
            {
                ExitWrite(nodeResult.Directory);
            }
            else if ((flags & FindNodeFlags.KeepParentNodeRead) != 0)
            {
                ExitRead(nodeResult.Directory);
            }
        }

        private NodeResult EnterFindNode(PathInfo path, FindNodeFlags flags, params NodeResult[] existingNodes)
        {
            var result = new NodeResult();

            if (path == PathInfo.Root)
            {
                if ((flags & FindNodeFlags.NodeWrite) != 0)
                {
                    EnterWrite(_rootDirectory, path);
                }
                else
                {
                    EnterRead(_rootDirectory, path);
                }
                result = new NodeResult(null, _rootDirectory, null, flags);
                return result;
            }

            var isParentWriting = (flags & (FindNodeFlags.CreatePathIfNotExist | FindNodeFlags.KeepParentNodeWrite)) != 0;

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
                                flags = flags & ~(FindNodeFlags.KeepParentNodeRead | FindNodeFlags.KeepParentNodeWrite);
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
                        EnterWriteDirectoryOrBlock(parentNode, path);
                    }
                    else
                    {
                        EnterReadDirectoryOrBlock(parentNode, path);
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
                            if ((flags & FindNodeFlags.NodeWrite) != 0)
                            {
                                EnterWrite(subNode, path);
                            }
                            else
                            {
                                EnterRead(subNode, path);
                            }
                        }

                        if ((flags & (FindNodeFlags.KeepParentNodeWrite | FindNodeFlags.KeepParentNodeRead)) != 0)
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
                            ExitWrite(parentNode);
                        }
                        else
                        {
                            ExitRead(parentNode);
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
            _globalLock.EnterShared();
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

        private void EnterReadDirectoryOrBlock(DirectoryNode node, PathInfo context)
        {
            EnterRead(node, context, true);
        }

        private void EnterWriteDirectoryOrBlock(DirectoryNode node, PathInfo context)
        {
            EnterWrite(node, context, true);
        }

        private void EnterWrite(FileSystemNode node, PathInfo context)
        {
            EnterWrite(node, context, node is DirectoryNode);
        }

        private void EnterRead(FileSystemNode node, PathInfo context)
        {
            EnterRead(node, context, node is DirectoryNode);
        }

        private void EnterRead(FileSystemNode node, PathInfo context, bool block)
        {
            if (block)
            {
                node.EnterShared();
            }
            else if (!node.TryEnterShared())
            {
                var pathType = node is FileNode ? "file" : "directory";
                throw new IOException($"The {pathType} `{context}` is already used for writing by another thread");
            }
        }

        private void ExitRead(FileSystemNode node)
        {
            node.ExitShared();
        }

        private void EnterWrite(FileSystemNode node, PathInfo context, bool block)
        {
            if (block)
            {
                node.EnterExclusive();
            }
            else if(!node.TryEnterExclusive())
            {
                var pathType = node is FileNode ? "file" : "directory";
                throw new IOException($"The {pathType} `{context}` is already used by another thread");
            }
        }

        private void ExitWrite(FileSystemNode node)
        {
            node.ExitExclusive();
        }

        private void TryLockWrite(FileSystemNode node, ListFileSystemNodes locks, bool recursive, PathInfo context)
        {
            if (locks == null) throw new ArgumentNullException(nameof(locks));

            if (node is DirectoryNode)
            {
                var directory = (DirectoryNode)node;
                if (recursive)
                {
                    foreach (var child in directory.Children)
                    {
                        EnterWrite(child.Value, context);
                        locks.Add(new KeyValuePair<string, FileSystemNode>(child.Key, child.Value));

                        TryLockWrite(child.Value, locks, true, context / child.Key);
                    }
                }
                else
                {
                    if (directory.Children.Count > 0)
                    {
                        throw new IOException($"The directory `{context}` contains is not empty");
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
                CreationTime = copyNode?.CreationTime ?? DateTime.Now;
                LastWriteTime = copyNode?.LastWriteTime ?? CreationTime;
                LastAccessTime = copyNode?.LastAccessTime ?? CreationTime;
            }

            public DirectoryNode Parent { get; private set; }

            public FileAttributes Attributes { get; set; }

            public DateTime CreationTime { get; set; }

            public DateTime LastWriteTime { get; set; }

            public DateTime LastAccessTime { get; set; }

            public bool IsDisposed { get; set; }

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

            private void CheckAlive(PathInfo context)
            {
                if (IsDisposed)
                {
                    throw new InvalidOperationException($"The path `{context}` does not exist anymore");
                }
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
                    _fs.ExitWrite(node.Value);
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
            }

            public bool IsRoot { get; }

            public bool Exists => IsRoot || Parent != null;

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
            private FileContent _content;

            public FileNode(DirectoryNode parent) : base(parent, null)
            {
                _content = new FileContent();
                Attributes = FileAttributes.Archive;
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
            private readonly MemoryFileSystem _fs;
            private readonly MemoryStream _stream;
            private readonly FileNode _fileNode;
            private readonly bool _canRead;
            private readonly bool _canWrite;

            public MemoryFileStream(MemoryFileSystem fs, FileNode fileNode, bool canWrite)
            {
                if (fileNode == null) throw new ArgumentNullException(nameof(fileNode));
                Debug.Assert(fileNode.IsLocked);
                Debug.Assert(fs != null);
                _fs = fs;
                _fileNode = fileNode;
                _canWrite = canWrite;
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

                    _fs.ExitWrite(_fileNode);
                }
                else
                {
                    _fs.ExitRead(_fileNode);
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

        /// <summary>
        /// Internal class used to synchronize shared-exclusive access to a <see cref="FileSystemNode"/>
        /// </summary>
        private class FileSystemNodeReadWriteLock
        {
            // _sharedCount  < 0 => This is an exclusive lock (_sharedCount == -1)
            // _sharedCount == 0 => No lock
            // _sharedCount  > 0 => This is a shared lock
            private int _sharedCount;

            internal bool IsLocked => _sharedCount != 0;

            public void EnterShared()
            {
                Monitor.Enter(this);
                try
                {
                    while (_sharedCount < 0)
                    {
                        Monitor.Wait(this);
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

            public bool TryEnterShared()
            {
                Monitor.Enter(this);
                try
                {
                    if (_sharedCount < 0)
                    {
                        return false;
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