using System;

namespace Zio.Watcher
{
    public class WrapFileSystemWatcher : FileSystemWatcher
    {
        private readonly IFileSystemWatcher _watcher;

        public WrapFileSystemWatcher(IFileSystem fileSystem, UPath path, IFileSystemWatcher watcher)
            : base(fileSystem, path)
        {
            if (watcher == null)
                throw new ArgumentNullException(nameof(watcher));

            _watcher = watcher;

            RegisterEvents(_watcher);
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterEvents(_watcher);
            _watcher.Dispose();
        }
    }
}
