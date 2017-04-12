// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Zio
{
    public struct SearchPattern
    {
        private static readonly char[] SpecialChars = {'?', '*'};

        private readonly string _exactMatch;
        private readonly Regex _regexMatch;

        public bool Match(PathInfo path)
        {
            path.AssertNotNull();
            var name = path.GetName();
            // if _execMatch is null and _regexMatch is null, we have a * match
            return _exactMatch != null ? _exactMatch == name : _regexMatch == null || _regexMatch.IsMatch(name);
        }

        public static SearchPattern Parse(ref PathInfo path, ref string searchPattern)
        {
            return new SearchPattern(ref path, ref searchPattern);
        }

        private SearchPattern(ref PathInfo path, ref string searchPattern)
        {
            path.AssertAbsolute();
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));

            _exactMatch = null;
            _regexMatch = null;

            // Optimized path, most common case
            if (searchPattern == "*")
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
                var pathPattern = new PathInfo(searchPattern);
                var directory = pathPattern.GetDirectory();
                if (!directory.IsNull && !directory.IsEmpty)
                {
                    path = path / directory;
                }
                searchPattern = pathPattern.GetName();

                // If the search pattern is again a plain any, optimized path
                if (searchPattern == "*")
                {
                    return;
                }
            }

            var startIndex = 0;
            int nextIndex;
            StringBuilder builder = null;
            while ((nextIndex = searchPattern.IndexOfAny(SpecialChars, startIndex)) >= 0)
            {
                if (builder == null)
                {
                    builder = PathInfo.GetSharedStringBuilder();
                    builder.Append("^");
                }

                var lengthToEscape = nextIndex - startIndex;
                if (lengthToEscape > 0)
                {
                    var toEscape = Regex.Escape(searchPattern.Substring(startIndex, lengthToEscape));
                    builder.Append(toEscape);
                }

                var wildcard = searchPattern[nextIndex] == '*' ? "[^/]*" : "[^/]";
                builder.Append(wildcard);

                startIndex = nextIndex + 1;
            }
            if (builder == null)
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
    }
}