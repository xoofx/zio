using System;
using System.Collections.Generic;

namespace Zio.FileSystems
{
    /// <summary>
    /// Aggregates events from multiple <see cref="FileSystemWatcher"/> into one.
    /// </summary>
    public class AggregateFileSystemWatcher : FileSystemWatcher
    {
        private readonly List<IFileSystemWatcher> _children;
        private int _internalBufferSize;
        private NotifyFilters _notifyFilter;
        private bool _enableRaisingEvents;
        private bool _includeSubdirectories;
        private string _filter;

        public AggregateFileSystemWatcher(IFileSystem fileSystem, UPath path)
            : base(fileSystem, path)
        {
            _children = new List<IFileSystemWatcher>();
            _internalBufferSize = 0;
            _notifyFilter = NotifyFilters.Default;
            _enableRaisingEvents = false;
            _includeSubdirectories = false;
            _filter = "*.*";
        }

        /// <summary>
        /// Adds an <see cref="IFileSystemWatcher"/> instance to aggregate events from.
        /// </summary>
        /// <param name="watcher">The <see cref="IFileSystemWatcher"/> instance to add.</param>
        public void Add(IFileSystemWatcher watcher)
        {
            if (watcher is null)
            {
                throw new ArgumentNullException(nameof(watcher));
            }

            lock (_children)
            {
                if (_children.Contains(watcher))
                {
                    throw new ArgumentException("The filesystem watcher is already added", nameof(watcher));
                }

                watcher.InternalBufferSize = InternalBufferSize;
                watcher.NotifyFilter = NotifyFilter;
                watcher.EnableRaisingEvents = EnableRaisingEvents;
                watcher.IncludeSubdirectories = IncludeSubdirectories;
                watcher.Filter = Filter;

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
            if (fileSystem is null)
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
                    _children.RemoveAt(i);
                    watcher.Dispose();
                }
            }
        }

        /// <summary>
        /// Removes all <see cref="IFileSystemWatcher"/> instances from this instance.
        /// </summary>
        /// <param name="excludeFileSystem">Exclude this filesystem from removal.</param>
        public void Clear(IFileSystem? excludeFileSystem = null)
        {
            lock (_children)
            {
                for (var i = _children.Count - 1; i >= 0; i--)
                {
                    var watcher = _children[i];
                    if (watcher.FileSystem == excludeFileSystem)
                    {
                        continue;
                    }

                    UnregisterEvents(watcher);
                    _children.RemoveAt(i);
                    watcher.Dispose();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Clear();
            }
        }

        /// <inheritdoc />
        public override int InternalBufferSize
        {
            get => _internalBufferSize;
            set
            {
                if (value == _internalBufferSize)
                {
                    return;
                }

                lock (_children)
                {
                    foreach (var watcher in _children)
                    {
                        watcher.InternalBufferSize = value;
                    }
                }

                _internalBufferSize = value;
            }
        }

        /// <inheritdoc />
        public override NotifyFilters NotifyFilter
        {
            get => _notifyFilter;
            set
            {
                if (value == _notifyFilter)
                {
                    return;
                }

                lock (_children)
                {
                    foreach (var watcher in _children)
                    {
                        watcher.NotifyFilter = value;
                    }
                }

                _notifyFilter = value;
            }
        }

        /// <inheritdoc />
        public override bool EnableRaisingEvents
        {
            get => _enableRaisingEvents;
            set
            {
                if (value == _enableRaisingEvents)
                {
                    return;
                }

                lock (_children)
                {
                    foreach (var watcher in _children)
                    {
                        watcher.EnableRaisingEvents = value;
                    }
                }

                _enableRaisingEvents = value;
            }
        }

        /// <inheritdoc />
        public override bool IncludeSubdirectories
        {
            get => _includeSubdirectories;
            set
            {
                if (value == _includeSubdirectories)
                {
                    return;
                }

                lock (_children)
                {
                    foreach (var watcher in _children)
                    {
                        watcher.IncludeSubdirectories = value;
                    }
                }

                _includeSubdirectories = value;
            }
        }

        /// <inheritdoc />
        public override string Filter
        {
            get => _filter;
            set
            {
                if (value == _filter)
                {
                    return;
                }

                lock (_children)
                {
                    foreach (var watcher in _children)
                    {
                        watcher.Filter = value;
                    }
                }

                _filter = value;
            }
        }
    }
}
