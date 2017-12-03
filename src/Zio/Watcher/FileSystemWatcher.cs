using System;

namespace Zio.Watcher
{
    public class FileSystemWatcher : IFileSystemWatcher
    {
        private string _filter;
        private FilterPattern _filterPattern;

        public event EventHandler<FileChangedEventArgs> Changed;
        public event EventHandler<FileChangedEventArgs> Created;
        public event EventHandler<FileChangedEventArgs> Deleted;
        public event EventHandler<FileSystemErrorEventArgs> Error;
        public event EventHandler<FileRenamedEventArgs> Renamed;
        
        public IFileSystem FileSystem { get; }
        public UPath Path { get; }

        public virtual int InternalBufferSize
        {
            get => 0;
            set { }
        }

        public virtual NotifyFilters NotifyFilter { get; set; }

        public virtual bool EnableRaisingEvents { get; set; }

        public virtual string Filter
        {
            get => _filter;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    value = "*";
                }

                if (value == _filter)
                {
                    return;
                }

                _filterPattern = FilterPattern.Parse(value);
                _filter = value;
            }
        }

        public virtual bool IncludeSubdirectories { get; set; }

        public FileSystemWatcher(IFileSystem fileSystem, UPath path)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }
            path.AssertAbsolute();

            FileSystem = fileSystem;
            Path = path;
            NotifyFilter = NotifyFilters.Default;
            Filter = "*";
        }

        ~FileSystemWatcher()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void RaiseChanged(FileChangedEventArgs args)
        {
            if (!ShouldRaiseEvent(args))
            {
                return;
            }

            Changed?.Invoke(this, args);
        }

        public void RaiseCreated(FileChangedEventArgs args)
        {
            if (!ShouldRaiseEvent(args))
            {
                return;
            }

            Created?.Invoke(this, args);
        }

        public void RaiseDeleted(FileChangedEventArgs args)
        {
            if (!ShouldRaiseEvent(args))
            {
                return;
            }

            Deleted?.Invoke(this, args);
        }

        public void RaiseError(FileSystemErrorEventArgs args)
        {
            if (!EnableRaisingEvents)
            {
                return;
            }

            Error?.Invoke(this, args);
        }

        public void RaiseRenamed(FileRenamedEventArgs args)
        {
            if (!ShouldRaiseEvent(args))
            {
                return;
            }

            Renamed?.Invoke(this, args);
        }

        private bool ShouldRaiseEvent(FileChangedEventArgs args)
        {
            return EnableRaisingEvents &&
                   _filterPattern.Match(args.Name) &&
                   ShouldRaiseEventImpl(args);
        }

        protected virtual bool ShouldRaiseEventImpl(FileChangedEventArgs args)
        {
            return args.FullPath.IsInDirectory(Path, IncludeSubdirectories);
        }

        protected void RegisterEvents(IFileSystemWatcher watcher)
        {
            watcher.Changed += OnChanged;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Error += OnError;
            watcher.Renamed += OnRenamed;
        }

        protected void UnregisterEvents(IFileSystemWatcher watcher)
        {
            watcher.Changed -= OnChanged;
        }

        protected virtual UPath ConvertPath(UPath pathFromEvent)
        {
            return pathFromEvent;
        }

        protected void OnChanged(object sender, FileChangedEventArgs args)
        {
            var newArgs = new FileChangedEventArgs(args.ChangeType, ConvertPath(args.FullPath));
            RaiseChanged(newArgs);
        }

        protected void OnCreated(object sender, FileChangedEventArgs args)
        {
            var newArgs = new FileChangedEventArgs(args.ChangeType, ConvertPath(args.FullPath));
            RaiseCreated(newArgs);
        }

        protected void OnDeleted(object sender, FileChangedEventArgs args)
        {
            var newArgs = new FileChangedEventArgs(args.ChangeType, ConvertPath(args.FullPath));
            RaiseDeleted(newArgs);
        }

        protected void OnError(object sender, FileSystemErrorEventArgs args)
        {
            RaiseError(args);
        }

        protected void OnRenamed(object sender, FileRenamedEventArgs args)
        {
            var newArgs = new FileRenamedEventArgs(
                args.ChangeType, ConvertPath(args.FullPath), ConvertPath(args.OldFullPath));

            RaiseRenamed(newArgs);
        }
    }
}
