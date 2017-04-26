// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.IO;

namespace Zio.FileSystems
{
    public static class FileSystemExceptionHelper
    {
        public static FileNotFoundException NewFileNotFoundException(UPath path)
        {
            return new FileNotFoundException($"Could not find file `{path}`.");
        }

        public static DirectoryNotFoundException NewDirectoryNotFoundException(UPath path)
        {
            return new DirectoryNotFoundException($"Could not find a part of the path `{path}`.");
        }
    }
}