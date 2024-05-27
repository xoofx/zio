// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

namespace Zio.Tests.FileSystems;

public class TestPhysicalFileSystemCompat : TestFileSystemCompactBase
{
    private readonly PhysicalDirectoryHelper _fsHelper;

    public TestPhysicalFileSystemCompat()
    {
        _fsHelper = new PhysicalDirectoryHelper(SystemPath);
        fs = _fsHelper.PhysicalFileSystem;
    }

    public override void TestDirectoryDeleteAndOpenFileOnWindows()
    {
        Skip.IfNot(IsWindows, "Linux allows files to be deleted when they are open");
        
        base.TestDirectoryDeleteAndOpenFileOnWindows();
    }

    public override void Dispose()
    {
        _fsHelper.Dispose();
        base.Dispose();
    }
}