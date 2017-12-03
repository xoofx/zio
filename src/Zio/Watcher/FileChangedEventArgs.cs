using System;

namespace Zio.Watcher
{
    public class FileChangedEventArgs : EventArgs
    {
        public WatcherChangeTypes ChangeType { get; }

        public UPath FullPath { get; }

        public string Name { get; }

        public FileChangedEventArgs(WatcherChangeTypes changeType, UPath fullPath)
        {
            fullPath.AssertNotNull(nameof(fullPath));
            fullPath.AssertAbsolute(nameof(fullPath));

            ChangeType = changeType;
            FullPath = fullPath;
            Name = fullPath.GetName();
        }
    }
}
