# Changelog

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