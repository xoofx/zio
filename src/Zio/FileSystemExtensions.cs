// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Zio
{
    /// <summary>
    /// Extension methods for <see cref="IFileSystem"/>
    /// </summary>
    public static class FileSystemExtensions
    {
        /// <summary>
        /// Copies a file from a source <see cref="IFileSystem"/> to a destination file system
        /// </summary>
        /// <param name="fs">The source filesystem</param>
        /// <param name="destFileSystem">The destination filesystem</param>
        /// <param name="srcPath">The source path of the file to copy from the source filesystem</param>
        /// <param name="destPath">The destination path of the file in the destination filesystem</param>
        /// <param name="overwrite"><c>true</c> to overwrite an existing destination file</param>
        public static void CopyFileTo(this IFileSystem fs, IFileSystem destFileSystem, PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            if (destFileSystem == null) throw new ArgumentNullException(nameof(destFileSystem));

            // If this is the same filesystem, don't try to copy it directly
            if (fs == destFileSystem)
            {
                fs.CopyFile(srcPath, destPath, overwrite);
                return;
            }

            srcPath.AssertAbsolute(nameof(srcPath));
            destPath.AssertAbsolute(nameof(destPath));

            if (!fs.FileExists(srcPath))
            {
                throw new FileNotFoundException($"The file path `{srcPath}` does not exist");
            }

            var destDirectory = destPath.GetDirectory();
            if (!destFileSystem.DirectoryExists(destDirectory))
            {
                throw new DirectoryNotFoundException($"The destination directory `{destDirectory}` does not exist");
            }

            if (destFileSystem.FileExists(destPath) && !overwrite)
            {
                throw new IOException($"The destination file path `{destPath}` already exist and overwrite is false");
            }

            using (var sourceStream = fs.OpenFile(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var destStream = destFileSystem.OpenFile(destPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    sourceStream.CopyTo(destStream);
                }
            }
        }

        public static string ReadAllText(this IFileSystem fs, PathInfo path)
        {
            var stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public static void WriteAllText(this IFileSystem fs, PathInfo path, string content, Encoding encoding = null)
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

        public static void AppendAllText(this IFileSystem fs, PathInfo path, string content, Encoding encoding = null)
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

        public static Stream CreateFile(this IFileSystem fileSystem, PathInfo path)
        {
            path.AssertAbsolute();
            return fileSystem.OpenFile(path, FileMode.Create, FileAccess.ReadWrite);
        }

        public static IEnumerable<PathInfo> EnumerateDirectories(this IFileSystem fileSystem, PathInfo path)
        {
            return EnumerateDirectories(fileSystem, path, "*");
        }

        public static IEnumerable<PathInfo> EnumerateDirectories(this IFileSystem fileSystem, PathInfo path, string searchPattern)
        {
            return EnumerateDirectories(fileSystem, path, "*", SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<PathInfo> EnumerateDirectories(this IFileSystem fileSystem, PathInfo path,
            string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in fileSystem.EnumeratePaths(path, searchPattern, searchOption,
                SearchTarget.Directory))
                yield return subPath;
        }

        public static IEnumerable<PathInfo> EnumerateFiles(this IFileSystem fileSystem, PathInfo path)
        {
            return EnumerateFiles(fileSystem, path, "*");
        }

        public static IEnumerable<PathInfo> EnumerateFiles(this IFileSystem fileSystem, PathInfo path,
            string searchPattern)
        {
            return EnumerateFiles(fileSystem, path, "*", SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<PathInfo> EnumerateFiles(this IFileSystem fileSystem, PathInfo path, string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in fileSystem.EnumeratePaths(path, searchPattern, searchOption, SearchTarget.File))
                yield return subPath;
        }

        public static IEnumerable<PathInfo> EnumeratePaths(this IFileSystem fileSystem, PathInfo path)
        {
            return EnumeratePaths(fileSystem, path, "*");
        }

        public static IEnumerable<PathInfo> EnumeratePaths(this IFileSystem fileSystem, PathInfo path,
            string searchPattern)
        {
            return EnumeratePaths(fileSystem, path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<PathInfo> EnumeratePaths(this IFileSystem fileSystem, PathInfo path, string searchPattern, SearchOption searchOption)
        {
            return fileSystem.EnumeratePaths(path, searchPattern, searchOption, SearchTarget.Both);
        }


        public static IEnumerable<FileEntry> EnumerateFileEntries(this IFileSystem fileSystem, PathInfo path)
        {
            return EnumerateFileEntries(fileSystem, path, "*");
        }

        public static IEnumerable<FileEntry> EnumerateFileEntries(this IFileSystem fileSystem, PathInfo path,
            string searchPattern)
        {
            return EnumerateFileEntries(fileSystem, path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<FileEntry> EnumerateFileEntries(this IFileSystem fileSystem, PathInfo path, string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in EnumerateFiles(fileSystem, path, searchPattern, searchOption))
            {
                yield return new FileEntry(fileSystem, subPath);
            }
        }

        public static IEnumerable<DirectoryEntry> EnumerateDirectoryEntries(this IFileSystem fileSystem, PathInfo path)
        {
            return EnumerateDirectoryEntries(fileSystem, path, "*");
        }

        public static IEnumerable<DirectoryEntry> EnumerateDirectoryEntries(this IFileSystem fileSystem, PathInfo path,
            string searchPattern)
        {
            return EnumerateDirectoryEntries(fileSystem, path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<DirectoryEntry> EnumerateDirectoryEntries(this IFileSystem fileSystem, PathInfo path, string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in EnumerateDirectories(fileSystem, path, searchPattern, searchOption))
            {
                yield return new DirectoryEntry(fileSystem, subPath);
            }
        }

        public static IEnumerable<FileSystemEntry> EnumerateEntries(this IFileSystem fileSystem, PathInfo path)
        {
            return EnumerateEntries(fileSystem, path, "*");
        }

        public static IEnumerable<FileSystemEntry> EnumerateEntries(this IFileSystem fileSystem, PathInfo path,
            string searchPattern)
        {
            return EnumerateEntries(fileSystem, path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<FileSystemEntry> EnumerateEntries(this IFileSystem fileSystem, PathInfo path, string searchPattern, SearchOption searchOption)
        {
            foreach (var subPath in fileSystem.EnumeratePaths(path, searchPattern, searchOption, SearchTarget.Both))
            {
                yield return fileSystem.DirectoryExists(subPath) ? (FileSystemEntry)new DirectoryEntry(fileSystem, subPath) : new FileEntry(fileSystem, subPath);
            }
        }

        public static FileEntry GetFileEntry(this IFileSystem fileSystem, PathInfo filePath)
        {
            if (!fileSystem.FileExists(filePath))
            {
                throw new FileNotFoundException($"The file `{filePath}` does not exist");
            }
            return new FileEntry(fileSystem, filePath);
        }

        public static DirectoryEntry GetDirectoryEntry(this IFileSystem fileSystem, PathInfo directoryPath)
        {
            if (!fileSystem.DirectoryExists(directoryPath))
            {
                throw new DirectoryNotFoundException($"The directory `{directoryPath}` was not found");
            }
            return new DirectoryEntry(fileSystem, directoryPath);
        }
    }
}