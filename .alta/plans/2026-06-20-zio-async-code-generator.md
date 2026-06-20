# Zio async code generator

- Status: Completed (amended after async/sync uncoupling review)
- Plan file: `.alta/plans/2026-06-20-zio-async-code-generator.md`
- Created: 2026-06-20
- Task: Add a Roslyn-based generator that derives net10-only async Zio APIs from the existing synchronous routes and emits guarded generated source.
- Git: `.alta/plans/` is not ignored; commit this plan with the related implementation work. Current user-owned dirty files to preserve: `.github/workflows/ci.yml`, `doc/add-async-specs.md`, `img/zio_banner.png`.

## Objective

- Build a Zio-specific async code generation tool, inspired by `C:\code\lunet\scriban\src\Scriban.AsyncCodeGen\`, that starts from `src/Zio/IFileSystem.cs`, generates async counterparts, and transitively generates async callers so async routes are not hand-duplicated.
- Use `ValueTask`/`ValueTask<T>` for scalar async operations and `IAsyncEnumerable<T>` for search/enumeration async operations.
- Generate async APIs only for the `net10.0` Zio target initially, guarded with `#if !ZIO_NO_ASYNC && NET10_0_OR_GREATER`, while older TFMs keep the existing synchronous surface only.
- Emit generated async code separately from the existing synchronous source, with generated files guarded so Zio can compile in a sync-only mode while the generator analyzes the project.
- Keep the existing filesystem implementations' synchronous bodies intact; only make minimal source edits needed for generated partial members and build integration.
- Non-goals for the first implementation: automatic build-time generation, async APIs for `netstandard2.0`/`netstandard2.1`/`net8.0`, wholesale refactors of filesystem logic, and broad true-async physical I/O rewrites unless a safe net10-specific mapping is explicit.

## Context and evidence

- `src/Zio/IFileSystem.cs:12-251` is the root sync interface, including special cases for optional `OpenFile(..., FileShare share = FileShare.None)`, `TryResolveLinkTarget(..., out UPath)`, `IEnumerable<UPath>`/`IEnumerable<FileSystemItem>` search APIs, watcher APIs, and path conversion/resolve APIs.
- `src/Zio/FileSystems/FileSystem.cs:15-604` centralizes public validation/disposal checks and routes to protected `*Impl` hooks. A generated partial `FileSystem` can reuse private members such as `AssertNotDisposed` if the class is made `partial`.
- The previous prototype commit `0b35eb5b045f26257feaca5c8e261b44d811bf01` added `IFileSystemAsync`, `FileSystemAsync`, wrapper async files, and tests, but introduced large manual duplicate routes (`AggregateFileSystem.Async.cs`, `MountFileSystem.Async.cs`, etc.) and used `ValueTask<IEnumerable<T>>` for enumeration. Use its API-shape lessons, but update search APIs to async streams and avoid manual duplication.
- `src/Zio/FileSystems/ComposeFileSystem.cs:13-280`, `SubFileSystem.cs:16-119`, `ReadOnlyFileSystem.cs:15-159`, `AggregateFileSystem.cs:17-427`, and `MountFileSystem.cs:19-907` are wrapper/composite filesystems where generated async overrides must preserve delegation, path translation, read-only policy, mount/aggregate lookup, and watcher wrapping.
- `src/Zio/FileSystems/MemoryFileSystem.cs:20`, `PhysicalFileSystem.cs:21`, and `ZipArchiveFileSystem.cs:12-19` cover concrete in-memory, physical, and zip filesystems. Zip output must preserve `HAS_ZIPARCHIVE` guards inside the net10 async guard.
- `src/Zio/FileSystemExtensions.cs:17-918`, `DirectoryEntry.cs:12-143`, `FileEntry.cs:15-367`, `FileSystemEntry.cs:13-158`, and `FileSystemItem.cs:13-154` contain convenience methods that call `IFileSystem` APIs; they should be included in the caller closure for method-based async routes.
- `src/Zio/Zio.csproj:8` targets `netstandard2.0;netstandard2.1;net8.0;net10.0`; `src/Zio/Zio.csproj:44-61` composes TFM constants from `$(AdditionalConstants)`. For net10-only async generation, generator-time compilation should use `TargetFramework=net10.0` and `AdditionalConstants=ZIO_NO_ASYNC`, not override `DefineConstants`.
- `src/Directory.Packages.props:1-13` uses central package management; Roslyn/MSBuild package versions belong there. Net10-only runtime async APIs should not need `System.Threading.Tasks.Extensions` or `Microsoft.Bcl.AsyncInterfaces`.
- Scriban's generator opens a solution with `MSBuildWorkspace` (`C:\code\lunet\scriban\src\Scriban.AsyncCodeGen\Program.cs:34-46`), uses `SymbolFinder.FindCallersAsync` (`Program.cs:160`), rewrites invocation syntax (`Program.cs:353-427`), and writes generated source deterministically (`Program.cs:581-596`). Avoid copying its source-mutating `workspace.TryApplyChanges(solution)` pattern (`Program.cs:578`) for generated partial changes; make required source edits explicit.

