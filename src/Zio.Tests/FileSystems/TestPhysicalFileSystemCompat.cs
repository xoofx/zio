// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestPhysicalFileSystemCompat : TestMemoryOrPhysicalFileSystemBase
    {
        private readonly DirectoryInfo _compatDirectory;

        public TestPhysicalFileSystemCompat()
        {
            // Make sure that we don't have any remaining Compat-folders
            foreach (var dir in Directory.EnumerateDirectories(SystemPath, "Compat-*"))
            {
                DeleteDirectoryForce(new DirectoryInfo(dir));
            }

            _compatDirectory = new DirectoryInfo(Path.Combine(SystemPath, "Compat-" + Guid.NewGuid()));
            _compatDirectory.Create();

            var pfs = new PhysicalFileSystem();
            fs = new SubFileSystem(pfs, pfs.ConvertFromSystem(_compatDirectory.FullName));
        }

        public override void Dispose()
        {
            DeleteDirectoryForce(_compatDirectory);

            base.Dispose();
        }


        private static void DeleteDirectoryForce(DirectoryInfo dir)
        {
            var infos = dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories);
            foreach (var info in infos)
            {
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    info.Attributes = 0;
                }
                if (info is FileInfo)
                {
                    try
                    {
                        info.Delete();
                    }
                    catch { }
                }
            }

            dir.Delete(true);
        }
    }
}