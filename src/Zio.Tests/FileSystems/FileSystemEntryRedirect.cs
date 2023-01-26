// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.IO;

using Zio.FileSystems;

namespace Zio.Tests.FileSystems;

/// <summary>
/// Mocking FileSystem redierecting to <see cref="FileEntry"/> and <see cref="DirectoryEntry"/> to verify that these API are correctly working.
/// </summary>
public class FileSystemEntryRedirect : FileSystem
{
    private readonly MemoryFileSystem _fs;

    public FileSystemEntryRedirect()
    {
        _fs = new MemoryFileSystem();
    }

    public FileSystemEntryRedirect(MemoryFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    protected override void CreateDirectoryImpl(UPath path)
    {
        new DirectoryEntry(_fs, path).Create();
    }

    protected override bool DirectoryExistsImpl(UPath path)
    {
        return new DirectoryEntry(_fs, path).Exists;
    }

    protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
    {
        new DirectoryEntry(_fs, srcPath).MoveTo(destPath);
    }

    protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
    {
        new DirectoryEntry(_fs, path).Delete(isRecursive);
    }

    protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
    {
        new FileEntry(_fs, srcPath).CopyTo(destPath, overwrite);
    }

    protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
    {
        new FileEntry(_fs, srcPath).ReplaceTo(destPath, destBackupPath, ignoreMetadataErrors);
    }

    protected override long GetFileLengthImpl(UPath path)
    {
        return new FileEntry(_fs, path).Length;
    }

    protected override bool FileExistsImpl(UPath path)
    {
        return new FileEntry(_fs, path).Exists;
    }

    protected override void MoveFileImpl(UPath srcPath, UPath destPath)
    {
        new FileEntry(_fs, srcPath).MoveTo(destPath);
    }

    protected override void DeleteFileImpl(UPath path)
    {
        new FileEntry(_fs, path).Delete();
    }

    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
    {
        return new FileEntry(_fs, path).Open(mode, access, share);
    }

    protected override FileAttributes GetAttributesImpl(UPath path)
    {
        return _fs.GetFileSystemEntry(path).Attributes;
    }

    protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
    {
        _fs.GetFileSystemEntry(path).Attributes = attributes;
    }

    protected override DateTime GetCreationTimeImpl(UPath path)
    {
        return _fs.TryGetFileSystemEntry(path)?.CreationTime ?? DefaultFileTime;
    }

    protected override void SetCreationTimeImpl(UPath path, DateTime time)
    {
        _fs.GetFileSystemEntry(path).CreationTime = time;
    }

    protected override DateTime GetLastAccessTimeImpl(UPath path)
    {
        return _fs.TryGetFileSystemEntry(path)?.LastAccessTime ?? DefaultFileTime;
    }

    protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
    {
        _fs.GetFileSystemEntry(path).LastAccessTime = time;
    }

    protected override DateTime GetLastWriteTimeImpl(UPath path)
    {
        return _fs.TryGetFileSystemEntry(path)?.LastWriteTime ?? DefaultFileTime;
    }

    protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
    {
        _fs.GetFileSystemEntry(path).LastWriteTime = time;
    }

    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        return _fs.GetDirectoryEntry(path).EnumerateEntries(searchPattern, searchOption, searchTarget).Select(e => e.Path);
    }
    
    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate searchPredicate)
    {
        return _fs.GetDirectoryEntry(path).EnumerateItems(searchOption);
    }

    protected override IFileSystemWatcher WatchImpl(UPath path)
    {
        return _fs.Watch(path);
    }

    protected override string ConvertPathToInternalImpl(UPath path)
    {
        return _fs.ConvertPathToInternal(path);
    }

    protected override UPath ConvertPathFromInternalImpl(string innerPath)
    {
        return _fs.ConvertPathFromInternal(innerPath);
    }
}