// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Collections;
using System.IO;

namespace Zio;

/// <summary>
/// Extension methods for <see cref="UPath"/>
/// </summary>
public static class UPathExtensions
{
    /// <summary>
    /// Converts the specified path to a relative path (by removing the leading `/`). If the path is already relative, returns the input.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>A relative path.</returns>
    /// <exception cref="ArgumentNullException">if path is <see cref="UPath.IsNull"/></exception>
    public static UPath ToRelative(this UPath path)
    {
        path.AssertNotNull();

        if (path.IsRelative)
        {
            return path;
        }

        return path.FullName is "/" ? UPath.Empty : new UPath(path.FullName.Substring(1), true);
    }

    /// <summary>
    /// Converts the specified path to an absolute path (by adding a leading `/`). If the path is already absolute, returns the input.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>An absolute path.</returns>
    /// <exception cref="ArgumentNullException">if path is <see cref="UPath.IsNull"/></exception>
    public static UPath ToAbsolute(this UPath path)
    {
        path.AssertNotNull();

        if (path.IsAbsolute)
        {
            return path;
        }

        return path.IsEmpty ? UPath.Root : UPath.Root / path;
    }

    /// <summary>
    /// Gets the directory of the specified path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The directory of the path.</returns>
    /// <exception cref="ArgumentNullException">if path is <see cref="UPath.IsNull"/></exception>
    public static UPath GetDirectory(this UPath path)
    {
        path.AssertNotNull();

        var fullname = path.FullName;

        if (fullname is "/")
        {
            return new UPath();
        }

        var lastIndex = fullname.LastIndexOf(UPath.DirectorySeparator);
        if (lastIndex > 0)
        {
            return new UPath(fullname.Substring(0, lastIndex), true);
        }
        return lastIndex == 0 ? UPath.Root : UPath.Empty;
    }

    /// <summary>
    /// Gets the directory of the specified path as a span.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The directory of the path.</returns>
    /// <exception cref="ArgumentNullException">if path is <see cref="UPath.IsNull"/></exception>
    public static ReadOnlySpan<char> GetDirectoryAsSpan(this UPath path)
    {
        path.AssertNotNull();

        var fullname = path.FullName;

        if (fullname is "/")
        {
            return ReadOnlySpan<char>.Empty;
        }

        var lastIndex = fullname.LastIndexOf(UPath.DirectorySeparator);
        if (lastIndex > 0)
        {
            return fullname.AsSpan(0, lastIndex);
        }
        return lastIndex == 0 ? UPath.Root.FullName.AsSpan() : ReadOnlySpan<char>.Empty;
    }

    /// <summary>
    /// Gets the first directory of the specified path and return the remaining path (/a/b/c, first directory: /a, remaining: b/c)
    /// </summary>
    /// <param name="path">The path to extract the first directory and remaining.</param>
    /// <param name="remainingPath">The remaining relative path after the first directory</param>
    /// <returns>The first directory of the path.</returns>
    /// <exception cref="ArgumentNullException">if path is <see cref="UPath.IsNull"/></exception>
    public static string GetFirstDirectory(this UPath path, out UPath remainingPath)
    {
        path.AssertNotNull();
        remainingPath = UPath.Empty;

        string firstDirectory;
        var fullname = path.FullName;
        var offset = path.IsRelative ? 0 : 1;
        var index = fullname.IndexOf(UPath.DirectorySeparator, offset);
        if (index < 0)
        {
            firstDirectory = fullname.Substring(offset, fullname.Length - offset);
        }
        else
        {
            firstDirectory = fullname.Substring(offset, index - offset);
            if (index + 1 < fullname.Length)
            {
                remainingPath = fullname.Substring(index + 1);
            }
        }
        return firstDirectory;
    }

