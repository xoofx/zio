using System;
using System.Collections.Generic;
using System.IO;

namespace Zio.FileSystems
{
    public abstract class DelegateFileSystem : FileSystemBase
    {
        protected DelegateFileSystem(IFileSystem fileSystem)
        {
            NextFileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public IFileSystem NextFileSystem { get; }

        // ----------------------------------------------
        // Directory API
        // ----------------------------------------------

        protected override void CreateDirectoryImpl(PathInfo path)
        {
            NextFileSystem.CreateDirectory(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override bool DirectoryExistsImpl(PathInfo path)
        {
            return NextFileSystem.DirectoryExists(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override void MoveDirectoryImpl(PathInfo srcPath, PathInfo destPath)
        {
            NextFileSystem.MoveDirectory(ConvertPathToDelegate(srcPath, nameof(srcPath)), ConvertPathToDelegate(destPath, nameof(destPath)));
        }

        protected override void DeleteDirectoryImpl(PathInfo path, bool isRecursive)
        {
            NextFileSystem.DeleteDirectory(ConvertPathToDelegate(path, nameof(path)), isRecursive);
        }

        // ----------------------------------------------
        // File API
        // ----------------------------------------------

        protected override void CopyFileImpl(PathInfo srcPath, PathInfo destPath, bool overwrite)
        {
            NextFileSystem.CopyFile(ConvertPathToDelegate(srcPath, nameof(srcPath)), ConvertPathToDelegate(destPath, nameof(destPath)), overwrite);
        }

        protected override void ReplaceFileImpl(PathInfo srcPath, PathInfo destPath, PathInfo destBackupPath,
            bool ignoreMetadataErrors)
        {
            NextFileSystem.ReplaceFile(ConvertPathToDelegate(srcPath, nameof(srcPath)), ConvertPathToDelegate(destPath, nameof(destPath)), ConvertPathToDelegate(destBackupPath, nameof(destBackupPath)), ignoreMetadataErrors);
        }

        protected override long GetFileLengthImpl(PathInfo path)
        {
            return NextFileSystem.GetFileLength(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override bool FileExistsImpl(PathInfo path)
        {
            return NextFileSystem.FileExists(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override void MoveFileImpl(PathInfo srcPath, PathInfo destPath)
        {
            NextFileSystem.MoveFile(ConvertPathToDelegate(srcPath, nameof(srcPath)), ConvertPathToDelegate(destPath, nameof(destPath)));
        }

        protected override void DeleteFileImpl(PathInfo path)
        {
            NextFileSystem.DeleteFile(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override Stream OpenFileImpl(PathInfo path, FileMode mode, FileAccess access, FileShare share = FileShare.None)
        {
            return NextFileSystem.OpenFile(ConvertPathToDelegate(path, nameof(path)), mode, access, share);
        }

        // ----------------------------------------------
        // Metadata API
        // ----------------------------------------------

        protected override FileAttributes GetAttributesImpl(PathInfo path)
        {
            return NextFileSystem.GetAttributes(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override void SetAttributesImpl(PathInfo path, FileAttributes attributes)
        {
            NextFileSystem.SetAttributes(ConvertPathToDelegate(path, nameof(path)), attributes);
        }

        protected override DateTime GetCreationTimeImpl(PathInfo path)
        {
            return NextFileSystem.GetCreationTime(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override void SetCreationTimeImpl(PathInfo path, DateTime time)
        {
            NextFileSystem.SetCreationTime(ConvertPathToDelegate(path, nameof(path)), time);
        }

        protected override DateTime GetLastAccessTimeImpl(PathInfo path)
        {
            return NextFileSystem.GetLastAccessTime(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override void SetLastAccessTimeImpl(PathInfo path, DateTime time)
        {
            NextFileSystem.SetLastAccessTime(ConvertPathToDelegate(path, nameof(path)), time);
        }

        protected override DateTime GetLastWriteTimeImpl(PathInfo path)
        {
            return NextFileSystem.GetLastWriteTime(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override void SetLastWriteTimeImpl(PathInfo path, DateTime time)
        {
            NextFileSystem.SetLastWriteTime(ConvertPathToDelegate(path, nameof(path)), time);
        }

        // ----------------------------------------------
        // Search API
        // ----------------------------------------------

        protected override IEnumerable<PathInfo> EnumeratePathsImpl(PathInfo path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
        {
            foreach (var subPath in NextFileSystem.EnumeratePaths(ConvertPathToDelegate(path, nameof(path)),
                searchPattern, searchOption, searchTarget))
            {
                yield return ConvertPathFromDelegate(subPath);
            }
        }

        // ----------------------------------------------
        // Path API
        // ----------------------------------------------

        protected override string ConvertToSystemImpl(PathInfo path)
        {
            return NextFileSystem.ConvertToSystem(ConvertPathToDelegate(path, nameof(path)));
        }

        protected override PathInfo ConvertFromSystemImpl(string systemPath)
        {
            return ConvertPathFromDelegate(NextFileSystem.ConvertFromSystem(systemPath));
        }

        protected abstract PathInfo ConvertPathToDelegate(PathInfo path, string name);

        protected abstract PathInfo ConvertPathFromDelegate(PathInfo path);
    }
}