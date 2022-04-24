# Zio [![Build Status](https://github.com/xoofx/zio/workflows/ci/badge.svg?branch=main)](https://github.com/xoofx/zio/actions) [![Coverage Status](https://coveralls.io/repos/github/xoofx/zio/badge.svg?branch=main)](https://coveralls.io/github/xoofx/zio?branch=main) [![NuGet](https://img.shields.io/nuget/v/Zio.svg)](https://www.nuget.org/packages/Zio/)

<img align="right" width="160px" height="160px" src="https://raw.githubusercontent.com/xoofx/zio/main/img/zio.png">

Zio provides a simple, powerful, cross-platform **filesystem abstraction for .NET** with many built-ins filesystems.

## Features

- Compatible with `.NET 4.0`, `4.5+`, `netstandard2.0`, `netstandard2.1` and `net6.0`
- API providing all operations provided by the regular System.IO API (e.g File.Move, Directory.Delete... etc.)
  - Allowing atomic filesystem operations (e.g File.Replace...)
- A simple interface abstraction [`IFileSystem`](https://github.com/xoofx/zio/blob/main/src/Zio/IFileSystem.cs)
- Supports for filesystem watcher through the `IFileSystem.Watch` method and the [`IFileSystemWatcher`](https://github.com/xoofx/zio/blob/main/src/Zio/IFileSystemWatcher.cs) interface
  - For all builtin filesystems (aggregates, memory...etc.)
- All paths are normalized through a lightweight uniform path struct [`UPath`](https://github.com/xoofx/zio/blob/main/src/Zio/UPath.cs)
- Multiple built-ins filesystems:
  - `PhysicalFileSystem` to access the physical disks, directories and folders.
    - With uniform paths, this filesystem on Windows is working like on a Windows Subsystem Linux (WSL), by remapping drives to mount directory (e.g path `/mnt/c/Windows` equivalent to `C:\Windows`)
  - `MemoryFileSystem` to access a filesystem in memory:
    - Trying to be 100% compatible with a true `PhysicalFileSystem` (including exceptions)
    - Efficient concurrency with a per node (file or directory) locking mechanism
    - A safe hierarchical locking strategy (following [Unix kernel recommendations for directory locking](https://www.kernel.org/doc/Documentation/filesystems/directory-locking))
    - Support for `FileShare.Read`, `FileShare.Write` and `FileShare.ReadWrite`
    - Internally support for filesystem atomic operations (`File.Replace`)
  - `ZipArchiveFileSystem` to access zip archives:
    - This filesystem is a wrapper around the [`ZipArchive`](https://docs.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive?view=netcore-3.1) class
	- It can work in case sensitive and case insensitive mode
	- Support for `FileShare.Read` with `ZipArchiveMode.Read`
  - On top of these final filesystem, you can compose more complex filesystems:
    - `AggregateFileSystem` providing a read-only filesystem aggregating multiple filesystem that offers a merged view
    - `MountFileSystem` to mount different filesystems at a specific mount point name
    - `SubFileSystem` to view a sub-folder of another filesystem as if it was a root `/` directory
    - `ReadOnlyFileSystem` to interact safely with another filesystem in read-only mode
- Higher level API similar to `FileSystemEntry`, `FileEntry` and `DirectoryEntry` offering a similar API to their respective `FileSystemInfo`, `FileInfo`, `DirectoryInfo`

## Usage

Accessing a physical filesystem:

```c#
var fs = new PhysicalFileSystem();
foreach(var dir in fs.EnumerateDirectories("/mnt/c"))
{
    // ...
}
```

Using an in-memory filesystem:

```c#
var fs = new MemoryFileSystem();
fs.WriteAllText("/temp.txt", "This is a content");
if (fs.FileExists("/temp.txt"))
{
    Console.WriteLine("The content of the file:" + fs.ReadAllText("/temp.txt"))
}
```

The following documentation provides more information about the API and how to use it.

## Documentation

The [documentation](https://github.com/xoofx/zio/tree/main/doc) is directly available as part of this repository in the `/doc` folder.

## Download

Zio is available as a NuGet package: [![NuGet](https://img.shields.io/nuget/v/Zio.svg)](https://www.nuget.org/packages/Zio/)

## Build

In order to build Zio, you need to install Visual Studio 2022 with latest [.NET 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

## License

This software is released under the [BSD-Clause 2 license](https://github.com/xoofx/zio/blob/main/license.txt).

## Credits

The logo is `File` by [jeff](https://thenounproject.com/jeff955/) from the Noun Project

## Author

Alexandre MUTEL aka [xoofx](https://xoofx.com)
