using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Zio.Watcher
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

        public FileSystemEventDispatcher()
        {
            _dispatchThread = new Thread(DispatchWorker);
            _dispatchQueue = new BlockingCollection<Action>(16);
            _dispatchCts = new CancellationTokenSource();
            _watchers = new List<T>();

            _dispatchThread.Start();
        }

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
            _dispatchQueue.CompleteAdding();

            lock (_watchers)
            {
                foreach (var watcher in _watchers)
                {
                    watcher.Dispose();
                }

                _watchers.Clear();
            }

            _dispatchCts.Cancel();
            _dispatchThread.Join();
            _dispatchQueue.Dispose();
        }

        public void Add(T watcher)
        {
            lock (_watchers)
            {
                _watchers.Add(watcher);
            }
        }

        public void Remove(T watcher)
        {
            lock (_watchers)
            {
                _watchers.Remove(watcher);
            }
        }

        public void RaiseChange(UPath path)
        {
            var args = new FileChangedEventArgs(WatcherChangeTypes.Changed, path);
            Dispatch(args, (w, a) => w.RaiseChanged(a));
        }

        public void RaiseCreated(UPath path)
        {
            var args = new FileChangedEventArgs(WatcherChangeTypes.Created, path);
            Dispatch(args, (w, a) => w.RaiseCreated(a));
        }

        public void RaiseDeleted(UPath path)
        {
            var args = new FileChangedEventArgs(WatcherChangeTypes.Deleted, path);
            Dispatch(args, (w, a) => w.RaiseDeleted(a));
        }

        public void RaiseRenamed(UPath newPath, UPath oldPath)
        {
            var args = new FileRenamedEventArgs(WatcherChangeTypes.Renamed, newPath, oldPath);
            Dispatch(args, (w, a) => w.RaiseRenamed(a));
        }

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
