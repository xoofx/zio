using System;

namespace Zio.Watcher
{
    public interface IFileSystemWatcher : IDisposable
    {
        event EventHandler<FileChangedEventArgs> Changed;

        event EventHandler<FileChangedEventArgs> Created;

        event EventHandler<FileChangedEventArgs> Deleted;

        event EventHandler<FileSystemErrorEventArgs> Error;

        event EventHandler<FileRenamedEventArgs> Renamed;

        IFileSystem FileSystem { get; }

        UPath Path { get; }

        int InternalBufferSize { get; set; }

        NotifyFilters NotifyFilter { get; set; }

        bool EnableRaisingEvents { get; set; }
        
        string Filter { get; set; }

        bool IncludeSubdirectories { get; set; }
    }
}