    /// <summary>
    /// Splits the specified path by directories using the directory separator character `/`
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>A list of sub path for each directory entry in the path (/a/b/c returns [a,b,c], or a/b/c returns [a,b,c].</returns>
    public static List<string> Split(this UPath path)
    {
        path.AssertNotNull();

        var fullname = path.FullName;
        if (fullname == string.Empty)
        {
            return new List<string>();
        }

        var paths = new List<string>();
        int previousIndex = path.IsAbsolute ? 1 : 0;
        int nextIndex;
        while ((nextIndex = fullname.IndexOf(UPath.DirectorySeparator, previousIndex)) >= 0)
        {
            if (nextIndex != 0)
            {
                paths.Add(fullname.Substring(previousIndex, nextIndex - previousIndex));
            }

            previousIndex = nextIndex + 1;
        }

        if (previousIndex < fullname.Length)
        {
            paths.Add(fullname.Substring(previousIndex, fullname.Length - previousIndex));
        }
        return paths;
    }

    /// <summary>
    /// Splits the specified path by directories using the directory separator character `/`
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>A list of sub path for each directory entry in the path (/a/b/c returns [a,b,c], or a/b/c returns [a,b,c].</returns>
    public static SplitEnumerator SpanSplit(this UPath path)
    {
        path.AssertNotNull();

        var span = path.IsAbsolute
            ? path.FullName.AsSpan(1)
            : path.FullName.AsSpan();

        return new SplitEnumerator(span);
    }

    /// <summary>
    /// Enumerator for <see cref="SpanSplit(UPath)"/>
    /// </summary>
    public ref struct SplitEnumerator
    {
        private ReadOnlySpan<char> _remaining;

        public ReadOnlySpan<char> Current { get; private set; }

        public int Count { get; }

        public SplitEnumerator(ReadOnlySpan<char> remaining)
        {
            _remaining = remaining;

            if (remaining.IsEmpty)
            {
                Count = 0;
            }
            else
            {
#if NET9_0_OR_GREATER
                Count = remaining.Count(UPath.DirectorySeparator) + 1;
#else
                Count++;
                foreach (var t in _remaining)
                {
                    if (t == UPath.DirectorySeparator)
                    {
                        Count++;
                    }
                }
#endif
            }
        }

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
            {
                return false;
            }

            var index = _remaining.IndexOf(UPath.DirectorySeparator);

            if (index < 0)
            {
                Current = _remaining;
                _remaining = ReadOnlySpan<char>.Empty;
            }
            else
            {
                Current = _remaining.Slice(0, index);
                _remaining = _remaining.Slice(index + 1);
            }

            return true;
        }

        public SplitEnumerator GetEnumerator() => this;
    }

    /// <summary>
    /// Gets the file or last directory name and extension of the specified path.
    /// </summary>
    /// <param name="path">The path string from which to obtain the file name and extension.</param>
    /// <returns>The characters after the last directory character in path. If path is null, this method returns null.</returns>
    public static string GetName(this UPath path)
    {
        return path.IsNull ? null! : Path.GetFileName(path.FullName);
    }

    /// <summary>
    /// Gets the file or last directory name without the extension for the specified path.
    /// </summary>
    /// <param name="path">The path string from which to obtain the file name without the extension.</param>
    /// <returns>The characters after the last directory character in path without the extension. If path is null, this method returns null.</returns>
    public static string? GetNameWithoutExtension(this UPath path)
    {
        return path.IsNull ? null : Path.GetFileNameWithoutExtension(path.FullName);
    }

    /// <summary>
    /// Gets the extension of the specified path.
    /// </summary>
    /// <param name="path">The path string from which to obtain the extension with a leading dot `.`.</param>
    /// <returns>The extension of the specified path (including the period "."), or null, or String.Empty. If path is null, GetExtension returns null. If path does not have extension information, GetExtension returns String.Empty..</returns>
    public static string? GetExtensionWithDot(this UPath path)
    {
        return path.IsNull ? null : Path.GetExtension(path.FullName);
    }

    /// <summary>
    /// Changes the extension of a path.
    /// </summary>
    /// <param name="path">The path information to modify. The path cannot contain any of the characters defined in GetInvalidPathChars.</param>
    /// <param name="extension">The new extension (with or without a leading period). Specify null to remove an existing extension from path.</param>
    /// <returns>The modified path information.</returns>
    public static UPath ChangeExtension(this UPath path, string extension)
    {
        return new UPath(Path.ChangeExtension(path.FullName, extension));
    }

    /// <summary>
    /// Checks if the path is in the given directory. Does not check if the paths exist.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <param name="directory">The directory to check the path against.</param>
    /// <param name="recursive">True to check if it is anywhere in the directory, false to check if it is directly in the directory.</param>
    /// <returns>True when the path is in the given directory.</returns>
    public static bool IsInDirectory(this UPath path, UPath directory, bool recursive)
    {
        path.AssertNotNull();
        directory.AssertNotNull(nameof(directory));

        if (path.IsAbsolute != directory.IsAbsolute)
        {
            throw new ArgumentException("Cannot mix absolute and relative paths", nameof(directory));
        }

        var target = path.FullName;
        var dir = directory.FullName;

        if (target.Length < dir.Length || !target.StartsWith(dir, StringComparison.Ordinal))
        {
            return false;
        }

        if (target.Length == dir.Length)
        {
            // exact match due to the StartsWith above
            // the directory parameter is interpreted as a directory so trailing separator isn't important
            return true;
        }

        var dirHasTrailingSeparator = dir[dir.Length - 1] == UPath.DirectorySeparator;

        if (!recursive)
        {
            // need to check if the directory part terminates 
            var lastSeparatorInTarget = target.LastIndexOf(UPath.DirectorySeparator);
            var expectedLastSeparator = dir.Length - (dirHasTrailingSeparator ? 1 : 0);

            if (lastSeparatorInTarget != expectedLastSeparator)
            {
                return false;
            }
        }

        if (!dirHasTrailingSeparator)
        {
            // directory is missing ending slash, check that target has it
            return target.Length > dir.Length && target[dir.Length] == UPath.DirectorySeparator;
        }

        return true;
    }

    /// <summary>
    /// Asserts the specified path is not null.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="name">The name of a parameter to include n the <see cref="ArgumentNullException"/>.</param>
    /// <returns>A path not modified.</returns>
    /// <exception cref="System.ArgumentNullException">If the path was null using the parameter name from <paramref name="name"/></exception>
    public static UPath AssertNotNull(this UPath path, string name = "path")
    {
        if (path.IsNull)
            Throw(name);

        return path;

        static void Throw(string name) => throw new ArgumentNullException(name);
    }

    /// <summary>
    /// Asserts the specified path is absolute.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="name">The name of a parameter to include n the <see cref="ArgumentNullException"/>.</param>
    /// <returns>A path not modified.</returns>
    /// <exception cref="System.ArgumentException">If the path is not absolute using the parameter name from <paramref name="name"/></exception>
    public static UPath AssertAbsolute(this UPath path, string name = "path")
    {
        if (!path.IsAbsolute)
            Throw(path, name);

        return path;

        static void Throw(UPath path, string name)
        {
            // Assert not null first, as a not absolute path could also be null, and if so, an ArgumentNullException shall be thrown
            path.AssertNotNull(name);
            throw new ArgumentException($"Path `{path}` must be absolute", name);
        }
    }
}
