// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

namespace Zio;

/// <summary>
/// Contains information about a filesystem error event.
/// </summary>
/// <inheritdoc />
public class FileSystemErrorEventArgs : EventArgs
{
    /// <summary>
    /// Exception that was thrown in the filesystem.
    /// </summary>
    public Exception Exception { get; }

    public FileSystemErrorEventArgs(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        Exception = exception;
    }
}
