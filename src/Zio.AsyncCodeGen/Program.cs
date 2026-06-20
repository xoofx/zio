using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Zio.AsyncCodeGen;

internal static class Program
{
    private const string Guard = "NET10_0_OR_GREATER && !ZIO_NO_ASYNC";

    private static readonly SymbolDisplayFormat TypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly string[] GeneratedRelativePaths =
    {
        @"Zio\generated\IFileSystemAsync.gen.cs",
        @"Zio\generated\FileSystemItemAsync.gen.cs",
        @"Zio\generated\FileSystemEntryAsync.gen.cs",
        @"Zio\generated\FileEntryAsync.gen.cs",
        @"Zio\generated\DirectoryEntryAsync.gen.cs",
        @"Zio\generated\IFileSystemWatcherAsync.gen.cs",
        @"Zio\generated\FileChangedEventArgsAsync.gen.cs",
        @"Zio\generated\FileRenamedEventArgsAsync.gen.cs",
        @"Zio\generated\FileSystemErrorEventArgsAsync.gen.cs",
        @"Zio\generated\SearchPredicateAsync.gen.cs",
        @"Zio\generated\FileSystemExtensionsAsync.gen.cs",
        @"Zio\generated\FileSystems\FileSystemAsync.gen.cs",
        @"Zio\generated\FileSystems\FileSystemWatcherAsync.gen.cs",
        @"Zio\generated\FileSystems\AggregateFileSystemWatcherAsync.gen.cs",
        @"Zio\generated\FileSystems\WrapFileSystemWatcherAsync.gen.cs",
        @"Zio\generated\FileSystems\FileSystemEventDispatcherAsync.gen.cs",
        @"Zio\generated\FileSystems\MemoryFileSystemAsync.gen.cs",
        @"Zio\generated\FileSystems\PhysicalFileSystemAsync.gen.cs",
        @"Zio\generated\FileSystems\ComposeFileSystemAsync.gen.cs",
        @"Zio\generated\FileSystems\ReadOnlyFileSystemAsync.gen.cs",
        @"Zio\generated\FileSystems\SubFileSystemAsync.gen.cs",
        @"Zio\generated\FileSystems\MountFileSystemAsync.gen.cs",
        @"Zio\generated\FileSystems\AggregateFileSystemAsync.gen.cs",
        @"Zio\generated\FileSystems\ZipArchiveFileSystemAsync.gen.cs"
    };

    private static readonly string[] GeneratedFileSystems =
    {
        "MemoryFileSystem",
        "PhysicalFileSystem",
        "ComposeFileSystem",
        "ReadOnlyFileSystem",
        "SubFileSystem",
        "MountFileSystem",
        "AggregateFileSystem",
        "ZipArchiveFileSystem"
    };

