// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Zio
{
    /// <summary>
    /// Extension methods for <see cref="PathInfo"/>
    /// </summary>
    public static class PathInfoExtensions
    {
        public static PathInfo ToRelative(this PathInfo path)
        {
            path.AssertNotNull();

            if (path.IsRelative)
            {
                return path;
            }

            return path.FullName == "/" ? PathInfo.Empty : new PathInfo(path.FullName.Substring(1), true);
        }

        public static PathInfo GetDirectory(this PathInfo path)
        {
            path.AssertNotNull();

            var fullname = path.FullName;

            if (fullname == "/")
            {
                return new PathInfo();
            }

            var lastIndex = fullname.LastIndexOf(PathInfo.DirectorySeparator);
            if (lastIndex > 0)
            {
                return fullname.Substring(0, lastIndex);
            }
            return lastIndex == 0 ? PathInfo.Root : PathInfo.Empty;
        }

        public static IEnumerable<string> Split(this PathInfo path)
        {
            path.AssertNotNull();

            var fullname = path.FullName;
            if (fullname == string.Empty)
            {
                yield break;
            }

            int previousIndex = 0;
            int index = 0;
            while ((index = fullname.IndexOf(PathInfo.DirectorySeparator, previousIndex)) >= 0)
            {
                if (index != 0)
                {
                    yield return fullname.Substring(previousIndex, index - previousIndex + 1);
                }

                previousIndex = index + 1;
            }

            if (index < fullname.Length)
            {
                yield return fullname.Substring(previousIndex, fullname.Length - previousIndex);
            }
        }

        public static void DequeueDirectory(this PathInfo path, out string firstDirectory, out PathInfo remainingPath)
        {
            path.AssertNotNull();
            remainingPath = new PathInfo();

            var fullname = path.FullName;
            var index = fullname.IndexOf(PathInfo.DirectorySeparator, 1);
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


        public static string GetName(this PathInfo path)
        {
            path.AssertNotNull();
            return Path.GetFileName(path.FullName);
        }

        public static string GetNameWithoutExtension(this PathInfo path)
        {
            path.AssertNotNull();
            return Path.GetFileNameWithoutExtension(path.FullName);
        }

        public static string GetDotExtension(this PathInfo path)
        {
            return Path.GetExtension(path.FullName);
        }

        public static PathInfo ChangeExtension(this PathInfo path, string extension)
        {
            return new PathInfo(Path.ChangeExtension(path.FullName, extension));
        }

        public static PathInfo AssertNotNull(this PathInfo path, string name = "path")
        {
            if (path.FullName == null)
                throw new ArgumentNullException(name);
            return path;
        }

        public static PathInfo AssertAbsolute(this PathInfo path, string name = "path")
        {
            AssertNotNull(path, name);

            if (!path.IsAbsolute)
                throw new ArgumentException($"Path `{path}` must be absolute", name);
            return path.FullName;
        }
    }
}