## Assumptions and open decisions

- Assumption: Generate a separate `IFileSystemAsync` interface rather than adding async members directly to `IFileSystem`, preserving compatibility for external `IFileSystem` implementations.
- Decision: Async generated APIs compile only for `net10.0` initially (`NET10_0_OR_GREATER`), so packages targeting `netstandard2.0`, `netstandard2.1`, or `net8.0` remain sync-only.
- Decision: `FileSystem` does not inherit from or implement `IFileSystemAsync`; the generator emits a separate `FileSystemAsync : IFileSystemAsync` base for direct async implementations.
- Decision: Built-in sync filesystems keep their sync-only inheritance contract; the generator emits separate `XXXFileSystemAsync` types such as `MemoryFileSystemAsync`, so sync implementers are not forced to add async members.
- Assumption: `IFileSystemAsync.EnumeratePathsAsync` and `EnumerateItemsAsync` return `IAsyncEnumerable<T>` directly, not `ValueTask<IEnumerable<T>>`.
- Assumption: The generator transforms methods, not properties. Property accessors discovered in the caller closure should be reported as diagnostics and either skipped or mapped to explicitly named async methods only in a later follow-up.
- Decision: User confirmed net10-only async APIs for the first implementation; broaden to `net8.0`/`netstandard2.1` only if explicitly requested in a later task.

## Design notes

- Add a console tool project under `src/Zio.AsyncCodeGen/` targeting `net10.0`, with Roslyn/MSBuild workspace dependencies centrally versioned. Add it to `src/Zio.slnx` but do not make normal Zio builds run it automatically.
- Generator entrypoint:
  - Register MSBuild with `Microsoft.Build.Locator`.
  - Open `src/Zio.slnx` or `src/Zio/Zio.csproj` with properties `TargetFramework=net10.0` and `AdditionalConstants=ZIO_NO_ASYNC`.
  - Fail fast if the sync-only net10 compilation has diagnostics with severity error.
- Async API root mapping:
  - Read `IFileSystem` method symbols and generate `IFileSystemAsync.gen.cs` with matching XML docs via `<inheritdoc cref="..." />` where possible.
  - Map `void` to `ValueTask`, scalar non-void to `ValueTask<T>`, `IEnumerable<T>` to `IAsyncEnumerable<T>`, `TryResolveLinkTarget(out UPath)` to `ValueTask<(bool Success, UPath ResolvedPath)>`, and append `CancellationToken cancellationToken = default` after existing parameters.
  - For async stream methods, use direct `IAsyncEnumerable<T>` return types; implementation methods that are async iterators should annotate the token with `[EnumeratorCancellation]` and use `await foreach (... .WithCancellation(cancellationToken).ConfigureAwait(false))` where delegating to async streams.
  - Preserve `OpenFileAsync(..., FileShare share = FileShare.None, CancellationToken cancellationToken = default)` and require generated call sites to use named `cancellationToken:` when skipping optional `share`.
- Base dispatch generation:
  - Generate `FileSystems/FileSystemAsync.gen.cs` as a separate abstract `FileSystemAsync : IFileSystemAsync` under `#if !ZIO_NO_ASYNC && NET10_0_OR_GREATER`; do not add async members to `FileSystem`.
  - Public async methods should mirror the sync validation/error path in `FileSystem`, then call protected virtual `*AsyncImpl` hooks.
  - Direct `FileSystemAsync` route implementations stay abstract where the sync `FileSystem` route is abstract; sync-backed `XXXFileSystemAsync` types provide cancellation-aware async-shaped forwarding to inherited synchronous public routes.
  - Sync-backed enumeration methods adapt sync `IEnumerable<T>` into `IAsyncEnumerable<T>` with cooperative cancellation between yielded items and without `Task.Run`.