    private static readonly IReadOnlyDictionary<string, string> AsyncTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["FileSystem"] = "FileSystemAsync",
        ["IFileSystem"] = "IFileSystemAsync",
        ["FileSystemItem"] = "FileSystemItemAsync",
        ["FileSystemEntry"] = "FileSystemEntryAsync",
        ["FileEntry"] = "FileEntryAsync",
        ["DirectoryEntry"] = "DirectoryEntryAsync",
        ["IFileSystemWatcher"] = "IFileSystemWatcherAsync",
        ["FileChangedEventArgs"] = "FileChangedEventArgsAsync",
        ["FileRenamedEventArgs"] = "FileRenamedEventArgsAsync",
        ["FileSystemErrorEventArgs"] = "FileSystemErrorEventArgsAsync",
        ["SearchPredicate"] = "SearchPredicateAsync",
        ["FileSystemWatcher"] = "FileSystemWatcherAsync",
        ["AggregateFileSystemWatcher"] = "AggregateFileSystemWatcherAsync",
        ["WrapFileSystemWatcher"] = "WrapFileSystemWatcherAsync",
        ["FileSystemEventDispatcher"] = "FileSystemEventDispatcherAsync",
        ["MemoryFileSystem"] = "MemoryFileSystemAsync",
        ["PhysicalFileSystem"] = "PhysicalFileSystemAsync",
        ["ComposeFileSystem"] = "ComposeFileSystemAsync",
        ["ReadOnlyFileSystem"] = "ReadOnlyFileSystemAsync",
        ["SubFileSystem"] = "SubFileSystemAsync",
        ["MountFileSystem"] = "MountFileSystemAsync",
        ["AggregateFileSystem"] = "AggregateFileSystemAsync",
        ["ZipArchiveFileSystem"] = "ZipArchiveFileSystemAsync"
    };

    private static readonly string[] SyncOnlyAsyncApiTypeNames =
    {
        "IFileSystemWatcher",
        "FileSystemEntry",
        "FileSystemItem",
        "FileEntry",
        "DirectoryEntry",
        "FileChangedEventArgs",
        "FileRenamedEventArgs",
        "FileSystemErrorEventArgs",
        "SearchPredicate",
        "IFileSystem"
    };

    private static readonly string[] SyncFileSystemTypeNames =
    {
        "MemoryFileSystem",
        "PhysicalFileSystem",
        "ComposeFileSystem",
        "ReadOnlyFileSystem",
        "AggregateFileSystem",
        "MountFileSystem",
        "SubFileSystem",
        "ZipArchiveFileSystem"
    };

    private static readonly HashSet<string> ExtensionMethodsToGenerate = new(StringComparer.Ordinal)
    {
        "CopyFileCross",
        "MoveFileCross",
        "ReadAllBytes",
        "ReadAllText",
        "ReadAllLines",
        "WriteAllBytes",
        "WriteAllText",
        "AppendAllText",
        "CreateFile",
        "EnumerateDirectories",
        "EnumerateFiles",
        "EnumeratePaths",
        "EnumerateFileEntries",
        "EnumerateDirectoryEntries",
        "EnumerateFileSystemEntries",
        "GetFileSystemEntry",
        "TryGetFileSystemEntry",
        "GetFileEntry",
        "GetDirectoryEntry",
        "TryWatch"
    };

    public static async Task<int> Main(string[] args)
    {
        var check = args.Any(static arg => string.Equals(arg, "--check", StringComparison.OrdinalIgnoreCase));
        var sourceRoot = FindSourceRoot(Environment.CurrentDirectory);
        var solutionPath = Path.Combine(sourceRoot, "Zio.slnx");

        Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();

        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["TargetFramework"] = "net10.0",
            ["AdditionalConstants"] = "ZIO_NO_ASYNC",
            ["MinVerMajor"] = "0",
            ["MinVerMinor"] = "0"
        });

        workspace.RegisterWorkspaceFailedHandler(static e => Console.Error.WriteLine(e.Diagnostic.Message));

        var solution = await workspace.OpenSolutionAsync(solutionPath).ConfigureAwait(false);
        var project = solution.Projects.Single(project => project.Name == "Zio");
        var compilation = await project.GetCompilationAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Unable to create a compilation for Zio.");

        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            Console.Error.WriteLine("Compilation errors while loading Zio with AdditionalConstants=ZIO_NO_ASYNC:");
            foreach (var error in errors)
            {
                Console.Error.WriteLine(error.ToString());
            }
            return 1;
        }

        var fileSystemInterface = compilation.GetTypeByMetadataName("Zio.IFileSystem")
            ?? throw new InvalidOperationException("Unable to find Zio.IFileSystem.");
        var fileSystemDeclaration = GetTypeDeclaration<ClassDeclarationSyntax>(compilation, "Zio.FileSystems.FileSystem");
        var extensionDeclaration = GetTypeDeclaration<ClassDeclarationSyntax>(compilation, "Zio.FileSystemExtensions");
        var catalog = AsyncCatalog.Create(compilation, fileSystemInterface, fileSystemDeclaration, extensionDeclaration);
        ValidateAsyncGenerationInputs(compilation, catalog);

        var generator = new AsyncSourceGenerator(compilation, catalog, fileSystemDeclaration, extensionDeclaration);
        var outputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [@"Zio\generated\IFileSystemAsync.gen.cs"] = generator.GenerateInterface(),
            [@"Zio\generated\FileSystemItemAsync.gen.cs"] = generator.GenerateAsyncEntryType("Zio.FileSystemItem"),
            [@"Zio\generated\FileSystemEntryAsync.gen.cs"] = generator.GenerateAsyncEntryType("Zio.FileSystemEntry"),
            [@"Zio\generated\FileEntryAsync.gen.cs"] = generator.GenerateAsyncEntryType("Zio.FileEntry"),
            [@"Zio\generated\DirectoryEntryAsync.gen.cs"] = generator.GenerateAsyncEntryType("Zio.DirectoryEntry"),
            [@"Zio\generated\IFileSystemWatcherAsync.gen.cs"] = generator.GenerateAsyncSupportType("Zio.IFileSystemWatcher"),
            [@"Zio\generated\FileChangedEventArgsAsync.gen.cs"] = generator.GenerateAsyncSupportType("Zio.FileChangedEventArgs"),
            [@"Zio\generated\FileRenamedEventArgsAsync.gen.cs"] = generator.GenerateAsyncSupportType("Zio.FileRenamedEventArgs"),
            [@"Zio\generated\FileSystemErrorEventArgsAsync.gen.cs"] = generator.GenerateAsyncSupportType("Zio.FileSystemErrorEventArgs"),
            [@"Zio\generated\SearchPredicateAsync.gen.cs"] = generator.GenerateSearchPredicateAsync(),
            [@"Zio\generated\FileSystemExtensionsAsync.gen.cs"] = generator.GenerateExtensions(),
            [@"Zio\generated\FileSystems\FileSystemAsync.gen.cs"] = generator.GenerateFileSystemAsyncBase(),
            [@"Zio\generated\FileSystems\FileSystemWatcherAsync.gen.cs"] = generator.GenerateAsyncSupportType("Zio.FileSystems.FileSystemWatcher"),
            [@"Zio\generated\FileSystems\AggregateFileSystemWatcherAsync.gen.cs"] = generator.GenerateAsyncSupportType("Zio.FileSystems.AggregateFileSystemWatcher"),
            [@"Zio\generated\FileSystems\WrapFileSystemWatcherAsync.gen.cs"] = generator.GenerateAsyncSupportType("Zio.FileSystems.WrapFileSystemWatcher"),
            [@"Zio\generated\FileSystems\FileSystemEventDispatcherAsync.gen.cs"] = generator.GenerateAsyncSupportType("Zio.FileSystems.FileSystemEventDispatcher`1")
        };

        foreach (var typeName in GeneratedFileSystems)
        {
            outputs[@$"Zio\generated\FileSystems\{typeName}Async.gen.cs"] = generator.GenerateAsyncFileSystem(
                typeName,
                extraGuard: typeName == "ZipArchiveFileSystem" ? "HAS_ZIPARCHIVE" : null);
        }

        foreach (var relativePath in outputs.Keys.ToArray())
        {
            outputs[relativePath] = NormalizeNewlines(outputs[relativePath]);
        }

        ValidateAsyncImplementationOutputs(outputs);

        var hasChanges = false;
        foreach (var relativePath in GeneratedRelativePaths.Except(outputs.Keys, StringComparer.Ordinal))
        {
            var path = Path.Combine(sourceRoot, relativePath);
            if (!File.Exists(path))
            {
                continue;
            }

            hasChanges = true;
            if (check)
            {
                Console.Error.WriteLine($"Generated file should be removed: {relativePath}");
            }
            else
            {
                File.Delete(path);
                Console.WriteLine($"Deleted {relativePath}");
            }
        }

        foreach (var (relativePath, content) in outputs)
        {
            var path = Path.Combine(sourceRoot, relativePath);
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            {
                continue;
            }

            hasChanges = true;
            if (check)
            {
                Console.Error.WriteLine($"Generated file is out of date: {relativePath}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"Wrote {relativePath}");
        }

        return check && hasChanges ? 1 : 0;
    }

    private static void ValidateAsyncImplementationOutputs(IReadOnlyDictionary<string, string> outputs)
    {
        foreach (var (relativePath, content) in outputs)
        {
            if (!relativePath.EndsWith(".gen.cs", StringComparison.Ordinal))
            {
                continue;
            }

            if (content.Contains(".GetAwaiter().GetResult(", StringComparison.Ordinal)
                || content.Contains(".Result", StringComparison.Ordinal)
                || content.Contains(".Wait()", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Generated async filesystem implementation contains sync-over-async blocking: {relativePath}");
            }

            if (content.Contains("AsSync", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Generated async output contains a sync file-system adapter reference: {relativePath}");
            }

            ValidateNoSyncOnlyTypeReferences(relativePath, content);
            ValidateAsyncFileSystemInheritance(relativePath, content);
        }
    }

    private static void ValidateNoSyncOnlyTypeReferences(string relativePath, string content)
    {
        var lineNumber = 0;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var syncTypeName in SyncOnlyAsyncApiTypeNames)
            {
                if (Regex.IsMatch(line, $@"\b{Regex.Escape(syncTypeName)}\b(?!Async)"))
                {
                    throw new InvalidOperationException($"Generated async output references sync-only type `{syncTypeName}` at {relativePath}:{lineNumber}.");
                }
            }
        }
    }

    private static void ValidateAsyncFileSystemInheritance(string relativePath, string content)
    {
        if (!relativePath.Contains(@"FileSystems\", StringComparison.Ordinal)
            && !relativePath.Contains("FileSystems/", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var syncTypeName in SyncFileSystemTypeNames)
        {
            if (Regex.IsMatch(content, $@"\bclass\s+\w+Async\s*:\s*{Regex.Escape(syncTypeName)}\b"))
            {
                throw new InvalidOperationException($"Generated async file system inherits sync type `{syncTypeName}`: {relativePath}.");
            }
        }
    }

    private static string NormalizeNewlines(string content) => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static TypeSyntax RewriteType(TypeSyntax type)
    {
        return (TypeSyntax)new TypeReferenceRewriter(AsyncTypeNames).Visit(type)!;
    }

    private static string FindSourceRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Zio.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to find src/Zio.slnx from the current directory.");
    }

    private static T GetTypeDeclaration<T>(Compilation compilation, string metadataName)
        where T : TypeDeclarationSyntax
    {
        var type = compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Unable to find required type `{metadataName}`.");
        return type.DeclaringSyntaxReferences.Select(static reference => reference.GetSyntax()).OfType<T>().FirstOrDefault()
            ?? throw new InvalidOperationException($"Unable to find a syntax declaration for `{metadataName}`.");
    }

    private static void ValidateAsyncGenerationInputs(Compilation compilation, AsyncCatalog catalog)
    {
        var seenMethodNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in catalog.FileSystemMethods)
        {
            if (!seenMethodNames.Add(method.Name))
            {
                throw new InvalidOperationException($"IFileSystem overloads are not supported by the async generator: {method.Name}");
            }

            if (method.Symbol.IsGenericMethod)
            {
                throw new InvalidOperationException($"Generic IFileSystem methods are not supported by the async generator: {method.Name}");
            }

            foreach (var parameter in method.Symbol.Parameters)
            {
                if (parameter.RefKind == RefKind.None)
                {
                    continue;
                }

                var isKnownTryResolveOutParameter = method.Name == "TryResolveLinkTarget"
                    && parameter.RefKind == RefKind.Out
                    && parameter.Type.ToDisplayString(TypeFormat) == "Zio.UPath";
                if (!isKnownTryResolveOutParameter)
                {
                    throw new InvalidOperationException($"Unsupported ref/out parameter on IFileSystem.{method.Name}: {parameter.Name}");
                }
            }
        }

        _ = compilation.GetTypeByMetadataName("Zio.FileSystems.FileSystem")
            ?? throw new InvalidOperationException("Unable to find Zio.FileSystems.FileSystem.");
        foreach (var typeName in GeneratedFileSystems)
        {
            _ = compilation.GetTypeByMetadataName("Zio.FileSystems." + typeName)
                ?? throw new InvalidOperationException($"Unable to find Zio.FileSystems.{typeName}.");
        }
    }

    private enum AsyncReturnKind
    {
        ValueTask,
        ValueTaskOfT,
        AsyncEnumerable,
        TryResolveLinkTarget
    }

    private sealed record MethodSpec(
        string Name,
        IMethodSymbol Symbol,
        MethodDeclarationSyntax InterfaceDeclaration,
        AsyncReturnKind Kind,
        TypeSyntax AsyncReturnType,
        TypeSyntax? SyncValueType,
        TypeSyntax? AsyncElementType)
    {
        public string AsyncName => Name + "Async";

        public string ImplName => Name + "Impl";

        public string AsyncImplName => Name + "AsyncImpl";

        public bool IsResolvePath => Name == "ResolvePath";

        public bool ReturnsAsyncEnumerable => Kind == AsyncReturnKind.AsyncEnumerable;

        public static MethodSpec FromInterfaceMethod(Compilation compilation, IMethodSymbol symbol, MethodDeclarationSyntax declaration)
        {
            if (symbol.Name == "TryResolveLinkTarget")
            {
                return new MethodSpec(
                    symbol.Name,
                    symbol,
                    declaration,
                    AsyncReturnKind.TryResolveLinkTarget,
                    ParseTypeName("ValueTask<(bool Success, UPath ResolvedPath)>"),
                    null,
                    null);
            }

            if (symbol.ReturnsVoid)
            {
                return new MethodSpec(symbol.Name, symbol, declaration, AsyncReturnKind.ValueTask, IdentifierName("ValueTask"), null, null);
            }

            if (TryGetEnumerableElement(symbol.ReturnType, out var elementType))
            {
                return new MethodSpec(
                    symbol.Name,
                    symbol,
                    declaration,
                    AsyncReturnKind.AsyncEnumerable,
                    GenericName("IAsyncEnumerable").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(RewriteType(ParseTypeName(elementType.ToDisplayString(TypeFormat)))))),
                    null,
                    RewriteType(ParseTypeName(elementType.ToDisplayString(TypeFormat))));
            }

            var syncType = symbol.Name == "ResolvePath"
                ? ParseTypeName("(IFileSystemAsync FileSystem, UPath Path)")
                : RewriteType(ParseTypeName(symbol.ReturnType.ToDisplayString(TypeFormat)));
            return new MethodSpec(
                symbol.Name,
                symbol,
                declaration,
                AsyncReturnKind.ValueTaskOfT,
                GenericName("ValueTask").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(syncType))),
                syncType,
                null);
        }

        public static MethodSpec FromMemberMethod(IMethodSymbol symbol, MethodDeclarationSyntax declaration)
        {
            if (symbol.ReturnsVoid)
            {
                return new MethodSpec(symbol.Name, symbol, declaration, AsyncReturnKind.ValueTask, IdentifierName("ValueTask"), null, null);
            }

            if (TryGetEnumerableElement(symbol.ReturnType, out var elementType))
            {
                var rewrittenElementType = RewriteType(ParseTypeName(elementType.ToDisplayString(TypeFormat)));
                return new MethodSpec(
                    symbol.Name,
                    symbol,
                    declaration,
                    AsyncReturnKind.AsyncEnumerable,
                    GenericName("IAsyncEnumerable").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(rewrittenElementType))),
                    null,
                    rewrittenElementType);
            }

            var syncType = RewriteType(ParseTypeName(symbol.ReturnType.ToDisplayString(TypeFormat)));
            return new MethodSpec(
                symbol.Name,
                symbol,
                declaration,
                AsyncReturnKind.ValueTaskOfT,
                GenericName("ValueTask").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(syncType))),
                syncType,
                null);
        }

        private static bool TryGetEnumerableElement(ITypeSymbol type, out ITypeSymbol elementType)
        {
            if (type is INamedTypeSymbol { Name: "IEnumerable", TypeArguments.Length: 1 } enumerable
                && enumerable.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
            {
                elementType = enumerable.TypeArguments[0];
                return true;
            }

            elementType = type;
            return false;
        }
    }

    private sealed class AsyncCatalog
    {
        private readonly Dictionary<string, MethodSpec> _methodsByName;
        private readonly Dictionary<string, MethodSpec> _methodsByImplName;
        private readonly Dictionary<IMethodSymbol, MethodSpec> _extensionMethods;
        private readonly Dictionary<IMethodSymbol, MethodSpec> _memberMethods;

        private AsyncCatalog(
            ImmutableArray<MethodSpec> fileSystemMethods,
            Dictionary<IMethodSymbol, MethodSpec> extensionMethods,
            Dictionary<IMethodSymbol, MethodSpec> memberMethods)
        {
            FileSystemMethods = fileSystemMethods;
            _methodsByName = fileSystemMethods.ToDictionary(static method => method.Name, StringComparer.Ordinal);
            _methodsByImplName = fileSystemMethods.ToDictionary(static method => method.ImplName, StringComparer.Ordinal);
            _extensionMethods = extensionMethods;
            _memberMethods = memberMethods;
        }

        public ImmutableArray<MethodSpec> FileSystemMethods { get; }

        public IEnumerable<IMethodSymbol> ExtensionMethodSymbols => _extensionMethods.Keys;

        public static AsyncCatalog Create(
            Compilation compilation,
            INamedTypeSymbol fileSystemInterface,
            ClassDeclarationSyntax fileSystemDeclaration,
            ClassDeclarationSyntax extensionDeclaration)
        {
            var interfaceMethods = new List<MethodSpec>();
            var interfaceMethodDeclarations = fileSystemInterface.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax())
                .OfType<InterfaceDeclarationSyntax>()
                .SelectMany(static declaration => declaration.Members.OfType<MethodDeclarationSyntax>())
                .ToDictionary(static declaration => declaration.Identifier.ValueText, StringComparer.Ordinal);

            foreach (var method in fileSystemInterface.GetMembers().OfType<IMethodSymbol>().Where(static method => method.MethodKind == MethodKind.Ordinary))
            {
                if (!interfaceMethodDeclarations.TryGetValue(method.Name, out var declaration))
                {
                    throw new InvalidOperationException($"Unable to find syntax for IFileSystem.{method.Name}.");
                }

                interfaceMethods.Add(MethodSpec.FromInterfaceMethod(compilation, method, declaration));
            }

            var fileSystemMethods = interfaceMethods.ToImmutableArray();
            var byName = fileSystemMethods.ToDictionary(static method => method.Name, StringComparer.Ordinal);
            var extensionMethods = new Dictionary<IMethodSymbol, MethodSpec>(SymbolEqualityComparer.Default);
            var extensionModel = compilation.GetSemanticModel(extensionDeclaration.SyntaxTree);
            foreach (var declaration in extensionDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!ExtensionMethodsToGenerate.Contains(declaration.Identifier.ValueText))
                {
                    continue;
                }

                var symbol = extensionModel.GetDeclaredSymbol(declaration);
                if (symbol is null)
                {
                    continue;
                }

                extensionMethods[symbol] = CreateExtensionSpec(symbol, declaration);
            }

            var memberMethods = CreateAsyncMemberMethodSpecs(compilation);

            return new AsyncCatalog(fileSystemMethods, extensionMethods, memberMethods);
        }

        public bool TryGetByName(string name, out MethodSpec method) => _methodsByName.TryGetValue(name, out method!);

        public bool TryGetByImplName(string name, out MethodSpec method) => _methodsByImplName.TryGetValue(name, out method!);

        public bool TryGetMemberMethod(IMethodSymbol symbol, out MethodSpec method)
        {
            method = null!;
            var original = symbol.ReducedFrom ?? symbol.OriginalDefinition ?? symbol;
            foreach (var (memberSymbol, memberSpec) in _memberMethods)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, memberSymbol.OriginalDefinition)
                    || SymbolEqualityComparer.Default.Equals(original, memberSymbol.OriginalDefinition)
                    || SymbolEqualityComparer.Default.Equals(symbol, memberSymbol))
                {
                    method = memberSpec;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetFileSystemImpl(IMethodSymbol symbol, out MethodSpec method)
        {
            return TryGetByImplName(symbol.Name, out method) && HasMatchingFileSystemParameters(symbol, method);
        }

        public bool TryGetInvocationTarget(IMethodSymbol symbol, out MethodSpec method, out string asyncName)
        {
            var original = symbol.ReducedFrom ?? symbol.OriginalDefinition ?? symbol;
            if (TryGetByImplName(symbol.Name, out method) && HasMatchingFileSystemParameters(symbol, method))
            {
                asyncName = method.AsyncImplName;
                return true;
            }

            if (symbol.MethodKind == MethodKind.Ordinary && IsFileSystemApiSymbol(symbol) && TryGetByName(symbol.Name, out method))
            {
                asyncName = method.AsyncName;
                return true;
            }

            foreach (var (extensionSymbol, extensionSpec) in _extensionMethods)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, extensionSymbol.OriginalDefinition)
                    || SymbolEqualityComparer.Default.Equals(original, extensionSymbol.OriginalDefinition)
                    || SymbolEqualityComparer.Default.Equals(symbol, extensionSymbol))
                {
                    method = extensionSpec;
                    asyncName = extensionSpec.AsyncName;
                    return true;
                }
            }

            if (TryGetMemberMethod(symbol, out method))
            {
                asyncName = method.AsyncName;
                return true;
            }

            method = null!;
            asyncName = string.Empty;
            return false;
        }

        private static bool IsFileSystemApiSymbol(IMethodSymbol symbol)
        {
            var containingType = symbol.ContainingType;
            if (containingType is null)
            {
                return false;
            }

            for (INamedTypeSymbol? current = containingType; current is not null; current = current.BaseType)
            {
                var typeName = current.ToDisplayString(TypeFormat);
                if (typeName is "Zio.IFileSystem" or "Zio.FileSystems.FileSystem")
                {
                    return true;
                }
            }

            return containingType.AllInterfaces.Any(static @interface => @interface.ToDisplayString(TypeFormat) == "Zio.IFileSystem");
        }

        private static bool HasMatchingFileSystemParameters(IMethodSymbol symbol, MethodSpec method)
        {
            if (symbol.Parameters.Length != method.Symbol.Parameters.Length)
            {
                return false;
            }

            for (var i = 0; i < symbol.Parameters.Length; i++)
            {
                var left = symbol.Parameters[i];
                var right = method.Symbol.Parameters[i];
                if (left.RefKind != right.RefKind || left.Type.ToDisplayString(TypeFormat) != right.Type.ToDisplayString(TypeFormat))
                {
                    return false;
                }
            }

            return true;
        }

        public IEnumerable<(IMethodSymbol Symbol, MethodSpec Spec)> ExtensionMethodsInSourceOrder(ClassDeclarationSyntax extensionDeclaration, Compilation compilation)
        {
            var model = compilation.GetSemanticModel(extensionDeclaration.SyntaxTree);
            foreach (var declaration in extensionDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(declaration);
                if (symbol is not null && _extensionMethods.TryGetValue(symbol, out var spec))
                {
                    yield return (symbol, spec);
                }
            }
        }

        private static Dictionary<IMethodSymbol, MethodSpec> CreateAsyncMemberMethodSpecs(Compilation compilation)
        {
            var methods = new Dictionary<IMethodSymbol, MethodSpec>(SymbolEqualityComparer.Default);
            foreach (var metadataName in new[] { "Zio.FileSystemItem", "Zio.FileSystemEntry", "Zio.FileEntry", "Zio.DirectoryEntry" })
            {
                var type = compilation.GetTypeByMetadataName(metadataName)
                    ?? throw new InvalidOperationException($"Unable to find {metadataName}.");
                var modelByTree = type.DeclaringSyntaxReferences
                    .Select(static reference => reference.GetSyntax())
                    .OfType<TypeDeclarationSyntax>()
                    .Select(declaration => (Declaration: declaration, Model: compilation.GetSemanticModel(declaration.SyntaxTree)));
                foreach (var (declaration, model) in modelByTree)
                {
                    foreach (var methodDeclaration in declaration.Members.OfType<MethodDeclarationSyntax>())
                    {
                        if (!ShouldGenerateAsyncMemberMethod(methodDeclaration))
                        {
                            continue;
                        }

                        var symbol = model.GetDeclaredSymbol(methodDeclaration);
                        if (symbol is not null)
                        {
                            methods[symbol] = MethodSpec.FromMemberMethod(symbol, methodDeclaration);
                        }
                    }
                }
            }

            return methods;
        }

        private static bool ShouldGenerateAsyncMemberMethod(MethodDeclarationSyntax method)
        {
            var name = method.Identifier.ValueText;
            return name is "Open" or "Exists" or "ReadAllText"
                or "Delete" or "CopyTo" or "Create" or "MoveTo" or "ReplaceTo" or "WriteAllText" or "AppendAllText" or "ReadAllLines" or "ReadAllBytes" or "WriteAllBytes"
                or "CreateSubdirectory" or "EnumerateDirectories" or "EnumerateFiles" or "EnumerateEntries" or "EnumerateItems";
        }

        private static MethodSpec CreateExtensionSpec(IMethodSymbol symbol, MethodDeclarationSyntax declaration)
        {
            if (symbol.ReturnsVoid)
            {
                return new MethodSpec(symbol.Name, symbol, declaration, AsyncReturnKind.ValueTask, IdentifierName("ValueTask"), null, null);
            }

            if (symbol.ReturnType is INamedTypeSymbol { Name: "IEnumerable", TypeArguments.Length: 1 } enumerable
                && enumerable.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
            {
                var elementType = RewriteType(ParseTypeName(enumerable.TypeArguments[0].ToDisplayString(TypeFormat)));
                return new MethodSpec(
                    symbol.Name,
                    symbol,
                    declaration,
                    AsyncReturnKind.AsyncEnumerable,
                    GenericName("IAsyncEnumerable").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(elementType))),
                    null,
                    elementType);
            }

            var syncType = RewriteType(ParseTypeName(symbol.ReturnType.ToDisplayString(TypeFormat)));
            return new MethodSpec(
                symbol.Name,
                symbol,
                declaration,
                AsyncReturnKind.ValueTaskOfT,
                GenericName("ValueTask").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(syncType))),
                syncType,
                null);
        }
    }

    private sealed class AsyncSourceGenerator
    {
        private readonly Compilation _compilation;
        private readonly AsyncCatalog _catalog;
        private readonly ClassDeclarationSyntax _fileSystemDeclaration;
        private readonly ClassDeclarationSyntax _extensionDeclaration;

        public AsyncSourceGenerator(
            Compilation compilation,
            AsyncCatalog catalog,
            ClassDeclarationSyntax fileSystemDeclaration,
            ClassDeclarationSyntax extensionDeclaration)
        {
            _compilation = compilation;
            _catalog = catalog;
            _fileSystemDeclaration = fileSystemDeclaration;
            _extensionDeclaration = extensionDeclaration;
        }

        public string GenerateInterface()
        {
            var members = new List<MemberDeclarationSyntax>();
            var typeRewriter = new TypeReferenceRewriter(AsyncTypeNames);
            foreach (var method in _catalog.FileSystemMethods)
            {
                var generated = method.InterfaceDeclaration
                    .WithIdentifier(Identifier(method.AsyncName))
                    .WithReturnType(method.AsyncReturnType)
                    .WithParameterList(AddCancellationParameter(RemoveKnownOutParameters((ParameterListSyntax)typeRewriter.Visit(method.InterfaceDeclaration.ParameterList)!), includeDefault: true, enumeratorCancellation: false))
                    .WithLeadingTrivia(GetInterfaceDocumentation(method))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                    .WithBody(null)
                    .WithExpressionBody(null);
                members.Add(generated);
            }

            var type = InterfaceDeclaration("IFileSystemAsync")
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddBaseListTypes(SimpleBaseType(IdentifierName("IAsyncDisposable")))
                .WithLeadingTrivia(ParseLeadingTrivia("/// <summary>\n/// Async counterpart to <see cref=\"IFileSystem\"/>.\n/// </summary>\n/// <remarks>\n/// Async APIs are generated for the net10.0 target only. Scalar operations return <see cref=\"ValueTask\"/> or <see cref=\"ValueTask{TResult}\"/>; search operations return <see cref=\"IAsyncEnumerable{T}\"/>. Async file systems are disposed with <see cref=\"IAsyncDisposable.DisposeAsync\"/>.\n/// </remarks>\n"))
                .AddMembers(members.ToArray());

            return BuildFile("Zio", new[] { "System", "System.Collections.Generic", "System.IO", "System.Threading", "System.Threading.Tasks" }, type);
        }

        public string GenerateFileSystemAsyncBase()
        {
            var model = _compilation.GetSemanticModel(_fileSystemDeclaration.SyntaxTree);
            var members = new List<MemberDeclarationSyntax>();
            foreach (var member in _fileSystemDeclaration.Members)
            {
                switch (member)
                {
                    case MethodDeclarationSyntax method when _catalog.TryGetByName(method.Identifier.ValueText, out var spec):
                        members.Add(TransformFileSystemPublicMethod(model, method, spec));
                        break;
                    case MethodDeclarationSyntax method when TryGetFileSystemImpl(model, method, out var spec):
                        members.Add(TransformFileSystemImplMethod(model, method, spec));
                        break;
                    case DestructorDeclarationSyntax:
                        break;
                    case MethodDeclarationSyntax method when IsPublicDisposeMethod(method):
                        members.Add(CreatePublicDisposeAsyncMethod());
                        break;
                    case MethodDeclarationSyntax method when IsDisposeBoolMethod(method):
                        members.Add(CreateBaseDisposeAsyncMethod());
                        break;
                    case MethodDeclarationSyntax method when IsDisposeInternalMethod(method):
                        members.Add(CreateDisposeInternalAsyncMethod());
                        break;
                    default:
                        var rewrittenMember = (MemberDeclarationSyntax)new TypeReferenceRewriter(AsyncTypeNames).Visit(member)!;
                        members.Add(EnsureGeneratedMemberDocumentation(rewrittenMember));
                        break;
                }
            }

            var type = _fileSystemDeclaration
                .WithIdentifier(Identifier("FileSystemAsync"))
                .WithAttributeLists(default)
                .WithModifiers(RemoveModifier(_fileSystemDeclaration.Modifiers, SyntaxKind.PartialKeyword))
                .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(IdentifierName("IFileSystemAsync")))))
                .WithMembers(List(members))
                .WithLeadingTrivia(ParseLeadingTrivia("/// <summary>\n/// Abstract base class for asynchronous file systems.\n/// </summary>\n/// <remarks>\n/// Derive from this type when implementing the async route directly. Synchronous <see cref=\"FileSystem\"/> implementations are not required to implement <see cref=\"IFileSystemAsync\"/>.\n/// </remarks>\n"));

            return BuildFile(
                "Zio.FileSystems",
                new[] { "System", "System.Collections.Generic", "System.Diagnostics", "System.IO", "System.Runtime.CompilerServices", "System.Threading", "System.Threading.Tasks", "static Zio.FileSystemExceptionHelper" },
                type);
        }

        public string GenerateAsyncFileSystem(string sourceTypeName, string? extraGuard = null)
        {
            var sourceType = _compilation.GetTypeByMetadataName("Zio.FileSystems." + sourceTypeName)
                ?? throw new InvalidOperationException($"Unable to find Zio.FileSystems.{sourceTypeName}.");
            var sourceDeclaration = sourceType.DeclaringSyntaxReferences.Select(static reference => reference.GetSyntax()).OfType<ClassDeclarationSyntax>().FirstOrDefault()
                ?? throw new InvalidOperationException($"Unable to find syntax for {sourceTypeName}.");
            var asyncTypeName = sourceTypeName + "Async";
            var model = _compilation.GetSemanticModel(sourceDeclaration.SyntaxTree);
            var members = new List<MemberDeclarationSyntax>();
            var typeRewriter = new TypeReferenceRewriter(AsyncTypeNames);
            var syncContextRewriter = new SyncContextRewriter(model, _catalog, AsyncTypeNames);

            foreach (var member in sourceDeclaration.Members)
            {
                if (sourceTypeName == "PhysicalFileSystem" && IsFieldNamed(member, "IsOnWindows"))
                {
                    continue;
                }

                switch (member)
                {
                    case ConstructorDeclarationSyntax constructor:
                        members.Add(TransformConstructor(constructor, asyncTypeName, syncContextRewriter));
                        break;
                    case MethodDeclarationSyntax method when IsDisposeBoolMethod(method):
                        members.Add(EnsureGeneratedMemberDocumentation(TransformDisposeOverrideMethod(model, method)));
                        break;
                    case MethodDeclarationSyntax method when TryGetFileSystemImpl(model, method, out var spec):
                        members.Add(TransformFileSystemOverrideMethod(model, method, spec, typeRewriter));
                        break;
                    default:
                        members.Add((MemberDeclarationSyntax)syncContextRewriter.Visit(member)!);
                        break;
                }
            }

            if (sourceTypeName == "PhysicalFileSystem")
            {
                members.Insert(2, ParseMemberDeclaration("internal static readonly bool IsOnWindows = CheckIsOnWindows();")!);
                members.Insert(3, ParseMemberDeclaration("""
                    private static bool CheckIsOnWindows()
                    {
                        switch (Environment.OSVersion.Platform)
                        {
                            case PlatformID.Xbox:
                            case PlatformID.Win32NT:
                            case PlatformID.Win32S:
                            case PlatformID.Win32Windows:
                            case PlatformID.WinCE:
                                return true;
                        }

                        return false;
                    }
                    """)!);
            }

            var type = sourceDeclaration
                .WithIdentifier(Identifier(asyncTypeName))
                .WithModifiers(RemoveModifier(sourceDeclaration.Modifiers, SyntaxKind.PartialKeyword))
                .WithBaseList(RewriteAsyncBaseList(sourceDeclaration.BaseList))
                .WithMembers(List(members))
                .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>\n/// Async counterpart to <see cref=\"{sourceTypeName}\"/>.\n/// </summary>\n"));

            type = AsyncImplementationRewriter.Rewrite(sourceTypeName, type);

            var usings = new List<string> { "System", "System.Collections.Generic", "System.Diagnostics", "System.Diagnostics.CodeAnalysis", "System.IO", "System.Linq", "System.Runtime.CompilerServices", "System.Threading", "System.Threading.Tasks", "static Zio.FileSystemExceptionHelper" };
            if (sourceTypeName == "ZipArchiveFileSystem")
            {
                usings.Add("System.IO.Compression");
            }

            return BuildFile("Zio.FileSystems", usings, type, extraGuard);
        }

        public string GenerateAsyncSupportType(string metadataName)
        {
            var sourceType = _compilation.GetTypeByMetadataName(metadataName)
                ?? throw new InvalidOperationException($"Unable to find {metadataName}.");
            var sourceDeclaration = sourceType.DeclaringSyntaxReferences.Select(static reference => reference.GetSyntax()).OfType<TypeDeclarationSyntax>().FirstOrDefault()
                ?? throw new InvalidOperationException($"Unable to find syntax for {metadataName}.");
            if (!AsyncTypeNames.TryGetValue(sourceType.Name, out var asyncTypeName))
            {
                throw new InvalidOperationException($"No async type name mapping exists for {metadataName}.");
            }

            var rewriter = new TypeReferenceRewriter(AsyncTypeNames);
            var members = new List<MemberDeclarationSyntax>();
            foreach (var member in sourceDeclaration.Members)
            {
                var rewritten = (MemberDeclarationSyntax)rewriter.Visit(member)!;
                if (rewritten is ConstructorDeclarationSyntax constructor)
                {
                    rewritten = constructor.WithIdentifier(Identifier(asyncTypeName));
                }
                else if (rewritten is DestructorDeclarationSyntax destructor)
                {
                    rewritten = destructor.WithIdentifier(Identifier(asyncTypeName));
                }

                members.Add(EnsureGeneratedMemberDocumentation(RewriteDocumentation(rewritten)));
            }

            var type = ((TypeDeclarationSyntax)rewriter.Visit(sourceDeclaration)!)
                .WithIdentifier(Identifier(asyncTypeName))
                .WithMembers(List(members))
                .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>\n/// Async counterpart to <see cref=\"{sourceType.Name}\"/>.\n/// </summary>\n"));

            var @namespace = sourceType.ContainingNamespace.ToDisplayString();
            var usings = @namespace == "Zio"
                ? new[] { "System", "System.Collections.Generic", "System.IO", "System.Text", "System.Threading", "System.Threading.Tasks", "System.Runtime.CompilerServices", "Zio.FileSystems" }
                : new[] { "System", "System.Collections.Concurrent", "System.Collections.Generic", "System.IO", "System.Linq", "System.Threading" };
            return BuildFile(@namespace, usings, type);
        }

        public string GenerateSearchPredicateAsync()
        {
            var type = DelegateDeclaration(PredefinedType(Token(SyntaxKind.BoolKeyword)), Identifier("SearchPredicateAsync"))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithParameterList(ParameterList(SingletonSeparatedList(
                    Parameter(Identifier("item"))
                        .AddModifiers(Token(SyntaxKind.RefKeyword))
                        .WithType(IdentifierName("FileSystemItemAsync")))))
                .WithLeadingTrivia(ParseLeadingTrivia("/// <summary>\n/// Used by <see cref=\"IFileSystemAsync.EnumerateItemsAsync\"/>.\n/// </summary>\n/// <param name=\"item\">The file system item to filter.</param>\n/// <returns><c>true</c> if the item should be kept; otherwise <c>false</c>.</returns>\n"));

            return BuildFile("Zio", new[] { "System" }, type);
        }

        public string GenerateAsyncEntryType(string metadataName)
        {
            var sourceType = _compilation.GetTypeByMetadataName(metadataName)
                ?? throw new InvalidOperationException($"Unable to find {metadataName}.");
            var sourceDeclaration = sourceType.DeclaringSyntaxReferences.Select(static reference => reference.GetSyntax()).OfType<TypeDeclarationSyntax>().FirstOrDefault()
                ?? throw new InvalidOperationException($"Unable to find syntax for {metadataName}.");
            if (!AsyncTypeNames.TryGetValue(sourceType.Name, out var asyncTypeName))
            {
                throw new InvalidOperationException($"No async type name mapping exists for {metadataName}.");
            }

            var model = _compilation.GetSemanticModel(sourceDeclaration.SyntaxTree);
            var typeRewriter = new TypeReferenceRewriter(AsyncTypeNames);
            var members = new List<MemberDeclarationSyntax>();
            foreach (var member in sourceDeclaration.Members)
            {
                switch (member)
                {
                    case ConstructorDeclarationSyntax constructor:
                        members.Add(EnsureGeneratedMemberDocumentation(((ConstructorDeclarationSyntax)typeRewriter.Visit(constructor)!).WithIdentifier(Identifier(asyncTypeName))));
                        break;
                    case MethodDeclarationSyntax method when TryGetAsyncMemberMethod(model, method, out var spec):
                        members.Add(TransformAsyncEntryMethod(model, method, spec, typeRewriter));
                        break;
                    case PropertyDeclarationSyntax property:
                        members.AddRange(TransformAsyncEntryProperty(model, property, typeRewriter));
                        break;
                    default:
                        members.Add(EnsureGeneratedMemberDocumentation(RewriteDocumentation((MemberDeclarationSyntax)typeRewriter.Visit(member)!)));
                        break;
                }
            }

            var type = ((TypeDeclarationSyntax)typeRewriter.Visit(sourceDeclaration)!)
                .WithIdentifier(Identifier(asyncTypeName))
                .WithMembers(List(members))
                .WithLeadingTrivia(ParseLeadingTrivia($"/// <summary>\n/// Async counterpart to <see cref=\"{sourceType.Name}\"/>.\n/// </summary>\n"));

            return BuildFile("Zio", new[] { "System", "System.Collections.Generic", "System.IO", "System.Text", "System.Threading", "System.Threading.Tasks", "Zio.FileSystems" }, type);
        }

        private bool TryGetAsyncMemberMethod(SemanticModel model, MethodDeclarationSyntax method, out MethodSpec spec)
        {
            spec = null!;
            var symbol = model.GetDeclaredSymbol(method);
            return symbol is not null && _catalog.TryGetMemberMethod(symbol, out spec);
        }

        private MethodDeclarationSyntax TransformAsyncEntryMethod(SemanticModel model, MethodDeclarationSyntax method, MethodSpec spec, TypeReferenceRewriter typeRewriter)
        {
            var rewriter = new AsyncBodyRewriter(model, _catalog, awaitScalarCalls: true, wrapFileSystemEntries: false, AsyncTypeNames);
            var transformed = method
                .WithIdentifier(Identifier(spec.AsyncName))
                .WithReturnType(spec.AsyncReturnType)
                .WithParameterList(AddCancellationParameter((ParameterListSyntax)typeRewriter.Visit(method.ParameterList)!, includeDefault: true, enumeratorCancellation: false))
                .WithLeadingTrivia(InheritdocTrivia());

            if (method.Body is not null)
            {
                transformed = transformed.WithBody((BlockSyntax)rewriter.Visit(method.Body)!);
            }

            if (method.ExpressionBody is not null)
            {
                transformed = transformed.WithExpressionBody((ArrowExpressionClauseSyntax)rewriter.Visit(method.ExpressionBody)!);
            }

            if (!spec.ReturnsAsyncEnumerable && !method.Modifiers.Any(SyntaxKind.AbstractKeyword) && (method.Body is not null || method.ExpressionBody is not null))
            {
                transformed = transformed.WithModifiers(EnsureModifier(transformed.Modifiers, SyntaxKind.AsyncKeyword));
            }

            return transformed;
        }

        private IEnumerable<MemberDeclarationSyntax> TransformAsyncEntryProperty(SemanticModel model, PropertyDeclarationSyntax property, TypeReferenceRewriter typeRewriter)
        {
            var name = property.Identifier.ValueText;
            if (name == "Attributes")
            {
                yield return TransformPropertyGetterToAsyncMethod(model, property, "GetAttributesAsync", IdentifierName("FileAttributes"));
                yield return TransformPropertySetterToAsyncMethod(model, property, "SetAttributesAsync", IdentifierName("FileAttributes"), "attributes");
                yield break;
            }

            if (name is "CreationTime" or "LastAccessTime" or "LastWriteTime")
            {
                yield return TransformPropertyGetterToAsyncMethod(model, property, "Get" + name + "Async", IdentifierName("DateTime"));
                yield return TransformPropertySetterToAsyncMethod(model, property, "Set" + name + "Async", IdentifierName("DateTime"), "time");
                yield break;
            }

            if (name == "Exists")
            {
                yield return TransformPropertyGetterToAsyncMethod(model, property, "ExistsAsync", GenericName("ValueTask").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(PredefinedType(Token(SyntaxKind.BoolKeyword))))));
                yield break;
            }

            if (name == "IsReadOnly")
            {
                yield return TransformPropertyGetterToAsyncMethod(model, property, "IsReadOnlyAsync", GenericName("ValueTask").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(PredefinedType(Token(SyntaxKind.BoolKeyword))))));
                yield break;
            }

            if (name == "Length")
            {
                yield return TransformPropertyGetterToAsyncMethod(model, property, "GetLengthAsync", GenericName("ValueTask").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(PredefinedType(Token(SyntaxKind.LongKeyword))))));
                yield break;
            }

            yield return EnsureGeneratedMemberDocumentation(RewriteDocumentation((MemberDeclarationSyntax)typeRewriter.Visit(property)!));
        }

        private MethodDeclarationSyntax TransformPropertyGetterToAsyncMethod(SemanticModel model, PropertyDeclarationSyntax property, string methodName, TypeSyntax returnType)
        {
            var valueTaskReturnType = returnType is GenericNameSyntax { Identifier.ValueText: "ValueTask" }
                ? returnType
                : GenericName("ValueTask").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(returnType)));
            var method = MethodDeclaration(valueTaskReturnType, Identifier(methodName))
                .WithModifiers(property.Modifiers)
                .WithParameterList(AddCancellationParameter(ParameterList(), includeDefault: true, enumeratorCancellation: false))
                .WithLeadingTrivia(InheritdocTrivia());

            if (property.Modifiers.Any(SyntaxKind.AbstractKeyword))
            {
                return method.WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
            }

            var expression = GetPropertyGetterExpression(property)
                ?? throw new InvalidOperationException($"Unable to find getter expression for {property.Identifier.ValueText}.");
            var rewriter = new AsyncBodyRewriter(model, _catalog, awaitScalarCalls: true, wrapFileSystemEntries: false, AsyncTypeNames);
            return method
                .WithModifiers(EnsureModifier(method.Modifiers, SyntaxKind.AsyncKeyword))
                .WithBody(Block(ReturnStatement((ExpressionSyntax)rewriter.Visit(expression)!)));
        }

        private MethodDeclarationSyntax TransformPropertySetterToAsyncMethod(SemanticModel model, PropertyDeclarationSyntax property, string methodName, TypeSyntax valueType, string parameterName)
        {
            var method = MethodDeclaration(IdentifierName("ValueTask"), Identifier(methodName))
                .WithModifiers(property.Modifiers)
                .WithParameterList(AddCancellationParameter(ParameterList(SingletonSeparatedList(Parameter(Identifier(parameterName)).WithType(valueType))), includeDefault: true, enumeratorCancellation: false))
                .WithLeadingTrivia(InheritdocTrivia());

            if (property.Modifiers.Any(SyntaxKind.AbstractKeyword))
            {
                return method.WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
            }

            var expression = GetPropertySetterExpression(property)
                ?? throw new InvalidOperationException($"Unable to find setter expression for {property.Identifier.ValueText}.");
            var rewriter = new AsyncBodyRewriter(model, _catalog, awaitScalarCalls: true, wrapFileSystemEntries: false, AsyncTypeNames);
            var rewrittenExpression = (ExpressionSyntax)rewriter.Visit(expression)!;
            rewrittenExpression = (ExpressionSyntax)new IdentifierRenamer("value", parameterName).Visit(rewrittenExpression)!;
            return method
                .WithModifiers(EnsureModifier(method.Modifiers, SyntaxKind.AsyncKeyword))
                .WithBody(Block(ExpressionStatement(rewrittenExpression)));
        }

        private static ExpressionSyntax? GetPropertyGetterExpression(PropertyDeclarationSyntax property)
        {
            if (property.ExpressionBody is not null)
            {
                return property.ExpressionBody.Expression;
            }

            return property.AccessorList?.Accessors.FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) switch
            {
                { ExpressionBody.Expression: { } expression } => expression,
                { Body.Statements.Count: 1 } accessor when accessor.Body.Statements[0] is ReturnStatementSyntax { Expression: { } expression } => expression,
                _ => null
            };
        }

        private static ExpressionSyntax? GetPropertySetterExpression(PropertyDeclarationSyntax property)
        {
            var setter = property.AccessorList?.Accessors.FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration));
            return setter switch
            {
                { ExpressionBody.Expression: { } setterExpression } => setterExpression,
                { Body.Statements.Count: 1 } accessor when accessor.Body.Statements[0] is ExpressionStatementSyntax { Expression: { } setterExpression } => setterExpression,
                _ => null
            };
        }

        private static MemberDeclarationSyntax RewriteDocumentation(MemberDeclarationSyntax member)
        {
            var leadingTrivia = member.GetLeadingTrivia().ToFullString();
            if (leadingTrivia.Length == 0)
            {
                return member;
            }

            foreach (var (syncName, asyncName) in AsyncTypeNames.OrderByDescending(static pair => pair.Key.Length))
            {
                leadingTrivia = Regex.Replace(leadingTrivia, $@"\b{Regex.Escape(syncName)}\b(?!Async)", asyncName);
            }

            return member.WithLeadingTrivia(ParseLeadingTrivia(leadingTrivia));
        }

        private sealed class IdentifierRenamer : CSharpSyntaxRewriter
        {
            private readonly string _oldName;
            private readonly string _newName;

            public IdentifierRenamer(string oldName, string newName)
            {
                _oldName = oldName;
                _newName = newName;
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                return node.Identifier.ValueText == _oldName
                    ? node.WithIdentifier(Identifier(node.Identifier.LeadingTrivia, _newName, node.Identifier.TrailingTrivia))
                    : node;
            }
        }

        private static bool IsFieldNamed(MemberDeclarationSyntax member, string name)
        {
            return member is FieldDeclarationSyntax field
                && field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == name);
        }

        private bool TryGetFileSystemImpl(SemanticModel model, MethodDeclarationSyntax method, out MethodSpec spec)
        {
            spec = null!;
            var symbol = model.GetDeclaredSymbol(method);
            return symbol is not null && _catalog.TryGetFileSystemImpl(symbol, out spec);
        }

        private static bool IsPublicDisposeMethod(MethodDeclarationSyntax method)
        {
            return method.Identifier.ValueText == "Dispose" && method.ParameterList.Parameters.Count == 0;
        }

        private static bool IsDisposeBoolMethod(MethodDeclarationSyntax method)
        {
            return method.Identifier.ValueText == "Dispose"
                && method.ParameterList.Parameters.Count == 1
                && method.ParameterList.Parameters[0].Identifier.ValueText == "disposing";
        }

        private static bool IsDisposeInternalMethod(MethodDeclarationSyntax method)
        {
            return method.Identifier.ValueText == "DisposeInternal" && method.ParameterList.Parameters.Count == 1;
        }

        private static bool ContainsAwait(SyntaxNode node)
        {
            return node.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any();
        }

        private static MethodDeclarationSyntax CreatePublicDisposeAsyncMethod()
        {
            return (MethodDeclarationSyntax)ParseMemberDeclaration("""
                /// <inheritdoc />
                public async ValueTask DisposeAsync()
                {
                    await DisposeInternalAsync(true).ConfigureAwait(false);
                    GC.SuppressFinalize(this);
                }
                """)!;
        }

        private static MethodDeclarationSyntax CreateBaseDisposeAsyncMethod()
        {
            return (MethodDeclarationSyntax)ParseMemberDeclaration("""
                /// <summary>
                /// Asynchronously releases unmanaged and - optionally - managed resources.
                /// </summary>
                /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
                protected virtual ValueTask DisposeAsync(bool disposing) => default;
                """)!;
        }

        private static MethodDeclarationSyntax CreateDisposeInternalAsyncMethod()
        {
            return (MethodDeclarationSyntax)ParseMemberDeclaration("""
                private async ValueTask DisposeInternalAsync(bool disposing)
                {
                    if (!IsDisposed)
                    {
                        AssertNotDisposed();
                        IsDisposing = true;
                        await DisposeAsync(disposing).ConfigureAwait(false);
                        IsDisposed = true;
                    }
                }
                """)!;
        }

        private static MethodDeclarationSyntax TransformDisposeOverrideMethod(SemanticModel model, MethodDeclarationSyntax method)
        {
            var rewriter = new AsyncDisposeBodyRewriter(model, AsyncTypeNames);
            var transformed = method
                .WithIdentifier(Identifier(method.Identifier.LeadingTrivia, "DisposeAsync", method.Identifier.TrailingTrivia))
                .WithReturnType(IdentifierName("ValueTask"));

            if (method.Body is not null)
            {
                var body = (BlockSyntax)rewriter.Visit(method.Body)!;
                if (ContainsAwait(body))
                {
                    transformed = transformed
                        .WithModifiers(EnsureModifier(transformed.Modifiers, SyntaxKind.AsyncKeyword))
                        .WithBody(body);
                }
                else
                {
                    body = (BlockSyntax)new VoidReturnToValueTaskReturnRewriter().Visit(body)!;
                    if (body.Statements.LastOrDefault() is not ReturnStatementSyntax and not ThrowStatementSyntax)
                    {
                        body = body.WithStatements(body.Statements.Add(ReturnStatement(LiteralExpression(SyntaxKind.DefaultLiteralExpression))));
                    }

                    transformed = transformed.WithBody(body);
                }
            }

            if (method.ExpressionBody is not null)
            {
                transformed = transformed
                    .WithExpressionBody((ArrowExpressionClauseSyntax)rewriter.Visit(method.ExpressionBody)!)
                    .WithSemicolonToken(method.SemicolonToken);
            }

            return transformed;
        }

        public string GenerateExtensions()
        {
            var model = _compilation.GetSemanticModel(_extensionDeclaration.SyntaxTree);
            var members = new List<MemberDeclarationSyntax>();
            foreach (var (symbol, spec) in _catalog.ExtensionMethodsInSourceOrder(_extensionDeclaration, _compilation))
            {
                var declaration = (MethodDeclarationSyntax)symbol.DeclaringSyntaxReferences[0].GetSyntax();
                members.Add(TransformExtensionMethod(model, declaration, spec));
            }

            var type = ClassDeclaration("FileSystemAsyncExtensions")
                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                .WithLeadingTrivia(ParseLeadingTrivia("/// <summary>\n/// Async extension methods for <see cref=\"IFileSystemAsync\"/>.\n/// </summary>\n"))
                .AddMembers(members.ToArray());

            return BuildFile(
                "Zio",
                new[] { "System", "System.Collections.Generic", "System.IO", "System.Runtime.CompilerServices", "System.Text", "System.Threading", "System.Threading.Tasks", "Zio.FileSystems", "static Zio.FileSystemExceptionHelper" },
                type);
        }

        private static ConstructorDeclarationSyntax TransformConstructor(
            ConstructorDeclarationSyntax constructor,
            string asyncTypeName,
            SyncContextRewriter rewriter)
        {
            var rewrittenConstructor = (ConstructorDeclarationSyntax)rewriter.Visit(constructor)!;
            return rewrittenConstructor.WithIdentifier(Identifier(asyncTypeName));
        }

        private static BaseListSyntax RewriteAsyncBaseList(BaseListSyntax? baseList)
        {
            if (baseList is null)
            {
                return BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(IdentifierName("FileSystemAsync"))));
            }

            var rewriter = new TypeReferenceRewriter(AsyncTypeNames);
            var rewritten = (BaseListSyntax)rewriter.Visit(baseList)!;
            if (rewritten.Types.Count == 0)
            {
                return BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(IdentifierName("FileSystemAsync"))));
            }

            return rewritten;
        }

        private MethodDeclarationSyntax TransformFileSystemOverrideMethod(
            SemanticModel model,
            MethodDeclarationSyntax method,
            MethodSpec spec,
            TypeReferenceRewriter typeRewriter)
        {
            if (spec.Kind == AsyncReturnKind.TryResolveLinkTarget)
            {
                return TransformTryResolveOverrideMethod(model, method, spec, typeRewriter);
            }

            if (spec.IsResolvePath && method.ExpressionBody is not null)
            {
                return CreateComposeResolvePathOverride(method, spec, typeRewriter);
            }

            var rewriter = new AsyncBodyRewriter(model, _catalog, awaitScalarCalls: true, wrapFileSystemEntries: false, AsyncTypeNames);
            var transformed = method
                .WithIdentifier(Identifier(spec.AsyncImplName))
                .WithReturnType(spec.AsyncReturnType)
                .WithParameterList(AddCancellationParameter(RemoveKnownOutParameters((ParameterListSyntax)typeRewriter.Visit(method.ParameterList)!), includeDefault: false, enumeratorCancellation: spec.ReturnsAsyncEnumerable))
                .WithLeadingTrivia(InheritdocTrivia());

            if (method.Body is not null)
            {
                var body = (BlockSyntax)rewriter.Visit(method.Body)!;
                if (spec.ReturnsAsyncEnumerable)
                {
                    body = (BlockSyntax)new AsyncEnumerableReturnRewriter().Visit(body)!;
                }

                transformed = transformed.WithBody(body);
            }

            if (method.ExpressionBody is not null)
            {
                if (spec.ReturnsAsyncEnumerable)
                {
                    var expression = (ExpressionSyntax)rewriter.Visit(method.ExpressionBody.Expression)!;
                    transformed = transformed
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(CreateAsyncEnumerableAdapterBody(expression));
                }
                else
                {
                    transformed = transformed.WithExpressionBody((ArrowExpressionClauseSyntax)rewriter.Visit(method.ExpressionBody)!);
                }
            }

            if (!spec.ReturnsAsyncEnumerable)
            {
                transformed = transformed.WithModifiers(EnsureModifier(transformed.Modifiers, SyntaxKind.AsyncKeyword));
            }
            else
            {
                transformed = transformed.WithModifiers(EnsureModifier(transformed.Modifiers, SyntaxKind.AsyncKeyword));
            }

            return transformed;
        }

        private static MethodDeclarationSyntax CreateComposeResolvePathOverride(
            MethodDeclarationSyntax method,
            MethodSpec spec,
            TypeReferenceRewriter typeRewriter)
        {
            return method
                .WithIdentifier(Identifier(spec.AsyncImplName))
                .WithReturnType(spec.AsyncReturnType)
                .WithParameterList(AddCancellationParameter(RemoveKnownOutParameters((ParameterListSyntax)typeRewriter.Visit(method.ParameterList)!), includeDefault: false, enumeratorCancellation: false))
                .WithLeadingTrivia(InheritdocTrivia())
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithModifiers(EnsureModifier(method.Modifiers, SyntaxKind.AsyncKeyword))
                .WithBody(Block(
                    LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
                        .AddVariables(VariableDeclarator("fallback")
                            .WithInitializer(EqualsValueClause(IdentifierName("Fallback"))))),
                    IfStatement(
                        IsPatternExpression(IdentifierName("fallback"), ConstantPattern(LiteralExpression(SyntaxKind.NullLiteralExpression))),
                        Block(ReturnStatement(AwaitConfigureAwait(InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("base"), IdentifierName(spec.AsyncImplName)),
                            ArgumentList(SeparatedList(new[]
                            {
                                Argument(IdentifierName("path")),
                                Argument(IdentifierName("cancellationToken")).WithNameColon(NameColon(IdentifierName("cancellationToken")))
                            }))))))),
                    ReturnStatement(AwaitConfigureAwait(InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("fallback"), IdentifierName(spec.AsyncName)),
                        ArgumentList(SeparatedList(new[]
                        {
                            Argument(InvocationExpression(IdentifierName("ConvertPathToDelegate"), ArgumentList(SingletonSeparatedList(Argument(IdentifierName("path")))))),
                            Argument(IdentifierName("cancellationToken")).WithNameColon(NameColon(IdentifierName("cancellationToken")))
                        })))))));
        }

        private MethodDeclarationSyntax TransformTryResolveOverrideMethod(
            SemanticModel model,
            MethodDeclarationSyntax method,
            MethodSpec spec,
            TypeReferenceRewriter typeRewriter)
        {
            var rewriter = new AsyncBodyRewriter(model, _catalog, awaitScalarCalls: true, wrapFileSystemEntries: false, AsyncTypeNames);
            var transformed = method
                .WithIdentifier(Identifier(spec.AsyncImplName))
                .WithReturnType(spec.AsyncReturnType)
                .WithParameterList(AddCancellationParameter(RemoveKnownOutParameters((ParameterListSyntax)typeRewriter.Visit(method.ParameterList)!), includeDefault: false, enumeratorCancellation: false))
                .WithLeadingTrivia(InheritdocTrivia())
                .WithModifiers(EnsureModifier(method.Modifiers, SyntaxKind.AsyncKeyword));

            if (method.Body is not null)
            {
                var rewrittenBody = (BlockSyntax)rewriter.Visit(method.Body)!;
                rewrittenBody = (BlockSyntax)new TryResolveReturnRewriter().Visit(rewrittenBody)!;
                rewrittenBody = rewrittenBody.WithStatements(rewrittenBody.Statements.Insert(0,
                    LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
                        .AddVariables(VariableDeclarator(Identifier("resolvedPath"))
                            .WithInitializer(EqualsValueClause(DefaultExpression(IdentifierName("UPath"))))))));
                transformed = transformed.WithBody(rewrittenBody);
            }

            if (method.ExpressionBody is not null)
            {
                var expression = (ExpressionSyntax)rewriter.Visit(method.ExpressionBody.Expression)!;
                transformed = transformed
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(Block(ReturnStatement(TupleExpression(SeparatedList(new[]
                    {
                        Argument(expression),
                        Argument(DefaultExpression(IdentifierName("UPath")))
                    })))));
            }

            return transformed;
        }

        private MethodDeclarationSyntax TransformFileSystemPublicMethod(SemanticModel model, MethodDeclarationSyntax method, MethodSpec spec)
        {
            var typeRewriter = new TypeReferenceRewriter(AsyncTypeNames);
            if (spec.Kind == AsyncReturnKind.TryResolveLinkTarget)
            {
                return MethodDeclaration(spec.AsyncReturnType, Identifier(spec.AsyncName))
                    .WithModifiers(method.Modifiers)
                    .WithParameterList(AddCancellationParameter(RemoveKnownOutParameters((ParameterListSyntax)typeRewriter.Visit(method.ParameterList)!), includeDefault: true, enumeratorCancellation: false))
                    .WithBody(Block(
                        ExpressionStatement(InvocationExpression(IdentifierName("AssertNotDisposed"))),
                        CancellationThrowStatement(),
                        ReturnStatement(AwaitConfigureAwait(InvocationExpression(
                            IdentifierName("TryResolveLinkTargetAsyncImpl"),
                            ArgumentList(SeparatedList(new[]
                            {
                                Argument(InvocationExpression(IdentifierName("ValidatePath"), ArgumentList(SingletonSeparatedList(Argument(IdentifierName("linkPath")))))),
                                Argument(IdentifierName("cancellationToken"))
                            })))))))
                    .WithModifiers(EnsureModifier(method.Modifiers, SyntaxKind.AsyncKeyword))
                    .WithLeadingTrivia(InheritdocTrivia());
            }

            var bodyRewriter = new AsyncBodyRewriter(model, _catalog, awaitScalarCalls: true, wrapFileSystemEntries: false, AsyncTypeNames);
            var transformed = method
                .WithIdentifier(Identifier(spec.AsyncName))
                .WithReturnType(spec.AsyncReturnType)
                .WithParameterList(AddCancellationParameter((ParameterListSyntax)typeRewriter.Visit(method.ParameterList)!, includeDefault: true, enumeratorCancellation: false))
                .WithLeadingTrivia(InheritdocTrivia());

            if (method.Body is not null)
            {
                transformed = transformed.WithBody(InsertCancellationThrow((BlockSyntax)bodyRewriter.Visit(method.Body)!));
            }

            if (method.ExpressionBody is not null)
            {
                transformed = transformed.WithExpressionBody((ArrowExpressionClauseSyntax)bodyRewriter.Visit(method.ExpressionBody)!);
            }

            if (!spec.ReturnsAsyncEnumerable)
            {
                transformed = transformed.WithModifiers(EnsureModifier(transformed.Modifiers, SyntaxKind.AsyncKeyword));
            }

            return transformed;
        }

        private MethodDeclarationSyntax TransformFileSystemImplMethod(SemanticModel model, MethodDeclarationSyntax method, MethodSpec spec)
        {
            var typeRewriter = new TypeReferenceRewriter(AsyncTypeNames);
            var transformed = method
                .WithIdentifier(Identifier(spec.AsyncImplName))
                .WithReturnType(spec.AsyncReturnType)
                .WithParameterList(AddCancellationParameter(RemoveKnownOutParameters((ParameterListSyntax)typeRewriter.Visit(method.ParameterList)!), includeDefault: false, enumeratorCancellation: false))
                .WithLeadingTrivia(InheritdocTrivia());

            if (method.Body is not null)
            {
                transformed = transformed.WithBody((BlockSyntax)new ValueTaskBodyRewriter(spec).Visit(method.Body)!);
            }

            if (method.ExpressionBody is not null)
            {
                transformed = transformed.WithExpressionBody((ArrowExpressionClauseSyntax)new ValueTaskBodyRewriter(spec).Visit(method.ExpressionBody)!);
            }

            return transformed;
        }

        private MethodDeclarationSyntax TransformExtensionMethod(SemanticModel model, MethodDeclarationSyntax method, MethodSpec spec)
        {
            var typeRewriter = new TypeReferenceRewriter(AsyncTypeNames);
            var rewriter = new AsyncBodyRewriter(model, _catalog, awaitScalarCalls: true, wrapFileSystemEntries: false, AsyncTypeNames);
            var transformedParameters = AddCancellationParameter(
                (ParameterListSyntax)typeRewriter.Visit(method.ParameterList)!,
                includeDefault: true,
                enumeratorCancellation: spec.ReturnsAsyncEnumerable && ContainsYield(method));
            var transformed = method
                .WithIdentifier(Identifier(spec.AsyncName))
                .WithReturnType(spec.AsyncReturnType)
                .WithParameterList(transformedParameters)
                .WithLeadingTrivia(InheritdocTrivia());

            if (method.Body is not null)
            {
                transformed = transformed.WithBody((BlockSyntax)rewriter.Visit(method.Body)!);
            }

            if (method.ExpressionBody is not null)
            {
                transformed = transformed.WithExpressionBody((ArrowExpressionClauseSyntax)rewriter.Visit(method.ExpressionBody)!);
            }

            if (!spec.ReturnsAsyncEnumerable)
            {
                transformed = transformed.WithModifiers(EnsureModifier(transformed.Modifiers, SyntaxKind.AsyncKeyword));
            }
            else if (ContainsYield(method))
            {
                transformed = transformed.WithModifiers(EnsureModifier(transformed.Modifiers, SyntaxKind.AsyncKeyword));
            }

            return transformed;
        }

        private MemberDeclarationSyntax GenerateSyncBackedMethod(MethodSpec spec)
        {
            var parameters = AddCancellationParameter(
                RemoveKnownOutParameters(spec.InterfaceDeclaration.ParameterList),
                includeDefault: true,
                enumeratorCancellation: spec.ReturnsAsyncEnumerable);
            var syncInvocation = InvocationExpression(IdentifierName(spec.Name), ArgumentList(SyncArguments(spec, includeOutVar: false)));

            var method = MethodDeclaration(spec.AsyncReturnType, Identifier(spec.AsyncName))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithParameterList(parameters)
                .WithLeadingTrivia(InheritdocTrivia());

            return spec.Kind switch
            {
                AsyncReturnKind.ValueTask => method.WithBody(Block(
                    CancellationThrowStatement(),
                    ExpressionStatement(syncInvocation),
                    ReturnStatement(LiteralExpression(SyntaxKind.DefaultLiteralExpression)))),
                AsyncReturnKind.ValueTaskOfT when spec.IsResolvePath => method.WithBody(Block(
                    CancellationThrowStatement(),
                    ExpressionStatement(syncInvocation),
                    ReturnValueTask(spec.AsyncReturnType, TupleExpression(SeparatedList(new[]
                    {
                        Argument(ThisExpression()),
                        Argument(IdentifierName("path"))
                    }))))),
                AsyncReturnKind.ValueTaskOfT => method.WithBody(Block(
                    CancellationThrowStatement(),
                    ReturnValueTask(spec.AsyncReturnType, syncInvocation))),
                AsyncReturnKind.TryResolveLinkTarget => method.WithBody(Block(
                    CancellationThrowStatement(),
                    LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
                        .AddVariables(VariableDeclarator(Identifier("success"))
                            .WithInitializer(EqualsValueClause(InvocationExpression(IdentifierName(spec.Name), ArgumentList(SyncArguments(spec, includeOutVar: true))))))),
                    ReturnValueTask(spec.AsyncReturnType, TupleExpression(SeparatedList(new[]
                    {
                        Argument(IdentifierName("success")),
                        Argument(IdentifierName("resolvedPath"))
                    }))))),
                AsyncReturnKind.AsyncEnumerable => method
                    .WithModifiers(EnsureModifier(method.Modifiers, SyntaxKind.AsyncKeyword))
                    .WithBody(Block(ForEachStatement(
                        IdentifierName("var"),
                        Identifier("item"),
                        syncInvocation,
                        Block(
                            CancellationThrowStatement(),
                            YieldStatement(SyntaxKind.YieldReturnStatement, IdentifierName("item")),
                            ExpressionStatement(AwaitConfigureAwait(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("Task"), IdentifierName("CompletedTask")))))))),
                _ => throw new InvalidOperationException($"Unsupported async return kind for {spec.Name}.")
            };
        }
    }

    private sealed class AsyncDisposeBodyRewriter : TypeReferenceRewriter
    {
        private readonly SemanticModel _model;

        public AsyncDisposeBodyRewriter(SemanticModel model, IReadOnlyDictionary<string, string> typeNames)
            : base(typeNames)
        {
            _model = model;
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (TryRewriteConditionalDispose(node.Expression, out var statement))
            {
                return statement.WithTriviaFrom(node);
            }

            return base.VisitExpressionStatement(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (IsBaseDisposeInvocation(node) || IsFileSystemDisposeInvocation(node))
            {
                return AwaitConfigureAwait(RenameInvocation(visited, "DisposeAsync")).WithTriviaFrom(node);
            }

            return visited;
        }

        private bool TryRewriteConditionalDispose(ExpressionSyntax expression, out StatementSyntax statement)
        {
            statement = EmptyStatement();
            if (expression is not ConditionalAccessExpressionSyntax conditionalAccess
                || conditionalAccess.WhenNotNull is not InvocationExpressionSyntax invocation
                || invocation.ArgumentList.Arguments.Count != 0
                || invocation.Expression is not MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Dispose" }
                || !IsFileSystemType(_model.GetTypeInfo(conditionalAccess.Expression).Type))
            {
                return false;
            }

            var receiver = (ExpressionSyntax)Visit(conditionalAccess.Expression)!;
            var disposeAsync = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, IdentifierName("DisposeAsync")),
                ArgumentList());
            statement = IfStatement(
                IsPatternExpression(receiver, UnaryPattern(ConstantPattern(LiteralExpression(SyntaxKind.NullLiteralExpression)))),
                Block(ExpressionStatement(AwaitConfigureAwait(disposeAsync))));
            return true;
        }

        private bool IsFileSystemDisposeInvocation(InvocationExpressionSyntax invocation)
        {
            return invocation.ArgumentList.Arguments.Count == 0
                && invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Dispose" } memberAccess
                && memberAccess.Expression is not BaseExpressionSyntax
                && IsFileSystemType(_model.GetTypeInfo(memberAccess.Expression).Type);
        }

        private static bool IsBaseDisposeInvocation(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: BaseExpressionSyntax,
                Name.Identifier.ValueText: "Dispose"
            };
        }
    }

    private sealed class VoidReturnToValueTaskReturnRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            return node.Expression is null
                ? node.WithExpression(LiteralExpression(SyntaxKind.DefaultLiteralExpression))
                : node;
        }
    }

    private sealed class SyncContextRewriter : TypeReferenceRewriter
    {
        private readonly SemanticModel _model;
        private readonly AsyncCatalog _catalog;

        public SyncContextRewriter(SemanticModel model, AsyncCatalog catalog, IReadOnlyDictionary<string, string> typeNames)
            : base(typeNames)
        {
            _model = model;
            _catalog = catalog;
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            var symbol = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (symbol is not null && _catalog.TryGetInvocationTarget(symbol, out var method, out var asyncName))
            {
                if (HasOutArgument(node))
                {
                    return visited.WithTriviaFrom(node);
                }

                if (method.ReturnsAsyncEnumerable)
                {
                    return AddCancellationArgument(RenameInvocation(visited, asyncName), useCancellationToken: false).WithTriviaFrom(node);
                }

                return GetAwaiterGetResult(AddCancellationArgument(RenameInvocation(visited, asyncName), useCancellationToken: false)).WithTriviaFrom(node);
            }

            return visited;
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            return base.VisitConditionalAccessExpression(node);
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            return base.VisitObjectCreationExpression(node);
        }

        public override SyntaxNode? VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            var visited = (ConstructorInitializerSyntax)base.VisitConstructorInitializer(node)!;
            var symbol = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            return visited;
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            return base.VisitPropertyDeclaration(node);
        }

        public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            return base.VisitAssignmentExpression(node);
        }

        private static bool HasOutArgument(InvocationExpressionSyntax invocation)
        {
            return invocation.ArgumentList.Arguments.Any(static argument => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword));
        }

        private static InvocationExpressionSyntax GetAwaiterGetResult(ExpressionSyntax expression)
        {
            var getAwaiter = InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, expression, IdentifierName("GetAwaiter")));
            return InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, getAwaiter, IdentifierName("GetResult")));
        }

    }

    private sealed class AsyncBodyRewriter : TypeReferenceRewriter
    {
        private readonly SemanticModel _model;
        private readonly AsyncCatalog _catalog;
        private readonly bool _awaitScalarCalls;
        private readonly bool _wrapFileSystemEntries;
        private readonly IReadOnlyDictionary<string, string> _typeNames;

        public AsyncBodyRewriter(
            SemanticModel model,
            AsyncCatalog catalog,
            bool awaitScalarCalls,
            bool wrapFileSystemEntries,
            IReadOnlyDictionary<string, string>? typeNames = null)
            : base(typeNames ?? ImmutableDictionary<string, string>.Empty)
        {
            _model = model;
            _catalog = catalog;
            _awaitScalarCalls = awaitScalarCalls;
            _wrapFileSystemEntries = wrapFileSystemEntries;
            _typeNames = typeNames ?? ImmutableDictionary<string, string>.Empty;
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            var symbol = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (symbol is not null && symbol.Name == "TryResolveLinkTarget" && HasOutArgument(node))
            {
                return visited.WithTriviaFrom(node);
            }

            if (symbol is not null && _catalog.TryGetInvocationTarget(symbol, out var method, out var asyncName))
            {
                var invocation = AddCancellationArgument(RenameInvocation(visited, asyncName));
                if (!_awaitScalarCalls || method.ReturnsAsyncEnumerable)
                {
                    return invocation.WithTriviaFrom(node);
                }

                return AwaitConfigureAwait(invocation);
            }

            if (symbol is not null && TryRewriteKnownAsyncBclCall(symbol, visited, out var rewritten))
            {
                return AwaitConfigureAwait(rewritten);
            }

            return visited;
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            return TryCreateConditionalAsyncInvocation(node, out var conditionalInvocation, out _)
                ? conditionalInvocation.WithTriviaFrom(node)
                : base.VisitConditionalAccessExpression(node);
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce
                && coalesce.Left is ConditionalAccessExpressionSyntax conditionalAccess
                && TryCreateConditionalAsyncInvocation(conditionalAccess, out var conditionalInvocation, out var method)
                && method.Kind == AsyncReturnKind.ValueTaskOfT)
            {
                var fallback = ObjectCreationExpression(method.AsyncReturnType)
                    .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument((ExpressionSyntax)Visit(coalesce.Right)!))));
                var awaitExpression = AwaitConfigureAwait(ParenthesizedExpression(BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    conditionalInvocation,
                    fallback)));
                return node.WithExpression(awaitExpression);
            }

            return base.VisitReturnStatement(node);
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is ConditionalAccessExpressionSyntax conditionalAccess
                && TryCreateConditionalAsyncInvocation(conditionalAccess, out var receiver, out var invocation, out var method)
                && method.Kind == AsyncReturnKind.ValueTask)
            {
                return IfStatement(
                    IsPatternExpression(receiver, UnaryPattern(ConstantPattern(LiteralExpression(SyntaxKind.NullLiteralExpression)))),
                    Block(ExpressionStatement(AwaitConfigureAwait(invocation))));
            }

            return base.VisitExpressionStatement(node);
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node.Expression).Symbol as IMethodSymbol;
            if (symbol is not null && _catalog.TryGetInvocationTarget(symbol, out var method, out _) && method.ReturnsAsyncEnumerable)
            {
                var expression = (ExpressionSyntax)Visit(node.Expression)!;
                var withCancellation = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, expression, IdentifierName("WithCancellation")),
                    ArgumentList(SingletonSeparatedList(Argument(IdentifierName("cancellationToken")))));
                var configured = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, withCancellation, IdentifierName("ConfigureAwait")),
                    ArgumentList(SingletonSeparatedList(Argument(LiteralExpression(SyntaxKind.FalseLiteralExpression)))));
                return node
                    .WithAwaitKeyword(Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(Space))
                    .WithExpression(configured)
                    .WithStatement((StatementSyntax)Visit(node.Statement)!);
            }

            return base.VisitForEachStatement(node);
        }

        private bool TryCreateConditionalAsyncInvocation(ConditionalAccessExpressionSyntax conditionalAccess, out ConditionalAccessExpressionSyntax conditionalInvocation, out MethodSpec method)
        {
            conditionalInvocation = conditionalAccess;
            if (!TryCreateConditionalAsyncInvocation(conditionalAccess, out var receiver, out var invocation, out method)
                || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            conditionalInvocation = ConditionalAccessExpression(
                receiver,
                invocation.WithExpression(MemberBindingExpression(memberAccess.Name)));
            return true;
        }

        private bool TryCreateConditionalAsyncInvocation(ConditionalAccessExpressionSyntax conditionalAccess, out ExpressionSyntax receiver, out InvocationExpressionSyntax invocation, out MethodSpec method)
        {
            receiver = conditionalAccess.Expression;
            invocation = InvocationExpression(IdentifierName("Missing"));
            method = null!;
            if (conditionalAccess.WhenNotNull is not InvocationExpressionSyntax originalInvocation
                || originalInvocation.Expression is not MemberBindingExpressionSyntax memberBinding)
            {
                return false;
            }

            var symbol = _model.GetSymbolInfo(originalInvocation).Symbol as IMethodSymbol;
            if (symbol is null || !_catalog.TryGetInvocationTarget(symbol, out method, out var asyncName) || method.ReturnsAsyncEnumerable)
            {
                return false;
            }

            receiver = (ExpressionSyntax)Visit(conditionalAccess.Expression)!;
            var arguments = (ArgumentListSyntax)Visit(originalInvocation.ArgumentList)!;
            invocation = AddCancellationArgument(InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, IdentifierName(asyncName)),
                arguments));
            return true;
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            return new SyncContextRewriter(_model, _catalog, _typeNames).Visit(node);
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            return base.VisitObjectCreationExpression(node);
        }

        public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            return base.VisitAssignmentExpression(node);
        }

        private static bool HasOutArgument(InvocationExpressionSyntax invocation)
        {
            return invocation.ArgumentList.Arguments.Any(static argument => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword));
        }

        private bool TryRewriteKnownAsyncBclCall(IMethodSymbol symbol, InvocationExpressionSyntax visited, out InvocationExpressionSyntax rewritten)
        {
            rewritten = visited;
            if (visited.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (symbol.Name == "CopyTo" && IsTypeOrBase(symbol.ContainingType, "System.IO.Stream") && visited.ArgumentList.Arguments.Count == 1)
            {
                rewritten = visited
                    .WithExpression(memberAccess.WithName(IdentifierName("CopyToAsync")))
                    .WithArgumentList(visited.ArgumentList.AddArguments(Argument(IdentifierName("cancellationToken"))));
                return true;
            }

            if (symbol.Name == "Write" && IsTypeOrBase(symbol.ContainingType, "System.IO.Stream") && visited.ArgumentList.Arguments.Count == 3)
            {
                var content = visited.ArgumentList.Arguments[0].Expression;
                var offset = visited.ArgumentList.Arguments[1].Expression;
                var count = visited.ArgumentList.Arguments[2].Expression;
                var asMemory = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, content, IdentifierName("AsMemory")),
                    ArgumentList(SeparatedList(new[] { Argument(offset), Argument(count) })));
                rewritten = visited.WithExpression(memberAccess.WithName(IdentifierName("WriteAsync")))
                    .WithArgumentList(ArgumentList(SeparatedList(new[] { Argument(asMemory), Argument(IdentifierName("cancellationToken")) })));
                return true;
            }

            if (symbol.Name == "ReadToEnd" && IsTypeOrBase(symbol.ContainingType, "System.IO.TextReader") && visited.ArgumentList.Arguments.Count == 0)
            {
                rewritten = visited.WithExpression(memberAccess.WithName(IdentifierName("ReadToEndAsync")))
                    .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName("cancellationToken")))));
                return true;
            }

            if (symbol.Name == "ReadLine" && IsTypeOrBase(symbol.ContainingType, "System.IO.TextReader") && visited.ArgumentList.Arguments.Count == 0)
            {
                rewritten = visited.WithExpression(memberAccess.WithName(IdentifierName("ReadLineAsync")))
                    .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName("cancellationToken")))));
                return true;
            }

            if (symbol.Name == "Write" && IsTypeOrBase(symbol.ContainingType, "System.IO.TextWriter") && visited.ArgumentList.Arguments.Count == 1)
            {
                var content = visited.ArgumentList.Arguments[0].Expression;
                var asMemory = InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, content, IdentifierName("AsMemory")));
                rewritten = visited.WithExpression(memberAccess.WithName(IdentifierName("WriteAsync")))
                    .WithArgumentList(ArgumentList(SeparatedList(new[] { Argument(asMemory), Argument(IdentifierName("cancellationToken")) })));
                return true;
            }

            if (symbol.Name == "Flush" && IsTypeOrBase(symbol.ContainingType, "System.IO.TextWriter") && visited.ArgumentList.Arguments.Count == 0)
            {
                rewritten = visited.WithExpression(memberAccess.WithName(IdentifierName("FlushAsync")))
                    .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName("cancellationToken")))));
                return true;
            }

            return false;
        }

        private static bool IsTypeOrBase(INamedTypeSymbol type, string metadataName)
        {
            for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (current.ToDisplayString(TypeFormat) == metadataName)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class TryResolveReturnRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression is null)
            {
                return node;
            }

            return node.WithExpression(TupleExpression(SeparatedList(new[]
            {
                Argument(node.Expression),
                Argument(IdentifierName("resolvedPath"))
            })));
        }
    }

    private sealed class AsyncEnumerableReturnRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression is null)
            {
                return YieldStatement(SyntaxKind.YieldBreakStatement).WithTriviaFrom(node);
            }

            return CreateAsyncEnumerableAdapterBody(node.Expression).WithTriviaFrom(node);
        }
    }

    private sealed class ValueTaskBodyRewriter : CSharpSyntaxRewriter
    {
        private readonly MethodSpec _method;

        public ValueTaskBodyRewriter(MethodSpec method)
        {
            _method = method;
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (_method.Kind == AsyncReturnKind.ValueTask)
            {
                return node.WithExpression(LiteralExpression(SyntaxKind.DefaultLiteralExpression));
            }

            if (_method.Kind == AsyncReturnKind.ValueTaskOfT && node.Expression is not null)
            {
                return node.WithExpression(WrapValueTask(node.Expression));
            }

            return base.VisitReturnStatement(node);
        }

        public override SyntaxNode? VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            if (_method.Kind == AsyncReturnKind.ValueTask)
            {
                return node.WithExpression(LiteralExpression(SyntaxKind.DefaultLiteralExpression));
            }

            if (_method.Kind == AsyncReturnKind.ValueTaskOfT)
            {
                return node.WithExpression(WrapValueTask(node.Expression));
            }

            return base.VisitArrowExpressionClause(node);
        }

        private ExpressionSyntax WrapValueTask(ExpressionSyntax expression)
        {
            return ObjectCreationExpression(_method.AsyncReturnType)
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(expression))));
        }
    }

    private sealed record AsyncImplementationConversion(
        string Name,
        string AsyncName,
        bool AddCancellationParameter,
        bool AddCancellationArgument,
        bool IsLocalFunction);

    private sealed class AsyncImplementationRewriter : CSharpSyntaxRewriter
    {
        private readonly string _sourceTypeName;
        private readonly IReadOnlyDictionary<string, AsyncImplementationConversion> _methodConversions;
        private readonly IReadOnlyDictionary<string, AsyncImplementationConversion> _localFunctionConversions;
        private readonly Stack<bool> _cancellationTokenScopes = new();

        private AsyncImplementationRewriter(
            string sourceTypeName,
            IReadOnlyDictionary<string, AsyncImplementationConversion> methodConversions,
            IReadOnlyDictionary<string, AsyncImplementationConversion> localFunctionConversions)
        {
            _sourceTypeName = sourceTypeName;
            _methodConversions = methodConversions;
            _localFunctionConversions = localFunctionConversions;
        }

        public static ClassDeclarationSyntax Rewrite(string sourceTypeName, ClassDeclarationSyntax type)
        {
            var localFunctionConversions = BuildLocalFunctionConversions(type);
            var methodConversions = BuildMethodConversions(type, localFunctionConversions.Keys);
            var rewriter = new AsyncImplementationRewriter(sourceTypeName, methodConversions, localFunctionConversions);
            return (ClassDeclarationSyntax)rewriter.Visit(type)!;
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var hasConversion = _methodConversions.TryGetValue(node.Identifier.ValueText, out var conversion);
            _cancellationTokenScopes.Push(hasConversion && conversion!.AddCancellationParameter || HasCancellationParameter(node.ParameterList));
            try
            {
                var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
                if (visited.Identifier.ValueText == "TryResolveLinkTargetAsyncImpl")
                {
                    visited = (MethodDeclarationSyntax)new TryResolveOutAwaitRewriter().Visit(visited)!;
                }

                return hasConversion
                    ? ConvertMethod(visited, conversion!)
                    : visited;
            }
            finally
            {
                _cancellationTokenScopes.Pop();
            }
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            return _localFunctionConversions.TryGetValue(node.Identifier.ValueText, out var conversion)
                ? ConvertLocalFunction(visited, conversion)
                : visited;
        }

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            _cancellationTokenScopes.Push(false);
            try
            {
                var constructor = _sourceTypeName == "PhysicalFileSystem" && node.Identifier.ValueText == "Watcher"
                    ? RewritePhysicalWatcherConstructor(node)
                    : RemoveBlockingConstructorStatements(node);
                return base.VisitConstructorDeclaration(constructor);
            }
            finally
            {
                _cancellationTokenScopes.Pop();
            }
        }

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            var statements = new List<StatementSyntax>();
            foreach (var statement in node.Statements)
            {
                var visited = (StatementSyntax)Visit(statement)!;
                statements.Add(visited);
            }

            return node.WithStatements(List(statements));
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var visited = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
            if (_sourceTypeName != "PhysicalFileSystem"
                || visited.Type is not IdentifierNameSyntax { Identifier.ValueText: "Watcher" }
                || visited.ArgumentList is not { Arguments.Count: 2 } argumentList
                || !argumentList.Arguments[0].Expression.IsKind(SyntaxKind.ThisExpression))
            {
                return visited;
            }

            var pathArgument = argumentList.Arguments[1].Expression;
            var convertPath = InvocationExpression(
                IdentifierName("ConvertPathToInternalAsync"),
                ArgumentList(SeparatedList(new[]
                {
                    Argument(pathArgument),
                    Argument(IdentifierName("cancellationToken")).WithNameColon(NameColon(IdentifierName("cancellationToken")))
                })));
            return visited.WithArgumentList(argumentList.AddArguments(Argument(AwaitConfigureAwait(convertPath))));
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (TryGetBlockingWaitReceiver(visited, out var receiver))
            {
                return AwaitConfigureAwait(UseCurrentCancellationToken(receiver)).WithTriviaFrom(node);
            }

            var name = GetInvocationName(visited);
            if (name is not null && TryGetConversion(name, out var conversion))
            {
                var invocation = RenameInvocation(visited, conversion.AsyncName);
                if (conversion.AddCancellationArgument && IsCancellationTokenInScope())
                {
                    invocation = AddCancellationArgument(invocation);
                }

                return AwaitConfigureAwait(invocation).WithTriviaFrom(node);
            }

            return visited;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            _cancellationTokenScopes.Push(false);
            try
            {
                var visited = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
                return ContainsAwait(visited.Body) && !visited.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)
                    ? visited.WithAsyncKeyword(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space))
                    : visited;
            }
            finally
            {
                _cancellationTokenScopes.Pop();
            }
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            _cancellationTokenScopes.Push(false);
            try
            {
                var visited = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
                return ContainsAwait(visited.Body) && !visited.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)
                    ? visited.WithAsyncKeyword(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space))
                    : visited;
            }
            finally
            {
                _cancellationTokenScopes.Pop();
            }
        }

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            _cancellationTokenScopes.Push(false);
            try
            {
                var visited = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!;
                return ContainsAwait(visited.Body) && !visited.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)
                    ? visited.WithAsyncKeyword(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space))
                    : visited;
            }
            finally
            {
                _cancellationTokenScopes.Pop();
            }
        }

        private static IReadOnlyDictionary<string, AsyncImplementationConversion> BuildLocalFunctionConversions(ClassDeclarationSyntax type)
        {
            return type.DescendantNodes()
                .OfType<LocalFunctionStatementSyntax>()
                .Where(static localFunction => ContainsBlockingWait(localFunction))
                .GroupBy(static localFunction => localFunction.Identifier.ValueText, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => new AsyncImplementationConversion(group.Key, group.Key + "Async", AddCancellationParameter: false, AddCancellationArgument: false, IsLocalFunction: true),
                    StringComparer.Ordinal);
        }

        private static IReadOnlyDictionary<string, AsyncImplementationConversion> BuildMethodConversions(ClassDeclarationSyntax type, IEnumerable<string> localFunctionConversionNames)
        {
            var methods = type.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            var convertedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in methods)
            {
                if (ShouldRenameMethod(method) && ContainsBlockingWaitOutsideLocalFunctions(method))
                {
                    convertedNames.Add(method.Identifier.ValueText);
                }
            }

            var awaitedNames = new HashSet<string>(convertedNames, StringComparer.Ordinal);
            awaitedNames.UnionWith(localFunctionConversionNames);
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var method in methods)
                {
                    var name = method.Identifier.ValueText;
                    if (!ShouldRenameMethod(method) || convertedNames.Contains(name) || !InvokesAny(method, awaitedNames))
                    {
                        continue;
                    }

                    convertedNames.Add(name);
                    awaitedNames.Add(name);
                    changed = true;
                }
            }

            return convertedNames.ToDictionary(
                static name => name,
                static name => new AsyncImplementationConversion(name, name + "Async", AddCancellationParameter: true, AddCancellationArgument: true, IsLocalFunction: false),
                StringComparer.Ordinal);
        }

        private static bool ShouldRenameMethod(MethodDeclarationSyntax method)
        {
            var name = method.Identifier.ValueText;
            if (name.EndsWith("Async", StringComparison.Ordinal) || name.EndsWith("AsyncImpl", StringComparison.Ordinal))
            {
                return false;
            }

            if (method.Modifiers.Any(SyntaxKind.OverrideKeyword) || method.Modifiers.Any(SyntaxKind.ExternKeyword))
            {
                return false;
            }

            if (method.ParameterList.Parameters.Any(static parameter => parameter.Modifiers.Any(SyntaxKind.RefKeyword) || parameter.Modifiers.Any(SyntaxKind.OutKeyword)))
            {
                if (ContainsBlockingWaitOutsideLocalFunctions(method))
                {
                    throw new InvalidOperationException($"Cannot generate async helper for `{name}` because it has ref/out parameters.");
                }

                return false;
            }

            return true;
        }

        private static bool InvokesAny(MethodDeclarationSyntax method, HashSet<string> names)
        {
            return DescendantNodesOutsideLocalFunctions(method)
                .OfType<InvocationExpressionSyntax>()
                .Select(GetInvocationName)
                .Any(name => name is not null && names.Contains(name));
        }

        private static bool ContainsBlockingWaitOutsideLocalFunctions(MethodDeclarationSyntax method)
        {
            return DescendantNodesOutsideLocalFunctions(method).OfType<InvocationExpressionSyntax>().Any(TryGetBlockingWaitReceiver);
        }

        private static IEnumerable<SyntaxNode> DescendantNodesOutsideLocalFunctions(MethodDeclarationSyntax method)
        {
            return method.DescendantNodes(static node => node is not LocalFunctionStatementSyntax && node is not AnonymousFunctionExpressionSyntax);
        }

        private static bool ContainsBlockingWait(SyntaxNode node)
        {
            return node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(TryGetBlockingWaitReceiver);
        }

        private static bool ContainsAwait(CSharpSyntaxNode node)
        {
            return node.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any();
        }

        private static bool HasCancellationParameter(ParameterListSyntax parameterList)
        {
            return parameterList.Parameters.Any(static parameter => parameter.Identifier.ValueText == "cancellationToken");
        }

        private bool IsCancellationTokenInScope()
        {
            return _cancellationTokenScopes.Count != 0 && _cancellationTokenScopes.Peek();
        }

        private bool TryGetConversion(string name, out AsyncImplementationConversion conversion)
        {
            return _methodConversions.TryGetValue(name, out conversion!) || _localFunctionConversions.TryGetValue(name, out conversion!);
        }

        private MethodDeclarationSyntax ConvertMethod(MethodDeclarationSyntax method, AsyncImplementationConversion conversion)
        {
            var converted = method
                .WithIdentifier(Identifier(method.Identifier.LeadingTrivia, conversion.AsyncName, method.Identifier.TrailingTrivia))
                .WithReturnType(ToValueTaskReturnType(method.ReturnType))
                .WithModifiers(EnsureModifier(method.Modifiers, SyntaxKind.AsyncKeyword));

            converted = RewriteDocumentationCrefs(converted);

            if (conversion.AddCancellationParameter && !HasCancellationParameter(converted.ParameterList))
            {
                converted = converted.WithParameterList(AddCancellationParameter(converted.ParameterList, includeDefault: true, enumeratorCancellation: false));
                converted = EnsureCancellationTokenDocumentation(converted);
            }

            return converted;
        }

        private MethodDeclarationSyntax RewriteDocumentationCrefs(MethodDeclarationSyntax method)
        {
            var leadingTrivia = method.GetLeadingTrivia().ToFullString();
            if (!leadingTrivia.Contains("cref", StringComparison.Ordinal))
            {
                return method;
            }

            foreach (var conversion in _methodConversions.Values)
            {
                leadingTrivia = leadingTrivia
                    .Replace($"cref = \"{conversion.Name}\"", $"cref = \"{conversion.AsyncName}\"", StringComparison.Ordinal)
                    .Replace($"cref=\"{conversion.Name}\"", $"cref=\"{conversion.AsyncName}\"", StringComparison.Ordinal);
            }

            return method.WithLeadingTrivia(ParseLeadingTrivia(leadingTrivia));
        }

        private static MethodDeclarationSyntax EnsureCancellationTokenDocumentation(MethodDeclarationSyntax method)
        {
            if (!method.Modifiers.Any(SyntaxKind.PublicKeyword) && !method.Modifiers.Any(SyntaxKind.ProtectedKeyword))
            {
                return method;
            }

            var leadingTrivia = method.GetLeadingTrivia().ToFullString();
            if (leadingTrivia.Contains("cancellationToken", StringComparison.Ordinal))
            {
                return method;
            }

            return method.WithLeadingTrivia(ParseLeadingTrivia(leadingTrivia + "/// <param name=\"cancellationToken\">A token to cancel the operation.</param>\n"));
        }

        private static LocalFunctionStatementSyntax ConvertLocalFunction(LocalFunctionStatementSyntax localFunction, AsyncImplementationConversion conversion)
        {
            return localFunction
                .WithIdentifier(Identifier(localFunction.Identifier.LeadingTrivia, conversion.AsyncName, localFunction.Identifier.TrailingTrivia))
                .WithReturnType(ToValueTaskReturnType(localFunction.ReturnType))
                .WithModifiers(EnsureModifier(localFunction.Modifiers, SyntaxKind.AsyncKeyword));
        }

        private static TypeSyntax ToValueTaskReturnType(TypeSyntax returnType)
        {
            return returnType is PredefinedTypeSyntax predefinedType && predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword)
                ? IdentifierName("ValueTask")
                : GenericName("ValueTask").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(returnType.WithoutTrivia())));
        }

        private static ConstructorDeclarationSyntax RemoveBlockingConstructorStatements(ConstructorDeclarationSyntax constructor)
        {
            return constructor.Body is null
                ? constructor
                : constructor.WithBody(constructor.Body.WithStatements(List(constructor.Body.Statements.Where(static statement => !ContainsBlockingWait(statement)))));
        }

        private static ConstructorDeclarationSyntax RewritePhysicalWatcherConstructor(ConstructorDeclarationSyntax constructor)
        {
            if (constructor.ParameterList.Parameters.Any(static parameter => parameter.Identifier.ValueText == "pathOnDisk"))
            {
                return constructor;
            }

            var rewritten = (ConstructorDeclarationSyntax)new PhysicalWatcherConstructorRewriter().Visit(constructor)!;
            var pathOnDisk = Parameter(Identifier("pathOnDisk")).WithType(PredefinedType(Token(SyntaxKind.StringKeyword)));
            return rewritten.WithParameterList(rewritten.ParameterList.WithParameters(rewritten.ParameterList.Parameters.Add(pathOnDisk)));
        }

        private ExpressionSyntax UseCurrentCancellationToken(ExpressionSyntax expression)
        {
            return IsCancellationTokenInScope()
                ? ReplaceDefaultCancellationTokenRewriter.Replace(expression)
                : expression;
        }

        private static bool TryGetBlockingWaitReceiver(InvocationExpressionSyntax invocation)
        {
            return TryGetBlockingWaitReceiver(invocation, out _);
        }

        private static bool TryGetBlockingWaitReceiver(InvocationExpressionSyntax invocation, out ExpressionSyntax receiver)
        {
            receiver = invocation;
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetResult" } getResultAccess
                || getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation
                || getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetAwaiter" } getAwaiterAccess)
            {
                return false;
            }

            receiver = getAwaiterAccess.Expression;
            return true;
        }

        private static string? GetInvocationName(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
                _ => null
            };
        }

        private sealed class ReplaceDefaultCancellationTokenRewriter : CSharpSyntaxRewriter
        {
            public static ExpressionSyntax Replace(ExpressionSyntax expression)
            {
                return (ExpressionSyntax)new ReplaceDefaultCancellationTokenRewriter().Visit(expression)!;
            }

            public override SyntaxNode? VisitArgument(ArgumentSyntax node)
            {
                var visited = (ArgumentSyntax)base.VisitArgument(node)!;
                if (visited.NameColon?.Name.Identifier.ValueText != "cancellationToken" || !IsDefaultExpression(visited.Expression))
                {
                    return visited;
                }

                return visited.WithExpression(IdentifierName("cancellationToken"));
            }

            private static bool IsDefaultExpression(ExpressionSyntax expression)
            {
                return expression.IsKind(SyntaxKind.DefaultLiteralExpression) || expression is DefaultExpressionSyntax;
            }
        }

        private sealed class PhysicalWatcherConstructorRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
                return TryGetBlockingWaitReceiver(visited, out var receiver) && receiver.ToString().Contains("ConvertPathToInternalAsync", StringComparison.Ordinal)
                    ? IdentifierName("pathOnDisk")
                    : visited;
            }
        }

        private sealed class TryResolveOutAwaitRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode? VisitBlock(BlockSyntax node)
            {
                var statements = new List<StatementSyntax>();
                foreach (var statement in node.Statements)
                {
                    var visited = (StatementSyntax)Visit(statement)!;
                    if (TryRewriteTryResolveStatement(visited, out var replacement))
                    {
                        statements.AddRange(replacement);
                    }
                    else
                    {
                        statements.Add(visited);
                    }
                }

                var block = node.WithStatements(List(statements));
                return new TryResolveOutVariableReferenceRewriter().Visit(block);
            }

            private static bool TryRewriteTryResolveStatement(StatementSyntax statement, out IEnumerable<StatementSyntax> replacement)
            {
                replacement = Array.Empty<StatementSyntax>();
                if (statement is not IfStatementSyntax ifStatement
                    || ifStatement.Condition is not PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } notExpression
                    || notExpression.Operand is not InvocationExpressionSyntax invocation
                    || invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "TryResolveLinkTarget" } memberAccess
                    || invocation.ArgumentList.Arguments.Count < 2
                    || !TryGetOutVariableName(invocation.ArgumentList.Arguments[^1], out var variableName))
                {
                    return false;
                }

                var target = memberAccess.Expression;
                var asyncArguments = invocation.ArgumentList.Arguments.RemoveAt(invocation.ArgumentList.Arguments.Count - 1)
                    .Add(Argument(IdentifierName("cancellationToken")).WithNameColon(NameColon(IdentifierName("cancellationToken"))));
                var asyncInvocation = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, target, IdentifierName("TryResolveLinkTargetAsync")),
                    ArgumentList(asyncArguments));
                var resultName = variableName + "Result";
                var resultDeclaration = LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
                    .AddVariables(VariableDeclarator(resultName)
                        .WithInitializer(EqualsValueClause(AwaitConfigureAwait(asyncInvocation)))));
                var rewrittenIf = ifStatement.WithCondition(PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(resultName), IdentifierName("Success"))));

                replacement = new StatementSyntax[] { resultDeclaration, rewrittenIf };
                return true;
            }

            private static bool TryGetOutVariableName(ArgumentSyntax argument, out string variableName)
            {
                variableName = string.Empty;
                if (!argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
                {
                    return false;
                }

                switch (argument.Expression)
                {
                    case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation }:
                        variableName = designation.Identifier.ValueText;
                        return true;
                    case IdentifierNameSyntax identifier:
                        variableName = identifier.Identifier.ValueText;
                        return true;
                    default:
                        return false;
                }
            }
        }

        private sealed class TryResolveOutVariableReferenceRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (node.Identifier.ValueText is "resolvedPathDelegate" or "resolved")
                {
                    return MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(node.Identifier.ValueText + "Result"),
                        IdentifierName("ResolvedPath"));
                }

                return node;
            }
        }
    }

    private class TypeReferenceRewriter : CSharpSyntaxRewriter
    {
        private readonly IReadOnlyDictionary<string, string> _typeNames;

        public TypeReferenceRewriter(IReadOnlyDictionary<string, string> typeNames)
        {
            _typeNames = typeNames;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            return IsTypeNameContext(node) && ShouldRewriteIdentifierName(node) && _typeNames.TryGetValue(node.Identifier.ValueText, out var replacement)
                ? IdentifierName(Identifier(node.Identifier.LeadingTrivia, replacement, node.Identifier.TrailingTrivia))
                : base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        {
            var visited = (GenericNameSyntax)base.VisitGenericName(node)!;
            return IsTypeNameContext(node) && ShouldRewriteSimpleName(node) && _typeNames.TryGetValue(node.Identifier.ValueText, out var replacement)
                ? visited.WithIdentifier(Identifier(node.Identifier.LeadingTrivia, replacement, node.Identifier.TrailingTrivia))
                : visited;
        }

        private static bool ShouldRewriteIdentifierName(IdentifierNameSyntax node)
        {
            return ShouldRewriteSimpleName(node);
        }

        private static bool ShouldRewriteSimpleName(SimpleNameSyntax node)
        {
            return node.Parent is not QualifiedNameSyntax qualifiedName
                || qualifiedName.Right != node
                || ShouldRewriteQualifiedName(qualifiedName);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var visited = (QualifiedNameSyntax)base.VisitQualifiedName(node)!;
            if (IsTypeNameContext(node) && ShouldRewriteQualifiedName(visited) && _typeNames.TryGetValue(visited.Right.Identifier.ValueText, out var replacement))
            {
                return visited.WithRight(IdentifierName(Identifier(visited.Right.Identifier.LeadingTrivia, replacement, visited.Right.Identifier.TrailingTrivia)));
            }

            return visited;
        }

        private static bool ShouldRewriteQualifiedName(QualifiedNameSyntax node)
        {
            var left = node.Left.ToString();
            return left is "Zio" or "Zio.FileSystems";
        }

        private static bool IsTypeNameContext(NameSyntax node)
        {
            return node.Parent switch
            {
                null => true,
                BaseTypeSyntax baseType when baseType.Type == node => true,
                ObjectCreationExpressionSyntax creation when creation.Type == node => true,
                ParameterSyntax parameter when parameter.Type == node => true,
                VariableDeclarationSyntax variableDeclaration when variableDeclaration.Type == node => true,
                PropertyDeclarationSyntax propertyDeclaration when propertyDeclaration.Type == node => true,
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.ReturnType == node => true,
                LocalFunctionStatementSyntax localFunction when localFunction.ReturnType == node => true,
                DelegateDeclarationSyntax delegateDeclaration when delegateDeclaration.ReturnType == node => true,
                CastExpressionSyntax castExpression when castExpression.Type == node => true,
                DefaultExpressionSyntax defaultExpression when defaultExpression.Type == node => true,
                TypeOfExpressionSyntax typeOfExpression when typeOfExpression.Type == node => true,
                SizeOfExpressionSyntax sizeOfExpression when sizeOfExpression.Type == node => true,
                ArrayTypeSyntax arrayType when arrayType.ElementType == node => true,
                NullableTypeSyntax nullableType when nullableType.ElementType == node => true,
                PointerTypeSyntax pointerType when pointerType.ElementType == node => true,
                TupleElementSyntax tupleElement when tupleElement.Type == node => true,
                TypeArgumentListSyntax => true,
                QualifiedNameSyntax qualifiedName when qualifiedName.Right == node => IsTypeNameContext(qualifiedName),
                AliasQualifiedNameSyntax aliasQualifiedName when aliasQualifiedName.Name == node => IsTypeNameContext(aliasQualifiedName),
                DeclarationPatternSyntax declarationPattern when declarationPattern.Type == node => true,
                RecursivePatternSyntax recursivePattern when recursivePattern.Type == node => true,
                RefTypeSyntax refType when refType.Type == node => true,
                TypeConstraintSyntax typeConstraint when typeConstraint.Type == node => true,
                _ => false
            };
        }
    }

    private static string BuildFile(string @namespace, IEnumerable<string> usings, MemberDeclarationSyntax type, string? extraGuard = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("// This file was generated by Zio.AsyncCodeGen. Do not edit this file directly.");
        builder.AppendLine("#nullable enable");
        builder.AppendLine($"#if {Guard}");
        if (extraGuard is not null)
        {
            builder.AppendLine($"#if {extraGuard}");
        }

        foreach (var @using in usings.Order(StringComparer.Ordinal))
        {
            builder.AppendLine(@using.StartsWith("static ", StringComparison.Ordinal)
                ? $"using {@using};"
                : $"using {@using};");
        }

        builder.AppendLine();
        builder.AppendLine($"namespace {@namespace};");
        builder.AppendLine();
        var typeText = type.NormalizeWhitespace().ToFullString()
            .Replace("FileSystemEnumerable<FileSystemItem>", "FileSystemEnumerable<FileSystemItemAsync>", StringComparison.Ordinal);
        builder.AppendLine(typeText);
        if (extraGuard is not null)
        {
            builder.AppendLine("#endif");
        }
        builder.AppendLine("#endif");
        return TrimTrailingWhitespace(builder.ToString());
    }

    private static string TrimTrailingWhitespace(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var builder = new StringBuilder(normalized.Length);
        var lastLine = lines.Length - 1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (i == lastLine && lines[i].Length == 0)
            {
                continue;
            }

            builder.Append(lines[i].TrimEnd(' ', '\t')).Append('\n');
        }

        return builder.ToString();
    }

    private static SyntaxTriviaList GetInterfaceDocumentation(MethodSpec method)
    {
        return method.Kind == AsyncReturnKind.TryResolveLinkTarget
            ? ParseLeadingTrivia("/// <summary>\n/// Resolves the target of a symbolic link.\n/// </summary>\n/// <param name=\"linkPath\">The path of the symbolic link to resolve.</param>\n/// <param name=\"cancellationToken\">A token to cancel the operation.</param>\n/// <returns>A tuple containing whether the link target was resolved and the resolved target path.</returns>\n")
            : ParseLeadingTrivia($"/// <inheritdoc cref=\"IFileSystem.{method.Name}\" />\n");
    }

    private static SyntaxTriviaList InheritdocTrivia() => ParseLeadingTrivia("/// <inheritdoc />\n");

    private static MemberDeclarationSyntax EnsureGeneratedMemberDocumentation(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method when RequiresGeneratedDocumentation(method.Modifiers, method.GetLeadingTrivia()) => method.WithLeadingTrivia(InheritdocTrivia()),
            ConstructorDeclarationSyntax constructor when RequiresGeneratedDocumentation(constructor.Modifiers, constructor.GetLeadingTrivia()) => constructor.WithLeadingTrivia(InheritdocTrivia()),
            PropertyDeclarationSyntax property when RequiresGeneratedDocumentation(property.Modifiers, property.GetLeadingTrivia()) => property.WithLeadingTrivia(InheritdocTrivia()),
            FieldDeclarationSyntax field when RequiresGeneratedDocumentation(field.Modifiers, field.GetLeadingTrivia()) => field.WithLeadingTrivia(InheritdocTrivia()),
            _ => member
        };
    }

    private static bool RequiresGeneratedDocumentation(SyntaxTokenList modifiers, SyntaxTriviaList leadingTrivia)
    {
        return (modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword))
            && !leadingTrivia.Any(static trivia => trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
    }

    private static ParameterListSyntax RemoveKnownOutParameters(ParameterListSyntax parameterList)
    {
        return parameterList.WithParameters(SeparatedList(parameterList.Parameters.Where(static parameter => !parameter.Modifiers.Any(SyntaxKind.OutKeyword))));
    }

    private static ParameterListSyntax ReplaceParameterTypes(ParameterListSyntax parameterList, bool replaceFileSystemWithAsync)
    {
        if (!replaceFileSystemWithAsync)
        {
            return parameterList;
        }

        var rewriter = new TypeReferenceRewriter(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IFileSystem"] = "IFileSystemAsync"
        });
        return (ParameterListSyntax)rewriter.Visit(parameterList)!;
    }

    private static ConstructorInitializerSyntax CreateBaseConstructorInitializer(ParameterListSyntax parameterList)
    {
        return ConstructorInitializer(
            SyntaxKind.BaseConstructorInitializer,
            ArgumentList(SeparatedList(parameterList.Parameters.Select(static parameter => Argument(IdentifierName(parameter.Identifier.ValueText))))));
    }

    private static ParameterListSyntax AddCancellationParameter(ParameterListSyntax parameterList, bool includeDefault, bool enumeratorCancellation)
    {
        var cancellationToken = Parameter(Identifier("cancellationToken"))
            .WithType(IdentifierName("CancellationToken"));
        if (includeDefault)
        {
            cancellationToken = cancellationToken.WithDefault(EqualsValueClause(LiteralExpression(SyntaxKind.DefaultLiteralExpression)));
        }

        if (enumeratorCancellation)
        {
            cancellationToken = cancellationToken.WithAttributeLists(SingletonList(AttributeList(SingletonSeparatedList(Attribute(IdentifierName("EnumeratorCancellation"))))));
        }

        var parameters = includeDefault
            ? parameterList.Parameters
            : SeparatedList(parameterList.Parameters.Select(static parameter => parameter.WithDefault(null)));
        return parameterList.WithParameters(parameters.Add(cancellationToken));
    }

    private static SeparatedSyntaxList<ParameterSyntax> AddEnumeratorCancellationAttribute(SeparatedSyntaxList<ParameterSyntax> parameters, bool enumeratorCancellation)
    {
        return parameters;
    }

    private static SyntaxTokenList RemoveModifier(SyntaxTokenList modifiers, SyntaxKind kind)
    {
        return TokenList(modifiers.Where(token => !token.IsKind(kind)));
    }

    private static SyntaxTokenList EnsureModifier(SyntaxTokenList modifiers, SyntaxKind kind)
    {
        return modifiers.Any(kind) ? modifiers : modifiers.Add(Token(kind));
    }

    private static StatementSyntax CancellationThrowStatement()
    {
        return ExpressionStatement(InvocationExpression(MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName("cancellationToken"),
            IdentifierName("ThrowIfCancellationRequested"))));
    }

    private static BlockSyntax InsertCancellationThrow(BlockSyntax body)
    {
        var insertIndex = body.Statements.FirstOrDefault() is ExpressionStatementSyntax expressionStatement
            && expressionStatement.Expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "AssertNotDisposed" } }
            ? 1
            : 0;
        return body.WithStatements(body.Statements.Insert(insertIndex, CancellationThrowStatement()));
    }

    private static BlockSyntax CreateAsyncEnumerableAdapterBody(ExpressionSyntax expression)
    {
        return Block(ForEachStatement(
            IdentifierName("var"),
            Identifier("item"),
            expression,
            Block(
                CancellationThrowStatement(),
                YieldStatement(SyntaxKind.YieldReturnStatement, IdentifierName("item")),
                ExpressionStatement(AwaitConfigureAwait(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("Task"), IdentifierName("CompletedTask")))))));
    }

    private static ReturnStatementSyntax ReturnValueTask(TypeSyntax valueTaskType, ExpressionSyntax value)
    {
        return ReturnStatement(ObjectCreationExpression(valueTaskType)
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(value)))));
    }

    private static SeparatedSyntaxList<ArgumentSyntax> SyncArguments(MethodSpec spec, bool includeOutVar)
    {
        var arguments = new List<ArgumentSyntax>();
        foreach (var parameter in spec.Symbol.Parameters)
        {
            if (parameter.RefKind == RefKind.Out)
            {
                if (includeOutVar)
                {
                    arguments.Add(Argument(DeclarationExpression(IdentifierName("var"), SingleVariableDesignation(Identifier(parameter.Name)))).WithRefKindKeyword(Token(SyntaxKind.OutKeyword)));
                }
                continue;
            }

            arguments.Add(Argument(IdentifierName(parameter.Name)));
        }

        return SeparatedList(arguments);
    }

    private static SeparatedSyntaxList<ArgumentSyntax> AsyncArgumentsWithoutCancellation(MethodSpec spec)
    {
        var arguments = new List<ArgumentSyntax>();
        foreach (var parameter in spec.Symbol.Parameters)
        {
            if (parameter.RefKind == RefKind.Out)
            {
                continue;
            }

            arguments.Add(Argument(IdentifierName(parameter.Name)));
        }

        return SeparatedList(arguments);
    }

    private static InvocationExpressionSyntax RenameInvocation(InvocationExpressionSyntax invocation, string asyncName)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => invocation.WithExpression(identifier.WithIdentifier(Identifier(identifier.Identifier.LeadingTrivia, asyncName, identifier.Identifier.TrailingTrivia))),
            MemberAccessExpressionSyntax memberAccess => invocation.WithExpression(memberAccess.WithName(IdentifierName(asyncName))),
            MemberBindingExpressionSyntax memberBinding => invocation.WithExpression(memberBinding.WithName(IdentifierName(asyncName))),
            _ => throw new NotSupportedException($"Unsupported invocation expression `{invocation.Expression}`.")
        };
    }

    private static InvocationExpressionSyntax AddCancellationArgument(InvocationExpressionSyntax invocation, bool useCancellationToken = true)
    {
        var expression = useCancellationToken
            ? (ExpressionSyntax)IdentifierName("cancellationToken")
            : LiteralExpression(SyntaxKind.DefaultLiteralExpression);
        var argument = Argument(expression)
            .WithNameColon(NameColon(IdentifierName("cancellationToken")));
        return invocation.WithArgumentList(invocation.ArgumentList.AddArguments(argument));
    }

    private static AwaitExpressionSyntax AwaitConfigureAwait(ExpressionSyntax expression)
    {
        var leadingTrivia = expression.GetLeadingTrivia();
        expression = expression.WithoutLeadingTrivia();
        var configureAwait = InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, expression, IdentifierName("ConfigureAwait")),
            ArgumentList(SingletonSeparatedList(Argument(LiteralExpression(SyntaxKind.FalseLiteralExpression)))));
        return AwaitExpression(Token(leadingTrivia, SyntaxKind.AwaitKeyword, TriviaList(Space)), configureAwait);
    }

    private static bool ContainsYield(MethodDeclarationSyntax method)
    {
        return method.DescendantNodes().OfType<YieldStatementSyntax>().Any();
    }

    private static bool IsFileSystemType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "IFileSystem" } namedType
            && namedType.ContainingNamespace.ToDisplayString() == "Zio";
    }

}
