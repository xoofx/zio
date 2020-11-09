// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Zio
{
    /// <summary>
    /// Search pattern compiler used for custom <see cref="IFileSystem.EnumeratePaths"/> implementations.
    /// Use the method <see cref="Parse"/> to create a pattern.
    /// </summary>
    public struct SearchPattern
    {
        private static readonly char[] SpecialChars = {'?', '*'};

        private readonly string? _exactMatch;
        private readonly Regex? _regexMatch;

        /// <summary>
        /// Tries to match the specified path with this instance.
        /// </summary>
        /// <param name="path">The path to match.</param>
        /// <returns><c>true</c> if the path was matched, <c>false</c> otherwise.</returns>
        public bool Match(UPath path)
        {
            path.AssertNotNull();
            var name = path.GetName();
            // if _execMatch is null and _regexMatch is null, we have a * match
            return _exactMatch != null ? _exactMatch == name : _regexMatch is null || _regexMatch.IsMatch(name);
        }

        /// <summary>
        /// Tries to match the specified path with this instance.
        /// </summary>
        /// <param name="name">The path to match.</param>
        /// <returns><c>true</c> if the path was matched, <c>false</c> otherwise.</returns>
        public bool Match(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            // if _execMatch is null and _regexMatch is null, we have a * match
            return _exactMatch != null ? _exactMatch == name : _regexMatch is null || _regexMatch.IsMatch(name);
        }

        /// <summary>
        /// Parses and normalize the specified path and <see cref="SearchPattern"/>.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="searchPattern">The search pattern.</param>
        /// <returns>An instance of <see cref="SearchPattern"/> in order to use <see cref="Match(Zio.UPath)"/> on a path.</returns>
        public static SearchPattern Parse(ref UPath path, ref string searchPattern)
        {
            return new SearchPattern(ref path, ref searchPattern);
        }

        /// <summary>
        /// Normalizes the specified path and <see cref="SearchPattern"/>.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="searchPattern">The search pattern.</param>
        public static void Normalize(ref UPath path, ref string searchPattern)
        {
            Parse(ref path, ref searchPattern);
        }

        private SearchPattern(ref UPath path, ref string searchPattern)
        {
            path.AssertAbsolute();
            if (searchPattern is null) throw new ArgumentNullException(nameof(searchPattern));

            _exactMatch = null;
            _regexMatch = null;

            // Optimized path, most common case
            if (searchPattern is "*")
            {
                return;
            }

            if (searchPattern.StartsWith("/"))
            {
                throw new ArgumentException($"The search pattern `{searchPattern}` cannot start by an absolute path `/`");
            }

            searchPattern = searchPattern.Replace('\\', '/');

            // If the path contains any directory, we need to concatenate the directory part with the input path
            if (searchPattern.IndexOf('/') > 0)
            {
                var pathPattern = new UPath(searchPattern);
                var directory = pathPattern.GetDirectory();
                if (!directory.IsNull && !directory.IsEmpty)
                {
                    path = path / directory;
                }
                searchPattern = pathPattern.GetName();

                // If the search pattern is again a plain any, optimized path
                if (searchPattern is "*")
                {
                    return;
                }
            }

            int startIndex = 0;
            int nextIndex;
            StringBuilder? builder = null;
            try
            {
                while ((nextIndex = searchPattern.IndexOfAny(SpecialChars, startIndex)) >= 0)
                {
                    if (builder is null)
                    {
                        builder = UPath.GetSharedStringBuilder();
                        builder.Append("^");
                    }

                    var lengthToEscape = nextIndex - startIndex;
                    if (lengthToEscape > 0)
                    {
                        var toEscape = Regex.Escape(searchPattern.Substring(startIndex, lengthToEscape));
                        builder.Append(toEscape);
                    }

                    var c = searchPattern[nextIndex];
                    var regexPatternPart = c == '*' ? "[^/]*" : "[^/]";
                    builder.Append(regexPatternPart);

                    startIndex = nextIndex + 1;
                }
                if (builder is null)
                {
                    _exactMatch = searchPattern;
                }
                else
                {
                    var length = searchPattern.Length - startIndex;
                    if (length > 0)
                    {
                        var toEscape = Regex.Escape(searchPattern.Substring(startIndex, length));
                        builder.Append(toEscape);
                    }

                    builder.Append("$");

                    var regexPattern = builder.ToString();
                    _regexMatch = new Regex(regexPattern);
                }
            }
            finally
            {
                if (builder != null)
                {
                    builder.Length = 0;
                }
            }
        }
    }
}
