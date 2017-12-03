using System;

namespace Zio.Watcher
{
    /// <summary>
    /// Contains information about a filesystem error event.
    /// </summary>
    /// <inheritdoc />
    public class FileSystemErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Exception that was thrown in the filesystem.
        /// </summary>
        public Exception Exception { get; }

        public FileSystemErrorEventArgs(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            Exception = exception;
        }
    }
}
