// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

using static Zio.FileSystems.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides an merged readonly view filesystem over multiple filesystems (overriding files/directory in order)
    /// </summary>
    public class AggregateFileSystem : ReadOnlyFileSystem
    {
        private readonly List<IFileSystem> _fileSystems;

        public AggregateFileSystem() : this(null)
        {
        }

        public AggregateFileSystem(IFileSystem fileSystem) : base(fileSystem)
        {
            _fileSystems = new List<IFileSystem>();
        }

        public List<IFileSystem> GetFileSystems()
        {
            lock (_fileSystems)
            {
                return new List<IFileSystem>(_fileSystems);
            }
        }

        public void AddFileSystem(IFileSystem fs)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            if (fs == this) throw new ArgumentException("Cannot add this instance as an aggregate delegate of itself");

            lock (_fileSystems)
            {
                if (!_fileSystems.Contains(fs))
                {
                    _fileSystems.Add(fs);
                }
                else
                {
                    throw new InvalidOperationException("The filesystem is already added");
                }
            }
        }

        public void RemoveFileSystem(IFileSystem fs)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            lock (_fileSystems)
            {
                if (!_fileSystems.Contains(fs))
                {
                    _fileSystems.Remove(fs);
                }
                else
                {
                    throw new ArgumentException("FileSystem was not found", nameof(fs));
                }
            }
        }

        protected override Stream OpenFileImpl(UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (mode != FileMode.Open)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            if ((access & FileAccess.Write) != 0)
            {
                throw new IOException(FileSystemIsReadOnly);
            }

            var entry = GetFile(path);
            return entry.FileSystem.OpenFile(path, mode, access, share);
        }

        protected override FileAttributes GetAttributesImpl(UPath path)
        {
            var entry = GetFile(path);
            return entry.FileSystem.GetAttributes(path);
        }

        protected override UPath ConvertPathToDelegate(UPath path)
        {
            return path;
        }

        protected override UPath ConvertPathFromDelegate(UPath path)
        {
            return path;
        }

        protected override bool DirectoryExistsImpl(UPath path)
        {
            var directory = TryGetDirectory(path);
            return directory.HasValue;
        }

        protected override long GetFileLengthImpl(UPath path)
        {
            var entry = GetFile(path);
            return entry.FileSystem.GetFileLength(path);
        }

        protected override bool FileExistsImpl(UPath path)
        {
            var entry = TryGetFile(path);
            return entry.HasValue;
        }

        protected override DateTime GetCreationTimeImpl(UPath path)
        {
            var entry = TryGetFile(path);
            return entry.HasValue ? entry.Value.SystemPath.FileSystem.GetCreationTime(path) : DefaultFileTime;
        }

        protected override DateTime GetLastAccessTimeImpl(UPath path)
        {
            var entry = TryGetFile(path);
            return entry.HasValue ? entry.Value.SystemPath.FileSystem.GetLastWriteTime(path) : DefaultFileTime;
        }

        protected override DateTime GetLastWriteTimeImpl(UPath path)
        {
            var entry = TryGetFile(path);
            return entry.HasValue ? entry.Value.SystemPath.FileSystem.GetLastWriteTime(path) : DefaultFileTime;
        }

        protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            var search = SearchPattern.Parse(ref path, ref searchPattern);

            var directoryToVisit = new List<UPath>();
            directoryToVisit.Add(path);

            var entries = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);
            var sortedDirectories = new SortedSet<UPath>(UPath.DefaultComparerIgnoreCase);
            var fileSystems = new List<IFileSystem>();

            if (NextFileSystem != null)
            {
                fileSystems.Add(NextFileSystem);
            }

            // Query all filesystems just once
            lock (_fileSystems)
            {
                fileSystems.AddRange(_fileSystems);
            }

            while (directoryToVisit.Count > 0)
            {
                var pathToVisit = directoryToVisit[0];
                directoryToVisit.RemoveAt(0);
                int dirIndex = 0;
                entries.Clear();
                sortedDirectories.Clear();

                for (var i = fileSystems.Count - 1; i >= 0; i--)
                {
                    var fileSystem = fileSystems[i];

                    if (fileSystem.DirectoryExists(pathToVisit))
                    {
                        foreach (var item in fileSystem.EnumeratePaths(pathToVisit, "*", SearchOption.TopDirectoryOnly, SearchTarget.Both))
                        {
                            if (!entries.Contains(item))
                            {
                                var isFile = fileSystem.FileExists(item);
                                var isDirectory = fileSystem.DirectoryExists(item);
                                var isMatching = search.Match(item);

                                if (isMatching && ((isFile && searchTarget != SearchTarget.Directory) || (isDirectory && searchTarget != SearchTarget.File)))
                                {
                                    entries.Add(item);
                                }

                                if (searchOption == SearchOption.AllDirectories && isDirectory)
                                {
                                    sortedDirectories.Add(item);
                                }
                            }
                        }
                    }
                }

                // Enqueue directories and respect order
                foreach (var nextDir in sortedDirectories)
                {
                    directoryToVisit.Insert(dirIndex++, nextDir);
                }

                // Return entries
                foreach (var entry in entries)
                {
                    yield return entry;
                }
            }
        }

        protected override string ConvertToSystemImpl(UPath path)
        {
            // TODO: how to implement this correctly?
            return path.FullName;
        }

        protected override UPath ConvertFromSystemImpl(string systemPath)
        {
            // TODO: how to implement this correctly?
            return (UPath) systemPath;
        }

        private FileSystemPath GetFile(UPath path)
        {
            var entry = TryGetFile(path);
            if (!entry.HasValue)
            {
                throw NewFileNotFoundException(path);
            }
            return entry.Value.SystemPath;
        }

        private FileSystemPath GetDirectory(UPath path)
        {
            var entry = TryGetDirectory(path);
            if (!entry.HasValue)
            {
                throw NewDirectoryNotFoundException(path);
            }
            return entry.Value.SystemPath;
        }

        private FileSystemPathExtended? TryGetFile(UPath path)
        {
            var entries = FindPaths(path, SearchTarget.File);
            return entries.Count > 0 ? (FileSystemPathExtended?)entries[0] : null;
        }

        private FileSystemPathExtended? TryGetDirectory(UPath path)
        {
            var entries = FindPaths(path, SearchTarget.Directory);
            return entries.Count > 0 ? (FileSystemPathExtended?)entries[0] : null;
        }

        private List<FileSystemPathExtended> FindPaths(UPath path, SearchTarget searchTarget)
        {
            var paths = new List<FileSystemPathExtended>();

            bool queryDirectory = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.Directory;
            bool queryFile = searchTarget == SearchTarget.Both || searchTarget == SearchTarget.File;

            lock (_fileSystems)
            {
                for (var i = _fileSystems.Count - 1; i >= -1; i--)
                {
                    var fileSystem = i < 0 ? NextFileSystem : _fileSystems[i];

                    if (fileSystem == null)
                    {
                        break;
                    }

                    bool isFile = false;
                    if ((queryDirectory && fileSystem.DirectoryExists(path)) || (queryFile && (isFile = fileSystem.FileExists(path))))
                    {
                        paths.Add(new FileSystemPathExtended(new FileSystemPath(fileSystem, path), isFile));
                    }
                }
            }

            return paths;
        }

        private struct FileSystemPathExtended
        {
            public FileSystemPathExtended(FileSystemPath systemPath, bool isFile)
            {
                SystemPath = systemPath;
                IsFile = isFile;
            }

            public readonly FileSystemPath SystemPath;

            public readonly bool IsFile;
        }
    }
}