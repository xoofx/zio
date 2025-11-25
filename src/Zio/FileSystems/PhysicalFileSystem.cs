// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using static Zio.FileSystemExceptionHelper;
#if NETSTANDARD2_1
using System.IO.Enumeration;
#endif

namespace Zio.FileSystems;

/// <summary>
/// Provides a <see cref="IFileSystem"/> for the physical filesystem.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "(),nq}")]
public class PhysicalFileSystem : FileSystem
{
    private const string DrivePrefixOnWindows = "/mnt/";
    private static readonly UPath PathDrivePrefixOnWindows = new UPath(DrivePrefixOnWindows);
#if NETSTANDARD
    internal static readonly bool IsOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
    internal static readonly bool IsOnWindows = CheckIsOnWindows();

    private static bool CheckIsOnWindows()
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Xbox:
            case PlatformID.Win32NT:
            case PlatformID.Win32S:
            case PlatformID.Win32Windows:
            case PlatformID.WinCE:
                return true;
        }
        return false;
    }
#endif

    // ----------------------------------------------
    // Directory API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override void CreateDirectoryImpl(UPath path)
    {
        if (IsWithinSpecialDirectory(path))
        {
            throw new UnauthorizedAccessException($"Cannot create a directory in the path `{path}`");
        }

        Directory.CreateDirectory(ConvertPathToInternal(path));
    }

    /// <inheritdoc />
    protected override bool DirectoryExistsImpl(UPath path)
    {
        return IsWithinSpecialDirectory(path) ? SpecialDirectoryExists(path) : Directory.Exists(ConvertPathToInternal(path));
    }

    /// <inheritdoc />
    protected override void MoveDirectoryImpl(UPath srcPath, UPath destPath)
    {
        if (IsOnWindows)
        {
            if (IsWithinSpecialDirectory(srcPath))
            {
                if (!SpecialDirectoryExists(srcPath))
                {
                    throw NewDirectoryNotFoundException(srcPath);
                }

                throw new UnauthorizedAccessException($"Cannot move the special directory `{srcPath}`");
            }

            if (IsWithinSpecialDirectory(destPath))
            {
                if (!SpecialDirectoryExists(destPath))
                {
                    throw NewDirectoryNotFoundException(destPath);
                }
                throw new UnauthorizedAccessException($"Cannot move to the special directory `{destPath}`");
            }
        }

        var systemSrcPath = ConvertPathToInternal(srcPath);
        var systemDestPath = ConvertPathToInternal(destPath);

        // If the souce path is a file
        var fileInfo = new FileInfo(systemSrcPath);
        if (fileInfo.Exists)
        {
            throw new IOException($"The source `{srcPath}` is not a directory");
        }

        Directory.Move(systemSrcPath, systemDestPath);
    }

    /// <inheritdoc />
    protected override void DeleteDirectoryImpl(UPath path, bool isRecursive)
    {
        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }
            throw new UnauthorizedAccessException($"Cannot delete directory `{path}`");
        }

        Directory.Delete(ConvertPathToInternal(path), isRecursive);
    }

    // ----------------------------------------------
    // File API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override void CopyFileImpl(UPath srcPath, UPath destPath, bool overwrite)
    {
        if (IsWithinSpecialDirectory(srcPath))
        {
            throw new UnauthorizedAccessException($"The access to `{srcPath}` is denied");
        }
        if (IsWithinSpecialDirectory(destPath))
        {
            throw new UnauthorizedAccessException($"The access to `{destPath}` is denied");
        }

        File.Copy(ConvertPathToInternal(srcPath), ConvertPathToInternal(destPath), overwrite);
    }

    /// <inheritdoc />
    protected override void ReplaceFileImpl(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
    {
        if (IsWithinSpecialDirectory(srcPath))
        {
            throw new UnauthorizedAccessException($"The access to `{srcPath}` is denied");
        }
        if (IsWithinSpecialDirectory(destPath))
        {
            throw new UnauthorizedAccessException($"The access to `{destPath}` is denied");
        }
        if (!destBackupPath.IsNull && IsWithinSpecialDirectory(destBackupPath))
        {
            throw new UnauthorizedAccessException($"The access to `{destBackupPath}` is denied");
        }

        if (!destBackupPath.IsNull)
        {
            CopyFileImpl(destPath, destBackupPath, true);
        }
        CopyFileImpl(srcPath, destPath, true);
        DeleteFileImpl(srcPath);

        // TODO: Add atomic version using File.Replace coming with .NET Standard 2.0
    }

    /// <inheritdoc />
    protected override long GetFileLengthImpl(UPath path)
    {
        if (IsWithinSpecialDirectory(path))
        {
            throw new UnauthorizedAccessException($"The access to `{path}` is denied");
        }
        return new FileInfo(ConvertPathToInternal(path)).Length;
    }

    /// <inheritdoc />
    protected override bool FileExistsImpl(UPath path)
    {
        return !IsWithinSpecialDirectory(path) && File.Exists(ConvertPathToInternal(path));
    }

    /// <inheritdoc />
    protected override void MoveFileImpl(UPath srcPath, UPath destPath)
    {
        if (IsWithinSpecialDirectory(srcPath))
        {
            throw new UnauthorizedAccessException($"The access to `{srcPath}` is denied");
        }
        if (IsWithinSpecialDirectory(destPath))
        {
            throw new UnauthorizedAccessException($"The access to `{destPath}` is denied");
        }
        File.Move(ConvertPathToInternal(srcPath), ConvertPathToInternal(destPath));
    }

    /// <inheritdoc />
    protected override void DeleteFileImpl(UPath path)
    {
        if (IsWithinSpecialDirectory(path))
        {
            throw new UnauthorizedAccessException($"The access to `{path}` is denied");
        }
        File.Delete(ConvertPathToInternal(path));
    }

    /// <inheritdoc />
    protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access,
        FileShare share = FileShare.None)
    {
        if (IsWithinSpecialDirectory(path))
        {
            throw new UnauthorizedAccessException($"The access to `{path}` is denied");
        }
        return File.Open(ConvertPathToInternal(path), mode, access, share);
    }

    /// <inheritdoc />
    protected override FileAttributes GetAttributesImpl(UPath path)
    {
        // Handle special folders to return valid FileAttributes
        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }

            // The path / and /drive are readonly
            if (path == PathDrivePrefixOnWindows || path == UPath.Root)
            {
                return FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly;
            }
            // Otherwise let the File.GetAttributes returns the proper attributes for root drive (e.g /drive/c)
        }

        return File.GetAttributes(ConvertPathToInternal(path));
    }

    // ----------------------------------------------
    // Metadata API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override void SetAttributesImpl(UPath path, FileAttributes attributes)
    {
        // Handle special folders
        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }
            throw new UnauthorizedAccessException($"Cannot set attributes on system directory `{path}`");
        }

        File.SetAttributes(ConvertPathToInternal(path), attributes);
    }

    /// <inheritdoc />
    protected override DateTime GetCreationTimeImpl(UPath path)
    {
        // Handle special folders

        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }

            // For /drive and /, get the oldest CreationTime of all folders (approx)
            if (path == PathDrivePrefixOnWindows || path == UPath.Root)
            {
                var creationTime = DateTime.MaxValue;

                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    var newCreationTime = drive.RootDirectory.CreationTime;
                    if (newCreationTime < creationTime)
                    {
                        creationTime = newCreationTime;
                    }
                }
                return creationTime;
            }
        }

        return File.GetCreationTime(ConvertPathToInternal(path));
    }

    /// <inheritdoc />
    protected override void SetCreationTimeImpl(UPath path, DateTime time)
    {
        // Handle special folders
        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }
            throw new UnauthorizedAccessException($"Cannot set creation time on system directory `{path}`");
        }

        var internalPath = ConvertPathToInternal(path);
        var attributes = File.GetAttributes(internalPath);

        if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
        {
            Directory.SetCreationTime(internalPath, time);
        }
        else
        {
            File.SetCreationTime(internalPath, time);
        }
    }

    /// <inheritdoc />
    protected override DateTime GetLastAccessTimeImpl(UPath path)
    {
        // Handle special folders to return valid LastAccessTime
        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }

            // For /drive and /, get the oldest CreationTime of all folders (approx)
            if (path == PathDrivePrefixOnWindows || path == UPath.Root)
            {
                var lastAccessTime = DateTime.MaxValue;

                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    var time = drive.RootDirectory.LastAccessTime;
                    if (time < lastAccessTime)
                    {
                        lastAccessTime = time;
                    }
                }
                return lastAccessTime;
            }

            // otherwise let the regular function running
        }

        return File.GetLastAccessTime(ConvertPathToInternal(path));
    }

    /// <inheritdoc />
    protected override void SetLastAccessTimeImpl(UPath path, DateTime time)
    {
        // Handle special folders
        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }
            throw new UnauthorizedAccessException($"Cannot set last access time on system directory `{path}`");
        }

        var internalPath = ConvertPathToInternal(path);
        var attributes = File.GetAttributes(internalPath);

        if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
        {
            Directory.SetLastAccessTime(internalPath, time);
        }
        else
        {
            File.SetLastAccessTime(internalPath, time);
        }
    }

    /// <inheritdoc />
    protected override DateTime GetLastWriteTimeImpl(UPath path)
    {
        // Handle special folders to return valid LastAccessTime
        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }

            // For /drive and /, get the oldest CreationTime of all folders (approx)
            if (path == PathDrivePrefixOnWindows || path == UPath.Root)
            {
                var lastWriteTime = DateTime.MaxValue;

                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    var time = drive.RootDirectory.LastWriteTime;
                    if (time < lastWriteTime)
                    {
                        lastWriteTime = time;
                    }
                }
                return lastWriteTime;
            }

            // otherwise let the regular function running
        }

        return File.GetLastWriteTime(ConvertPathToInternal(path));
    }

    /// <inheritdoc />
    protected override void SetLastWriteTimeImpl(UPath path, DateTime time)
    {
        // Handle special folders
        if (IsWithinSpecialDirectory(path))
        {
            if (!SpecialDirectoryExists(path))
            {
                throw NewDirectoryNotFoundException(path);
            }
            throw new UnauthorizedAccessException($"Cannot set last write time on system directory `{path}`");
        }

        var internalPath = ConvertPathToInternal(path);
        var attributes = File.GetAttributes(internalPath);

        if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
        {
            Directory.SetLastWriteTime(internalPath, time);
        }
        else
        {
            File.SetLastWriteTime(internalPath, time);
        }
    }

    protected override void CreateSymbolicLinkImpl(UPath path, UPath pathToTarget)
    {
        if (IsWithinSpecialDirectory(path))
        {
            throw new UnauthorizedAccessException($"The access to `{path}` is denied");
        }

        if (IsWithinSpecialDirectory(pathToTarget))
        {
            throw new UnauthorizedAccessException($"The access to `{pathToTarget}` is denied");
        }

        var systemPath = ConvertPathToInternal(path);

        if (File.Exists(systemPath))
        {
            throw NewDestinationFileExistException(path);
        }

        if (Directory.Exists(systemPath))
        {
            throw NewDestinationDirectoryExistException(path);
        }

        var systemPathToTarget = ConvertPathToInternal(pathToTarget);

        bool isDirectory;

        if (File.Exists(systemPathToTarget))
        {
            isDirectory = false;
        }
        else if (Directory.Exists(systemPathToTarget))
        {
            isDirectory = true;
        }
        else
        {
            throw NewDirectoryNotFoundException(path);
        }

#if NET
        if (isDirectory)
        {
            Directory.CreateSymbolicLink(systemPath, systemPathToTarget);
        }
        else
        {
            File.CreateSymbolicLink(systemPath, systemPathToTarget);
        }
#else
        bool success;

        if (IsOnWindows)
        {
            var type = isDirectory ? Interop.Windows.SymbolicLink.Directory : Interop.Windows.SymbolicLink.File;

            success = Interop.Windows.CreateSymbolicLink(systemPath, systemPathToTarget, type);

            if (!success && Marshal.GetLastWin32Error() == 1314)
            {
                throw new UnauthorizedAccessException($"Could not create symbolic link `{path}` to `{pathToTarget}` due to insufficient privileges");
            }
        }
        else
        {
            success = Interop.Unix.symlink(systemPathToTarget, systemPath) == 0;
        }

        if (!success)
        {
            throw new IOException($"Could not create symbolic link `{path}` to `{pathToTarget}`");
        }
#endif
    }

    /// <inheritdoc />
    protected override bool TryResolveLinkTargetImpl(UPath linkPath, out UPath resolvedPath)
    {
        if (IsWithinSpecialDirectory(linkPath))
        {
            throw new UnauthorizedAccessException($"The access to `{linkPath}` is denied");
        }

        var systemPath = ConvertPathToInternal(linkPath);
        bool isDirectory = false;
        if (!File.Exists(systemPath))
        {
            isDirectory = Directory.Exists(systemPath);
            if (!isDirectory)
            {
                resolvedPath = default;
                return false;
            }
        }

#if NET
        var systemResult = isDirectory ? Directory.ResolveLinkTarget(systemPath, true)?.FullName : File.ResolveLinkTarget(systemPath, true)?.FullName;
#else
        var systemResult = IsOnWindows ? Interop.Windows.GetFinalPathName(systemPath) : Interop.Unix.readlink(systemPath);
#endif

        if (systemResult == null)
        {
            resolvedPath = default;
            return false;
        }

        resolvedPath = ConvertPathFromInternal(systemResult);
        return true;
    }

    // ----------------------------------------------
    // Search API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        // Special case for Windows as we need to provide list for:
        // - the root folder / (which should just return the /drive folder)
        // - the drive folders /drive/c, drive/e...etc.
        var search = SearchPattern.Parse(ref path, ref searchPattern);
        if (IsOnWindows)
        {
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw NewDirectoryNotFoundException(path);
                }

                var searchForDirectory = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.Directory;

                // Only sub folder "/drive/" on root folder /
                if (path == UPath.Root)
                {
                    if (searchForDirectory)
                    {
                        yield return PathDrivePrefixOnWindows;

                        if (searchOption == SearchOption.AllDirectories)
                        {
                            foreach (var subPath in EnumeratePathsImpl(PathDrivePrefixOnWindows, searchPattern, searchOption, searchTarget))
                            {
                                yield return subPath;
                            }
                        }
                    }

                    yield break;
                }

                // When listing for /drive, return the list of drives available
                if (path == PathDrivePrefixOnWindows)
                {
                    var pathDrives = new List<UPath>();
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.Name.Length < 2 || drive.Name[1] != ':')
                        {
                            continue;
                        }

                        var pathDrive = PathDrivePrefixOnWindows / char.ToLowerInvariant(drive.Name[0]).ToString();

                        if (search.Match(pathDrive))
                        {
                            pathDrives.Add(pathDrive);

                            if (searchForDirectory)
                            {
                                yield return pathDrive;
                            }
                        }
                    }

                    if (searchOption == SearchOption.AllDirectories)
                    {
                        foreach (var pathDrive in pathDrives)
                        {
                            foreach (var subPath in EnumeratePathsImpl(pathDrive, searchPattern, searchOption, searchTarget))
                            {
                                yield return subPath;
                            }
                        }
                    }

                    yield break;
                }
            }
        }

        IEnumerable<string> results;
        switch (searchTarget)
        {
            case SearchTarget.File:
                results = Directory.EnumerateFiles(ConvertPathToInternal(path), searchPattern, searchOption);
                break;

            case SearchTarget.Directory:
                results = Directory.EnumerateDirectories(ConvertPathToInternal(path), searchPattern, searchOption);
                break;

            case SearchTarget.Both:
                results = Directory.EnumerateFileSystemEntries(ConvertPathToInternal(path), searchPattern, searchOption);
                break;
            
            default:
                yield break;
        }

        foreach (var subPath in results)
        {
            // Windows will truncate the search pattern's extension to three characters if the filesystem
            // has 8.3 paths enabled. This means searching for *.docx will list *.doc as well which is
            // not what we want. Check against the search pattern again to filter out those false results.
            if (!IsOnWindows || search.Match(Path.GetFileName(subPath)))
            {
                yield return ConvertPathFromInternal(subPath);
            }
        }
    }

    /// <inheritdoc />
    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        if (IsOnWindows)
        {
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw NewDirectoryNotFoundException(path);
                }

                // Only sub folder "/drive/" on root folder /
                if (path == UPath.Root)
                {
                    var item = new FileSystemItem(this, PathDrivePrefixOnWindows, true);
                    if (searchPredicate == null || searchPredicate(ref item))
                    {
                        yield return item;
                    }

                    if (searchOption == SearchOption.AllDirectories)
                    {
                        foreach (var subItem in EnumerateItemsImpl(PathDrivePrefixOnWindows, searchOption, searchPredicate))
                        {
                            yield return subItem;
                        }
                    }

                    yield break;
                }

                // When listing for /drive, return the list of drives available
                if (path == PathDrivePrefixOnWindows)
                {
                    var pathDrives = new List<UPath>();
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.Name.Length < 2 || drive.Name[1] != ':')
                        {
                            continue;
                        }

                        var pathDrive = PathDrivePrefixOnWindows / char.ToLowerInvariant(drive.Name[0]).ToString();

                        pathDrives.Add(pathDrive);

                        var item = new FileSystemItem(this, pathDrive, true);
                        if (searchPredicate == null || searchPredicate(ref item))
                        {
                            yield return item;
                        }
                    }

                    if (searchOption == SearchOption.AllDirectories)
                    {
                        foreach (var pathDrive in pathDrives)
                        {
                            foreach (var subItem in EnumerateItemsImpl(pathDrive, searchOption, searchPredicate))
                            {
                                yield return subItem;
                            }
                        }
                    }

                    yield break;
                }
            }
        }
        var pathOnDisk = ConvertPathToInternal(path);
        if (!Directory.Exists(pathOnDisk)) yield break;

