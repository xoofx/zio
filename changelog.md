# Changelog

## 0.7.6 (28 Jan 2020)
- Fix assembly to use Portable debug info.

## 0.7.5 (28 Jan 2020)
- Make UPath struct readonly.
- Don't throw if Dispose is being called multiple times on a FileSystem via [(PR #38)](https://github.com/xoofx/zio/pull/38).
- Add SourceLink support.

## 0.7.4 (11 May 2019)
- Add MountFS.TryGetMount and MountFS.TryGetMountName via [(PR #36)](https://github.com/xoofx/zio/pull/36)

## 0.7.3 (02 Feb 2019)
- Properly show mount paths when enumerating MountFS (fixes #28) [(PR #29)](https://github.com/xoofx/zio/pull/29)
- Don't throw when enumerating root on empty MountFS [(PR #31)](https://github.com/xoofx/zio/pull/31)
- Fix IFileSystemWatcher instances not being removed from AggregateFSWs [(PR #32)](https://github.com/xoofx/zio/pull/32)
- Fix dispose not removing the watchers from aggregate and mount FS [(PR #34)](https://github.com/xoofx/zio/pull/34) 

## 0.7.2 (04 Apr 2018)
- Fix MountFS watchers having incorrect paths when created in Mount() [(PR #26)](https://github.com/xoofx/zio/pull/26)

## 0.7.1 (12 Jan 2018)
- Add CanWatch impls to physical and composite FS [(PR #24)](https://github.com/xoofx/zio/pull/24)

## 0.7.0 (11 Jan 2018)
- Use dispose for all aggregate file systems. Add support for owned FS [(PR #22)](https://github.com/xoofx/zio/pull/22)
- Fix SearchPattern special case for Windows. Via [(PR #23)](https://github.com/xoofx/zio/pull/23)
- Correct MountFS watch behavior for arbitrary mounts. Via [(PR #17)](https://github.com/xoofx/zio/pull/17)
- Add IFileSystem.CanWatch. Via [(PR #18)](https://github.com/xoofx/zio/pull/18)
- Add support for netstandard2.0 to avoid pulling dependencies there. Via [(PR #21)](https://github.com/xoofx/zio/pull/21)

## 0.6.0 (23 Dec 2017)
- Add support for mount points at any path for `MountFileSystem`. Courtesy of [Rohan Singh](https://github.com/Rohansi) via [PR #11](https://github.com/xoofx/zio/pull/11)

## 0.5.0 (10 Dec 2017)
- Propagate the originating IFileSystem to the IFileSystemWatcher events

## 0.4.0 (9 Dec 2017)
- Add support for `IFileSystemWatcher`, courtesy of [Rohan Singh](https://github.com/Rohansi) via [PR #9](https://github.com/xoofx/zio/pull/9)

## 0.3.6 (19 Nov 2017)
- Try to fix a sporadic Unauthorized access when using CopyFileCross with a PhysicalFileSystem as a destination

## 0.3.5 (19 Nov 2017)
- Add FileEntry.CopyTo across filesystems

## 0.3.4 (19 Nov 2017)
- Fix FileSystemEntry.Parent (FileEntry.Directory). Should return a DirectoryEntry even if it does not exist instead of throwing an exception

## 0.3.3 (19 Nov 2017)
- Add extension method IFileSystem.GetOrCreateSubFileSystem

## 0.3.2 (14 Nov 2017)
- Fix issue when combining a root path `/` with an empty path (#7)
- Add == operator to FileSystemEntrty

## 0.3.1 (15 May 2017)
- Add IEquatable&lt;FileSystemEntry&gt; to FileSystemEntry

## 0.3.0 (14 May 2017)
- Add AggregateFileSystem.ClearFileSystems and AggregateFileSystem.FindFileSystemEntries
- Add FileEntry.ReadAllText/WriteAllText/AppendAllText/ReadAllBytes/WriteAllBytes 

## 0.2.0 (5 May 2017)
- Fix directory/file locking issue in MemoryFileSystem

## 0.1.0 (2 May 2017)

- Initial version