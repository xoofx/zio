// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.
using System.IO;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestPhysicalCompatToMemoryFileSystem : TestMemoryOrPhysicalFileSystemBase
    {
        private readonly DirectoryInfo _compatDirectory;

        public TestPhysicalCompatToMemoryFileSystem()
        {
            _compatDirectory = new DirectoryInfo(Path.Combine(SystemPath, "Compat"));
            _compatDirectory.Create();

            var pfs = new PhysicalFileSystem();
            fs = new SubFileSystem(pfs, pfs.ConvertFromSystem(_compatDirectory.FullName));
        }


        public override void Dispose()
        {
            var infos = _compatDirectory.EnumerateFileSystemInfos("*", SearchOption.AllDirectories);
            foreach (var info in infos)
            {
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    info.Attributes = 0;
                }
            }

            _compatDirectory.Delete(true);
        }
    }
}