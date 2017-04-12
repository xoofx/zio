// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
namespace Zio
{
    /// <summary>
    /// Defines the behavior of <see cref="IFileSystem.EnumeratePaths"/> when looking for files and/or folders.
    /// </summary>
    public enum SearchTarget
    {
        /// <summary>
        /// Search for both files and folders.
        /// </summary>
        Both,

        /// <summary>
        /// Search for files.
        /// </summary>
        File,

        /// <summary>
        /// Search for directories.
        /// </summary>
        Directory
    }
}