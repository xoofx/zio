// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class PhysicalDirectoryHelper : IDisposable
    {
        private readonly DirectoryInfo _compatDirectory;

        public PhysicalDirectoryHelper(string rootPath)
        {
            _compatDirectory = new DirectoryInfo(Path.Combine(rootPath, "Physical-" + Guid.NewGuid()));
            _compatDirectory.Create();

            var pfs = new PhysicalFileSystem();
            PhysicalFileSystem = new SubFileSystem(pfs, pfs.ConvertPathFromInternal(_compatDirectory.FullName));
        }

        public IFileSystem PhysicalFileSystem { get; }

        public void Dispose()
        {
            DeleteDirectoryForce(_compatDirectory);
        }

        private static void DeleteDirectoryForce(DirectoryInfo dir)
        {
            var infos = dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories);
            foreach (var info in infos)
            {
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    info.Attributes = FileAttributes.Normal;
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

            try
            {
                dir.Delete(true);
            }
            catch {}
        }
    }
}