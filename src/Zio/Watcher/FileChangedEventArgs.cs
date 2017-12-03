using System;

namespace Zio.Watcher
{
    /// <summary>
    /// The <see cref="EventArgs"/> base class for file and directory events. Used for
    /// <see cref="WatcherChangeTypes.Created"/>, <see cref="WatcherChangeTypes.Deleted"/>,
    /// and <see cref="WatcherChangeTypes.Changed"/>.
    /// </summary>
    /// <inheritdoc />
    public class FileChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The type of change that occurred.
        /// </summary>
        public WatcherChangeTypes ChangeType { get; }

        /// <summary>
        /// Absolute path to the file or directory.
        /// </summary>
        public UPath FullPath { get; }

        /// <summary>
        /// Name of the file or directory.
        /// </summary>
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
