# Zio [![Build Status](https://github.com/xoofx/zio/workflows/ci/badge.svg?branch=master)](https://github.com/xoofx/zio/actions) [![Coverage Status](https://coveralls.io/repos/github/xoofx/zio/badge.svg?branch=master)](https://coveralls.io/github/xoofx/zio?branch=master) [![NuGet](https://img.shields.io/nuget/v/Zio.svg)](https://www.nuget.org/packages/Zio/)

<img align="right" width="160px" height="160px" src="img/zio.png">

Zio provides a simple, powerful, cross-platform **filesystem abstraction for .NET** with many built-ins filesystems.

## Features

- Compatible with `.NET 4.0`, `4.5+` and the cross platform `.NET Core/Standard 1.3+`
- API providing all operations provided by the regular System.IO API (e.g File.Move, Directory.Delete... etc.)
  - Allowing atomic filesystem operations (e.g File.Replace...)
- A simple interface abstraction [`IFileSystem`](src/Zio/IFileSystem.cs)
- Supports for filesystem watcher through the `IFileSystem.Watch` method and the [`IFileSystemWatcher`](src/Zio/IFileSystemWatcher.cs) interface
  - For all builtin filesystems (aggregates, memory...etc.)
- All paths are normalized through a lightweight uniform path struct [`UPath`](src/Zio/UPath.cs)
- Multiple built-ins filesystems:
  - `PhysicalFileSystem` to access the physical disks, directories and folders.
    - With uniform paths, this filesystem on Windows is working like on a Windows Subsystem Linux (WSL), by remapping drives to mount directory (e.g path `/mnt/c/Windows` equivalent to `C:\Windows`)
  - `MemoryFileSystem` to access a filesystem in memory:
    - Trying to be 100% compatible with a true `PhysicalFileSystem` (including exceptions)
    - Efficient concurrency with a per node (file or directory) locking mechanism
    - A safe hierarchical locking strategy (following [Unix kernel recommendations for directory locking](https://www.kernel.org/doc/Documentation/filesystems/directory-locking))
    - Support for `FileShare.Read`, `FileShare.Write` and `FileShare.ReadWrite`
    - Internally support for filesystem atomic operations (`File.Replace`)
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

The [documentation](doc) is directly available as part of this repository in the [`/doc`](doc) folder.

## Download

Zio is available as a NuGet package: [![NuGet](https://img.shields.io/nuget/v/Zio.svg)](https://www.nuget.org/packages/Zio/)

## Build

In order to build Zio, you need to install Visual Studio 2017 with latest [.NET Core](https://www.microsoft.com/net/core)

## TODO

- [ ] Add support for ZipArchive (readonly, readwrite)
- [ ] Add support for Git FileSystem (readonly)

## License

This software is released under the [BSD-Clause 2 license](license.txt).

## Credits

The logo is `File` by [jeff](https://thenounproject.com/jeff955/) from the Noun Project

## Author

Alexandre MUTEL aka [xoofx](http://xoofx.com)
