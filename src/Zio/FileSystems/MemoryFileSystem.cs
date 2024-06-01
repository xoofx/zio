// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;

using static Zio.FileSystemExceptionHelper;

namespace Zio.FileSystems;

/// <summary>
/// Provides an in-memory <see cref="IFileSystem"/> compatible with the way a real <see cref="PhysicalFileSystem"/> is working.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "(),nq}")]
[DebuggerTypeProxy(typeof(DebuggerProxy))]
public class MemoryFileSystem : FileSystem
{
    // The locking strategy is based on https://www.kernel.org/doc/Documentation/filesystems/directory-locking

    private readonly DirectoryNode _rootDirectory;
    private readonly FileSystemNodeReadWriteLock _globalLock;
    private readonly object _dispatcherLock;
    private FileSystemEventDispatcher<Watcher>? _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryFileSystem"/> class.
    /// </summary>
    public MemoryFileSystem()
    {
        _rootDirectory = new DirectoryNode(this);
        _globalLock = new FileSystemNodeReadWriteLock();
        _dispatcherLock = new object();
    }

    /// <summary>
    /// Constructor used for deep cloning.
    /// </summary>
    /// <param name="copyFrom">The MemoryFileStream to clone from</param>
    protected MemoryFileSystem(MemoryFileSystem copyFrom)
    {
        if (copyFrom is null) throw new ArgumentNullException(nameof(copyFrom));
        Debug.Assert(copyFrom._globalLock.IsLocked);
        _rootDirectory = (DirectoryNode)copyFrom._rootDirectory.Clone(null, null);
        _globalLock = new FileSystemNodeReadWriteLock();
        _dispatcherLock = new object();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            TryGetDispatcher()?.Dispose();
        }
    }

    /// <summary>
    /// Deep clone of this filesystem
    /// </summary>
    /// <returns>A deep clone of this filesystem</returns>
    public MemoryFileSystem Clone()
    {
        EnterFileSystemExclusive();
        try
        {
            return CloneImpl();
        }
        finally
        {
            ExitFileSystemExclusive();
        }
    }
    
    protected virtual MemoryFileSystem CloneImpl()
    {
        return new MemoryFileSystem(this);
    }

    protected override string DebuggerDisplay()
    {
        return $"{base.DebuggerDisplay()} {_rootDirectory.DebuggerDisplay()}";
    }

    // ----------------------------------------------
    // Directory API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override void CreateDirectoryImpl(UPath path)
    {
        EnterFileSystemShared();
        try
        {
            CreateDirectoryNode(path);

            TryGetDispatcher()?.RaiseCreated(path);
        }
        finally
        {
            ExitFileSystemShared();
        }
    }

    /// <inheritdoc />
    protected override bool DirectoryExistsImpl(UPath path)
    {
        if (path == UPath.Root)
        {
            return true;
        }

        EnterFileSystemShared();
        try
        {
            // NodeCheck doesn't take a lock, on the return node
            // but allows us to check if it is a directory or a file
            var result = EnterFindNode(path, FindNodeFlags.NodeCheck);
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

    /// <inheritdoc />
    protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
    {
        MoveFileOrDirectory(srcPath, destPath, true);
    }

    /// <inheritdoc />
    protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
    {
        EnterFileSystemShared();
        try
        {
            var result = EnterFindNode(path, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive);

            bool deleteRootDirectory = false;
            try
            {
                ValidateDirectory(result.Node, path);

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
                        var lockFile = locks[i];
                        locks.RemoveAt(i);
                        lockFile.Value.DetachFromParent();
                        lockFile.Value.Dispose();

                        ExitExclusive(lockFile.Value);
                    }
                }
                deleteRootDirectory = true;
            }
            finally
            {
                if (deleteRootDirectory && result.Node != null)
                {
                    result.Node.DetachFromParent();
                    result.Node.Dispose();

                    TryGetDispatcher()?.RaiseDeleted(path);
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

    /// <inheritdoc />
    protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
    {
        EnterFileSystemShared();
        try
        {
            var srcResult = EnterFindNode(srcPath, FindNodeFlags.NodeShared);
            try
            {
                // The source file must exist
                var srcNode = srcResult.Node;
                if (srcNode is DirectoryNode)
                {
                    throw new UnauthorizedAccessException($"Cannot copy file. The path `{srcPath}` is a directory");
                }
                if (srcNode is null)
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
                    if (destDirectory is null)
                    {
                        throw NewDirectoryNotFoundException(destPath);
                    }
                    if (destNode is DirectoryNode)
                    {
                        throw new IOException($"The target file `{destPath}` is a directory, not a file.");
                    }

                    // If the destination is empty, we need to create it
                    if (destNode is null)
                    {
                        // Constructor copies and attaches to directory for us
                        var newFileNode = new FileNode(this, destDirectory, destFileName, (FileNode)srcNode);

                        TryGetDispatcher()?.RaiseCreated(destPath);
                        TryGetDispatcher()?.RaiseChange(destPath);
                    }
                    else if (overwrite)
                    {
                        if (destNode.IsReadOnly)
                        {
                            throw new UnauthorizedAccessException($"Access to path `{destPath}` is denied.");
                        }
                        var destFileNode = (FileNode)destNode;
                        destFileNode.Content.CopyFrom(((FileNode)srcNode).Content);

                        TryGetDispatcher()?.RaiseChange(destPath);
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

    /// <inheritdoc />
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
                    else
                    {
                        flags |= FindNodeFlags.NodeShared;
                    }
                    results[pathPair.Value] = EnterFindNode(pathPair.Key, flags, results);
                }

                var srcResult = results[0];
                var destResult = results[1];

                ValidateFile(srcResult.Node, srcPath);
                ValidateFile(destResult.Node, destPath);

                if (!destBackupPath.IsNull)
                {
                    var backupResult = results[2];
                    ValidateDirectory(backupResult.Directory, destPath);

                    if (backupResult.Node != null)
                    {
                        ValidateFile(backupResult.Node, destBackupPath);
                        backupResult.Node.DetachFromParent();
                        backupResult.Node.Dispose();

                        TryGetDispatcher()?.RaiseDeleted(destBackupPath);
                    }

                    destResult.Node.DetachFromParent();
                    destResult.Node.AttachToParent(backupResult.Directory!, backupResult.Name!);

                    TryGetDispatcher()?.RaiseRenamed(destBackupPath, destPath);
                }
                else
                {
                    destResult.Node.DetachFromParent();
                    destResult.Node.Dispose();

                    TryGetDispatcher()?.RaiseDeleted(destPath);
                }

                srcResult.Node.DetachFromParent();
                srcResult.Node.AttachToParent(destResult.Directory!, destResult.Name!);

                TryGetDispatcher()?.RaiseRenamed(destPath, srcPath);
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override bool FileExistsImpl(UPath path)
    {
        EnterFileSystemShared();
        try
        {
            // NodeCheck doesn't take a lock, on the return node
            // but allows us to check if it is a directory or a file
            var result = EnterFindNode(path, FindNodeFlags.NodeCheck);
            ExitFindNode(result);
            return result.Node is FileNode;
        }
        finally
        {
            ExitFileSystemShared();
        }
    }

    /// <inheritdoc />
    protected override void MoveFileImpl(UPath srcPath, UPath destPath)
    {
        MoveFileOrDirectory(srcPath, destPath, false);
    }

    /// <inheritdoc />
    protected override void DeleteFileImpl(UPath path)
    {
        EnterFileSystemShared();
        try
        {
            var result = EnterFindNode(path, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive);
            try
            {
                var srcNode = result.Node;
                if (srcNode is null)
                {
                    // If the file to be deleted does not exist, no exception is thrown.
                    return;
                }
                if (srcNode is DirectoryNode || srcNode.IsReadOnly)
                {
                    throw new UnauthorizedAccessException($"Access to path `{path}` is denied.");
                }

                srcNode.DetachFromParent();
                srcNode.Dispose();

                TryGetDispatcher()?.RaiseDeleted(path);
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

    /// <inheritdoc />
    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
    {
        if (mode == FileMode.Append && (access & FileAccess.Read) != 0)
        {
            throw new ArgumentException("Combining FileMode: Append with FileAccess: Read is invalid.", nameof(access));
        }

        var isReading = (access & FileAccess.Read) != 0;
        var isWriting = (access & FileAccess.Write) != 0;
        var isExclusive = share == FileShare.None;

        EnterFileSystemShared();
        DirectoryNode? parentDirectory = null;
        FileNode? fileNodeToRelease = null;
        try
        {
            var result = EnterFindNode(path, (isExclusive ? FindNodeFlags.NodeExclusive : FindNodeFlags.NodeShared) | FindNodeFlags.KeepParentNodeExclusive, share);
            if (result.Directory is null)
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

            var fileNode = (FileNode)srcNode!;

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
                    throw NewDestinationFileExistException(path);
                }

                fileNode = new FileNode(this, parentDirectory, filename, null);

                TryGetDispatcher()?.RaiseCreated(path);

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
                if (fileNode is null)
                {
                    throw NewFileNotFoundException(path);
                }

                ExitExclusive(parentDirectory);
                parentDirectory = null;
            }

            // TODO: Add checks between mode and access

            // Create a memory file stream
            var stream = new MemoryFileStream(this, fileNode, isReading, isWriting, isExclusive);
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
    {
        // We don't store the attributes Normal or directory
        // As they are returned by GetAttributes and we don't want
        // to duplicate the information with the type inheritance (FileNode or DirectoryNode)
        attributes &= ~FileAttributes.Normal;
        attributes &= ~FileAttributes.Directory;

        var node = FindNodeSafe(path, false);
        node.Attributes = attributes;

        TryGetDispatcher()?.RaiseChange(path);
    }

    /// <inheritdoc />
    protected override DateTime GetCreationTimeImpl(UPath path)
    {
        return TryFindNodeSafe(path)?.CreationTime ?? DefaultFileTime;
    }

    /// <inheritdoc />
    protected override void SetCreationTimeImpl(UPath path, DateTime time)
    {
        FindNodeSafe(path, false).CreationTime = time;

        TryGetDispatcher()?.RaiseChange(path);
    }

    /// <inheritdoc />
    protected override DateTime GetLastAccessTimeImpl(UPath path)
    {
        return TryFindNodeSafe(path)?.LastAccessTime ?? DefaultFileTime;
    }

    /// <inheritdoc />
    protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
    {
        FindNodeSafe(path, false).LastAccessTime = time;

        TryGetDispatcher()?.RaiseChange(path);
    }

    /// <inheritdoc />
    protected override DateTime GetLastWriteTimeImpl(UPath path)
    {
        return TryFindNodeSafe(path)?.LastWriteTime ?? DefaultFileTime;
    }

    /// <inheritdoc />
    protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
    {
        FindNodeSafe(path, false).LastWriteTime = time;

        TryGetDispatcher()?.RaiseChange(path);
    }

    /// <inheritdoc />
    protected override void CreateSymbolicLinkImpl(UPath path, UPath pathToTarget)
    {
        throw new NotSupportedException("Symbolic links are not supported by MemoryFileSystem");
    }

    /// <inheritdoc />
    protected override bool TryResolveLinkTargetImpl(UPath linkPath, out UPath resolvedPath)
    {
        resolvedPath = default;
        return false;
    }

    // ----------------------------------------------
    // Search API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        var search = SearchPattern.Parse(ref path, ref searchPattern);

        var foldersToProcess = new List<UPath>();
        foldersToProcess.Add(path);

        var entries = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);
        while (foldersToProcess.Count > 0)
        {
            var directoryPath = foldersToProcess[0];
            foldersToProcess.RemoveAt(0);
            int dirIndex = 0;
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
                var result = EnterFindNode(directoryPath, FindNodeFlags.NodeShared);
                try
                {
                    if (directoryPath == path)
                    {
                        // The first folder must be a directory, if it is not, throw an error
                        ValidateDirectory(result.Node, directoryPath);
                    }
                    else
                    {
                        // Might happen during the time a DirectoryNode is enqueued into foldersToProcess
                        // and the time we are going to actually visit it, it might have been
                        // removed in the meantime, so we make sure here that we have a folder
                        // and we don't throw an error if it is not
                        if (result.Node is not DirectoryNode)
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

                        var canFollowFolder = searchOption == SearchOption.AllDirectories && nodePair.Value is DirectoryNode;

                        var addEntry = (nodePair.Value is FileNode && searchTarget != SearchTarget.Directory && isEntryMatching)
                                       || (nodePair.Value is DirectoryNode && searchTarget != SearchTarget.File && isEntryMatching);

                        var fullPath = directoryPath / nodePair.Key;

                        if (canFollowFolder)
                        {
                            foldersToProcess.Insert(dirIndex++, fullPath);
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

    /// <inheritdoc />
    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        var foldersToProcess = new List<UPath>();
        foldersToProcess.Add(path);

        var entries = new List<FileSystemItem>();
        while (foldersToProcess.Count > 0)
        {
            var directoryPath = foldersToProcess[0];
            foldersToProcess.RemoveAt(0);
            int dirIndex = 0;
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
                var result = EnterFindNode(directoryPath, FindNodeFlags.NodeShared);
                try
                {
                    if (directoryPath == path)
                    {
                        // The first folder must be a directory, if it is not, throw an error
                        ValidateDirectory(result.Node, directoryPath);
                    }
                    else
                    {
                        // Might happen during the time a DirectoryNode is enqueued into foldersToProcess
                        // and the time we are going to actually visit it, it might have been
                        // removed in the meantime, so we make sure here that we have a folder
                        // and we don't throw an error if it is not
                        if (result.Node is not DirectoryNode)
                        {
                            continue;
                        }
                    }

                    var directory = (DirectoryNode)result.Node;
                    foreach (var nodePair in directory.Children)
                    {
                        var node = nodePair.Value;
                        var canFollowFolder = searchOption == SearchOption.AllDirectories && nodePair.Value is DirectoryNode;
                        var fullPath = directoryPath / nodePair.Key;

                        if (canFollowFolder)
                        {
                            foldersToProcess.Insert(dirIndex++, fullPath);
                        }

                        var item = new FileSystemItem
                        {
                            FileSystem = this,
                            AbsolutePath = fullPath,
                            Path = fullPath,
                            Attributes = node.Attributes,
                            CreationTime = node.CreationTime,
                            LastWriteTime = node.LastWriteTime,
                            LastAccessTime = node.LastAccessTime,
                            Length = node is FileNode fileNode ? fileNode.Content.Length : 0,
                        };

                        if (searchPredicate == null || searchPredicate(ref item))
                        {
                            entries.Add(item);
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
    // Watch API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override IFileSystemWatcher WatchImpl(UPath path)
    {
        var watcher = new Watcher(this, path);
        GetOrCreateDispatcher().Add(watcher);
        return watcher;
    }

    private class Watcher : FileSystemWatcher
    {
        private readonly MemoryFileSystem _fileSystem;

        public Watcher(MemoryFileSystem fileSystem, UPath path)
            : base(fileSystem, path)
        {
            _fileSystem = fileSystem;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_fileSystem.IsDisposing)
            {
                _fileSystem.TryGetDispatcher()?.Remove(this);
            }
        }
    }

    // ----------------------------------------------
    // Path API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override string ConvertPathToInternalImpl(UPath path)
    {
        return path.FullName;
    }

    /// <inheritdoc />
    protected override UPath ConvertPathFromInternalImpl(string innerPath)
    {
        return new UPath(innerPath);
    }

    // ----------------------------------------------
    // Internals
    // ----------------------------------------------

    private void MoveFileOrDirectory(UPath srcPath, UPath destPath, bool expectDirectory)
    {
        var parentSrcPath = srcPath.GetDirectory();
        var parentDestPath = destPath.GetDirectory();

        void AssertNoDestination(FileSystemNode? node)
        {
            if (expectDirectory)
            {
                if (node is FileNode || node != null)
                {
                    throw NewDestinationFileExistException(destPath);
                }
            }
            else
            {
                if (node is DirectoryNode || node != null)
                {
                    throw NewDestinationDirectoryExistException(destPath);
                }
            }
        }

        // Same directory move
        bool isSamefolder = parentSrcPath == parentDestPath;
        // Check that Destination folder is not a subfolder of source directory
        if (!isSamefolder && expectDirectory)
        {
            var checkParentDestDirectory = destPath.GetDirectory();
            while (!checkParentDestDirectory.IsNull)
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
                    destResult = EnterFindNode(destPath, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeShared);
                    srcResult = EnterFindNode(srcPath, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive, destResult);
                }
                else
                {
                    srcResult = EnterFindNode(srcPath, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeExclusive);
                    destResult = EnterFindNode(destPath, FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.NodeShared, srcResult);
                }

                if (expectDirectory)
                {
                    ValidateDirectory(srcResult.Node, srcPath);
                }
                else
                {
                    ValidateFile(srcResult.Node, srcPath);
                }
                ValidateDirectory(destResult.Directory, destPath);

                AssertNoDestination(destResult.Node);

                srcResult.Node.DetachFromParent();
                srcResult.Node.AttachToParent(destResult.Directory, destResult.Name!);

                TryGetDispatcher()?.RaiseDeleted(srcPath);
                TryGetDispatcher()?.RaiseCreated(destPath);
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

    private static void ValidateDirectory([NotNull] FileSystemNode? node, UPath srcPath)
    {
        if (node is FileNode)
        {
            throw new IOException($"The source directory `{srcPath}` is a file");
        }

        if (node is null)
        {
            throw NewDirectoryNotFoundException(srcPath);
        }
    }

    private static void ValidateFile([NotNull] FileSystemNode? node, UPath srcPath)
    {
        if (node is null)
        {
            throw NewFileNotFoundException(srcPath);
        }
    }

    private FileSystemNode? TryFindNodeSafe(UPath path)
    {
        EnterFileSystemShared();
        try
        {
            var result = EnterFindNode(path, FindNodeFlags.NodeShared);
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

        if (node is null)
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
        ExitFindNode(EnterFindNode(path, FindNodeFlags.CreatePathIfNotExist | FindNodeFlags.NodeShared));
    }

    private readonly struct NodeResult
    {
        public NodeResult(DirectoryNode? directory, FileSystemNode? node, string? name, FindNodeFlags flags)
        {
            Directory = directory;
            Node = node;
            Name = name;
            Flags = flags;
        }

        public readonly DirectoryNode? Directory;

        public readonly FileSystemNode? Node;

        public readonly string? Name;

        public readonly FindNodeFlags Flags;
    }

    [Flags]
    private enum FindNodeFlags
    {
        CreatePathIfNotExist = 1 << 1,

        NodeCheck = 1 << 2,

        NodeShared = 1 << 3,

        NodeExclusive = 1 << 4,

        KeepParentNodeExclusive = 1 << 5,

        KeepParentNodeShared = 1 << 6,
    }

    private void ExitFindNode(in NodeResult nodeResult)
    {
        var flags = nodeResult.Flags;

        // Unlock first the node
        if (nodeResult.Node != null)
        {
            if ((flags & FindNodeFlags.NodeExclusive) != 0)
            {
                ExitExclusive(nodeResult.Node);
            }
            else if ((flags & FindNodeFlags.NodeShared) != 0)
            {
                ExitShared(nodeResult.Node);
            }
        }

        if (nodeResult.Directory is null)
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
        // TODO: Split the flags between parent and node to make the code more clear
        var result = new NodeResult();

        // This method should be always called with at least one of these
        Debug.Assert((flags & (FindNodeFlags.NodeExclusive|FindNodeFlags.NodeShared|FindNodeFlags.NodeCheck)) != 0);

        var sharePath = share ?? FileShare.Read;
        bool isLockOnRootAlreadyTaken = IsNodeAlreadyLocked(_rootDirectory, existingNodes);

        // Even if it is not valid, the EnterFindNode may be called with a root directory
        // So we handle it as a special case here
        if (path == UPath.Root)
        {
            if (!isLockOnRootAlreadyTaken)
            {
                if ((flags & FindNodeFlags.NodeExclusive) != 0)
                {
                    EnterExclusive(_rootDirectory, path);
                }
                else if ((flags & FindNodeFlags.NodeShared) != 0)
                {
                    EnterShared(_rootDirectory, path, sharePath);
                }
            }
            else
            {
                // If the lock was already taken, we make sure that NodeResult
                // will not try to release it
                flags &= ~(FindNodeFlags.NodeExclusive | FindNodeFlags.NodeShared);
            }
            result = new NodeResult(null, _rootDirectory, null, flags);
            return result;
        }

        var isRequiringExclusiveLockForParent = (flags & (FindNodeFlags.CreatePathIfNotExist | FindNodeFlags.KeepParentNodeExclusive)) != 0;

        var parentNode = _rootDirectory;
        var names = path.Split();

        // Walking down the nodes in locking order:
        // /a/b/c.txt
        //
        // Lock /
        // Lock /a
        // Unlock /
        // Lock /a/b
        // Unlock /a
        // Lock /a/b/c.txt

        // Start by locking the parent directory (only if it is not already locked)
        bool isParentLockTaken = false;
        if (!isLockOnRootAlreadyTaken)
        {
            EnterExclusiveOrSharedDirectoryOrBlock(_rootDirectory, path, isRequiringExclusiveLockForParent);
            isParentLockTaken = true;
        }

        for (var i = 0; i < names.Count && parentNode != null; i++)
        {
            var name = names[i];
            bool isLast = i + 1 == names.Count;

            DirectoryNode? nextParent = null;
            bool isNextParentLockTaken = false;
            try
            {
                FileSystemNode? subNode;
                if (!parentNode.Children.TryGetValue(name, out subNode))
                {
                    if ((flags & FindNodeFlags.CreatePathIfNotExist) != 0)
                    {
                        subNode = new DirectoryNode(this, parentNode, name);
                    }
                }
                else
                {
                    // If we are trying to create a directory and one of the node on the way is a file
                    // this is an error
                    if ((flags & FindNodeFlags.CreatePathIfNotExist) != 0 && subNode is FileNode)
                    {
                        throw new IOException($"Cannot create directory `{path}` on an existing file");
                    }
                }

                // Special case of the last entry
                if (isLast)
                {
                    // If the lock was not taken by the parent, modify the flags 
                    // so that Exit(NodeResult) will not try to release the lock on the parent
                    if (!isParentLockTaken)
                    {
                        flags &= ~(FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.KeepParentNodeShared);
                    }

                    result = new NodeResult(parentNode, subNode, name, flags);

                    // The last subnode may be null but we still want to return a valid parent
                    // otherwise, lock the final node if necessary
                    if (subNode != null)
                    {
                        if ((flags & FindNodeFlags.NodeExclusive) != 0)
                        {
                            EnterExclusive(subNode, path);
                        }
                        else if ((flags & FindNodeFlags.NodeShared) != 0)
                        {
                            EnterShared(subNode, path, sharePath);
                        }
                    }

                    // After we have taken the lock, and we need to keep a lock on the parent, make sure
                    // that the finally {} below will not unlock the parent
                    // This is important to perform this here, as the previous EnterExclusive/EnterShared
                    // could have failed (e.g trying to lock exclusive on a file already locked)
                    // and thus, we would have to release the lock of the parent in finally
                    if ((flags & (FindNodeFlags.KeepParentNodeExclusive | FindNodeFlags.KeepParentNodeShared)) != 0)
                    {
                        parentNode = null;
                        break;
                    }
                }
                else
                {
                    // Going down the directory, 
                    nextParent = subNode as DirectoryNode;
                    if (nextParent != null && !IsNodeAlreadyLocked(nextParent, existingNodes))
                    {
                        EnterExclusiveOrSharedDirectoryOrBlock(nextParent, path, isRequiringExclusiveLockForParent);
                        isNextParentLockTaken = true;
                    }
                }
            }
            finally
            {
                // We unlock the parent only if it was taken
                if (isParentLockTaken && parentNode != null)
                {
                    ExitExclusiveOrShared(parentNode, isRequiringExclusiveLockForParent);
                }
            }

            parentNode = nextParent;
            isParentLockTaken = isNextParentLockTaken;
        }

        return result;
    }

    private static bool IsNodeAlreadyLocked(DirectoryNode directoryNode, NodeResult[] existingNodes)
    {
        foreach (var existingNode in existingNodes)
        {
            if (existingNode.Directory == directoryNode || existingNode.Node == directoryNode)
            {
                return true;
            }
        }
        return false;
    }

    private FileSystemEventDispatcher<Watcher> GetOrCreateDispatcher()
    {
        lock (_dispatcherLock)
        {
            _dispatcher ??= new FileSystemEventDispatcher<Watcher>(this);
            
            return _dispatcher;
        }
    }

    private FileSystemEventDispatcher<Watcher>? TryGetDispatcher()
    {
        lock (_dispatcherLock)
        {
            return _dispatcher;
        }
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

    private void EnterExclusiveOrSharedDirectoryOrBlock(DirectoryNode node, UPath context, bool isExclusive)
    {
        if (isExclusive)
        {
            EnterExclusiveDirectoryOrBlock(node, context);
        }
        else
        {
            EnterSharedDirectoryOrBlock(node, context);
        }
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
            throw new IOException($"The {pathType} `{context}` is already used for writing by another thread.");
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
            throw new IOException($"The {pathType} `{context}` is already locked.");
        }
    }

    private void ExitExclusiveOrShared(FileSystemNode node, bool isExclusive)
    {
        if (isExclusive)
        {
            node.ExitExclusive();
        }
        else
        {
            node.ExitShared();
        }
    }

    private void ExitExclusive(FileSystemNode node)
    {
        node.ExitExclusive();
    }

    private void TryLockExclusive(FileSystemNode node, ListFileSystemNodes locks, bool recursive, UPath context)
    {
        if (locks is null) throw new ArgumentNullException(nameof(locks));

        if (node is DirectoryNode directory)
        {
            if (recursive)
            {
                foreach (var child in directory.Children)
                {
                    EnterExclusive(child.Value, context);

                    var path = context / child.Key;
                    locks.Add(child);

                    TryLockExclusive(child.Value, locks, true, path);
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
        protected readonly MemoryFileSystem FileSystem;

        protected FileSystemNode(MemoryFileSystem fileSystem, DirectoryNode? parentNode, string? name, FileSystemNode? copyNode)
        {
            Debug.Assert((parentNode is null) == string.IsNullOrEmpty(name));

            FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

            if (parentNode != null && name is { Length: > 0 })
            {
                Debug.Assert(parentNode.IsLocked);

                parentNode.Children.Add(name, this);
                Parent = parentNode;
                Name = name;
            }

            if (copyNode != null && copyNode.Attributes != 0)
            {
                Attributes = copyNode.Attributes;
            }
            CreationTime = DateTime.Now;
            LastWriteTime = copyNode?.LastWriteTime ?? CreationTime;
            LastAccessTime = copyNode?.LastAccessTime ?? CreationTime;
        }

        public DirectoryNode? Parent { get; private set; }

        public string? Name { get; private set; }

        public FileAttributes Attributes { get; set; }

        public DateTime CreationTime { get; set; }

        public DateTime LastWriteTime { get; set; }

        public DateTime LastAccessTime { get; set; }

        public bool IsDisposed { get; set; }

        public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;

        public void DetachFromParent()
        {
            Debug.Assert(IsLocked);
            var parent = Parent!;
            Debug.Assert(parent.IsLocked);

            parent.Children.Remove(Name!);
            Parent = null!;
            Name = null!;
        }

        public void AttachToParent(DirectoryNode parentNode, string name)
        {
            if (parentNode is null) 
                throw new ArgumentNullException(nameof(parentNode));

            if (string.IsNullOrEmpty(name)) 
                throw new ArgumentNullException(nameof(name));

            Debug.Assert(parentNode.IsLocked);
            Debug.Assert(IsLocked);
            Debug.Assert(Parent is null);

            Parent = parentNode;
            Parent.Children.Add(name, this);
            Name = name;
        }

        public void Dispose()
        {
            Debug.Assert(IsLocked);
            // In order to issue a Dispose, we need to have control on this node
            IsDisposed = true;
        }

        public virtual FileSystemNode Clone(DirectoryNode? newParent, string? newName)
        {
            Debug.Assert((newParent is null) == string.IsNullOrEmpty(newName));

            var clone = (FileSystemNode)Clone();
            clone.Parent = newParent;
            clone.Name = newName;
            return clone;
        }
    }

    private class ListFileSystemNodes : List<KeyValuePair<string, FileSystemNode>>, IDisposable
    {
        private readonly MemoryFileSystem _fs;
        
        public ListFileSystemNodes(MemoryFileSystem fs)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        }

        public void Dispose()
        {
            for (var i = this.Count - 1; i >= 0; i--)
            {
                var entry = this[i];
                _fs.ExitExclusive(entry.Value);
            }
            Clear();
        }
    }

    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "(),nq}")]
    [DebuggerTypeProxy(typeof(DebuggerProxyInternal))]
    private class DirectoryNode : FileSystemNode
    {
        internal Dictionary<string, FileSystemNode> _children;

        public DirectoryNode(MemoryFileSystem fileSystem) : base(fileSystem, null, null, null)
        {
            _children = new Dictionary<string, FileSystemNode>();
            Attributes = FileAttributes.Directory;
        }

        public DirectoryNode(MemoryFileSystem fileSystem, DirectoryNode parent, string name) : base(fileSystem, parent, name, null)
        {
            Debug.Assert(parent != null);
            _children = new Dictionary<string, FileSystemNode>();
            Attributes = FileAttributes.Directory;
        }

        public Dictionary<string, FileSystemNode> Children
        {
            get
            {
                Debug.Assert(IsLocked);
                return _children;
            }
        }

        public override FileSystemNode Clone(DirectoryNode? newParent, string? newName)
        {
            var dir = (DirectoryNode)base.Clone(newParent, newName);
            dir._children = new Dictionary<string, FileSystemNode>();
            foreach (var name in _children.Keys)
            {
                dir._children[name] = _children[name].Clone(dir, name);
            }
            return dir;
        }

        public override string DebuggerDisplay()
        {
            return Name is null ? $"Count = {_children.Count}{base.DebuggerDisplay()}"  : $"Folder: {Name}, Count = {_children.Count}{base.DebuggerDisplay()}";
        }

        private sealed class DebuggerProxyInternal
        {
            private readonly DirectoryNode _directoryNode;

            public DebuggerProxyInternal(DirectoryNode directoryNode)
            {
                _directoryNode = directoryNode;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public FileSystemNode[] Items => _directoryNode._children.Values.ToArray();
        }
    }

    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "(),nq}")]
    private sealed class FileNode : FileSystemNode
    {
        public FileNode(MemoryFileSystem fileSystem, DirectoryNode parentNode, string? name, FileNode? copyNode)
            : base(fileSystem, parentNode, name, copyNode)
        {
            if (copyNode != null)
            {
                Content = new FileContent(this, copyNode.Content);
            }
            else
            {
                // Mimic OS-specific attributes.
                Attributes = PhysicalFileSystem.IsOnWindows ? FileAttributes.Archive : FileAttributes.Normal;
                Content = new FileContent(this);
            }
        }

        public FileContent Content { get; private set; }

        public override FileSystemNode Clone(DirectoryNode? newParent, string? newName)
        {
            var copy = (FileNode)base.Clone(newParent, newName);
            copy.Content = new FileContent(copy, Content);
            return copy;
        }

        public override string DebuggerDisplay()
        {
            return $"File: {Name}, {Content.DebuggerDisplay()}{base.DebuggerDisplay()}";
        }

        public void ContentChanged()
        {
            var dispatcher = FileSystem.TryGetDispatcher();
            if (dispatcher != null)
            {
                // TODO: cache this
                var path = GeneratePath();

                dispatcher.RaiseChange(path);
            }
        }

        private UPath GeneratePath()
        {
            var builder = UPath.GetSharedStringBuilder();
            FileSystemNode node = this;
            var parent = Parent;

            while (parent != null)
            {
                builder.Insert(0, node.Name);
                builder.Insert(0, UPath.DirectorySeparator);

                node = parent;
                parent = parent.Parent;
            }

            return builder.ToString();
        }
    }

    private sealed class FileContent
    {
        private readonly FileNode _fileNode;
        private readonly MemoryStream _stream;

        public FileContent(FileNode fileNode)
        {
            _fileNode = fileNode ?? throw new ArgumentNullException(nameof(fileNode));
            _stream = new MemoryStream();
        }

        public FileContent(FileNode fileNode, FileContent copy)
        {
            _fileNode = fileNode ?? throw new ArgumentNullException(nameof(fileNode));
            var length = copy.Length;
            _stream = new MemoryStream(length <= int.MaxValue ? (int)length : int.MaxValue);
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

            _fileNode.ContentChanged();
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

                _fileNode.ContentChanged();
            }
        }

        public string DebuggerDisplay() => $"Size = {_stream.Length}";
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

        public MemoryFileStream(MemoryFileSystem fs, FileNode fileNode, bool canRead, bool canWrite, bool isExclusive)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _fileNode = fileNode ?? throw new ArgumentNullException(nameof(fs));
            _canWrite = canWrite;
            _canRead = canRead;
            _isExclusive = isExclusive;
            _position = 0;

            Debug.Assert(fileNode.IsLocked);
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

        protected FileSystemNodeReadWriteLock Clone()
        {
            var locker = (FileSystemNodeReadWriteLock)MemberwiseClone();
            // Erase any locks
            locker._sharedCount = 0;
            locker._shared = null;
            return locker;
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

        public virtual string DebuggerDisplay()
        {
            return _sharedCount < 0 ? " (exclusive lock)" : _sharedCount > 0 ? $" (shared lock: {_sharedCount})" : string.Empty;
        }
    }

    private sealed class DebuggerProxy
    {
        private readonly MemoryFileSystem _fs;

        public DebuggerProxy(MemoryFileSystem fs)
        {
            _fs = fs;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public FileSystemNode[] Items => _fs._rootDirectory._children.Select(x => x.Value).ToArray();
    }
}