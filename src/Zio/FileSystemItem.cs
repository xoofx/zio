// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Zio
{
    /// <summary>
    /// Similar to <see cref="FileSystemEntry"/> but returned directly by <see cref="IFileSystem.EnumerateItems"/>
    /// </summary>
    public struct FileSystemItem
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="fileSystem">The root filesystem</param>
        /// <param name="path">The path relative to the root filesystem</param>
        /// <param name="directory"><c>true</c> if this is a directory; otherwise it is a file.</param>
        public FileSystemItem(IFileSystem fileSystem, UPath path, bool directory) : this()
        {
            FileSystem = fileSystem;
            AbsolutePath = path;
            Path = path;
            Attributes = directory ? FileAttributes.Directory : FileAttributes.Normal;
        }

        /// <summary>
        /// Return true if this item is empty;
        /// </summary>
        public readonly bool IsEmpty => FileSystem == null;

        /// <summary>
        /// Parent file system.
        /// </summary>
        public IFileSystem? FileSystem;

        /// <summary>
        /// The path of this item from the <see cref="FileSystem"/>.
        /// </summary>
        public UPath AbsolutePath { get; set; }

        /// <summary>
        /// Gets the full name
        /// </summary>
        public readonly string FullName => Path.FullName;

        /// <summary>
        /// Gets the name of the file or directory (with its extension).
        /// </summary>
        public readonly string GetName() => Path.GetName();

        /// <summary>
        /// The path of this item relative to the composite file system.
        /// </summary>
        public UPath Path;
        
        /// <summary>
        /// The creation time for the entry or the oldest available time stamp if the
        /// operating system does not support creation time stamps.
        /// </summary>
        public DateTimeOffset CreationTime;

        /// <summary>
        /// Last Access time (UTC).
        /// </summary>
        public DateTimeOffset LastAccessTime;

        /// <summary>
        /// Last Write Time (UTC).
        /// </summary>
        public DateTimeOffset LastWriteTime;

        /// <summary>
        /// File attributes.
        /// </summary>
        public FileAttributes Attributes;

        /// <summary>
        /// Length of the file.
        /// </summary>
        public long Length;

        /// <summary>
        /// Returns true if this entry is a directory.
        /// </summary>
        public readonly bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

        /// <summary>
        /// Returns true if the file has the hidden attribute.
        /// </summary>
        public readonly bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;

        /// <summary>Opens a file in the specified mode with read, write, or read/write access and the specified sharing option.</summary>
        /// <returns>A <see cref="T:System.IO.FileStream" /> object opened with the specified mode, access, and sharing options.</returns>
        /// <param name="mode">
        ///     A <see cref="T:System.IO.FileMode" /> constant specifying the mode (for example, Open or Append) in
        ///     which to open the file.
        /// </param>
        /// <param name="access">
        ///     A <see cref="T:System.IO.FileAccess" /> constant specifying whether to open the file with Read,
        ///     Write, or ReadWrite file access.
        /// </param>
        /// <param name="share">
        ///     A <see cref="T:System.IO.FileShare" /> constant specifying the type of access other FileStream
        ///     objects have to this file.
        /// </param>
        /// <exception cref="T:System.Security.SecurityException">The caller does not have the required permission. </exception>
        /// <exception cref="T:System.IO.FileNotFoundException">The file is not found. </exception>
        /// <exception cref="T:System.UnauthorizedAccessException">
        ///     The path is read-only or is a directory.
        /// </exception>
        /// <exception cref="T:System.IO.DirectoryNotFoundException">
        ///     The specified path is invalid, such as being on an unmapped
        ///     drive.
        /// </exception>
        /// <exception cref="T:System.IO.IOException">The file is already open. </exception>
        public readonly ValueTask<Stream> Open(FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            if (FileSystem == null) throw NewThrowNotInitialized();
            return FileSystem.OpenFile(AbsolutePath, mode, access, share);
        }

        /// <summary>
        /// Checks if the file exists.
        /// </summary>
        /// <returns></returns>
        public readonly async ValueTask<bool> Exists() => FileSystem != null && (IsDirectory ? await FileSystem.DirectoryExists(AbsolutePath) : await FileSystem.FileExists(AbsolutePath));

        /// <summary>
        ///     Opens a file, reads all lines of the file with the specified encoding, and then closes the file.
        /// </summary>
        /// <returns>A string containing all lines of the file.</returns>
        /// <remarks>
        ///     This method attempts to automatically detect the encoding of a file based on the presence of byte order marks.
        ///     Encoding formats UTF-8 and UTF-32 (both big-endian and little-endian) can be detected.
        /// </remarks>
        public readonly Task<string> ReadAllText()
        {
            if (FileSystem == null) throw NewThrowNotInitialized();
            return FileSystem.ReadAllText(AbsolutePath);
        }
        private readonly InvalidOperationException NewThrowNotInitialized()
        {
            throw new InvalidOperationException("This instance is not initialized");
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return AbsolutePath.FullName;
        }
    }
}