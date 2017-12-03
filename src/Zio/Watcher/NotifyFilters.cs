using System;

namespace Zio.Watcher
{
    [Flags]
    public enum NotifyFilters
    {
        FileName = 1,
        DirectoryName = 2,
        Attributes = 4,
        Size = 8,
        LastWrite = 16,
        LastAccess = 32,
        CreationTime = 64,
        Security = 256,
        Default = FileName | DirectoryName | LastWrite
    }
}
