using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Zio.Tests.FileSystems
{
    public abstract class TestFileSystemBase : IDisposable
    {
        private static readonly object Lock = new object();

        protected TestFileSystemBase()
        {
            SystemPath = Path.GetDirectoryName(typeof(TestFileSystemBase).GetTypeInfo().Assembly.Location);
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            // Use a static lock to make sure a single process is running
            // as we may have changed on the disk that may interact with other tests
            Monitor.Enter(Lock);
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

        public virtual void Dispose()
        {
            Monitor.Exit(Lock);
        }
    }
}