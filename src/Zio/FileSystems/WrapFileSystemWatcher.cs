using System;

namespace Zio.FileSystems
{
    /// <summary>
    /// Wraps another <see cref="IFileSystemWatcher"/> instance to allow event modification and filtering.
    /// </summary>
    public class WrapFileSystemWatcher : FileSystemWatcher
    {
        private readonly IFileSystemWatcher _watcher;

        public WrapFileSystemWatcher(IFileSystem fileSystem, UPath path, IFileSystemWatcher watcher)
            : base(fileSystem, path)
        {
            if (watcher is null)
            {
                throw new ArgumentNullException(nameof(watcher));
            }

            _watcher = watcher;

            RegisterEvents(_watcher);
        }

        /// <inheritdoc />
        public override int InternalBufferSize
        {
            get => _watcher.InternalBufferSize;
            set => _watcher.InternalBufferSize = value;
        }

        /// <inheritdoc />
        public override NotifyFilters NotifyFilter
        {
            get => _watcher.NotifyFilter;
            set => _watcher.NotifyFilter = value;
        }

        /// <inheritdoc />
        public override bool EnableRaisingEvents
        {
            get => _watcher.EnableRaisingEvents;
            set => _watcher.EnableRaisingEvents = value;
        }

        /// <inheritdoc />
        public override string Filter
        {
            get => _watcher.Filter;
            set => _watcher.Filter = value;
        }

        /// <inheritdoc />
        public override bool IncludeSubdirectories
        {
            get => _watcher.IncludeSubdirectories;
            set => _watcher.IncludeSubdirectories = value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnregisterEvents(_watcher);
                _watcher.Dispose();
            }
        }
    }
}