- Async propagation generation:
  - Seed the graph with async roots from `IFileSystem` plus `FileSystem` protected `*Impl` hooks and their overrides.
  - Use semantic symbols (`SymbolFinder`, `SemanticModel`, `SymbolEqualityComparer`) rather than text matching to find callers, overrides, and call sites within the Zio project only.
  - Generate async variants for method callers in `FileSystemExtensions`, entry/item types, and filesystem partial classes; generate protected `*AsyncImpl` overrides for classes that override protected `*Impl` hooks, including read-only throw-only overrides so async write attempts cannot bypass policy.
  - Rewrites should rename only mapped Zio methods to `*Async`, append/pass `cancellationToken` by name, wrap awaited scalar calls with `.ConfigureAwait(false)`, use `await foreach` for async streams, and keep unmapped external calls unchanged unless an explicit safe net10 BCL async mapping is configured.
  - Convert iterator methods (`yield return`) that consume generated async streams into `async IAsyncEnumerable<T>` methods. Preserve ordering and filtering semantics; if a sync iterator cannot be safely transformed, emit a diagnostic and require a handwritten rule rather than materializing silently.
  - Emit diagnostics for unsupported constructs (`out`/`ref` async methods other than the known tuple mapping, property accessors, constructors, operators, event handlers, unknown external async candidates) instead of silently generating wrong code.
- Generated file layout:
  - Prefer per-type generated files under `src/Zio/generated/` for reviewability and guard control, e.g. `src/Zio/generated/IFileSystemAsync.gen.cs`, `src/Zio/generated/FileSystems/FileSystemAsync.gen.cs`, `src/Zio/generated/FileSystemExtensionsAsync.gen.cs`, `src/Zio/generated/FileSystems/ComposeFileSystemAsync.gen.cs`, `src/Zio/generated/FileSystems/ReadOnlyFileSystemAsync.gen.cs`, `src/Zio/generated/FileSystems/MountFileSystemAsync.gen.cs`, `src/Zio/generated/FileSystems/AggregateFileSystemAsync.gen.cs`, `src/Zio/generated/FileSystems/MemoryFileSystemAsync.gen.cs`, `src/Zio/generated/FileSystems/PhysicalFileSystemAsync.gen.cs`, and `src/Zio/generated/FileSystems/ZipArchiveFileSystemAsync.gen.cs`.
  - Every generated file should start with an auto-generated header, `#nullable enable`, and `#if !ZIO_NO_ASYNC && NET10_0_OR_GREATER` before async-only usings. Zip generated output should use a nested or combined `HAS_ZIPARCHIVE` guard.
  - Deterministic output: stable file ordering, stable member ordering, normalized newlines, UTF-8 encoding, no timestamps.
- Build integration:
  - Add source `partial` keywords only where generated members are needed (`FileSystem`, built-in filesystem classes, `FileSystemExtensions`, and method-based convenience types included in generation).
  - Do not add runtime async support packages for older TFMs in the net10-only plan. If the scope later broadens beyond net10, revisit `System.Threading.Tasks.Extensions` and `Microsoft.Bcl.AsyncInterfaces` only for the affected TFMs.
  - Ensure tests that reference async APIs compile only when `NET10_0_OR_GREATER && !ZIO_NO_ASYNC`; net472 and non-net10 test targets must remain sync-only.
- Docs:
  - Document that async APIs are available only for the net10 package target in the initial implementation.
  - Update async/codegen documentation after the API shape stabilizes. Preserve the current untracked `doc/add-async-specs.md`; do not overwrite or delete it without checking its diff and intent.

## Risks and challenges

