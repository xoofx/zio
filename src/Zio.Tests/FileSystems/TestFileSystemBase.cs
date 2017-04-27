using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public abstract class TestFileSystemBase : IDisposable
    {
        private static readonly UPath[] Directories = new UPath[] { "a", "b", "C", "d" };
        private static readonly UPath[] Files = new UPath[] { "b.txt", "c.txt1", "d.i", "f.i1", "A.txt", "a/a.txt", "b/b.i", "E" };
        private static readonly object Lock = new object();
        private PhysicalDirectoryHelper _physicalDirectoryHelper;

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
            if (_physicalDirectoryHelper != null)
            {
                _physicalDirectoryHelper.Dispose();
            }

            Monitor.Exit(Lock);
        }


        protected IFileSystem GetPhysicalFileSystem()
        {
            if (_physicalDirectoryHelper == null)
            {
                _physicalDirectoryHelper = new PhysicalDirectoryHelper(SystemPath);
                CreateFolderStructure(_physicalDirectoryHelper.PhysicalFileSystem);
            }
            return _physicalDirectoryHelper.PhysicalFileSystem;
        }

        protected MemoryFileSystem GetMemoryFileSystem()
        {
            var fs = new MemoryFileSystem();
            CreateFolderStructure(fs);
            return fs;
        }

        protected AggregateFileSystem GetAggregateFileSystem()
        {
            var fs1 = new MemoryFileSystem();
            CreateFolderStructure(fs1);
            var fs2 = fs1.Clone();
            var fs3 = fs2.Clone();

            // Delete part of fs2 so that it will fallback to fs1
            fs2.DeleteDirectory("/a/a", true);
            fs2.DeleteDirectory("/a/b", true);
            fs2.DeleteDirectory("/b", true);

            // Delete on fs3 to fallback to fs2 and fs1
            fs3.DeleteDirectory("/a", true);
            fs3.DeleteDirectory("/C", true);
            fs3.DeleteFile("/b.txt");
            fs3.DeleteFile("/E");

            var aggfs = new AggregateFileSystem(fs1);
            aggfs.AddFileSystem(fs2);
            aggfs.AddFileSystem(fs3);

            return aggfs;
        }

        protected MountFileSystem GetMountFileSystem()
        {
            // Check on MountFileSystem directly with backup mount
            var fs = new MemoryFileSystem();
            CreateFolderStructure(fs);
            var mountfs = new MountFileSystem(fs);
            return mountfs;
        }

        protected MountFileSystem GetMountFileSystem1()
        {
            // Check on MountFileSystem
            // with real mount
            var fs = new MemoryFileSystem();
            CreateFolderStructure(fs);
            fs.DeleteDirectory("/b", true);
            fs.DeleteDirectory("/C", true);

            var fs1 = new MemoryFileSystem();
            fs1.WriteAllText("/b.i", "content");

            var mountfs = new MountFileSystem(fs);
            mountfs.Mount("/b", fs1);
            mountfs.Mount("/C", new MemoryFileSystem());

            return mountfs;
        }

        private void CreateFolderStructure(IFileSystem fs)
        {
            void CreateFolderStructure(UPath root)
            {

                foreach (var dir in Directories)
                {
                    var pathDir = root / dir;
                    fs.CreateDirectory(pathDir);
                }

                for (var i = 0; i < Files.Length; i++)
                {
                    var file = Files[i];
                    var pathFile = root / file;
                    fs.WriteAllText(pathFile, "content" + i);
                }
            }

            CreateFolderStructure(UPath.Root);
            CreateFolderStructure(UPath.Root / "a");
        }
    }
}