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

        public void RemoveFrom(IFileSystem fs)
        {
            if (fs == null)
            {
                throw new ArgumentNullException(nameof(fs));
            }

            lock (_children)
            {
                for (var i = _children.Count - 1; i >= 0; i--)
                {
                    var watcher = _children[i];
                    if (watcher.FileSystem != fs)
                    {
                        continue;
                    }
                    
                    UnregisterEvents(watcher);
                    _children.Remove(watcher);
                    watcher.Dispose();
                    break;
                }
            }
        }

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
