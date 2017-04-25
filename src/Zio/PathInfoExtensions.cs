// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Zio
{
    /// <summary>
    /// Extension methods for <see cref="UPath"/>
    /// </summary>
    public static class UPathExtensions
    {
        public static UPath ToRelative(this UPath path)
        {
            path.AssertNotNull();

            if (path.IsRelative)
            {
                return path;
            }

            return path.FullName == "/" ? UPath.Empty : new UPath(path.FullName.Substring(1), true);
        }

        public static UPath GetDirectory(this UPath path)
        {
            path.AssertNotNull();

            var fullname = path.FullName;

            if (fullname == "/")
            {
                return new UPath();
            }

            var lastIndex = fullname.LastIndexOf(UPath.DirectorySeparator);
            if (lastIndex > 0)
            {
                return fullname.Substring(0, lastIndex);
            }
            return lastIndex == 0 ? UPath.Root : UPath.Empty;
        }

        public static void ExtractFirstDirectory(this UPath path, out string firstDirectory, out UPath remainingPath)
        {
            path.AssertNotNull();
            remainingPath = new UPath();

            var fullname = path.FullName;
            var index = fullname.IndexOf(UPath.DirectorySeparator, 1);
            if (index < 0)
            {
                firstDirectory = fullname.Substring(1, fullname.Length - 1);
            }
            else
            {
                firstDirectory = fullname.Substring(1, index);
                remainingPath = fullname.Substring(index);
            }
        }

        public static IEnumerable<string> Split(this UPath path)
        {
            path.AssertNotNull();

            var fullname = path.FullName;
            if (fullname == string.Empty)
            {
                yield break;
            }

            int previousIndex = path.IsAbsolute ? 1 : 0;
            int nextIndex = 0;
            while ((nextIndex = fullname.IndexOf(UPath.DirectorySeparator, previousIndex)) >= 0)
            {
                if (nextIndex != 0)
                {
                    yield return fullname.Substring(previousIndex, nextIndex - previousIndex);
                }

                previousIndex = nextIndex + 1;
            }

            if (previousIndex < fullname.Length)
            {
                yield return fullname.Substring(previousIndex, fullname.Length - previousIndex);
            }
        }

        public static string GetName(this UPath path)
        {
            path.AssertNotNull();
            return Path.GetFileName(path.FullName);
        }

        public static string GetNameWithoutExtension(this UPath path)
        {
            path.AssertNotNull();
            return Path.GetFileNameWithoutExtension(path.FullName);
        }

        public static string GetDotExtension(this UPath path)
        {
            return Path.GetExtension(path.FullName);
        }

        public static UPath ChangeExtension(this UPath path, string extension)
        {
            return new UPath(Path.ChangeExtension(path.FullName, extension));
        }

        public static UPath AssertNotNull(this UPath path, string name = "path")
        {
            if (path.FullName == null)
                throw new ArgumentNullException(name);
            return path;
        }

        public static UPath AssertAbsolute(this UPath path, string name = "path")
        {
            AssertNotNull(path, name);

            if (!path.IsAbsolute)
                throw new ArgumentException($"Path `{path}` must be absolute", name);
            return path.FullName;
        }
    }
}