- Net10-only async APIs create intentional TFM-specific public API differences; docs and tests must make this explicit.
- Adding generated async members to existing public types requires `partial` source edits; this is small but still touches existing type declarations.
- Extending `IFileSystem` directly with async methods would break external implementers; the plan avoids that with `IFileSystemAsync`.
- Async stream generation is more complex than `ValueTask<IEnumerable<T>>`: iterator state machines, `[EnumeratorCancellation]`, `await foreach`, and deferred validation/cancellation behavior all require tests.
- `ReadOnlyFileSystem` has throw-only sync overrides; relying only on caller discovery would miss them and could allow async writes through inherited delegation.
- Cancellation ordering can subtly change exception behavior. Generated methods should check cancellation before I/O/mutation and between async-stream yields, but avoid changing cheap validation/disposal semantics without tests.
- Existing user-owned dirty files must be preserved; do not reset, delete, or opportunistically reformat them.

## Implementation checklist

- [x] Record `git status --short` before editing and avoid touching `.github/workflows/ci.yml`, `doc/add-async-specs.md`, and `img/zio_banner.png` unless the user explicitly accepts those doc/image/workflow changes.
- [x] Add `src/Zio.AsyncCodeGen/Zio.AsyncCodeGen.csproj` with Roslyn/MSBuild workspace dependencies and central package versions in `src/Directory.Packages.props`; add the project to `src/Zio.slnx`.
- [x] Mark target source types as `partial` with minimal declaration-only edits: `FileSystem`, filesystem classes that receive generated partials, `FileSystemExtensions`, and selected method-based entry/item types.
- [x] Implement generator project loading with `MSBuildWorkspace`, `TargetFramework=net10.0`, and `AdditionalConstants=ZIO_NO_ASYNC`; fail on compilation errors.
- [x] Implement async descriptor creation from `IFileSystem` method symbols, including return type conversion (`IEnumerable<T>` to `IAsyncEnumerable<T>`), cancellation token parameters, XML doc inheritance, optional parameter handling, and the `TryResolveLinkTarget` tuple special case.
- [x] Implement generated `IFileSystemAsync.gen.cs`, separate `FileSystemAsync.gen.cs`, and separate built-in `XXXFileSystemAsync` outputs, all guarded by `!ZIO_NO_ASYNC && NET10_0_OR_GREATER`.
- [x] Implement sync-enumerable-to-async-stream adapter generation for default enumeration hooks, with cancellation checks between yielded items and no `Task.Run`.
- [x] Implement symbol-based graph discovery for `FileSystem` protected impl overrides and transitive method callers in Zio source, with explicit diagnostics for unsupported constructs.
- [x] Implement invocation/body rewriting for generated async methods: mapped call renaming, named cancellation token propagation, `await ...ConfigureAwait(false)`, `await foreach`/`WithCancellation` for async streams, expression-bodied method handling, `using`/`try` preservation, and iterator-safe handling.
- [x] Implement per-type generated file writing with stable member ordering, auto-generated headers, net10 async guards, and zip-specific `HAS_ZIPARCHIVE` guards.
- [x] Run the generator once, review generated files, and adjust the generator rather than hand-editing `.gen.cs` output.
- [x] Add net10-only async tests under `src/Zio.Tests/` for API shape, forwarding/parity on `MemoryFileSystem`, async stream enumeration, read-only rejection, compose/sub path translation, mount/aggregate dispatch/order, cancellation, zip behavior when enabled, and a physical temp-file smoke path.
- [x] Guard async tests or test files so `net472` and `ZIO_NO_ASYNC` builds compile without async generated APIs.
- [x] Update docs/readme or a dedicated async-codegen doc to state net10-only async API availability and async-stream search behavior, preserving the existing untracked async proposal file unless intentionally editing it.
- [x] Rework the generator away from printf-style full-file generation to Roslyn syntax/semantic rewriting of existing sync methods, with string output limited to file guards/header/trivia scaffolding.
- [x] Self-review generated and handwritten diffs for minimal source churn, no hidden source mutations from the generator, and no unrelated changes.

## Verification checklist

- [x] From `src`, run `dotnet run --project Zio.AsyncCodeGen -- --check` (or the implemented check-mode equivalent) to verify generated output is up to date without rewriting files.
- [x] From `src`, run the generator twice and verify generated files are deterministic with no second-run diff.
- [x] From `src`, run `dotnet build -c Release` for the normal solution; verify async APIs compile for `net10.0` and are absent from older Zio TFMs.
- [x] From `src`, run `dotnet build -c Release -p:AdditionalConstants=ZIO_NO_ASYNC` to prove sync-only compilation works.
- [x] From `src`, run `dotnet test -c Release`.
- [x] Run targeted net10 async tests first if the full suite is slow or fails for unrelated reasons, then report any skipped/full-suite failures explicitly.
- [x] Run `git diff --check` and inspect the final diff, especially handwritten files vs generated files.
- [x] Confirm public net10 async APIs have XML docs/inheritdoc and generated files contain no timestamps or local absolute paths except in generator diagnostics/logs.

