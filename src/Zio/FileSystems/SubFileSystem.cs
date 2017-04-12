using System;

namespace Zio.FileSystems
{
    public class SubFileSystem : DelegateFileSystem
    {
        public SubFileSystem(IFileSystem fileSystem, string subSystemPath) : base(fileSystem)
        {
            SubPath = fileSystem.ConvertFromSystem(subSystemPath);
            if (!fileSystem.DirectoryExists(SubPath))
            {
                throw new ArgumentException($"The directory `{SubPath}` does not exist in the delegated FileSystem", nameof(subSystemPath));
            }
        }

        public SubFileSystem(IFileSystem fileSystem, PathInfo subPath) : base(fileSystem)
        {
            SubPath = subPath.AssertAbsolute(nameof(subPath));
            if (!fileSystem.DirectoryExists(SubPath))
            {
                throw new ArgumentException($"The directory `{SubPath}` does not exist in the delegated FileSystem", nameof(subPath));
            }
        }

        public PathInfo SubPath { get; }

        protected override PathInfo ConvertPathToDelegate(PathInfo path, string name)
        {
            var safePath = path.ToRelative();
            return SubPath / safePath;
        }

        protected override PathInfo ConvertPathFromDelegate(PathInfo path)
        {
            var fullPath = path.FullName;
            if (!fullPath.StartsWith(SubPath.FullName) || fullPath.Length <= SubPath.FullName.Length || fullPath[SubPath.FullName.Length] != PathInfo.DirectorySeparator)
            {
                throw new InvalidOperationException($"The path `{path}` is not rooted to `{SubPath}`");
            }

            return new PathInfo(fullPath.Substring(SubPath.FullName.Length), true);
        }
    }
}