// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

#if !NET7_0_OR_GREATER
using System.Runtime.InteropServices;

namespace Zio;

internal static class Interop
{
    public static class Windows
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

        public enum SymbolicLink
        {
            File = 0,
            Directory = 1
        }
    }

    public static class Unix
    {
        [DllImport("libc", SetLastError = true)]
        public static extern int symlink(string target, string linkpath);
    }
}
#endif