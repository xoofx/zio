using System;

namespace Zio
{
    /// <summary>
    /// Interface for a filesystem watcher.
    /// </summary>
    /// <inheritdoc />
    public interface IFileSystemWatcher : IDisposable
    {
        /// <summary>
        /// Event for when a file or directory changes.
        /// </summary>
        event EventHandler<FileChangedEventArgs>? Changed;

        /// <summary>
        /// Event for when a file or directory is created.
        /// </summary>
        event EventHandler<FileChangedEventArgs>? Created;

        /// <summary>
        /// Event for when a file or directory is deleted.
        /// </summary>
        event EventHandler<FileChangedEventArgs>? Deleted;

        /// <summary>
        /// Event for when the filesystem encounters an error.
        /// </summary>
        event EventHandler<FileSystemErrorEventArgs>? Error;

        /// <summary>
        /// Event for when a file or directory is renamed.
        /// </summary>
        event EventHandler<FileRenamedEventArgs>? Renamed;

        /// <summary>
        /// The <see cref="IFileSystem"/> this instance is watching.
        /// </summary>
        IFileSystem FileSystem { get; }

        /// <summary>
        /// The path being watched by the filesystem.
        /// </summary>
        UPath Path { get; }

        /// <summary>
        /// Implementation-defined buffer size for storing events.
        /// </summary>
        int InternalBufferSize { get; set; }

        /// <summary>
        /// Implementation-defined filters for filtering events.
        /// </summary>
        NotifyFilters NotifyFilter { get; set; }

        /// <summary>
        /// True to enable raising events, false to never raise them. Default false.
        /// </summary>
        bool EnableRaisingEvents { get; set; }
        
        /// <summary>
        /// File name and extension filter. Use <c>"*"</c> to specify variable length placeholder, <c>"?"</c>
        /// for a single character placeholder. Default is <c>"*.*"</c> for all files.
        /// </summary>
        string Filter { get; set; }

        /// <summary>
        /// True to watch all subdirectories in <see cref="Path"/>, false to only watch entries directly
        /// in <see cref="Path"/>.
        /// </summary>
        bool IncludeSubdirectories { get; set; }
    }
}
