using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Zio.Tests.FileSystems
{
    public abstract class TestFileSystemBase
    {
        protected TestFileSystemBase()
        {
            SystemPath = Path.GetDirectoryName(typeof(TestFileSystemBase).GetTypeInfo().Assembly.Location);
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        public string SystemPath { get; }

        public bool IsWindows { get; }

        protected static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception)
            {
            }
        }
        protected static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}