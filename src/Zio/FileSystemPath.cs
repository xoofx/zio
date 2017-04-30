// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;

namespace Zio
{
    /// <summary>
    /// Tuple between a <see cref="IFileSystem"/> and an associated <see cref="UPath"/>
    /// </summary>
    internal struct FileSystemPath : IEquatable<FileSystemPath>
    {
        public static readonly FileSystemPath Empty = new FileSystemPath();

        public FileSystemPath(IFileSystem fileSystem, UPath path)
        {
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            path.AssertAbsolute();
            FileSystem = fileSystem;
            Path = path;
        }

        public readonly IFileSystem FileSystem;

        public readonly UPath Path;

        public bool Equals(FileSystemPath other)
        {
            return Equals(FileSystem, other.FileSystem) && Path.Equals(other.Path);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is FileSystemPath && Equals((FileSystemPath) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((FileSystem != null ? FileSystem.GetHashCode() : 0) * 397) ^ Path.GetHashCode();
            }
        }

        public static bool operator ==(FileSystemPath left, FileSystemPath right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FileSystemPath left, FileSystemPath right)
        {
            return !left.Equals(right);
        }
    }
}