// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

namespace Zio;

/// <summary>
/// The <see cref="EventArgs"/> base class for file and directory events. Used for
/// <see cref="WatcherChangeTypes.Created"/>, <see cref="WatcherChangeTypes.Deleted"/>,
/// and <see cref="WatcherChangeTypes.Changed"/>.
/// </summary>
/// <inheritdoc />
public class FileChangedEventArgs : EventArgs
{
    /// <summary>
    /// The type of change that occurred.
    /// </summary>
    public WatcherChangeTypes ChangeType { get; }

    /// <summary>
    /// The filesystem originating this change.
    /// </summary>
    public IFileSystem FileSystem { get; }

    /// <summary>
    /// Absolute path to the file or directory.
    /// </summary>
    public UPath FullPath { get; }

    /// <summary>
    /// Name of the file or directory.
    /// </summary>
    public string Name { get; }

    public FileChangedEventArgs(IFileSystem fileSystem, WatcherChangeTypes changeType, UPath fullPath)
    {
        if (fileSystem is null) throw new ArgumentNullException(nameof(fileSystem));
        fullPath.AssertNotNull(nameof(fullPath));
        fullPath.AssertAbsolute(nameof(fullPath));

        FileSystem = fileSystem;
        ChangeType = changeType;
        FullPath = fullPath;
        Name = fullPath.GetName();
    }
}