## Handoff notes

- This is a generator-first plan: if generated output is wrong, fix the generator and regenerate rather than manually patching `.gen.cs` files.
- The approved plan incorporates review feedback by using `IAsyncEnumerable<T>` for enumeration and net10-only async APIs for the first implementation.
- Keep implementation in small phases: tool skeleton, API/base generation, async-stream support, filesystem override generation, convenience method generation, tests/docs.
- The child read-only critique emphasized preserving wrapper semantics, avoiding `DefineConstants` override, per-type generated files, iterator caution, and dirty file preservation; those points are still folded into this plan.
- Do not commit or stage in Plan mode. In Default mode, commit this plan with the related implementation work if commits are requested by the project workflow.

## Follow-up amendment notes

- [x] Regenerated async filesystem types after adding transitive async helper conversion, including `TryGetFileAsync`, `TryGetDirectoryAsync`, `TryGetPathAsync`, and `FindPathsAsync` in aggregate lookup code.
- [x] Removed generated sync-over-async blocking from async implementations and added generator output validation for `.GetAwaiter().GetResult(`, `.Result`, and `.Wait()`.
- [x] Removed unused `partial` from sync filesystem and extension classes after confirming sync partial counterparts are not generated.
- [x] Verified generator `--check`, normal Release build, `ZIO_NO_ASYNC` Release build, full Release tests, and `git diff --check`.

## Async-disposal amendment notes

- [x] Changed generated async file-system surface so `IFileSystemAsync` inherits `IAsyncDisposable`, `FileSystemAsync` exposes the async dispose pattern, and generated `Dispose(bool)` overrides become `DisposeAsync(bool)` overrides.
- [x] Updated generated composite disposal to await owned async file systems (`Fallback`, aggregate entries, and mount entries) without sync blocking or forcing sync `FileSystem` types to implement async disposal.
- [x] Removed the sync adapter from `FileSystemAsync`; async implementations no longer expose `AsSync`.
- [x] Added broader net10 async tests for async-only disposal shape, owned/non-owned disposal, memory mutation/metadata/watch, direct compose, read-only, sub, mount, aggregate, cross-filesystem copy/move, physical, and zip behavior.
- [x] Fixed generated async compose `ResolvePathAsyncImpl` fallback dispatch while adding coverage for delegated resolve paths.
- [x] Re-ran generator `--check`, normal Release build, `ZIO_NO_ASYNC` Release build, and full Release tests for the amendment.

## Async/sync uncoupling amendment notes

- [x] Generated async duplicates for entry/item/watcher/event-args/search-predicate types so async APIs use `IFileSystemAsync`, `IFileSystemWatcherAsync`, `FileSystemEntryAsync`, `FileSystemItemAsync`, and related async event args instead of sync-only types.
- [x] Removed `FileSystemAsync.AsSync` and sync bridge generation, and rewrote async watcher/entry call sites to stay on async-native types.
- [x] Kept generated built-in filesystems inheriting async-only bases and fixed generated watcher/finalizer/type rewrites from sync support sources.
- [x] Added tests proving async entry helpers and watchers expose async-native types and that `FileSystemAsync` has no `AsSync` bridge or sync interface inheritance.
- [x] Verified generator `--check`, normal Release build, `ZIO_NO_ASYNC` Release build, full Release tests, and generated-output greps for `AsSync` and sync filesystem inheritance.

## Documentation/future-proofing amendment notes

- [x] Removed the top-level README async code sample and kept it as a short pointer to detailed documentation.
- [x] Updated `doc/readme.md` to use `MemoryFileSystemAsync`, explain that async classes/support types are entirely generated from sync source, and document regeneration commands, guards, limitations, and no `AsSync` bridge.
- [x] Updated `AGENTS.md` with guidance to regenerate and verify generated async classes whenever sync filesystem sources change.
- [x] Added generator output validation for stale sync coupling: `AsSync`, sync-over-async blocking patterns, sync-only async API type references, and generated async filesystems inheriting sync filesystem types.
