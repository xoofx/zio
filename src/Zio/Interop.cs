// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

#if !NET
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Zio;

internal static class Interop
{
    public static class Windows
    {
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private const uint FILE_READ_EA = 0x0008;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x2000000;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint GetFinalPathNameByHandle(IntPtr hFile, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateFile(
            [MarshalAs(UnmanagedType.LPTStr)] string filename,
            [MarshalAs(UnmanagedType.U4)] uint access,
            [MarshalAs(UnmanagedType.U4)] FileShare share,
            IntPtr securityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)] uint flagsAndAttributes,
            IntPtr templateFile);

        public static string GetFinalPathName(string path)
        {
            var h = CreateFile(path,
                FILE_READ_EA,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (h == INVALID_HANDLE_VALUE)
            {
                throw new Win32Exception();
            }

            try
            {
                var sb = new StringBuilder(1024);
                var res = GetFinalPathNameByHandle(h, sb, 1024, 0);
                if (res == 0)
                {
                    throw new Win32Exception();
                }

                // Trim '\\?\'
                if (sb.Length >= 4 && sb[0] == '\\' && sb[1] == '\\' && sb[2] == '?' && sb[3] == '\\')
                {
                    sb.Remove(0, 4);

                    // Trim 'UNC\'
                    if (sb.Length >= 4 && sb[0] == 'U' && sb[1] == 'N' && sb[2] == 'C' && sb[3] == '\\')
                    {
                        sb.Remove(0, 4);

                        // Add the default UNC prefix
                        sb.Insert(0, @"\\");
                    }
                }

                return sb.ToString();
            }
            finally
            {
                CloseHandle(h);
            }
        }

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

        [DllImport ("libc")]
        private static extern int readlink (string path, byte[] buffer, int buflen);

        public static string? readlink(string path)
        {
            var buf = new byte[1024];
            var ret = readlink(path, buf, buf.Length);

            return ret == -1 ? null : Encoding.Default.GetString(buf, 0, ret);
        }
    }
}
#endif