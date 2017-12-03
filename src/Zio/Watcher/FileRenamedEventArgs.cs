
namespace Zio.Watcher
{
    public class FileRenamedEventArgs : FileChangedEventArgs
    {
        public UPath OldFullPath { get; }

        public string OldName { get; }

        public FileRenamedEventArgs(WatcherChangeTypes changeType, UPath fullPath, UPath oldFullPath)
            : base(changeType, fullPath)
        {
            fullPath.AssertNotNull(nameof(oldFullPath));
            fullPath.AssertAbsolute(nameof(oldFullPath));

            OldFullPath = oldFullPath;
            OldName = oldFullPath.GetName();
        }
    }
}
