// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System;
using System.IO;

namespace Zio
{
    /// <summary>
    /// Similar to <see cref="FileSystemInfo"/> but to use with <see cref="IFileSystem"/>, provides the base class 
    /// for both <see cref="FileEntry"/> and <see cref="DirectoryEntry"/> objects.
    /// </summary>
    public abstract class FileSystemEntry
    {
        protected FileSystemEntry(IFileSystem fileSystem, UPath path)
        {
            FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            path.AssertAbsolute();
            Path = path;
        }

        /// <summary>
        /// Gets the path of this entry.
        /// </summary>
        public UPath Path { get; }

        /// <summary>
        /// Gets the file system used by this entry.
        /// </summary>
        public IFileSystem FileSystem { get; }

        /// <summary>
        /// Gets the full path of the directory or file.
        /// </summary>
        public string FullName => Path.FullName;

        /// <summary>
        /// Gets the name of the file or directory (with its extension).
        /// </summary>
        public string Name => Path.GetName();

        /// <summary>
        /// Gets the name of the file or directory without its extension.
        /// </summary>
        public string NameWithoutExtension => Path.GetNameWithoutExtension();

        /// <summary>
        /// Gets the extension with a leading dot.
        /// </summary>
        public string ExtensionWithDot => Path.GetExtensionWithDot();

        /// <summary>
        /// Gets or sets the attributes for the current file or directory
        /// </summary>
        public FileAttributes Attributes
        {
            get => FileSystem.GetAttributes(Path);

            set => FileSystem.SetAttributes(Path, value);
        }

        /// <summary>
        /// Gets a value indicating whether this file or directory exists.
        /// </summary>
        /// <value><c>true</c> if this file or directory exists; otherwise, <c>false</c>.</value>
        public abstract bool Exists { get; }

        /// <summary>
        /// Gets or sets the creation time of the current file or directory.
        /// </summary>
        public DateTime CreationTime
        {
            get => FileSystem.GetCreationTime(Path);
            set => FileSystem.SetCreationTime(Path, value);
        }

        /// <summary>
        /// Gets or sets the last access time of the current file or directory.
        /// </summary>
        public DateTime LastAccessTime
        {
            get => FileSystem.GetLastAccessTime(Path);
            set => FileSystem.SetLastAccessTime(Path, value);
        }

        /// <summary>
        /// Gets or sets the last write time of the current file or directory.
        /// </summary>
        public DateTime LastWriteTime
        {
            get => FileSystem.GetLastWriteTime(Path);
            set => FileSystem.SetLastWriteTime(Path, value);
        }

        /// <summary>
        /// Deletes a file or directory.
        /// </summary>
        public abstract void Delete();

        public override string ToString()
        {
            return Path.FullName;
        }
    }
}