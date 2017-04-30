// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System;
using System.IO;

using static Zio.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a secure view on a sub folder of another delegate <see cref="IFileSystem"/>
    /// </summary>
    public class SubFileSystem : ComposeFileSystem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SubFileSystem"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system to create a view from.</param>
        /// <param name="subPath">The sub path view to create filesystem.</param>
        /// <exception cref="DirectoryNotFoundException">If the directory subPath does not exist in the delegate FileSystem</exception>
        public SubFileSystem(IFileSystem fileSystem, UPath subPath) : base(fileSystem)
        {
            SubPath = subPath.AssertAbsolute(nameof(subPath));
            if (!fileSystem.DirectoryExists(SubPath))
            {
                throw NewDirectoryNotFoundException(SubPath);
            }
        }

        /// <summary>
        /// Gets the sub path relative to the delegate <see cref="ComposeFileSystem.NextFileSystem"/>
        /// </summary>
        public UPath SubPath { get; }

        /// <inheritdoc />
        protected override UPath ConvertPathToDelegate(UPath path)
        {
            var safePath = path.ToRelative();
            return SubPath / safePath;
        }

        /// <inheritdoc />
        protected override UPath ConvertPathFromDelegate(UPath path)
        {
            var fullPath = path.FullName;
            if (!fullPath.StartsWith(SubPath.FullName) || fullPath.Length <= SubPath.FullName.Length || fullPath[SubPath.FullName.Length] != UPath.DirectorySeparator)
            {
                // More a safe guard, as it should never happen, but if a delegate filesystem doesn't respect its root path
                // we are throwing an exception here
                throw new InvalidOperationException($"The path `{path}` returned by the delegate filesystem is not rooted to the subpath `{SubPath}`");
            }

            return new UPath(fullPath.Substring(SubPath.FullName.Length), true);
        }
    }
}