// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using static Zio.FileSystems.FileSystemExceptionHelper;

namespace Zio
{
    /// <summary>
    /// Extension methods for <see cref="IFileSystem"/>
    /// </summary>
    public static class FileSystemExtensions
    {
        /// <summary>
        /// Copies a file between two filesystems.
        /// </summary>
        /// <param name="fs">The source filesystem</param>
        /// <param name="destFileSystem">The destination filesystem</param>
        /// <param name="srcPath">The source path of the file to copy from the source filesystem</param>
        /// <param name="destPath">The destination path of the file in the destination filesystem</param>
        /// <param name="overwrite"><c>true</c> to overwrite an existing destination file</param>
        public static void CopyFileTo(this IFileSystem fs, IFileSystem destFileSystem, UPath srcPath, UPath destPath, bool overwrite)
        {
            if (destFileSystem == null) throw new ArgumentNullException(nameof(destFileSystem));

            // If this is the same filesystem, use the file system directly to perform the action
            if (fs == destFileSystem)
            {
                fs.CopyFile(srcPath, destPath, overwrite);
                return;
            }

            srcPath.AssertAbsolute(nameof(srcPath));
            if (!fs.FileExists(srcPath))
            {
                throw NewFileNotFoundException(srcPath);
            }

            destPath.AssertAbsolute(nameof(destPath));
            var destDirectory = destPath.GetDirectory();
            if (!destFileSystem.DirectoryExists(destDirectory))
            {
                throw NewDirectoryNotFoundException(destDirectory);
            }

            if (destFileSystem.FileExists(destPath) && !overwrite)
            {
                throw new IOException($"The destination file path `{destPath}` already exist and overwrite is false");
            }

            using (var sourceStream = fs.OpenFile(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool copied = false;
                try
                {
                    using (var destStream = destFileSystem.OpenFile(destPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        sourceStream.CopyTo(destStream);
                    }

                    // Preserve attributes and LastWriteTime as a regular File.Copy
                    destFileSystem.SetAttributes(destPath, fs.GetAttributes(srcPath));
                    destFileSystem.SetLastWriteTime(destPath, fs.GetLastWriteTime(srcPath));

                    copied = true;
                }
                finally
                {
                    if (!copied)
                    {
                        try
                        {
                            destFileSystem.DeleteFile(destPath);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Moves a file between two filesystems.
        /// </summary>
        /// <param name="fs">The source filesystem</param>
        /// <param name="destFileSystem">The destination filesystem</param>
        /// <param name="srcPath">The source path of the file to move from the source filesystem</param>
        /// <param name="destPath">The destination path of the file in the destination filesystem</param>
        public static void MoveFileTo(this IFileSystem fs, IFileSystem destFileSystem, UPath srcPath, UPath destPath)
        {
            if (destFileSystem == null) throw new ArgumentNullException(nameof(destFileSystem));

            // If this is the same filesystem, use the file system directly to perform the action
            if (fs == destFileSystem)
            {
                fs.MoveFile(srcPath, destPath);
                return;
            }

            // Check source
            srcPath.AssertAbsolute(nameof(srcPath));
            if (!fs.FileExists(srcPath))
            {
                throw NewFileNotFoundException(srcPath);
            }

            // Check destination
            destPath.AssertAbsolute(nameof(destPath));
            var destDirectory = destPath.GetDirectory();
            if (!destFileSystem.DirectoryExists(destDirectory))
            {
                throw NewDirectoryNotFoundException(destPath);
            }

            if (destFileSystem.DirectoryExists(destPath))
            {
                throw NewDestinationDirectoryExistException(destPath);
            }

            if (destFileSystem.FileExists(destPath))
            {
                throw NewDestinationFileExistException(destPath);
            }

            using (var sourceStream = fs.OpenFile(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool copied = false;
                try
                {
                    using (var destStream = destFileSystem.OpenFile(destPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        sourceStream.CopyTo(destStream);
                    }

                    // Preserve all attributes and times
                    destFileSystem.SetAttributes(destPath, fs.GetAttributes(srcPath));
                    destFileSystem.SetCreationTime(destPath, fs.GetCreationTime(srcPath));
                    destFileSystem.SetLastAccessTime(destPath, fs.GetLastAccessTime(srcPath));
                    destFileSystem.SetLastWriteTime(destPath, fs.GetLastWriteTime(srcPath));
                    copied = true;
                }
                finally
                {
                    if (!copied)
                    {
                        try
                        {
                            destFileSystem.DeleteFile(destPath);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }

            bool deleted = false;
            try
            {
                fs.DeleteFile(srcPath);
                deleted = true;
            }
            finally
            {
                if (!deleted)
                {
                    try
                    {
                        destFileSystem.DeleteFile(destPath);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }

        public static string ReadAllText(this IFileSystem fs, UPath path)
        {
            var stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public static void WriteAllText(this IFileSystem fs, UPath path, string content, Encoding encoding = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var stream = fs.OpenFile(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            {
                using (var writer = new StreamWriter(stream, encoding ?? Encoding.UTF8))
                {
                    writer.Write(content);
                    writer.Flush();
                }
            }
        }

        public static void AppendAllText(this IFileSystem fs, UPath path, string content, Encoding encoding = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var stream = fs.OpenFile(path, FileMode.Append, FileAccess.Write);
            {
                using (var writer = new StreamWriter(stream, encoding ?? Encoding.UTF8))
                {
                    writer.Write(content);
                    writer.Flush();
                }
            }
        }

        public static Stream CreateFile(this IFileSystem fileSystem, UPath path)
        {
            path.AssertAbsolute();
            return fileSystem.OpenFile(path, FileMode.Create, FileAccess.ReadWrite);
        }

        public static IEnumerable<UPath> EnumerateDirectories(this IFileSystem fileSystem, UPath path)
        {
            return EnumerateDirectories(fileSystem, path, "*");
        }

        public static IEnumerable<UPath> EnumerateDirectories(this IFileSystem fileSystem, UPath path, string searchPattern)
        {
            return EnumerateDirectories(fileSystem, path, "*", SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<UPath> EnumerateDirectories(this IFileSystem fileSystem, UPath path,
            string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in fileSystem.EnumeratePaths(path, searchPattern, searchOption,
                SearchTarget.Directory))
                yield return subPath;
        }

        public static IEnumerable<UPath> EnumerateFiles(this IFileSystem fileSystem, UPath path)
        {
            return EnumerateFiles(fileSystem, path, "*");
        }

        public static IEnumerable<UPath> EnumerateFiles(this IFileSystem fileSystem, UPath path,
            string searchPattern)
        {
            return EnumerateFiles(fileSystem, path, "*", SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<UPath> EnumerateFiles(this IFileSystem fileSystem, UPath path, string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in fileSystem.EnumeratePaths(path, searchPattern, searchOption, SearchTarget.File))
                yield return subPath;
        }

        public static IEnumerable<UPath> EnumeratePaths(this IFileSystem fileSystem, UPath path)
        {
            return EnumeratePaths(fileSystem, path, "*");
        }

        public static IEnumerable<UPath> EnumeratePaths(this IFileSystem fileSystem, UPath path,
            string searchPattern)
        {
            return EnumeratePaths(fileSystem, path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<UPath> EnumeratePaths(this IFileSystem fileSystem, UPath path, string searchPattern, SearchOption searchOption)
        {
            return fileSystem.EnumeratePaths(path, searchPattern, searchOption, SearchTarget.Both);
        }


        public static IEnumerable<FileEntry> EnumerateFileEntries(this IFileSystem fileSystem, UPath path)
        {
            return EnumerateFileEntries(fileSystem, path, "*");
        }

        public static IEnumerable<FileEntry> EnumerateFileEntries(this IFileSystem fileSystem, UPath path,
            string searchPattern)
        {
            return EnumerateFileEntries(fileSystem, path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<FileEntry> EnumerateFileEntries(this IFileSystem fileSystem, UPath path, string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in EnumerateFiles(fileSystem, path, searchPattern, searchOption))
            {
                yield return new FileEntry(fileSystem, subPath);
            }
        }

        public static IEnumerable<DirectoryEntry> EnumerateDirectoryEntries(this IFileSystem fileSystem, UPath path)
        {
            return EnumerateDirectoryEntries(fileSystem, path, "*");
        }

        public static IEnumerable<DirectoryEntry> EnumerateDirectoryEntries(this IFileSystem fileSystem, UPath path,
            string searchPattern)
        {
            return EnumerateDirectoryEntries(fileSystem, path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<DirectoryEntry> EnumerateDirectoryEntries(this IFileSystem fileSystem, UPath path, string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in EnumerateDirectories(fileSystem, path, searchPattern, searchOption))
            {
                yield return new DirectoryEntry(fileSystem, subPath);
            }
        }

        public static IEnumerable<FileSystemEntry> EnumerateEntries(this IFileSystem fileSystem, UPath path)
        {
            return EnumerateEntries(fileSystem, path, "*");
        }

        public static IEnumerable<FileSystemEntry> EnumerateEntries(this IFileSystem fileSystem, UPath path,
            string searchPattern)
        {
            return EnumerateEntries(fileSystem, path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<FileSystemEntry> EnumerateEntries(this IFileSystem fileSystem, UPath path, string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in fileSystem.EnumeratePaths(path, searchPattern, searchOption, SearchTarget.Both))
            {
                yield return fileSystem.DirectoryExists(subPath) ? (FileSystemEntry)new DirectoryEntry(fileSystem, subPath) : new FileEntry(fileSystem, subPath);
            }
        }

        public static FileEntry GetFileEntry(this IFileSystem fileSystem, UPath filePath)
        {
            if (!fileSystem.FileExists(filePath))
            {
                throw NewFileNotFoundException(filePath);
            }
            return new FileEntry(fileSystem, filePath);
        }

        public static DirectoryEntry GetDirectoryEntry(this IFileSystem fileSystem, UPath directoryPath)
        {
            if (!fileSystem.DirectoryExists(directoryPath))
            {
                throw NewDirectoryNotFoundException(directoryPath);
            }
            return new DirectoryEntry(fileSystem, directoryPath);
        }
    }
}