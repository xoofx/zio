using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public abstract class TestFileSystemBase : IDisposable
    {
        private static readonly UPath[] Directories = new UPath[] { "a", "b", "C", "d" };
        private static readonly UPath[] Files = new UPath[] { "b.txt", "c.txt1", "d.i", "f.i1", "A.txt", "a/a1.txt", "b/b.i", "E" };
        private PhysicalDirectoryHelper _physicalDirectoryHelper;
        private readonly EnumeratePathsResult _referenceEnumeratePathsResult;
        private static readonly SemaphoreSlim Lock = new SemaphoreSlim(1);


        // -------------------------------------
        // This creates the following FileSystem
        // -------------------------------------
        // /a
        //     /a
        //        a1.txt
        //     /b
        //        b.i
        //     /C
        //     /d
        //     a1.txt
        //     A.txt
        //     b.txt
        //     c.txt1
        //     d.i
        //     f.i1
        //     E
        // /b
        //    b.i
        // /C
        // /d
        // A.txt
        // b.txt
        // c.txt1
        // d.i
        // f.i1
        // E

        protected TestFileSystemBase()
        {
            SystemPath = Path.GetDirectoryName(typeof(TestFileSystemBase).GetTypeInfo().Assembly.Location);
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            // Use a static lock to make sure a single process is running
            // as we may have changed on the disk that may interact with other tests.
            // SemaphoreSlim because it doesn't require the releasing thread to be the same.

            Lock.Wait();

            _referenceEnumeratePathsResult = EnumeratePathsResult.Create(GetCommonPhysicalFileSystem().GetAwaiter().GetResult()).GetAwaiter().GetResult();
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
            try
            {
                if (_physicalDirectoryHelper != null)
                {
                    _physicalDirectoryHelper.Dispose();
                }

                
            }
            finally
            {
                Lock.Release();
            }
        }

        protected async ValueTask<IFileSystem> GetCommonPhysicalFileSystem()
        {
            if (_physicalDirectoryHelper == null)
            {
                _physicalDirectoryHelper = await PhysicalDirectoryHelper.Create(SystemPath);
                await CreateFolderStructure(_physicalDirectoryHelper.PhysicalFileSystem);
            }
            return _physicalDirectoryHelper.PhysicalFileSystem;
        }

        protected async ValueTask<MemoryFileSystem> GetCommonMemoryFileSystem()
        {
            var fs = new MemoryFileSystem();
            await CreateFolderStructure(fs);
            return fs;
        }

        protected async ValueTask<(AggregateFileSystem aggregate, MemoryFileSystem fs1, MemoryFileSystem fs2, MemoryFileSystem fs3)> GetCommonAggregateFileSystem()
        {
            // ----------------------------------------------
            // This creates the following AggregateFileSystem
            // ----------------------------------------------
            // /a                 -> fs2
            //     /a             -> fs1
            //        a1.txt      -> fs1
            //     /b             -> fs1
            //        b.i         -> fs1
            //     /C             -> fs2
            //     /d             -> fs2
            //     a1.txt         -> fs2
            //     A.txt          -> fs2
            //     b.txt          -> fs2
            //     c.txt1         -> fs2
            //     d.i            -> fs2
            //     f.i1           -> fs2
            //     E              -> fs2
            // /b                 -> fs1
            //    b.i             -> fs1
            // /C                 -> fs2
            // /d                 -> fs3
            // A.txt              -> fs3
            // b.txt              -> fs2
            // c.txt1             -> fs3
            // d.i                -> fs3
            // f.i1               -> fs3
            // E                  -> fs2

            MemoryFileSystem fs1 = new MemoryFileSystem() {Name = "mem0"};
            await CreateFolderStructure(fs1);
            MemoryFileSystem fs2 = fs1.Clone();
            fs2.Name = "mem1";
            MemoryFileSystem fs3 = fs2.Clone();
            fs3.Name = "mem2";

            // Delete part of fs2 so that it will fallback to fs1
            await fs2.DeleteDirectory("/a/a", true);
            await fs2.DeleteDirectory("/a/b", true);
            await fs2.DeleteDirectory("/b", true);

            // Delete on fs3 to fallback to fs2 and fs1
            await fs3.DeleteDirectory("/a", true);
            await fs3.DeleteDirectory("/C", true);
            await fs3.DeleteFile("/b.txt");
            await fs3.DeleteFile("/E");

            var aggfs = new AggregateFileSystem(fs1);
            aggfs.AddFileSystem(fs2);
            aggfs.AddFileSystem(fs3);

            return (aggfs, fs1, fs2, fs3);
        }

        protected async ValueTask<MountFileSystem> GetCommonMountFileSystemWithOnlyBackup()
        {
            // Check on MountFileSystem directly with backup mount
            var fs = new MemoryFileSystem();
            await CreateFolderStructure(fs);
            var mountfs = new MountFileSystem(fs);
            return mountfs;
        }

        protected async ValueTask<MountFileSystem> GetCommonMountFileSystemWithMounts()
        {
            // Check on MountFileSystem
            // with real mount
            var fs = new MemoryFileSystem();
            await CreateFolderStructure(fs);
            await fs.DeleteDirectory("/b", true);
            await fs.DeleteDirectory("/C", true);

            var fs1 = new MemoryFileSystem();
            await fs1.WriteAllText("/b.i", "content");

            var mountfs = new MountFileSystem(fs);
            mountfs.Mount("/b", fs1);
            mountfs.Mount("/C", new MemoryFileSystem());

            return mountfs;
        }

        protected async ValueTask AssertCommonReadOnly(IFileSystem fs)
        {
            Assert.True(await fs.DirectoryExists("/"));

            await Assert.ThrowsAsync<IOException>(async () => await fs.CreateDirectory("/test"));
            await Assert.ThrowsAsync<IOException>(async () => await fs.DeleteDirectory("/test", true));
            await Assert.ThrowsAsync<IOException>(async () => await fs.MoveDirectory("/drive", "/drive2"));

            await Assert.ThrowsAsync<IOException>(async() => await fs.CreateFile("/toto.txt"));
            await Assert.ThrowsAsync<IOException>(async() => await fs.CopyFile("/toto.txt", "/dest.txt", true));
            await Assert.ThrowsAsync<IOException>(async() => await fs.MoveFile("/drive", "/drive2"));
            await Assert.ThrowsAsync<IOException>(async() => await fs.DeleteFile("/toto.txt"));
            await Assert.ThrowsAsync<IOException>(async() => await fs.OpenFile("/toto.txt", FileMode.Create, FileAccess.ReadWrite));
            await Assert.ThrowsAsync<IOException>(async() => await fs.OpenFile("/toto.txt", FileMode.Open, FileAccess.Write));
            await Assert.ThrowsAsync<IOException>(async() => await fs.ReplaceFile("/a/a/a1.txt", "/A.txt", "/titi.txt", true));

            await Assert.ThrowsAsync<IOException>(async() => await fs.SetAttributes("/toto.txt", FileAttributes.ReadOnly));
            await Assert.ThrowsAsync<IOException>(async() => await fs.SetCreationTime("/toto.txt", DateTime.Now));
            await Assert.ThrowsAsync<IOException>(async() => await fs.SetLastAccessTime("/toto.txt", DateTime.Now));
            await Assert.ThrowsAsync<IOException>(async () => await fs.SetLastWriteTime("/toto.txt", DateTime.Now));

            await AssertCommonRead(fs, true);
        }

        protected async ValueTask AssertCommonRead(IFileSystem fs, bool isReadOnly = false)
        {
            {
                var innerPath = fs.ConvertPathToInternal("/");
                var reverseInnerPath = fs.ConvertPathFromInternal(innerPath);
                Assert.Equal(UPath.Root, reverseInnerPath);
            }

            {
                var innerPath = fs.ConvertPathToInternal("/a/a");
                var reverseInnerPath = fs.ConvertPathFromInternal(innerPath);
                Assert.Equal("/a/a", reverseInnerPath);
            }

            {
                var innerPath = fs.ConvertPathToInternal("/b");
                var reverseInnerPath = fs.ConvertPathFromInternal(innerPath);
                Assert.Equal("/b", reverseInnerPath);
            }

            Assert.True(await fs.DirectoryExists("/"));
            Assert.False(await fs.FileExists(new UPath()));

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await fs.EnumeratePaths("/", null));
            Assert.Throws<ArgumentNullException>(() => fs.ConvertPathFromInternal(null));

            Assert.True(await fs.FileExists("/A.txt"));
            Assert.True(await fs.FileExists("/b.txt"));
            Assert.True(await fs.FileExists("/b/b.i"));
            Assert.True(await fs.FileExists("/a/a/a1.txt"));
            Assert.False(await fs.FileExists("/yoyo.txt"));

            Assert.True(await fs.DirectoryExists("/a"));
            Assert.True(await fs.DirectoryExists("/a/b"));
            Assert.True(await fs.DirectoryExists("/a/C"));
            Assert.True(await fs.DirectoryExists("/b"));
            Assert.True(await fs.DirectoryExists("/C"));
            Assert.True(await fs.DirectoryExists("/d"));
            Assert.False(await fs.DirectoryExists("/yoyo"));
            Assert.False(await fs.DirectoryExists("/a/yoyo"));

            Assert.StartsWith("content", await fs.ReadAllText("/A.txt"));
            Assert.StartsWith("content", await fs.ReadAllText("/b.txt"));
            Assert.StartsWith("content", await fs.ReadAllText("/a/a/a1.txt"));


            var readOnlyFlag = isReadOnly ? FileAttributes.ReadOnly : 0;

            Assert.Equal(readOnlyFlag | FileAttributes.Archive, await fs.GetAttributes("/A.txt"));
            Assert.Equal(readOnlyFlag | FileAttributes.Archive, await fs.GetAttributes("/b.txt"));
            Assert.Equal(readOnlyFlag | FileAttributes.Archive, await fs.GetAttributes("/a/a/a1.txt"));

            Assert.True(await fs.GetFileLength("/A.txt") > 0);
            Assert.True(await fs.GetFileLength("/b.txt") > 0);
            Assert.True(await fs.GetFileLength("/a/a/a1.txt") > 0);

            Assert.Equal(readOnlyFlag | FileAttributes.Directory, await fs.GetAttributes("/a"));
            Assert.Equal(readOnlyFlag | FileAttributes.Directory, await fs.GetAttributes("/a/a"));
            Assert.Equal(readOnlyFlag | FileAttributes.Directory, await fs.GetAttributes("/C"));
            Assert.Equal(readOnlyFlag | FileAttributes.Directory, await fs.GetAttributes("/d"));

            Assert.NotEqual(FileSystem.DefaultFileTime, await fs.GetCreationTime("/A.txt"));
            Assert.NotEqual(FileSystem.DefaultFileTime, await fs.GetLastAccessTime("/A.txt"));
            Assert.NotEqual(FileSystem.DefaultFileTime, await fs.GetLastWriteTime("/A.txt"));
            Assert.NotEqual(FileSystem.DefaultFileTime, await fs.GetCreationTime("/a/a/a1.txt"));
            Assert.NotEqual(FileSystem.DefaultFileTime, await fs.GetLastAccessTime("/a/a/a1.txt"));
            Assert.NotEqual(FileSystem.DefaultFileTime, await fs.GetLastWriteTime("/a/a/a1.txt"));

            (await EnumeratePathsResult.Create(fs)).Check(_referenceEnumeratePathsResult);
        }

        protected async ValueTask AssertFileSystemEqual(IFileSystem from, IFileSystem to)
        {
            (await EnumeratePathsResult.Create(from)).Check(await EnumeratePathsResult.Create(to));
        }
        
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        class EnumeratePathsResult
        {
            private List<UPath> TopDirs;
            private List<UPath> TopFiles;
            private List<UPath> TopEntries;
            private List<UPath> AllDirs;
            private List<UPath> AllFiles;
            private List<UPath> AllEntries;
            private List<UPath> AllFiles_txt;
            private List<UPath> AllDirs_a1;
            private List<UPath> AllDirs_a2;
            private List<UPath> AllFiles_i;
            private List<UPath> AllEntries_b;

            private EnumeratePathsResult()
            {

            }

            public static async ValueTask<EnumeratePathsResult> Create(IFileSystem fs)
            {
                var result = new EnumeratePathsResult();

                result.TopDirs = (await fs.EnumeratePaths("/", "*", SearchOption.TopDirectoryOnly, SearchTarget.Directory)).ToList();
                // Check extension method
                Assert.Equal(result.TopDirs, (await fs.EnumerateDirectories("/")).ToList());
                Assert.Equal(result.TopDirs, (await fs.EnumerateDirectoryEntries("/")).Select(e => (UPath)e.FullName).ToList());
                Assert.Equal(result.TopDirs.OrderBy(x => x.FullName).ToList(), (await fs.EnumerateItems("/", SearchOption.TopDirectoryOnly)).Where(e => e.IsDirectory).OrderBy(e => e.Path.FullName).Select(e => e.Path).ToList());

                result.TopFiles = (await fs.EnumeratePaths("/", "*", SearchOption.TopDirectoryOnly, SearchTarget.File)).ToList();
                // Check extension method
                Assert.Equal(result.TopFiles, (await fs.EnumerateFiles("/")).ToList());
                Assert.Equal(result.TopFiles, (await fs.EnumerateFileEntries("/")).Select(e => (UPath)e.FullName).ToList());
                Assert.Equal(result.TopFiles.OrderBy(x => x.FullName).ToList(), (await fs.EnumerateItems("/", SearchOption.TopDirectoryOnly)).Where(e => !e.IsDirectory).OrderBy(e => e.Path.FullName.ToLowerInvariant()).Select(e => e.Path).ToList());

                result.TopEntries = (await fs.EnumeratePaths("/", "*", SearchOption.TopDirectoryOnly, SearchTarget.Both)).ToList();
                // Check extension method
                Assert.Equal(result.TopEntries, (await fs.EnumeratePaths("/")).ToList());
                Assert.Equal(result.TopEntries, (await fs.EnumerateFileSystemEntries("/")).Select(e => (UPath)e.FullName).ToList());
                Assert.Equal(result.TopEntries.OrderBy(x => x.FullName).ToList(), (await fs.EnumerateItems("/", SearchOption.TopDirectoryOnly)).OrderBy(e => e.Path.FullName).Select(e => e.Path).ToList());

                result.AllDirs = (await fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Directory)).ToList();

                result.AllFiles = (await fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.File)).ToList();
                // Check extension method
                Assert.Equal(result.AllFiles, (await fs.EnumerateFiles("/", "*", SearchOption.AllDirectories)).ToList());
                Assert.Equal(result.AllFiles, (await fs.EnumerateFileEntries("/", "*", SearchOption.AllDirectories)).Select(e => (UPath)e.FullName).ToList());
                var expected = result.AllFiles.OrderBy(x => x.FullName).ToList();
                var actual = (await fs.EnumerateItems("/", SearchOption.AllDirectories)).Where(e => !e.IsDirectory).OrderBy(e => e.Path.FullName).Select(e => e.Path).ToList();
                Assert.Equal(expected, actual);

                result.AllEntries = (await fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Both)).ToList();

                result.AllFiles_txt = (await fs.EnumeratePaths("/", "*.txt", SearchOption.AllDirectories, SearchTarget.File)).ToList();
                // Check extension method
                Assert.Equal(result.AllFiles_txt, (await fs.EnumerateFiles("/", "*.txt", SearchOption.AllDirectories)).ToList());
                Assert.Equal(result.AllFiles_txt, (await fs.EnumerateFileEntries("/", "*.txt", SearchOption.AllDirectories)).Select(e => (UPath)e.FullName).ToList());

                result.AllDirs_a1 = (await fs.EnumeratePaths("/", "a/*", SearchOption.AllDirectories, SearchTarget.Directory)).ToList();
                result.AllDirs_a2 = (await fs.EnumeratePaths("/a", "*", SearchOption.AllDirectories, SearchTarget.Directory)).ToList();
                result.AllFiles_i = (await fs.EnumeratePaths("/", "*.i", SearchOption.AllDirectories, SearchTarget.File)).ToList();
                result.AllEntries_b = (await fs.EnumeratePaths("/", "b*", SearchOption.AllDirectories, SearchTarget.Both)).ToList();

                return result;
            }

            public void Check(EnumeratePathsResult other)
            {
                AssertEx.Equivalent(TopDirs, other.TopDirs);
                AssertEx.Equivalent(TopFiles, other.TopFiles);
                AssertEx.Equivalent(TopEntries, other.TopEntries);

                AssertEx.Equivalent(AllDirs, other.AllDirs);
                AssertEx.Equivalent(AllFiles, other.AllFiles);
                AssertEx.Equivalent(AllEntries, other.AllEntries);

                AssertEx.Equivalent(AllFiles_txt, other.AllFiles_txt);
                AssertEx.Equivalent(AllFiles_i, other.AllFiles_i);
                AssertEx.Equivalent(AllEntries_b, other.AllEntries_b);
                AssertEx.Equivalent(AllDirs_a1, other.AllDirs_a1);
                AssertEx.Equivalent(AllDirs_a2, other.AllDirs_a2);
            }
        }

        private async ValueTask CreateFolderStructure(IFileSystem fs)
        {
            async ValueTask CreateFolderStructure(UPath root)
            {

                foreach (var dir in Directories)
                {
                    var pathDir = root / dir;
                    await fs.CreateDirectory(pathDir);
                }

                for (var i = 0; i < Files.Length; i++)
                {
                    var file = Files[i];
                    var pathFile = root / file;
                    await fs.WriteAllText(pathFile, "content" + i);
                }
            }

            await CreateFolderStructure(UPath.Root);
            await CreateFolderStructure(UPath.Root / "a");
        }
    }
}