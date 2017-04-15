// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a <see cref="IFileSystem"/> for the physical filesystem.
    /// </summary>
    public class PhysicalFileSystem : FileSystemBase
    {
        private const string DrivePrefixOnWindows = "/drive/";
        private static readonly PathInfo PathDrivePrefixOnWindows = new PathInfo(DrivePrefixOnWindows);
#if NETSTANDARD
        private static readonly bool IsOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
        private static readonly bool IsOnWindows = CheckIsOnWindows();

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
        protected override void CreateDirectoryImpl(PathInfo path)
        {
            if (IsWithinSpecialDirectory(path))
            {
                throw new UnauthorizedAccessException($"Cannot create a directory in the path `{path}`");
            }

            Directory.CreateDirectory(ConvertToSystem(path));
        }

        /// <inheritdoc />
        protected override bool DirectoryExistsImpl(PathInfo path)
        {
            return IsWithinSpecialDirectory(path) ? SpecialDirectoryExists(path) : Directory.Exists(ConvertToSystem(path));
        }

        /// <inheritdoc />
        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            if (IsOnWindows)
            {
                if (IsWithinSpecialDirectory(srcPath))
                {
                    if (!SpecialDirectoryExists(srcPath))
                    {
                        throw new DirectoryNotFoundException($"The directory `{srcPath}` does not exist");
                    }

                    throw new UnauthorizedAccessException($"Cannot move the special directory `{srcPath}`");
                }

                if (IsWithinSpecialDirectory(destPath))
                {
                    if (!SpecialDirectoryExists(destPath))
                    {
                        throw new DirectoryNotFoundException($"The directory `{destPath}` does not exist");
                    }
                    throw new UnauthorizedAccessException($"Cannot move to the special directory `{destPath}`");
                }
            }

            Directory.Move(ConvertToSystem(srcPath), ConvertToSystem(destPath));
        }

        /// <inheritdoc />
        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }
                throw new UnauthorizedAccessException($"Cannot delete directory `{path}`");
            }

            Directory.Delete(ConvertToSystem(path), isRecursive);
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            if (IsWithinSpecialDirectory(srcPath))
            {
                throw new UnauthorizedAccessException($"The access to `{srcPath}` is denied");
            }
            if (IsWithinSpecialDirectory(destPath))
            {
                throw new UnauthorizedAccessException($"The access to `{destPath}` is denied");
            }

            File.Copy(ConvertToSystem(srcPath), ConvertToSystem(destPath), overwrite);
        }

        /// <inheritdoc />
        protected override void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath, bool ignoreMetadataErrors)
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

            // Non atomic version
            if (!FileExistsImpl(srcPath))
            {
                throw new FileNotFoundException($"Unable to find the source file `{srcPath}`");
            }
            if (!FileExistsImpl(destPath))
            {
                throw new FileNotFoundException($"Unable to find the source file `{destPath}`");
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
        protected override long GetFileLengthImpl(PathInfo path)
        {
            if (IsWithinSpecialDirectory(path))
            {
                throw new UnauthorizedAccessException($"The access to `{path}` is denied");
            }
            return new FileInfo(ConvertToSystem(path)).Length;
        }

        /// <inheritdoc />
        protected override bool FileExistsImpl(PathInfo path)
        {
            return !IsWithinSpecialDirectory(path) && File.Exists(ConvertToSystem(path));
        }

        /// <inheritdoc />
        protected override void MoveFileImpl(PathInfo srcPath, PathInfo destPath)
        {
            if (IsWithinSpecialDirectory(srcPath))
            {
                throw new UnauthorizedAccessException($"The access to `{srcPath}` is denied");
            }
            if (IsWithinSpecialDirectory(destPath))
            {
                throw new UnauthorizedAccessException($"The access to `{destPath}` is denied");
            }
            File.Move(ConvertToSystem(srcPath), ConvertToSystem(destPath));
        }

        /// <inheritdoc />
        protected override void DeleteFileImpl(PathInfo path)
        {
            if (IsWithinSpecialDirectory(path))
            {
                throw new UnauthorizedAccessException($"The access to `{path}` is denied");
            }
            File.Delete(ConvertToSystem(path));
        }

        /// <inheritdoc />
        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access,
            FileShare share = FileShare.None)
        {
            if (IsWithinSpecialDirectory(path))
            {
                throw new UnauthorizedAccessException($"The access to `{path}` is denied");
            }
            return File.Open(ConvertToSystem(path), mode, access, share);
        }

        /// <inheritdoc />
        protected override FileAttributes GetAttributesImpl(PathInfo path)
        {
            // Handle special folders to return valid FileAttributes
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }

                // The path / and /drive are readonly
                if (path == PathDrivePrefixOnWindows || path == PathInfo.Root)
                {
                    return FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly;
                }
                // Otherwise let the File.GetAttributes returns the proper attributes for root drive (e.g /drive/c)
            }

            return File.GetAttributes(ConvertToSystem(path));
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override void SetAttributesImpl(PathInfo path, FileAttributes attributes)
        {
            // Handle special folders
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }
                throw new UnauthorizedAccessException($"Cannot set attributes on system directory `{path}`");
            }

            File.SetAttributes(ConvertToSystem(path), attributes);
        }

        /// <inheritdoc />
        protected override DateTime GetCreationTimeImpl(PathInfo path)
        {
            // Handle special folders

            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }

                // For /drive and /, get the oldest CreationTime of all folders (approx)
                if (path == PathDrivePrefixOnWindows || path == PathInfo.Root)
                {
                    var creationTime = DateTime.MaxValue;

                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        var newCreationTime = drive.RootDirectory.CreationTime;
                        if (newCreationTime < creationTime)
                        {
                            creationTime = newCreationTime;
                        }
                    }
                    return creationTime;
                }
            }

            return File.GetCreationTime(ConvertToSystem(path));
        }

        /// <inheritdoc />
        protected override void SetCreationTimeImpl(PathInfo path, DateTime time)
        {
            // Handle special folders
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }
                throw new UnauthorizedAccessException($"Cannot set creation time on system directory `{path}`");
            }

            File.SetCreationTime(ConvertToSystem(path), time);
        }

        /// <inheritdoc />
        protected override DateTime GetLastAccessTimeImpl(PathInfo path)
        {
            // Handle special folders to return valid LastAccessTime
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }

                // For /drive and /, get the oldest CreationTime of all folders (approx)
                if (path == PathDrivePrefixOnWindows || path == PathInfo.Root)
                {
                    var lastAccessTime = DateTime.MaxValue;

                    foreach (var drive in DriveInfo.GetDrives())
                    {
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

            return File.GetLastAccessTime(ConvertToSystem(path));
        }

        /// <inheritdoc />
        protected override void SetLastAccessTimeImpl(PathInfo path, DateTime time)
        {
            // Handle special folders
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }
                throw new UnauthorizedAccessException($"Cannot set last access time on system directory `{path}`");
            }
            File.SetLastAccessTime(ConvertToSystem(path), time);
        }

        /// <inheritdoc />
        protected override DateTime GetLastWriteTimeImpl(PathInfo path)
        {
            // Handle special folders to return valid LastAccessTime
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }

                // For /drive and /, get the oldest CreationTime of all folders (approx)
                if (path == PathDrivePrefixOnWindows || path == PathInfo.Root)
                {
                    var lastWriteTime = DateTime.MaxValue;

                    foreach (var drive in DriveInfo.GetDrives())
                    {
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

            return File.GetLastWriteTime(ConvertToSystem(path));
        }

        /// <inheritdoc />
        protected override void SetLastWriteTimeImpl(PathInfo path, DateTime time)
        {
            // Handle special folders
            if (IsWithinSpecialDirectory(path))
            {
                if (!SpecialDirectoryExists(path))
                {
                    throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                }
                throw new UnauthorizedAccessException($"Cannot set last write time on system directory `{path}`");
            }

            File.SetLastWriteTime(ConvertToSystem(path), time);
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
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
                        throw new DirectoryNotFoundException($"The directory `{path}` does not exist");
                    }

                    var searchForDirectory = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.Directory;

                    // Only sub folder "/drive/" on root folder /
                    if (path == PathInfo.Root)
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
                        var pathDrives = new List<PathInfo>();
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

            switch (searchTarget)
            {
                case SearchTarget.File:
                    foreach (var subPath in Directory.EnumerateFiles(ConvertToSystem(path), searchPattern, searchOption))
                        yield return ConvertFromSystem(subPath);
                    break;
                case SearchTarget.Directory:
                    foreach (var subPath in Directory.EnumerateDirectories(ConvertToSystem(path), searchPattern, searchOption))
                        yield return ConvertFromSystem(subPath);
                    break;
                case SearchTarget.Both:
                    foreach (var subPath in Directory.EnumerateFileSystemEntries(ConvertToSystem(path), searchPattern, searchOption))
                        yield return ConvertFromSystem(subPath);
                    break;
            }
        }

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        /// <inheritdoc />
        protected override string ConvertToSystemImpl(PathInfo path)
        {
            var absolutePath = path.FullName;

            if (IsOnWindows)
            {
                if (!absolutePath.StartsWith(DrivePrefixOnWindows) ||
                    absolutePath.Length == DrivePrefixOnWindows.Length ||
                    !IsDriveLetter(absolutePath[DrivePrefixOnWindows.Length]))
                    throw new ArgumentException($"A path on Windows must start by `{DrivePrefixOnWindows}` followed by the drive letter");

                var driveLetter = char.ToUpper(absolutePath[DrivePrefixOnWindows.Length]);
                if (absolutePath.Length != DrivePrefixOnWindows.Length + 1 &&
                    absolutePath[DrivePrefixOnWindows.Length + 1] !=
                    PathInfo.DirectorySeparator)
                    throw new ArgumentException($"The driver letter `/{DrivePrefixOnWindows}{absolutePath[DrivePrefixOnWindows.Length]}` must be followed by a `/` or nothing in the path -> `{absolutePath}`");

                var builder = PathInfo.GetSharedStringBuilder();
                builder.Append(driveLetter).Append(":\\");
                if (absolutePath.Length > DrivePrefixOnWindows.Length + 1)
                    builder.Append(absolutePath.Replace(PathInfo.DirectorySeparator, '\\').Substring(DrivePrefixOnWindows.Length + 2));

                var result = builder.ToString();
                builder.Length = 0;
                return result;
            }
            return absolutePath;
        }

        /// <inheritdoc />
        protected override PathInfo ConvertFromSystemImpl(string systemPath)
        {
            if (IsOnWindows)
            {
                // We currently don't support special Windows files (\\.\ \??\  DosDevices...etc.)
                if (systemPath.StartsWith(@"\\") || systemPath.StartsWith(@"\?"))
                    throw new NotSupportedException($"Path starting with `\\\\` or `\\?` are not supported -> `{systemPath}` ");

                var absolutePath = Path.GetFullPath(systemPath);
                var driveIndex = absolutePath.IndexOf(":\\", StringComparison.Ordinal);
                if (driveIndex != 1)
                    throw new ArgumentException($"Expecting a drive for the path `{absolutePath}`");

                var builder = PathInfo.GetSharedStringBuilder();
                builder.Append(DrivePrefixOnWindows).Append(char.ToLowerInvariant(absolutePath[0])).Append('/');
                if (absolutePath.Length > 2)
                    builder.Append(absolutePath.Substring(2));

                var result = builder.ToString();
                builder.Length = 0;
                return new PathInfo(result);
            }
            return systemPath;
        }

        private static bool IsWithinSpecialDirectory(PathInfo path)
        {
            if (!IsOnWindows)
            {
                return false;
            }

            var parentDirectory = path.GetDirectory();
            return path == PathDrivePrefixOnWindows ||
                   path == PathInfo.Root ||
                   parentDirectory == PathDrivePrefixOnWindows ||
                   parentDirectory == PathInfo.Root;
        }

        private static bool SpecialDirectoryExists(PathInfo path)
        {
            // /drive or / can be read
            if (path == PathDrivePrefixOnWindows || path == PathInfo.Root)
            {
                return true;
            }

            // If /xxx, invalid (parent folder is /)
            var parentDirectory = path.GetDirectory();
            if (parentDirectory == PathInfo.Root)
            {
                return false;
            }

            var dirName = path.GetName();
            // Else check that we have a valid drive path (e.g /drive/c)
            return parentDirectory == PathDrivePrefixOnWindows && 
                   dirName.Length == 1 && 
                   DriveInfo.GetDrives().Any(p => char.ToLowerInvariant(p.Name[0]) == char.ToLowerInvariant(dirName[0]));
        }

        private static bool IsDriveLetter(char c)
        {
            return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z';
        }
    }


    //#if NET35
    //            switch (searchTarget)
    //            {
    //                case SearchTarget.File:
    //                    foreach (var subPath in Directory.GetFiles(ConvertToSystem(path), searchPattern))
    //                        yield return ConvertFromSystem(subPath);
    //                    break;
    //                case SearchTarget.Directory:
    //                    foreach (var subPath in Directory.GetDirectories(ConvertToSystem(path), searchPattern, searchOption))
    //                        yield return ConvertFromSystem(subPath);
    //                    break;
    //                case SearchTarget.Both:
    //                    foreach (var subDirectory in Directory.GetDirectories(ConvertToSystem(path), searchPattern, searchOption))
    //                    {
    //                        yield return ConvertFromSystemImpl(subDirectory);
    //                        foreach (var filePath in Directory.GetFiles(subDirectory, searchPattern))
    //                            yield return ConvertFromSystem(filePath);
    //                    }
    //                    break;
    //            }
    //#else
}