#if NETSTANDARD2_1
        var enumerable = new FileSystemEnumerable<FileSystemItem>(pathOnDisk, TransformToFileSystemItem, searchOption == SearchOption.AllDirectories ? CompatibleRecursive : Compatible);

        foreach (var item in enumerable)
        {
            var localItem = item;
            if (searchPredicate == null || searchPredicate(ref localItem))
            {
                yield return localItem;
            }
        }
#else
        var results = Directory.EnumerateFileSystemEntries(pathOnDisk, "*", searchOption);
        foreach (var subPath in results)
        {
            var fileInfo = new FileInfo(subPath);
            var fullPath = ConvertPathFromInternal(subPath);
            var item = new FileSystemItem
            {
                FileSystem = this,
                AbsolutePath = fullPath,
                Path = fullPath,
                Attributes = fileInfo.Attributes,
                CreationTime = fileInfo.CreationTimeUtc.ToLocalTime(),
                LastAccessTime = fileInfo.LastAccessTimeUtc.ToLocalTime(),
                LastWriteTime = fileInfo.LastWriteTimeUtc.ToLocalTime(),
                Length = (fileInfo.Attributes & FileAttributes.Directory) > 0 ? 0 : fileInfo.Length
            };
            if (searchPredicate == null || searchPredicate(ref item))
            {
                yield return item;
            }
        }
