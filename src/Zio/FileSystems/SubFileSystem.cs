// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System;
using System.Diagnostics;
using System.IO;
using static Zio.FileSystemExceptionHelper;

namespace Zio.FileSystems
{
    /// <summary>
    /// Provides a secure view on a sub folder of another delegate <see cref="IFileSystem"/>
    /// </summary>
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "(),nq}")]
    public class SubFileSystem : ComposeFileSystem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SubFileSystem"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system to create a view from.</param>
        /// <param name="subPath">The sub path view to create filesystem.</param>
        /// <param name="owned">True if <paramref name="fileSystem"/> should be disposed when this instance is disposed.</param>
        /// <exception cref="DirectoryNotFoundException">If the directory subPath does not exist in the delegate FileSystem</exception>
        public SubFileSystem(IFileSystem fileSystem, UPath subPath, bool owned = true) : base(fileSystem, owned)
        {
            SubPath = subPath.AssertAbsolute(nameof(subPath));
            if (!fileSystem.DirectoryExists(SubPath))
            {
                throw NewDirectoryNotFoundException(SubPath);
            }
        }

        /// <summary>
        /// Gets the sub path relative to the delegate <see cref="ComposeFileSystem.Fallback"/>
        /// </summary>
        public UPath SubPath { get; }

        protected override string DebuggerDisplay()
        {
            return $"{base.DebuggerDisplay()} Path: {SubPath}";
        }

        /// <inheritdoc />
        protected override IFileSystemWatcher WatchImpl(UPath path)
        {
            var delegateWatcher = base.WatchImpl(path);
            return new Watcher(this, path, delegateWatcher);
        }

        private class Watcher : WrapFileSystemWatcher
        {
            private readonly SubFileSystem _fileSystem;

            public Watcher(SubFileSystem fileSystem, UPath path, IFileSystemWatcher watcher)
                : base(fileSystem, path, watcher)
            {
                _fileSystem = fileSystem;
            }

            protected override UPath? TryConvertPath(UPath pathFromEvent)
            {
                if (!pathFromEvent.IsInDirectory(_fileSystem.SubPath, true))
                {
                    return null;
                }

                return _fileSystem.ConvertPathFromDelegate(pathFromEvent);
            }
        }

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
            if (!fullPath.StartsWith(SubPath.FullName) || (fullPath.Length > SubPath.FullName.Length && fullPath[SubPath.FullName.Length] != UPath.DirectorySeparator))
            {
                // More a safe guard, as it should never happen, but if a delegate filesystem doesn't respect its root path
                // we are throwing an exception here
                throw new InvalidOperationException($"The path `{path}` returned by the delegate filesystem is not rooted to the subpath `{SubPath}`");
            }

            var subPath = fullPath.Substring(SubPath.FullName.Length);
            return subPath == string.Empty ? UPath.Root : new UPath(subPath, true);
        }
    }
}