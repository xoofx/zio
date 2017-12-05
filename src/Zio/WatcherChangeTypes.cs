using System;

namespace Zio
{
    /// <summary>
    /// Changes that might occur to a file or directory.
    /// </summary>
    [Flags]
    public enum WatcherChangeTypes
    {
        /// <summary>
        /// The creation of a file or directory.
        /// </summary>
        Created = 1,

        /// <summary>
        /// The deletion of a file or directory.
        /// </summary>
        Deleted = 2,

        /// <summary>
        /// The change of a file or directory. This could include attributes, contents, access time, etc.
        /// </summary>
        Changed = 4,

        /// <summary>
        /// The renaming of a file or directory.
        /// </summary>
        Renamed = 8,

        /// <summary>
        /// All possible changes.
        /// </summary>
        All = Created | Deleted | Changed | Renamed
    }
}
