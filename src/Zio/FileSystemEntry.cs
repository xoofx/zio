using System;

namespace Zio
{
    public abstract class FileSystemEntry
    {
        protected FileSystemEntry(IFileSystem fileSystem, PathInfo path)
        {
            FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            path.AssertAbsolute();
            Path = path;
        }

        public PathInfo Path { get; }

        public IFileSystem FileSystem { get; }

        public DateTime CreationTime => FileSystem.GetCreationTime(Path);

        public bool Exists => FileSystem.FileExists(Path);

        public string Extension => System.IO.Path.GetExtension(Path.FullName);

        public string FullName => Path.FullName;

        public DateTime LastAccessTime => FileSystem.GetLastAccessTime(Path);

        public DateTime LastWriteTime => FileSystem.GetLastWriteTime(Path);
        
        public string Name => Path.GetName();

        public string DotExtension => Path.GetDotExtension(); 

        public abstract void Delete();

        public override string ToString()
        {
            return Path.FullName;
        }
    }
}