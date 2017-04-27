// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Xunit;
using Zio.FileSystems;

namespace Zio.Tests.FileSystems
{
    public class TestEnumeratePaths : TestFileSystemBase
    {
        private readonly EnumeratePathsResult _reference;

        public TestEnumeratePaths()
        {
            _reference = new EnumeratePathsResult(GetPhysicalFileSystem());
        }

        [Fact]
        public void MemoryFileSystem()
        {
            CheckFileSystem(GetMemoryFileSystem());
        }

        [Fact]
        public void AggregateFileSystem()
        {
            CheckFileSystem(GetAggregateFileSystem());
        }

        [Fact]
        public void MountFileSystem()
        {
            CheckFileSystem(GetMountFileSystem());
        }

        [Fact]
        public void MountFileSystem1()
        {
            CheckFileSystem(GetMountFileSystem1());
        }

        public void CheckFileSystem(IFileSystem fs)
        {
            new EnumeratePathsResult(fs).Check(_reference);
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        class EnumeratePathsResult
        {
            private readonly List<UPath> TopDirs;
            private readonly List<UPath> TopFiles;
            private readonly List<UPath> TopEntries;
            private readonly List<UPath> AllDirs;
            private readonly List<UPath> AllFiles;
            private readonly List<UPath> AllEntries;
            private readonly List<UPath> AllFiles_txt;
            private readonly List<UPath> AllDirs_a1;
            private readonly List<UPath> AllDirs_a2;
            private readonly List<UPath> AllFiles_i;
            private readonly List<UPath> AllEntries_b;

            public void Check(EnumeratePathsResult other)
            {
                Assert.Equal(TopDirs, other.TopDirs);
                Assert.Equal(TopFiles, other.TopFiles);
                Assert.Equal(TopEntries, other.TopEntries);

                Assert.Equal(AllDirs, other.AllDirs);
                Assert.Equal(AllFiles, other.AllFiles);
                Assert.Equal(AllEntries, other.AllEntries);

                Assert.Equal(AllFiles_txt, other.AllFiles_txt);
                Assert.Equal(AllFiles_i, other.AllFiles_i);
                Assert.Equal(AllEntries_b, other.AllEntries_b);
                Assert.Equal(AllDirs_a1, other.AllDirs_a1);
                Assert.Equal(AllDirs_a2, other.AllDirs_a2);
            }

            public EnumeratePathsResult(IFileSystem fs)
            {
                TopDirs = fs.EnumeratePaths("/", "*", SearchOption.TopDirectoryOnly, SearchTarget.Directory).ToList();
                TopFiles = fs.EnumeratePaths("/", "*", SearchOption.TopDirectoryOnly, SearchTarget.File).ToList();
                TopEntries = fs.EnumeratePaths("/", "*", SearchOption.TopDirectoryOnly, SearchTarget.Both).ToList();

                AllDirs = fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Directory).ToList();
                AllFiles = fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.File).ToList();
                AllEntries = fs.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Both).ToList();

                AllFiles_txt = fs.EnumeratePaths("/", "*.txt", SearchOption.AllDirectories, SearchTarget.File).ToList();
                AllDirs_a1 = fs.EnumeratePaths("/", "a/*", SearchOption.AllDirectories, SearchTarget.Directory).ToList();
                AllDirs_a2 = fs.EnumeratePaths("/a", "*", SearchOption.AllDirectories, SearchTarget.Directory).ToList();
                AllFiles_i = fs.EnumeratePaths("/", "*.i", SearchOption.AllDirectories, SearchTarget.File).ToList();
                AllEntries_b = fs.EnumeratePaths("/", "b*", SearchOption.AllDirectories, SearchTarget.Both).ToList();
            }
        }
    }
}