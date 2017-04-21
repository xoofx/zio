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
        public static string ReadAllText(this IFileSystem fs, PathInfo path)
        {
            using (var stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read))
            {
                var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        public static void WriteAllText(this IFileSystem fs, PathInfo path, string content, Encoding encoding = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            using (var stream = fs.OpenFile(path, FileMode.OpenOrCreate, FileAccess.Write))
            {
                var writer = new StreamWriter(stream, encoding ?? Encoding.UTF8);
                writer.Write(content);
                writer.Flush();
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