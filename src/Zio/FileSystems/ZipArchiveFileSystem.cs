// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

#if HAS_ZIPARCHIVE
namespace Zio.FileSystems;

/// <summary>
///     Provides a <see cref="IFileSystem" /> for the ZipArchive filesystem.
/// </summary>
public class ZipArchiveFileSystem : FileSystem
{
    private readonly bool _isCaseSensitive;
    
    private readonly ZipArchive _archive;
    private readonly CompressionLevel _compressionLevel;

    private readonly ReaderWriterLockSlim _entriesLock = new();
    
    private FileSystemEventDispatcher<FileSystemWatcher>? _dispatcher;
    private readonly object _dispatcherLock = new();
    
    private readonly DateTime _creationTime;

    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    
    private readonly Dictionary<ZipArchiveEntry, EntryState> _openStreams;
    private readonly object _openStreamsLock = new();

#if NETFRAMEWORK // .Net4.5 uses a backslash as directory separator
    private const char DirectorySeparator = '\\';
#else
    private const char DirectorySeparator = '/';
#endif


    /// <summary>
    ///     Initializes a new instance of the <see cref="ZipArchiveFileSystem" /> class.
    /// </summary>
    /// <param name="archive">An instance of <see cref="ZipArchive" /></param>
    /// <param name="isCaseSensitive">Specifies if entry names should be case sensitive</param>
    /// <exception cref="ArgumentNullException"></exception>
    public ZipArchiveFileSystem(ZipArchive archive, bool isCaseSensitive = false, CompressionLevel compressionLevel = CompressionLevel.NoCompression)
    {
        _archive = archive;
        _isCaseSensitive = isCaseSensitive;
        _creationTime = DateTime.Now;
        _compressionLevel = compressionLevel;
        if (archive == null)
        {
            throw new ArgumentNullException(nameof(archive));
        }
#if NETFRAMEWORK // .Net4.5 uses a backslash as directory separator
        foreach (var entry in _archive.Entries)
        {
            entry.FullName.Replace('/', DirectorySeparator);
        }
#else
        foreach (var entry in _archive.Entries)
        {
            entry.FullName.Replace('\\', DirectorySeparator);
        }
#endif
        if (!_isCaseSensitive)
        {
            _entries = _archive.Entries.ToDictionary(e => e.FullName.ToLowerInvariant(), e => e);
        }

        _openStreams = new Dictionary<ZipArchiveEntry, EntryState>();
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ZipArchiveFileSystem" /> class.
    /// </summary>
    /// <param name="stream">Instance of stream to create <see cref="ZipArchive" /> from</param>
    /// <param name="mode">Mode of <see cref="ZipArchive" /></param>
    /// <param name="leaveOpen">True to leave the stream open when <see cref="ZipArchive" /> is disposed</param>
    /// <param name="isCaseSensitive"></param>
    public ZipArchiveFileSystem(Stream stream, ZipArchiveMode mode = ZipArchiveMode.Update, bool leaveOpen = false, bool isCaseSensitive = false, CompressionLevel compressionLevel = CompressionLevel.NoCompression)
        : this(new ZipArchive(stream, mode, leaveOpen), isCaseSensitive, compressionLevel)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ZipArchiveFileSystem" /> class from file.
    /// </summary>
    /// <param name="path">Path to zip file</param>
    /// <param name="mode">Mode of <see cref="ZipArchive" /></param>
    /// <param name="leaveOpen">True to leave the stream open when <see cref="ZipArchive" /> is disposed</param>
    /// <param name="isCaseSensitive">Specifies if entry names should be case sensitive</param>
    public ZipArchiveFileSystem(string path, ZipArchiveMode mode = ZipArchiveMode.Update, bool leaveOpen = false, bool isCaseSensitive = false, CompressionLevel compressionLevel = CompressionLevel.NoCompression)
        : this(new ZipArchive(File.Open(path, FileMode.OpenOrCreate), mode, leaveOpen), isCaseSensitive, compressionLevel)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ZipArchiveFileSystem" /> class with a <see cref="MemoryStream" />
    /// </summary>
    /// <param name="mode">Mode of <see cref="ZipArchive" /></param>
    /// <param name="leaveOpen">True to leave the stream open when <see cref="ZipArchive" /> is disposed</param>
    /// <param name="isCaseSensitive">Specifies if entry names should be case sensitive</param>
    public ZipArchiveFileSystem(ZipArchiveMode mode = ZipArchiveMode.Update, bool leaveOpen = false, bool isCaseSensitive = false, CompressionLevel compressionLevel = CompressionLevel.NoCompression)
        : this(new ZipArchive(new MemoryStream(), mode, leaveOpen), isCaseSensitive, compressionLevel)
    {
    }

    private ZipArchiveEntry? GetEntry(string path)
    {
#if NETFRAMEWORK
        path = path.Replace('/', DirectorySeparator);
#else
        path = path.Replace('\\', DirectorySeparator);
#endif
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        path = RemoveLeadingSlash(path);

        _entriesLock.EnterReadLock();
        try
        {
            if (_isCaseSensitive)
            {
                return _archive.GetEntry(path);
            }

            return _entries.TryGetValue(path.ToLowerInvariant(), out var foundEntry) ? foundEntry : null;
        }
        finally
        {
            _entriesLock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    protected override UPath ConvertPathFromInternalImpl(string innerPath)
    {
        return new UPath(innerPath);
    }

    /// <inheritdoc />
    protected override string ConvertPathToInternalImpl(UPath path)
    {
        return path.FullName;
    }

    /// <inheritdoc />
    protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
    {
        if (srcPath == destPath)
        {
            throw new IOException("Source and destination path must be different.");
        }

        var srcEntry = GetEntry(srcPath.FullName);
        if (srcEntry == null)
        {
            if (DirectoryExistsImpl(srcPath))
            {
                throw new UnauthorizedAccessException(nameof(srcPath) + " is a directory.");
            }

            if (!DirectoryExistsImpl(srcPath.GetDirectory()))
            {
                throw new DirectoryNotFoundException(srcPath.GetDirectory().FullName);
            }

            throw FileSystemExceptionHelper.NewFileNotFoundException(srcPath);
        }

        var parentDirectory = destPath.GetDirectory();
        if (!DirectoryExistsImpl(parentDirectory))
        {
            throw FileSystemExceptionHelper.NewDirectoryNotFoundException(parentDirectory);
        }

        if (DirectoryExistsImpl(destPath))
        {
            if (!FileExistsImpl(destPath))
            {
                throw new IOException("Destination path is a directory");
            }
        }

        var destEntry = GetEntry(destPath.FullName);
        if (destEntry != null)
        {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            if ((destEntry.ExternalAttributes & (int)FileAttributes.ReadOnly) == (int)FileAttributes.ReadOnly)
            {
                throw new UnauthorizedAccessException("Destination file is read only");
            }
#endif
            if (!overwrite)
            {
                throw FileSystemExceptionHelper.NewDestinationFileExistException(srcPath);
            }

            RemoveEntry(destEntry);
            TryGetDispatcher()?.RaiseDeleted(destPath);
        }

        destEntry = CreateEntry(destPath.FullName);
        using (var destStream = destEntry.Open())
        {
            using var srcStream = srcEntry.Open();
            srcStream.CopyTo(destStream);
        }
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        destEntry.ExternalAttributes = srcEntry.ExternalAttributes | (int)FileAttributes.Archive;
#endif
        TryGetDispatcher()?.RaiseCreated(destPath);
    }

    /// <inheritdoc />
    protected override void CreateDirectoryImpl(UPath path)
    {
        if (FileExistsImpl(path))
        {
            throw FileSystemExceptionHelper.NewDestinationFileExistException(path);
        }

        if (DirectoryExistsImpl(path))
        {
            throw FileSystemExceptionHelper.NewDestinationDirectoryExistException(path);
        }

        var parentPath = new UPath(GetParent(path.FullName));
        if (parentPath != "")
        {
            if (!DirectoryExistsImpl(parentPath))
            {
                CreateDirectoryImpl(parentPath);
            }
        }

        var entryPath = RemoveLeadingSlash(path);
#if NETFRAMEWORK
        entryPath = entryPath.Replace('/', DirectorySeparator);
#else
        entryPath = entryPath.Replace('\\', DirectorySeparator);
#endif
        CreateEntry(ConvertPathToDirectory(entryPath));
        TryGetDispatcher()?.RaiseCreated(entryPath);
    }

    /// <inheritdoc />
    protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
    {
        var entryPath = RemoveLeadingSlash(ConvertPathToDirectory(path));
#if NETFRAMEWORK
        entryPath = entryPath.Replace('/', DirectorySeparator);
#else
        entryPath = entryPath.Replace('\\', DirectorySeparator);
#endif
        var entries = new List<ZipArchiveEntry>();
        if (!isRecursive)
        {
            // folder name ends with slash so StartWith check is enough
            _entriesLock.EnterReadLock();
            try
            {
                entries = _archive.Entries.Where(x => x.FullName.StartsWith(entryPath, _isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            }
            finally
            {
                _entriesLock.ExitReadLock();
            }

            if (entries.Count == 0)
            {
                if (FileExistsImpl(path))
                {
                    throw new IOException($"{path} is a file");
                }

                throw FileSystemExceptionHelper.NewDirectoryNotFoundException(path);
            }

            if (entries.Count == 1)
            {
                RemoveEntry(entries[0]);
            }

            if (entries.Count == 2)
            {
                throw new IOException("Directory is not empty");
            }

            TryGetDispatcher()?.RaiseDeleted(path);
            return;
        }

        _entriesLock.EnterReadLock();
        try
        {
            entries = _archive.Entries.Where(x => x.FullName.StartsWith(entryPath, _isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count == 0)
            {
                entryPath = entryPath.Substring(0, entryPath.Length - 1);
                if (_isCaseSensitive ? _archive.GetEntry(entryPath) != null : _entries.ContainsKey(entryPath.ToLowerInvariant()))
                {
                    throw new IOException($"{path} is a file");
                }

                throw FileSystemExceptionHelper.NewDirectoryNotFoundException(path);
            }

            // check if there are no open file in directory
            foreach (var entry in entries)
            {
                lock (_openStreamsLock)
                {
                    if (_openStreams.ContainsKey(entry))
                    {
                        throw new IOException($"There is an open file {entry.FullName} in directory");
                    }
                }
            }
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            // check if there are none readonly entries
            foreach (var entry in entries)
            {
                if ((entry.ExternalAttributes & (int)FileAttributes.ReadOnly) == (int)FileAttributes.ReadOnly)
                {
                    throw entry.FullName == entryPath
                        ? new IOException("Directory is read only")
                        : new UnauthorizedAccessException($"Cannot delete directory that contains readonly entry {entry.FullName}");
                }
            }
#endif
        }
        finally
        {
            _entriesLock.ExitReadLock();
        }

        _entriesLock.EnterWriteLock();
        try
        {
            foreach (var entry in entries)
            {
                entry.Delete();

                if (!_isCaseSensitive)
                {
                    _entries.Remove(entry.FullName.ToLowerInvariant());
                }
            }
        }
        finally
        {
            _entriesLock.ExitWriteLock();
        }

        TryGetDispatcher()?.RaiseDeleted(path);
    }

    /// <inheritdoc />
    protected override void DeleteFileImpl(UPath path)
    {
        if (DirectoryExistsImpl(path))
        {
            throw new IOException("Cannot delete a directory");
        }

        var entry = GetEntry(path.FullName);
        if (entry == null)
        {
            return;
        }
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        if ((entry.ExternalAttributes & (int)FileAttributes.ReadOnly) == (int)FileAttributes.ReadOnly)
        {
            throw new UnauthorizedAccessException("Cannot delete file with readonly attribute");
        }
#endif

        TryGetDispatcher()?.RaiseDeleted(path);
        RemoveEntry(entry);
    }

    /// <inheritdoc />
    protected override bool DirectoryExistsImpl(UPath path)
    {
        if (path.FullName is "/" or "\\" or "")
        {
            return true;
        }

        return GetEntry(ConvertPathToDirectory(path.FullName)) != null;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _archive.Dispose();

        if (disposing)
        {
            TryGetDispatcher()?.Dispose();
        }
    }

    /// <inheritdoc />
    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        return EnumeratePathsStr(path, "*", searchOption, SearchTarget.Both).Select(p => new FileSystemItem(this, p, p[p.Length - 1] == DirectorySeparator));
    }

    /// <inheritdoc />
    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        return EnumeratePathsStr(path, searchPattern, searchOption, searchTarget).Select(x => new UPath(x));
    }

    private IEnumerable<string> EnumeratePathsStr(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        var search = SearchPattern.Parse(ref path, ref searchPattern);
        var entryPath = RemoveLeadingSlash(path);
        var root = ConvertPathToDirectory(entryPath);
#if NETFRAMEWORK
        root = root.Replace('/', '\\');
#else
        root = root.Replace('\\', '/');
#endif
        _entriesLock.EnterReadLock();
        var entriesList = new List<ZipArchiveEntry>();
        try
        {
            entriesList = root == ""
                ? _archive.Entries.ToList()
                : _archive.Entries.Where(e => e.FullName.StartsWith(root, _isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) && e.FullName.Length > root.Length).ToList();
        }
        finally
        {
            _entriesLock.ExitReadLock();
        }

        if (entriesList.Count == 0)
        {
            return Enumerable.Empty<string>();
        }

        var entries = (IEnumerable<ZipArchiveEntry>)entriesList;
        if (searchOption == SearchOption.TopDirectoryOnly)
        {
            var dir = GetParent(entries.First().FullName);
            entries = entries.Where(e => string.Equals(ConvertPathToDirectory(GetParent(e.FullName)), root, _isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
        }

        if (searchTarget == SearchTarget.File)
        {
            entries = entries.Where(e => e.FullName[e.FullName.Length - 1] != DirectorySeparator);
        }
        else if (searchTarget == SearchTarget.Directory)
        {
            entries = entries.Where(e => e.FullName[e.FullName.Length - 1] == DirectorySeparator);
        }

        if (!string.IsNullOrEmpty(searchPattern))
        {
            entries = entries.Where(e => search.Match(GetName(e)));
        }

        return entries.Select(e => '/' + e.FullName);
    }

    /// <inheritdoc />
    protected override bool FileExistsImpl(UPath path)
    {
        return GetEntry(path.FullName) != null;
    }

    /// <inheritdoc />
    protected override FileAttributes GetAttributesImpl(UPath path)
    {
        var entry = GetEntry(path.FullName) ?? GetEntry(ConvertPathToDirectory(path));
        if (entry is null)
        {
            throw FileSystemExceptionHelper.NewFileNotFoundException(path);
        }

        var attributes = entry.FullName[entry.FullName.Length - 1] == DirectorySeparator ? FileAttributes.Directory : 0;

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        if (entry.ExternalAttributes == 0 && attributes == 0)
        {
            attributes |= FileAttributes.Normal;
        }

        return (FileAttributes)entry.ExternalAttributes | attributes;
#endif
        // return standard attributes if it's not NetStandard2.1
        return attributes == FileAttributes.Directory ? FileAttributes.Directory : entry.LastWriteTime >= _creationTime ? FileAttributes.Archive : FileAttributes.Normal;
    }

    /// <inheritdoc />
    protected override long GetFileLengthImpl(UPath path)
    {
        var entry = GetEntry(path.FullName);
        if (entry == null)
        {
            throw FileSystemExceptionHelper.NewFileNotFoundException(path);
        }

        try
        {
            return entry.Length;
        }
        catch (Exception ex) // for some reason entry.Length doesn't work with MemoryStream used in tests
        {
            Debug.WriteLine(ex.Message);
            using var stream = OpenFileImpl(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.Length;
        }
    }

    /// <summary>
    ///     Not supported by zip format. Return last write time.
    /// </summary>
    protected override DateTime GetCreationTimeImpl(UPath path)
    {
        return GetLastWriteTimeImpl(path);
    }

    /// <summary>
    ///     Not supported by zip format. Return last write time
    /// </summary>
    protected override DateTime GetLastAccessTimeImpl(UPath path)
    {
        return GetLastWriteTimeImpl(path);
    }

    /// <inheritdoc />
    protected override DateTime GetLastWriteTimeImpl(UPath path)
    {
        var entry = GetEntry(path.FullName) ?? GetEntry(ConvertPathToDirectory(path));
        if (entry == null)
        {
            return DefaultFileTime;
        }

        return entry.LastWriteTime.DateTime;
    }

    /// <inheritdoc />
    protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
    {
        if (destPath.IsInDirectory(srcPath, true))
        {
            throw new IOException("Cannot move directory to itself or a subdirectory.");
        }

        var srcDir = RemoveLeadingSlash(ConvertPathToDirectory(srcPath.FullName));
        var destDir = RemoveLeadingSlash(ConvertPathToDirectory(destPath.FullName));
#if NETFRAMEWORK
        srcDir = srcDir.Replace('/', DirectorySeparator);
        destDir = destDir.Replace('/', DirectorySeparator);
#else
        srcDir = srcDir.Replace('\\', DirectorySeparator);
        destDir = destDir.Replace('\\', DirectorySeparator);
#endif
        _entriesLock.EnterReadLock();
        var entries = Array.Empty<ZipArchiveEntry>();
        try
        {
            entries = _archive.Entries.Where(e => e.FullName.StartsWith(srcDir, _isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        finally
        {
            _entriesLock.ExitReadLock();
        }

        if (entries.Length == 0)
        {
            if (FileExistsImpl(srcPath))
            {
                throw new IOException(nameof(srcPath) + " is a file.");
            }

            throw FileSystemExceptionHelper.NewDirectoryNotFoundException(srcPath);
        }

        CreateDirectoryImpl(destPath);
        foreach (var entry in entries)
        {
            if (entry.FullName.Length == srcDir.Length)
            {
                RemoveEntry(entry);
                continue;
            }

            using (var entryStream = entry.Open())
            {
                var entryName = entry.FullName.Substring(srcDir.Length);
                var destEntry = CreateEntry(destDir + entryName);
                using (var destEntryStream = destEntry.Open())
                {
                    entryStream.CopyTo(destEntryStream);
                }
            }

            TryGetDispatcher()?.RaiseCreated(destPath);
            RemoveEntry(entry);
            TryGetDispatcher()?.RaiseDeleted(srcPath);
        }
    }

    /// <inheritdoc />
    protected override void MoveFileImpl(UPath srcPath, UPath destPath)
    {
        var srcEntry = GetEntry(srcPath.FullName) ?? throw FileSystemExceptionHelper.NewFileNotFoundException(srcPath);

        if (!DirectoryExistsImpl(destPath.GetDirectory()))
        {
            throw FileSystemExceptionHelper.NewDirectoryNotFoundException(destPath.GetDirectory());
        }
        
        var destEntry = GetEntry(destPath.FullName);
        if (destEntry != null)
        {
            throw new IOException("Cannot overwrite existing file.");
        }        

        destEntry = CreateEntry(destPath.FullName);
        TryGetDispatcher()?.RaiseCreated(destPath);
        using (var destStream = destEntry.Open())
        {
            using var srcStream = srcEntry.Open();
            srcStream.CopyTo(destStream);
        }

        RemoveEntry(srcEntry);
        TryGetDispatcher()?.RaiseDeleted(srcPath);
    }

    /// <inheritdoc />
    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share)
    {
        if (_archive.Mode == ZipArchiveMode.Read && access == FileAccess.Write)
        {
            throw new UnauthorizedAccessException("Cannot open a file for writing in a read-only archive.");
        }

        if (access == FileAccess.Read && (mode == FileMode.CreateNew || mode == FileMode.Create || mode == FileMode.Truncate || mode == FileMode.Append))
        {
            throw new ArgumentException("Cannot write in a read-only access.");
        }

        var entry = GetEntry(path.FullName);

        if (entry == null)
        {
            if (mode is FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate or FileMode.Append)
            {
                entry = CreateEntry(path.FullName);
#if NETSTANDARD2_1
                entry.ExternalAttributes = (int)FileAttributes.Archive;
#endif
                TryGetDispatcher()?.RaiseCreated(path);
            }
            else
            {
                if (DirectoryExistsImpl(path))
                {
                    throw new UnauthorizedAccessException(nameof(path) + " is a directory.");
                }

                if (!DirectoryExistsImpl(path.GetDirectory()))
                {
                    throw FileSystemExceptionHelper.NewDirectoryNotFoundException(path.GetDirectory());
                }

                throw FileSystemExceptionHelper.NewFileNotFoundException(path);
            }
        }
        else if (mode == FileMode.CreateNew)
        {
            throw new IOException("Cannot create a file in CreateNew mode if it already exists.");
        }
        else if (mode == FileMode.Create)
        {
            RemoveEntry(entry);
            entry = CreateEntry(path.FullName);
        }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        if ((access == FileAccess.Write || access == FileAccess.ReadWrite) && (entry.ExternalAttributes & (int)FileAttributes.ReadOnly) == (int)FileAttributes.ReadOnly)
        {
            throw new UnauthorizedAccessException("Cannot open a file for writing in a file with readonly attribute.");
        }
#endif

        var stream = new ZipEntryStream(share, this, entry);

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        if (access is FileAccess.Write or FileAccess.ReadWrite)
        {
            entry.ExternalAttributes |= (int)FileAttributes.Archive;
        }
#endif

        if (mode == FileMode.Append)
        {
            stream.Seek(0, SeekOrigin.End);
        }
        else if (mode == FileMode.Truncate)
        {
            stream.SetLength(0);
        }

        return stream;
    }

    /// <inheritdoc />
    protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
    {
        var sourceEntry = GetEntry(srcPath.FullName);
        if (sourceEntry is null)
        {
            throw FileSystemExceptionHelper.NewFileNotFoundException(srcPath);
        }

        var destEntry = GetEntry(destPath.FullName);
        if (destEntry == sourceEntry)
        {
            throw new IOException("Cannot replace the file with itself.");
        }

        if (destEntry != null)
        {
            // create a backup at destBackupPath if its not null
            if (destBackupPath != null)
            {
                var destBackupEntry = CreateEntry(destBackupPath.FullName);
                using var destBackupStream = destBackupEntry.Open();
                using var destStream = destEntry.Open();
                destStream.CopyTo(destBackupStream);
            }

            RemoveEntry(destEntry);
        }

        var newEntry = CreateEntry(destPath.FullName);
        using (var newStream = newEntry.Open())
        {
            using (var sourceStream = sourceEntry.Open())
            {
                sourceStream.CopyTo(newStream);
            }
        }

        RemoveEntry(sourceEntry);
        TryGetDispatcher()?.RaiseDeleted(srcPath);
        TryGetDispatcher()?.RaiseCreated(destPath);
    }

    /// <summary>
    ///     Implementation for <see cref="SetAttributes" />, <paramref name="path" /> is guaranteed to be absolute and
    ///     validated through <see cref="ValidatePath" />. Works only in Net Standard 2.1
    ///     Sets the specified <see cref="FileAttributes" /> of the file or directory on the specified path.
    /// </summary>
    /// <param name="path">The path to the file or directory.</param>
    /// <param name="attributes">A bitwise combination of the enumeration values.</param>
    protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
    {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        var entryPath = RemoveLeadingSlash(path);
        var entry = GetEntry(entryPath) ?? GetEntry(ConvertPathToDirectory(entryPath));
        if (entry == null)
        {
            throw FileSystemExceptionHelper.NewFileNotFoundException(path);
        }

        entry.ExternalAttributes = (int)attributes;
        TryGetDispatcher()?.RaiseChange(path);
        return;
#endif
        Debug.WriteLine("SetAttributes don't work in NetStandard2.0 or older.");
    }

    /// <summary>
    ///     Not supported by zip format. Does nothing.
    /// </summary>
    protected override void SetCreationTimeImpl(UPath path, DateTime time)
    {

    }

    /// <summary>
    ///     Not supported by zip format. Does nothing.
    /// </summary>
    protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
    {

    }

    /// <inheritdoc />
    protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
    {
        var entry = GetEntry(path.FullName) ?? GetEntry(ConvertPathToDirectory(path.FullName));
        if (entry is null)
        {
            throw FileSystemExceptionHelper.NewFileNotFoundException(path);
        }

        TryGetDispatcher()?.RaiseChange(path);
        entry.LastWriteTime = time;
    }

    /// <inheritdoc />
    protected override void CreateSymbolicLinkImpl(UPath path, UPath pathToTarget)
    {
        throw new NotSupportedException("Symbolic links are not supported by ZipArchiveFileSystem");
    }

    /// <inheritdoc />
    protected override bool TryResolveLinkTargetImpl(UPath linkPath, out UPath resolvedPath)
    {
        resolvedPath = UPath.Empty;
        return false;
    }

    /// <inheritdoc />
    protected override IFileSystemWatcher WatchImpl(UPath path)
    {
        var watcher = new FileSystemWatcher(this, path);
        lock (_dispatcherLock)
        {
            _dispatcher ??= new FileSystemEventDispatcher<FileSystemWatcher>(this);
            _dispatcher.Add(watcher);
        }

        return watcher;
    }

    private void RemoveEntry(ZipArchiveEntry entry)
    {
        _entriesLock.EnterWriteLock();
        try
        {
            entry.Delete();

            if (!_isCaseSensitive)
            {
                _entries.Remove(entry.FullName.ToLowerInvariant());
            }
        }
        finally
        {
            _entriesLock.ExitWriteLock();
        }
    }

    private ZipArchiveEntry CreateEntry(string path)
    {
        path = RemoveLeadingSlash(path);
#if NETFRAMEWORK
        path = path.Replace('/', DirectorySeparator);
#else
        path = path.Replace('\\', DirectorySeparator);
#endif
        _entriesLock.EnterWriteLock();
        try
        {
            var entry = _archive.CreateEntry(path, _compressionLevel);

            if (!_isCaseSensitive)
            {
                _entries.Add(entry.FullName.ToLowerInvariant(), entry);
            }

            return entry;
        }
        finally
        {
            _entriesLock.ExitWriteLock();
        }
    }

    private static readonly char[] s_slashChars = { '/', '\\' };

    private static string GetName(ZipArchiveEntry entry)
    {
        var name = entry.FullName.TrimEnd(s_slashChars);
        var index = name.LastIndexOfAny(s_slashChars);
        return name.Substring(index + 1);
    }

    private static string GetParent(string path)
    {
        path = path.TrimEnd(s_slashChars);
        var lastIndex = path.LastIndexOfAny(s_slashChars);
        return lastIndex == -1 ? "" : path.Substring(0, lastIndex);
    }

    private static string ConvertPathToDirectory(UPath path)
    {
        return ConvertPathToDirectory(path.FullName);
    }

    private static string ConvertPathToDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        return path[path.Length - 1] is DirectorySeparator ? path : path + DirectorySeparator;
    }

    private static string RemoveLeadingSlash(UPath path)
    {
        return path.FullName[0] is '\\' or '/' ? path.FullName.Substring(1) : path.FullName;
    }

    private static string RemoveLeadingSlash(string path)
    {
        return path[0] is '\\' or '/' ? path.Substring(1) : path;
    }

    private FileSystemEventDispatcher<FileSystemWatcher>? TryGetDispatcher()
    {
        lock (_dispatcherLock)
        {
            return _dispatcher;
        }
    }

    private sealed class ZipEntryStream : Stream
    {
        private readonly ZipArchiveEntry _entry;
        private readonly ZipArchiveFileSystem _fileSystem;
        private readonly Stream _streamImplementation;
        private bool _isDisposed;

        public ZipEntryStream(FileShare share, ZipArchiveFileSystem system, ZipArchiveEntry entry)
        {
            _entry = entry;
            _fileSystem = system;

            lock (_fileSystem._openStreamsLock)
            {
                var fileShare = _fileSystem._openStreams.TryGetValue(entry, out var fileData) ? fileData.Share : FileShare.ReadWrite;
                if (fileData != null)
                {
                    // we only check for read share, because ZipArchive doesn't support write share
                    if (share is not FileShare.Read and not FileShare.ReadWrite)
                    {
                        throw new IOException("File is already opened for reading");
                    }

                    if (fileShare is not FileShare.Read and not FileShare.ReadWrite)
                    {
                        throw new IOException("File is already opened for reading by another stream with non compatible share");
                    }

                    fileData.Count++;
                }
                else
                {
                    _fileSystem._openStreams.Add(_entry, new EntryState(share));
                }
                _streamImplementation = entry.Open();
            }

            Share = share;
        }

        private FileShare Share { get; }

        public override bool CanRead => _streamImplementation.CanRead;

        public override bool CanSeek => _streamImplementation.CanSeek;

        public override bool CanWrite => _streamImplementation.CanWrite;

        public override long Length => _streamImplementation.Length;

        public override long Position
        {
            get => _streamImplementation.Position;
            set => _streamImplementation.Position = value;
        }

        public override void Flush()
        {
            _streamImplementation.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _streamImplementation.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _streamImplementation.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _streamImplementation.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _streamImplementation.Write(buffer, offset, count);
        }

        public override void Close()
        {
            if (_isDisposed)
            {
                return;
            }

            _streamImplementation.Close();
            _isDisposed = true;
            lock (_fileSystem._openStreamsLock)
            {
                _fileSystem._openStreams.TryGetValue(_entry, out var fileData);
                fileData.Count--;
                if (fileData.Count == 0)
                {
                    _fileSystem._openStreams.Remove(_entry);
                }
            }
        }
    }

    private sealed class EntryState
    {
        public EntryState(FileShare share)
        {
            Share = share;
            Count = 1;
        }

        public FileShare Share { get; }

        public int Count;

    }
}
#endif