#endif
    }

#if NETSTANDARD2_1

    internal static EnumerationOptions Compatible { get; } = new EnumerationOptions()
    {
        MatchType = MatchType.Win32,
        AttributesToSkip = (FileAttributes)0,
        IgnoreInaccessible = false
    };

    private static EnumerationOptions CompatibleRecursive { get; } = new EnumerationOptions()
    {
        RecurseSubdirectories = true,
        MatchType = MatchType.Win32,
        AttributesToSkip = (FileAttributes)0,
        IgnoreInaccessible = false
    };

    private FileSystemItem TransformToFileSystemItem(ref System.IO.Enumeration.FileSystemEntry entry)
    {
        var fullPath = ConvertPathFromInternal(entry.ToFullPath());
        return new FileSystemItem
        {
            FileSystem = this,
            AbsolutePath = fullPath,
            Path = fullPath,
            Attributes = entry.Attributes,
            CreationTime = entry.CreationTimeUtc.ToLocalTime(),
            LastAccessTime = entry.LastAccessTimeUtc.ToLocalTime(),
            LastWriteTime = entry.LastWriteTimeUtc.ToLocalTime(),
            Length = entry.Length
        };
    }
#endif

    // ----------------------------------------------
    // Watch API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override bool CanWatchImpl(UPath path)
    {
        if (IsWithinSpecialDirectory(path))
        {
            return SpecialDirectoryExists(path);
        }

        return Directory.Exists(ConvertPathToInternal(path));
    }

    /// <inheritdoc />
    protected override IFileSystemWatcher WatchImpl(UPath path)
    {
        if (IsWithinSpecialDirectory(path))
        {
            throw new UnauthorizedAccessException($"The access to `{path}` is denied");
        }

        return new Watcher(this, path);
    }

    private sealed class Watcher : IFileSystemWatcher
    {
        private readonly PhysicalFileSystem _fileSystem;
        private readonly System.IO.FileSystemWatcher _watcher;

        public event EventHandler<FileChangedEventArgs>? Changed;
        public event EventHandler<FileChangedEventArgs>? Created;
        public event EventHandler<FileChangedEventArgs>? Deleted;
        public event EventHandler<FileSystemErrorEventArgs>? Error;
        public event EventHandler<FileRenamedEventArgs>? Renamed;

        public IFileSystem FileSystem => _fileSystem;
        public UPath Path { get; }

        public int InternalBufferSize
        {
            get => _watcher.InternalBufferSize;
            set => _watcher.InternalBufferSize = value;
        }

        public NotifyFilters NotifyFilter
        {
            get => (NotifyFilters)_watcher.NotifyFilter;
            set => _watcher.NotifyFilter = (System.IO.NotifyFilters)value;
        }

        public bool EnableRaisingEvents
        {
            get => _watcher.EnableRaisingEvents;
            set => _watcher.EnableRaisingEvents = value;
        }

        public string Filter
        {
            get => _watcher.Filter;
            set => _watcher.Filter = value;
        }

        public bool IncludeSubdirectories
        {
            get => _watcher.IncludeSubdirectories;
            set => _watcher.IncludeSubdirectories = value;
        }

        public Watcher(PhysicalFileSystem fileSystem, UPath path)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _watcher = new System.IO.FileSystemWatcher(_fileSystem.ConvertPathToInternal(path))
            {
                Filter = "*"
            };
            Path = path;

            _watcher.Changed += (sender, args) => Changed?.Invoke(this, Remap(args));
            _watcher.Created += (sender, args) => Created?.Invoke(this, Remap(args));
            _watcher.Deleted += (sender, args) => Deleted?.Invoke(this, Remap(args));
            _watcher.Error += (sender, args) => Error?.Invoke(this, Remap(args));
            _watcher.Renamed += (sender, args) => Renamed?.Invoke(this, Remap(args));
        }

        ~Watcher()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _watcher.Dispose();
            }
        }

        private FileChangedEventArgs Remap(FileSystemEventArgs args)
        {
            var newChangeType = (WatcherChangeTypes)args.ChangeType;
            var newPath = _fileSystem.ConvertPathFromInternal(args.FullPath);
            return new FileChangedEventArgs(FileSystem, newChangeType, newPath);
        }

        private FileSystemErrorEventArgs Remap(ErrorEventArgs args)
        {
            return new FileSystemErrorEventArgs(args.GetException());
        }

        private FileRenamedEventArgs Remap(RenamedEventArgs args)
        {
            var newChangeType = (WatcherChangeTypes)args.ChangeType;
            var newPath = _fileSystem.ConvertPathFromInternal(args.FullPath);
            var newOldPath = _fileSystem.ConvertPathFromInternal(args.OldFullPath);
            return new FileRenamedEventArgs(FileSystem, newChangeType, newPath, newOldPath);
        }
    }

    // ----------------------------------------------
    // Path API
    // ----------------------------------------------

    /// <inheritdoc />
    protected override string ConvertPathToInternalImpl(UPath path)
    {
        var absolutePath = path.FullName;

        if (IsOnWindows)
        {
            if (!absolutePath.StartsWith(DrivePrefixOnWindows, StringComparison.Ordinal) ||
                absolutePath.Length == DrivePrefixOnWindows.Length ||
                !IsDriveLetter(absolutePath[DrivePrefixOnWindows.Length]))
                throw new ArgumentException($"A path on Windows must start by `{DrivePrefixOnWindows}` followed by the drive letter");

            var driveLetter = char.ToUpper(absolutePath[DrivePrefixOnWindows.Length]);
            if (absolutePath.Length != DrivePrefixOnWindows.Length + 1 &&
                absolutePath[DrivePrefixOnWindows.Length + 1] !=
                UPath.DirectorySeparator)
                throw new ArgumentException($"The driver letter `/{DrivePrefixOnWindows}{absolutePath[DrivePrefixOnWindows.Length]}` must be followed by a `/` or nothing in the path -> `{absolutePath}`");

            var builder = UPath.GetSharedStringBuilder();
            builder.Append(driveLetter).Append(":\\");
            if (absolutePath.Length > DrivePrefixOnWindows.Length + 1)
                builder.Append(absolutePath.Replace(UPath.DirectorySeparator, '\\').Substring(DrivePrefixOnWindows.Length + 2));

            return builder.ToString();
        }
        return absolutePath;
    }

    /// <inheritdoc />
    protected override UPath ConvertPathFromInternalImpl(string innerPath)
    {
        if (IsOnWindows)
        {
            // We currently don't support special Windows files (\\.\ \??\  DosDevices...etc.)
            if (innerPath.StartsWith(@"\\", StringComparison.Ordinal) || innerPath.StartsWith(@"\?", StringComparison.Ordinal))
                throw new NotSupportedException($"Path starting with `\\\\` or `\\?` are not supported -> `{innerPath}` ");

            // We want to avoid using Path.GetFullPath unless absolutely necessary,
            // because it can change the case of already rooted paths that contain a ~
            var absolutePath = HasWindowsVolumeLabel(innerPath) ? innerPath : Path.GetFullPath(innerPath);

            // Assert that Path.GetFullPath returned the format we expect
            if (!HasWindowsVolumeLabel(absolutePath))
                throw new ArgumentException($"Expecting a drive for the path `{absolutePath}`");

            var builder = UPath.GetSharedStringBuilder();
            builder.Append(DrivePrefixOnWindows).Append(char.ToLowerInvariant(absolutePath[0])).Append('/');
            if (absolutePath.Length > 2)
                builder.Append(absolutePath.Substring(2));

            return new UPath(builder.ToString());
        }
        return innerPath;
    }

    private static bool IsWithinSpecialDirectory(UPath path)
    {
        if (!IsOnWindows)
        {
            return false;
        }

        var parentDirectory = path.GetDirectory();
        return path == PathDrivePrefixOnWindows ||
               path == UPath.Root ||
               parentDirectory == PathDrivePrefixOnWindows ||
               parentDirectory == UPath.Root;
    }

    private static bool SpecialDirectoryExists(UPath path)
    {
        // /drive or / can be read
        if (path == PathDrivePrefixOnWindows || path == UPath.Root)
        {
            return true;
        }

        // If /xxx, invalid (parent folder is /)
        var parentDirectory = path.GetDirectory();
        if (parentDirectory == UPath.Root)
        {
            return false;
        }

        var dirName = path.GetName();
        // Else check that we have a valid drive path (e.g /drive/c)
        return parentDirectory == PathDrivePrefixOnWindows && 
               dirName.Length == 1 && 
               DriveInfo.GetDrives().Any(p => char.ToLowerInvariant(p.Name[0]) == dirName[0]);
    }

    private static bool IsDriveLetter(char c)
    {
        return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z';
    }

    private static bool HasWindowsVolumeLabel( string path )
    {
        if ( !IsOnWindows )
            throw new NotSupportedException( $"{nameof( HasWindowsVolumeLabel )} is only supported on Windows platforms." );

        return path.Length >= 3 && path[1] == ':' && path[2] is '\\' or '/';
    }
}