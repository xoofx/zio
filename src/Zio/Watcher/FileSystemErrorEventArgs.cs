using System;

namespace Zio.Watcher
{
    public class FileSystemErrorEventArgs : EventArgs
    {
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
