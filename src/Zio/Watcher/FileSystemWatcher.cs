using System;

namespace Zio.Watcher
{
    public class FileSystemWatcher : IFileSystemWatcher
    {
        private string _filter;
        private FilterPattern _filterPattern;

        /// <inheritdoc />
        public event EventHandler<FileChangedEventArgs> Changed;

        /// <inheritdoc />
        public event EventHandler<FileChangedEventArgs> Created;

        /// <inheritdoc />
        public event EventHandler<FileChangedEventArgs> Deleted;

        /// <inheritdoc />
        public event EventHandler<FileSystemErrorEventArgs> Error;

        /// <inheritdoc />
        public event EventHandler<FileRenamedEventArgs> Renamed;

        /// <inheritdoc />
        public IFileSystem FileSystem { get; }

        /// <inheritdoc />
        public UPath Path { get; }

        /// <inheritdoc />
        public virtual int InternalBufferSize
        {
            get => 0;
            set { }
        }

        /// <inheritdoc />
        public virtual NotifyFilters NotifyFilter { get; set; }

        /// <inheritdoc />
        public virtual bool EnableRaisingEvents { get; set; }

        /// <inheritdoc />
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

        /// <inheritdoc />
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
            Filter = "*.*";
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

        /// <summary>
        /// Raises the <see cref="Changed"/> event. 
        /// </summary>
        /// <param name="args">Arguments for the event.</param>
        public void RaiseChanged(FileChangedEventArgs args)
        {
            if (!ShouldRaiseEvent(args))
            {
                return;
            }

            Changed?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the <see cref="Created"/> event. 
        /// </summary>
        /// <param name="args">Arguments for the event.</param>
        public void RaiseCreated(FileChangedEventArgs args)
        {
            if (!ShouldRaiseEvent(args))
            {
                return;
            }

            Created?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the <see cref="Deleted"/> event. 
        /// </summary>
        /// <param name="args">Arguments for the event.</param>
        public void RaiseDeleted(FileChangedEventArgs args)
        {
            if (!ShouldRaiseEvent(args))
            {
                return;
            }

            Deleted?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the <see cref="Error"/> event. 
        /// </summary>
        /// <param name="args">Arguments for the event.</param>
        public void RaiseError(FileSystemErrorEventArgs args)
        {
            if (!EnableRaisingEvents)
            {
                return;
            }

            Error?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the <see cref="Renamed"/> event. 
        /// </summary>
        /// <param name="args">Arguments for the event.</param>
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

        /// <summary>
        /// Checks if the event should be raised for the given arguments. Default implementation
        /// checks if the <see cref="FileChangedEventArgs.FullPath"/> is contained in <see cref="Path"/>.
        /// </summary>
        /// <param name="args">Arguments for the event.</param>
        /// <returns>True if the event should be raised, false to ignore it.</returns>
        protected virtual bool ShouldRaiseEventImpl(FileChangedEventArgs args)
        {
            return args.FullPath.IsInDirectory(Path, IncludeSubdirectories);
        }

        /// <summary>
        /// Listens to events from another <see cref="IFileSystemWatcher"/> instance to forward them
        /// into this instance.
        /// </summary>
        /// <param name="watcher">Other instance to listen to.</param>
        protected void RegisterEvents(IFileSystemWatcher watcher)
        {
            watcher.Changed += OnChanged;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Error += OnError;
            watcher.Renamed += OnRenamed;
        }

        /// <summary>
        /// Stops listening to events from another <see cref="IFileSystemWatcher"/>.
        /// </summary>
        /// <param name="watcher">Instance to remove event handlers from.</param>
        protected void UnregisterEvents(IFileSystemWatcher watcher)
        {
            watcher.Changed -= OnChanged;
        }

        /// <summary>
        /// Converts paths from an existing event in another <see cref="IFileSystem"/> into
        /// this <see cref="FileSystem"/>.
        /// </summary>
        /// <param name="pathFromEvent">Path from the other filesystem.</param>
        /// <returns>Path in this filesystem.</returns>
        protected virtual UPath ConvertPath(UPath pathFromEvent)
        {
            return pathFromEvent;
        }

        private void OnChanged(object sender, FileChangedEventArgs args)
        {
            var newArgs = new FileChangedEventArgs(args.ChangeType, ConvertPath(args.FullPath));
            RaiseChanged(newArgs);
        }

        private void OnCreated(object sender, FileChangedEventArgs args)
        {
            var newArgs = new FileChangedEventArgs(args.ChangeType, ConvertPath(args.FullPath));
            RaiseCreated(newArgs);
        }

        private void OnDeleted(object sender, FileChangedEventArgs args)
        {
            var newArgs = new FileChangedEventArgs(args.ChangeType, ConvertPath(args.FullPath));
            RaiseDeleted(newArgs);
        }

        private void OnError(object sender, FileSystemErrorEventArgs args)
        {
            RaiseError(args);
        }

        private void OnRenamed(object sender, FileRenamedEventArgs args)
        {
            var newArgs = new FileRenamedEventArgs(
                args.ChangeType, ConvertPath(args.FullPath), ConvertPath(args.OldFullPath));

            RaiseRenamed(newArgs);
        }
    }
}
