// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Zio
{
    /// <summary>
    /// A uniform unix like path.
    /// </summary>
    /// <seealso cref="UPath" />
    public struct UPath : IEquatable<UPath>, IComparable<UPath>
    {
        [ThreadStatic] private static InternalHelper _internalHelperTls;

        /// <summary>
        /// An empty path.
        /// </summary>
        public static readonly UPath Empty = new UPath(string.Empty, true);

        /// <summary>
        /// The root path `/`
        /// </summary>
        public static readonly UPath Root = new UPath("/", true);

        /// <summary>
        /// The directory separator `/`
        /// </summary>
        public const char DirectorySeparator = '/';

        private static InternalHelper InternalHelperTls => _internalHelperTls ?? (_internalHelperTls = new InternalHelper());

        public static readonly IComparer<UPath> DefaultComparer = new ComparerCaseSensitive();
        public static readonly IComparer<UPath> DefaultComparerIgnoreCase = new ComparerIgnoreCase();

        /// <summary>
        /// Initializes a new instance of the <see cref="UPath"/> struct.
        /// </summary>
        /// <param name="path">The path that will be normalized.</param>
        public UPath(string path) : this(path, false)
        {
        }

        internal UPath(string path, bool safe)
        {
            if (safe)
            {
                FullName = path;
            }
            else
            {
                string errorMessage;
                FullName = ValidateAndNormalize(path, out errorMessage);
                if (errorMessage != null)
                    throw new ArgumentException(errorMessage, nameof(path));
            }
        }

        /// <summary>
        /// Gets the full name of this path (Note hat it may be null).
        /// </summary>
        /// <value>The full name of this path.</value>
        public string FullName { get; }

        /// <summary>
        /// Gets a value indicating whether this path is null.
        /// </summary>
        /// <value><c>true</c> if this instance is null; otherwise, <c>false</c>.</value>
        public bool IsNull => FullName == null;

        /// <summary>
        /// Gets a value indicating whether this path is empty (<see cref="FullName"/> equals to the empty string)
        /// </summary>
        /// <value><c>true</c> if this instance is empty; otherwise, <c>false</c>.</value>
        public bool IsEmpty => FullName == string.Empty;

        /// <summary>
        /// Gets a value indicating whether this path is absolute by starting with a leading `/`.
        /// </summary>
        /// <value><c>true</c> if this path is absolute; otherwise, <c>false</c>.</value>
        public bool IsAbsolute => FullName?.StartsWith("/") ?? false;

        /// <summary>
        /// Gets a value indicating whether this path is relative by **not** starting with a leading `/`.
        /// </summary>
        /// <value><c>true</c> if this instance is relative; otherwise, <c>false</c>.</value>
        public bool IsRelative => !IsAbsolute;

        /// <summary>
        /// Performs an implicit conversion from <see cref="System.String"/> to <see cref="UPath"/>.
        /// </summary>
        /// <param name="path">The path as a string.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator UPath(string path)
        {
            return new UPath(path);
        }

        /// <summary>
        /// Performs an explicit conversion from <see cref="UPath"/> to <see cref="System.String"/>.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The result as a string of the conversion.</returns>
        public static explicit operator string(UPath path)
        {
            return path.FullName;
        }

        /// <summary>
        /// Combines two paths into a new path.
        /// </summary>
        /// <param name="path1">The first path to combine.</param>
        /// <param name="path2">The second path to combine.</param>
        /// <returns>The combined paths. If one of the specified paths is a zero-length string, this method returns the other path. If path2 contains an absolute path, this method returns path2.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// path1
        /// or
        /// path2
        /// </exception>
        /// <exception cref="System.ArgumentException">If an error occurs while trying to combine paths.</exception>
        public static UPath Combine(UPath path1, UPath path2)
        {
            if (path1.FullName == null)
                throw new ArgumentNullException(nameof(path1));

            if (path2.FullName == null)
                throw new ArgumentNullException(nameof(path2));

            if (path1.IsEmpty && path2.IsEmpty)
                return Empty;

            // If the right path is absolute, it takes priority over path1
            if (path2.IsAbsolute)
                return path2;

            var builder = InternalHelperTls.Builder;
            Debug.Assert(builder.Length == 0);

            if (!path1.IsEmpty)
            {
                builder.Append(path1.FullName);
                builder.Append('/');
            }

            if (!path2.IsEmpty)
                builder.Append(path2.FullName);

            try
            {
                var newPath = builder.ToString();
                // Make sure to clean the builder as it is going to be when creating a new UPath
                builder.Length = 0;
                return new UPath(newPath);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Unable to combine path `{path1}` with `{path2}`", ex);
            }
        }

        /// <summary>
        /// Implements the / operator equivalent of <see cref="Combine"/>
        /// </summary>
        /// <param name="path1">The first path to combine.</param>
        /// <param name="path2">The second path to combine.</param>
        /// <returns>The combined paths. If one of the specified paths is a zero-length string, this method returns the other path. If path2 contains an absolute path, this method returns path2.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// path1
        /// or
        /// path2
        /// </exception>
        /// <exception cref="System.ArgumentException">If an error occurs while trying to combine paths.</exception>
        public static UPath operator /(UPath path1, UPath path2)
        {
            return Combine(path1, path2);
        }

        /// <inheritdoc />
        public bool Equals(UPath other)
        {
            return string.Equals(FullName, other.FullName);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is UPath && Equals((UPath) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return FullName?.GetHashCode() ?? 0;
        }

        /// <summary>
        /// Implements the == operator.
        /// </summary>
        /// <param name="left">The left.</param>
        /// <param name="right">The right.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator ==(UPath left, UPath right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Implements the != operator.
        /// </summary>
        /// <param name="left">The left.</param>
        /// <param name="right">The right.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator !=(UPath left, UPath right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return FullName;
        }

        /// <summary>
        /// Tries to parse the specified string into a <see cref="UPath"/>
        /// </summary>
        /// <param name="path">The path as a string.</param>
        /// <param name="pathInfo">The path parsed if successfull.</param>
        /// <returns><c>true</c> if path was parsed successfully, <c>false</c> otherwise.</returns>
        public static bool TryParse(string path, out UPath pathInfo)
        {
            string errorMessage;
            path = ValidateAndNormalize(path, out errorMessage);
            pathInfo = errorMessage == null ? new UPath(path, true) : new UPath();
            return errorMessage == null;
        }

        internal static StringBuilder GetSharedStringBuilder()
        {
            var builder = InternalHelperTls.Builder;
            builder.Length = 0;
            return builder;
        }

        private static string ValidateAndNormalize(string path, out string errorMessage)
        {
            errorMessage = null;

            // Early exit
            switch (path)
            {
                case null:
                    return null;
                case "/":
                case "..":
                case ".":
                    return path;
                case "\\":
                    return "/";
            }

            // Optimized path

            var internalHelper = InternalHelperTls;
            var parts = internalHelper.Slices;
            parts.Clear();

            var builder = internalHelper.Builder;
            builder.Length = 0;

            var lastIndex = 0;
            try
            {
                var i = 0;
                var processParts = false;
                var dotCount = 0;
                for (; i < path.Length; i++)
                {
                    var c = path[i];

                    // We don't disallow characters, as we let the IFileSystem implementations decided for them
                    // depending on the platform

                    //if (c < ' ' || c == ':' || c == '<' || c == '>' || c == '"' || c == '|')
                    //{
                    //    throw new InvalidUPathException($"The path `{path}` contains invalid characters `{c}`");
                    //}

                    if (c == '.')
                        dotCount++;

                    if (c == DirectorySeparator || c == '\\')
                    {
                        // optimization: If we don't expect to process the path
                        // and we only have a trailing / or \\, then just perform
                        // a substring on the path
                        if (!processParts && i + 1 == path.Length)
                            return path.Substring(0, path.Length - 1);

                        if (c == '\\')
                            processParts = true;

                        var previousIndex = i - 1;
                        for (i++; i < path.Length; i++)
                        {
                            c = path[i];
                            if (c == DirectorySeparator || c == '\\')
                            {
                                // If we have consecutive / or \\, we need to process parts
                                processParts = true;
                                continue;
                            }
                            break;
                        }

                        if (previousIndex >= lastIndex || previousIndex == -1)
                        {
                            var part = new TextSlice(lastIndex, previousIndex);
                            parts.Add(part);

                            // If the previous part had only dots, we need to process it
                            if (part.Length == dotCount)
                                processParts = true;
                        }
                        dotCount = c == '.' ? 1 : 0;
                        lastIndex = i;
                    }
                }

                if (lastIndex < path.Length)
                {
                    var part = new TextSlice(lastIndex, path.Length - 1);
                    parts.Add(part);

                    // If the previous part had only dots, we need to process it
                    if (part.Length == dotCount)
                        processParts = true;
                }

                // Optimized path if we don't need to compact the path
                if (!processParts)
                    return path;

                // Slow path, we need to process the parts
                for (i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    var partLength = part.Length;
                    if (partLength < 1)
                        continue;

                    if (path[part.Start] != '.')
                        continue;

                    if (partLength == 1)
                    {
                        // We have a '.'
                        if (parts.Count > 1)
                            parts.RemoveAt(i--);
                    }
                    else
                    {
                        if (path[part.Start + 1] != '.')
                            continue;

                        // Throws an exception if our slice parth contains only `.`  and is longer than 2 characters
                        if (partLength > 2)
                        {
                            var isValid = false;
                            for (var j = part.Start + 2; j <= part.End; j++)
                            {
                                if (path[j] != '.')
                                {
                                    isValid = true;
                                    break;
                                }
                            }

                            if (!isValid)
                            {
                                errorMessage = $"The path `{path}` contains invalid dots `{path.Substring(part.Start, part.Length)}` while only `.` or `..` are supported";
                                return string.Empty;
                            }

                            // Otherwise, it is a valid path part
                            continue;
                        }

                        if (i - 1 >= 0)
                        {
                            var previousSlice = parts[i - 1];
                            if (!IsDotDot(previousSlice, path))
                            {
                                if (previousSlice.Length == 0)
                                {
                                    errorMessage = $"The path `{path}` cannot go to the parent (..) of a root path /";
                                    return string.Empty;
                                }
                                parts.RemoveAt(i--);
                                parts.RemoveAt(i--);
                            }
                        }
                    }
                }

                for (i = 0; i < parts.Count; i++)
                {
                    var slice = parts[i];
                    if (slice.Length > 0)
                        builder.Append(path, slice.Start, slice.Length);
                    if (i + 1 < parts.Count)
                        builder.Append('/');
                }
                return builder.ToString();
            }
            finally
            {
                parts.Clear();
                builder.Length = 0;
            }
        }

        private static bool IsDotDot(TextSlice slice, string path)
        {
            if (slice.Length != 2)
                return false;
            return path[slice.Start] == '.' && path[slice.End] == '.';
        }

        private class InternalHelper
        {
            public readonly StringBuilder Builder;

            public readonly List<TextSlice> Slices;

            public InternalHelper()
            {
                Builder = new StringBuilder();
                Slices = new List<TextSlice>();
            }
        }

        private struct TextSlice
        {
            public TextSlice(int start, int end)
            {
                Start = start;
                End = end;
            }

            public readonly int Start;

            public readonly int End;

            public int Length => End - Start + 1;
        }

        public int CompareTo(UPath other)
        {
            return string.Compare(FullName, other.FullName, StringComparison.Ordinal);
        }

        private class ComparerCaseSensitive : IComparer<UPath>
        {
            public int Compare(UPath x, UPath y)
            {
                return string.Compare(x.FullName, y.FullName, StringComparison.Ordinal);
            }
        }

        private class ComparerIgnoreCase : IComparer<UPath>
        {
            public int Compare(UPath x, UPath y)
            {
                return string.Compare(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}