// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

namespace Zio;

/// <summary>
/// Specifies changes to watch for in a file or directory.
/// </summary>
[Flags]
public enum NotifyFilters
{
    /// <summary>
    /// The name of the file.
    /// </summary>
    FileName = 1,

    /// <summary>
    /// The name of the directory.
    /// </summary>
    DirectoryName = 2,

    /// <summary>
    /// The attributes of the file or directory.
    /// </summary>
    Attributes = 4,

    /// <summary>
    /// The size of the file or directory.
    /// </summary>
    Size = 8,

    /// <summary>
    /// The date the file or directory last had something written to it.
    /// </summary>
    LastWrite = 16,

    /// <summary>
    /// The date the file or directory was last opened.
    /// </summary>
    LastAccess = 32,

    /// <summary>
    /// The date the file or directory was created.
    /// </summary>
    CreationTime = 64,

    /// <summary>
    /// The security settings of the file or directory.
    /// </summary>
    Security = 256,

    /// <summary>
    /// The default watch filters for <see cref="IFileSystemWatcher"/>.
    /// </summary>
    Default = FileName | DirectoryName | LastWrite
}
