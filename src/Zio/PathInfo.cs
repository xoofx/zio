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
    /// Uniform and secure path wrapper.
    /// </summary>
    /// <seealso cref="PathInfo" />
    public struct PathInfo : IEquatable<PathInfo>, IComparable<PathInfo>
    {
        [ThreadStatic] private static InternalHelper _internalHelperTls;

        public static readonly PathInfo Empty = new PathInfo(string.Empty, true);

        public static readonly PathInfo Root = new PathInfo("/", true);

        public const char DirectorySeparator = '/';

        private static InternalHelper InternalHelperTls => _internalHelperTls ?? (_internalHelperTls = new InternalHelper());

        /// <summary>
        /// Initializes a new instance of the <see cref="PathInfo"/> struct.
        /// </summary>
        /// <param name="path">The path that will be normalized.</param>
        public PathInfo(string path) : this(path, false)
        {
        }

        internal PathInfo(string path, bool safe)
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

        public string FullName { get; }

        public bool IsNull => FullName == null;

        public bool IsEmpty => FullName == string.Empty;

        public bool IsAbsolute => FullName?.StartsWith("/") ?? false;

        public bool IsRelative => !IsAbsolute;


        public static implicit operator PathInfo(string path)
        {
            return new PathInfo(path);
        }

        public static explicit operator string(PathInfo path)
        {
            return path.FullName;
        }

        public static PathInfo Combine(PathInfo path1, PathInfo path2)
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
                // Make sure to clean the builder as it is going to be when creating a new PathInfo
                builder.Length = 0;
                return new PathInfo(newPath);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Unable to combine path `{path1}` with `{path2}`", ex);
            }
        }

        public static PathInfo operator /(PathInfo path1, PathInfo path2)
        {
            return Combine(path1, path2);
        }

        public bool Equals(PathInfo other)
        {
            return string.Equals(FullName, other.FullName);
        }

        public override bool Equals(object obj)
        {
            return obj is PathInfo && Equals((PathInfo) obj);
        }

        public override int GetHashCode()
        {
            return FullName?.GetHashCode() ?? 0;
        }

        public static bool operator ==(PathInfo left, PathInfo right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PathInfo left, PathInfo right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return FullName;
        }

        public static bool TryParse(string path, out PathInfo pathInfo)
        {
            string errorMessage;
            path = ValidateAndNormalize(path, out errorMessage);
            pathInfo = errorMessage == null ? new PathInfo(path, true) : new PathInfo();
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
                    //    throw new InvalidPathInfoException($"The path `{path}` contains invalid characters `{c}`");
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

        public int CompareTo(PathInfo other)
        {
            return string.Compare(FullName, other.FullName, StringComparison.Ordinal);
        }
    }
}