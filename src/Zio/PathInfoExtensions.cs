// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
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
            var dirName = Path.GetDirectoryName(path.FullName) ?? string.Empty;
            return new PathInfo(dirName);
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