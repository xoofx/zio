using System;
using System.Collections.Generic;

namespace Zio.Watcher
{
    /// <summary>
    /// Aggregates events from multiple <see cref="FileSystemWatcher"/> into one.
    /// </summary>
    public class AggregateFileSystemWatcher : FileSystemWatcher
    {
        private readonly List<IFileSystemWatcher> _children;

        public AggregateFileSystemWatcher(IFileSystem fileSystem, UPath path)
            : base(fileSystem, path)
        {
            _children = new List<IFileSystemWatcher>();
        }

        /// <summary>
        /// Adds an <see cref="IFileSystemWatcher"/> instance to aggregate events from.
        /// </summary>
        /// <param name="watcher">The <see cref="IFileSystemWatcher"/> instance to add.</param>
        public void Add(IFileSystemWatcher watcher)
        {
            if (watcher == null)
            {
                throw new ArgumentNullException(nameof(watcher));
            }

            lock (_children)
            {
                if (_children.Contains(watcher))
                {
                    throw new ArgumentException("The filesystem watcher is already added", nameof(watcher));
                }

                RegisterEvents(watcher);
                _children.Add(watcher);
            }
        }

        /// <summary>
        /// Removes <see cref="IFileSystemWatcher"/> instances from this instance.
        /// </summary>
        /// <param name="fileSystem">The <see cref="IFileSystem"/> to stop aggregating events from.</param>
        public void RemoveFrom(IFileSystem fileSystem)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }

            lock (_children)
            {
                for (var i = _children.Count - 1; i >= 0; i--)
                {
                    var watcher = _children[i];
                    if (watcher.FileSystem != fileSystem)
                    {
                        continue;
                    }
                    
                    UnregisterEvents(watcher);
                    _children.Remove(watcher);
                    watcher.Dispose();
                }
            }
        }

        /// <summary>
        /// Removes all <see cref="IFileSystemWatcher"/> instances from this instance.
        /// </summary>
        public void Clear()
        {
            lock (_children)
            {
                foreach (var watcher in _children)
                {
                    UnregisterEvents(watcher);
                    watcher.Dispose();
                }

                _children.Clear();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Clear();
            }
        }
    }
}
