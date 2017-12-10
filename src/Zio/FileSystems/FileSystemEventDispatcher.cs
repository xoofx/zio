using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Zio.FileSystems
{
    /// <summary>
    /// Stores <see cref="FileSystemWatcher"/> instances to dispatch events to. Events are
    /// called on a separate thread.
    /// </summary>
    /// <typeparam name="T">The <see cref="FileSystemWatcher"/> type to store.</typeparam>
    public class FileSystemEventDispatcher<T> : IDisposable
        where T : FileSystemWatcher
    {
        private readonly Thread _dispatchThread;
        private readonly BlockingCollection<Action> _dispatchQueue;
        private readonly CancellationTokenSource _dispatchCts;
        private readonly List<T> _watchers;

        public FileSystemEventDispatcher(IFileSystem fileSystem)
        {
            FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _dispatchThread = new Thread(DispatchWorker)
            {
                Name = "FileSystem Event Dispatch",
                IsBackground = true
            };

            _dispatchQueue = new BlockingCollection<Action>(16);
            _dispatchCts = new CancellationTokenSource();
            _watchers = new List<T>();

            _dispatchThread.Start();
        }

        public IFileSystem FileSystem { get; }

        ~FileSystemEventDispatcher()
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
            _dispatchCts?.Cancel();
            _dispatchThread?.Join();

            if (!disposing)
            {
                return;
            }

            _dispatchQueue.CompleteAdding();

            lock (_watchers)
            {
                foreach (var watcher in _watchers)
                {
                    watcher.Dispose();
                }

                _watchers.Clear();
            }

            _dispatchQueue.Dispose();
        }

        /// <summary>
        /// Adds a <see cref="FileSystemWatcher"/> instance to dispatch events to.
        /// </summary>
        /// <param name="watcher">Instance to add.</param>
        public void Add(T watcher)
        {
            lock (_watchers)
            {
                _watchers.Add(watcher);
            }
        }

        /// <summary>
        /// Removes a <see cref="FileSystemWatcher"/> instance to stop dispatching events.
        /// </summary>
        /// <param name="watcher">Instance to remove.</param>
        public void Remove(T watcher)
        {
            lock (_watchers)
            {
                _watchers.Remove(watcher);
            }
        }

        /// <summary>
        /// Raise the <see cref="IFileSystemWatcher.Changed"/> event on watchers.
        /// </summary>
        /// <param name="path">Absolute path to the changed file or directory.</param>
        public void RaiseChange(UPath path)
        {
            var args = new FileChangedEventArgs(FileSystem, WatcherChangeTypes.Changed, path);
            Dispatch(args, (w, a) => w.RaiseChanged(a));
        }

        /// <summary>
        /// Raise the <see cref="IFileSystemWatcher.Created"/> event on watchers.
        /// </summary>
        /// <param name="path">Absolute path to the new file or directory.</param>
        public void RaiseCreated(UPath path)
        {
            var args = new FileChangedEventArgs(FileSystem, WatcherChangeTypes.Created, path);
            Dispatch(args, (w, a) => w.RaiseCreated(a));
        }
        
        /// <summary>
        /// Raise the <see cref="IFileSystemWatcher.Deleted"/> event on watchers.
        /// </summary>
        /// <param name="path">Absolute path to the changed file or directory.</param>
        public void RaiseDeleted(UPath path)
        {
            var args = new FileChangedEventArgs(FileSystem, WatcherChangeTypes.Deleted, path);
            Dispatch(args, (w, a) => w.RaiseDeleted(a));
        }

        /// <summary>
        /// Raise the <see cref="IFileSystemWatcher.Renamed"/> event on watchers.
        /// </summary>
        /// <param name="newPath">Absolute path to the new file or directory.</param>
        /// <param name="oldPath">Absolute path to the old file or directory.</param>
        public void RaiseRenamed(UPath newPath, UPath oldPath)
        {
            var args = new FileRenamedEventArgs(FileSystem, WatcherChangeTypes.Renamed, newPath, oldPath);
            Dispatch(args, (w, a) => w.RaiseRenamed(a));
        }

        /// <summary>
        /// Raise the <see cref="IFileSystemWatcher.Error"/> event on watchers.
        /// </summary>
        /// <param name="exception">Exception that occurred.</param>
        public void RaiseError(Exception exception)
        {
            var args = new FileSystemErrorEventArgs(exception);
            Dispatch(args, (w, a) => w.RaiseError(a), false);
        }

        private void Dispatch<TArgs>(TArgs eventArgs, Action<T, TArgs> handler, bool captureError = true)
            where TArgs : EventArgs
        {
            List<T> watchersSnapshot;
            lock (_watchers)
            {
                if (_watchers.Count == 0)
                {
                    return;
                }

                watchersSnapshot = _watchers.ToList(); // TODO: reduce allocations
            }

            // The events should be called on a separate thread because the filesystem code
            // could be holding locks that must be released.
            _dispatchQueue.Add(() =>
            {
                foreach (var watcher in watchersSnapshot)
                {
                    try
                    {
                        handler(watcher, eventArgs);
                    }
                    catch (Exception e) when (captureError)
                    {
                        RaiseError(e);
                    }
                }
            });
        }

        // Worker runs on dedicated thread to call events
        private void DispatchWorker()
        {
            var ct = _dispatchCts.Token;

            try
            {
                foreach (var action in _dispatchQueue.GetConsumingEnumerable(ct))
                {
                    action();
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
