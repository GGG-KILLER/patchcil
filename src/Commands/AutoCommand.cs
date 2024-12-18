using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using AsmResolver.IO;
using AsmResolver.PE;
using PatchCil.Helpers;
using PatchCil.Model;

namespace PatchCil.Commands;

internal sealed class AutoCommand
{
    private readonly Option<string> _ridOption;
    private readonly Option<bool> _noRecurseOption;
    private readonly Option<IEnumerable<FileSystemInfo>> _libraryPathsOption;
    private readonly Option<IEnumerable<string>> _ignoreMissingOption;
    private readonly Option<bool> _dryRunOption;
    private readonly Option<IEnumerable<FileSystemInfo>> _pathsOption;

    public AutoCommand()
    {
        Command = new Command("auto", "Automatically patches all imports in all provided assemblies.");

        _noRecurseOption = new Option<bool>(
            name: "--no-recurse",
            description: "Disable the recursive traversal of paths to patch.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        _ridOption = new Option<string>(
            aliases: ["-r", "--runtime", "--rid"],
            description: "The RID (Runtime Identifier) which the application will run under. Examples: win-x86, linux-x64, linux-arm64, osx-arm64.")
        {
            Arity = ArgumentArity.ExactlyOne,
        };

        _pathsOption = new Option<IEnumerable<FileSystemInfo>>(
            name: "--paths",
            description: "Paths whose content needs to be patched. Single files and directories are accepted. Directories are traversed recursively by default.")
        {
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        }
            .ExistingOnly();

        _libraryPathsOption = new Option<IEnumerable<FileSystemInfo>>(
            name: "--libs",
            description: "Paths where libraries are searched for. Single files and directories are accepted. Directories are not searched recursively.")
        {
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        }
            .ExistingOnly();

        _ignoreMissingOption = new Option<IEnumerable<string>>(
            name: "--ignore-missing",
            description: "Do not fail when some dependencies are not found.")
        {
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };

        _dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Does not overwrite the assemblies after resolving dependencies.");

        Command.Add(_noRecurseOption);
        Command.Add(_ridOption);
        Command.Add(_pathsOption);
        Command.Add(_libraryPathsOption);
        Command.Add(_ignoreMissingOption);
        Command.Add(_dryRunOption);

        Command.SetHandler(CommandHandler);
    }

    public Command Command { get; }

    private void CommandHandler(InvocationContext context)
    {
        var recurse = !context.ParseResult.GetValueForOption(_noRecurseOption);
        var rid = context.ParseResult.GetValueForOption(_ridOption)!;
        var assemblyPaths = context.ParseResult.GetValueForOption(_pathsOption)!;
        var libraryPaths = context.ParseResult.GetValueForOption(_libraryPathsOption)!.ToImmutableArray();
        var allowedMissing = context.ParseResult.GetValueForOption(_ignoreMissingOption)?.Select(DotNet.Globbing.Glob.Parse).ToHashSet()
            ?? [];
        var dryRun = context.ParseResult.GetValueForOption(_dryRunOption);

        if (!RuntimeIdentifiers.IsValid(rid))
        {
            context.Console.WriteError("invalid RID (Runtime Identifier) provided.");
            context.ExitCode = 1;
            return;
        }

        var assemblies = assemblyPaths
            .SelectMany(path =>
            {
                if (path is FileInfo file)
                {
                    return [file];
                }
                else if (path is DirectoryInfo directory)
                {
                    return directory.EnumerateFiles("*.dll", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        MatchType = MatchType.Simple,
                        MatchCasing = MatchCasing.PlatformDefault,
                        RecurseSubdirectories = recurse,
                        AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint, // Skip symlinks and system files
                    });
                }

                throw new Exception("Unreacheable code path reached.");
            })
            .Where(x =>
            {
                try
                {
                    AssemblyDefinition.FromFile(x.FullName, new ModuleReaderParameters(
                        x.Directory!.FullName,
                        new PEReaderParameters(ThrowErrorListener.Instance)));
                    return true;
                }
                catch (BadImageFormatException)
                {
                    return false;
                }
            })
            .ToImmutableArray();

        if (assemblies.Length == 0)
        {
            context.Console.WriteLine("notice: no assemblies to patch found.");
            return;
        }

        var dependencies = AutoPatchAssemblies(context.Console, recurse, rid, libraryPaths, assemblies, dryRun);
        var missingDependencies = dependencies.Where(x => !x.Satisfied).ToImmutableArray();

        var failed = false;
        context.Console.WriteLine($"patchcil-auto: {missingDependencies.Length} dependencies could not be satisfied");
        foreach (var missing in missingDependencies)
        {
            if (allowedMissing.Any(pattern => pattern.IsMatch(missing.LibraryName)))
            {
                context.Console.WriteLine($"warn: patchcil-auto ignoring missing {missing.LibraryName} wanted by {missing.Assembly}");
            }
            else
            {
                context.Console.WriteLine($"error: patchcil-auto could not satisfy dependency {missing.LibraryName} wanted by {missing.Assembly}");
                failed = true;
            }
        }

        if (failed)
        {
            context.Console.Error.WriteLine("patchcil-auto failed to find all the required dependencies.");
            context.Console.Error.WriteLine("Add the missing dependencies to --libs or use `--ignore-missing=foo.so.1 bar.so etc.so`.");
            context.ExitCode = ExitCodes.MissingDependencies;
        }
    }

    public static ImmutableArray<Dependency> AutoPatchAssemblies(
        IConsole console,
        bool recurse,
        string rid,
        ImmutableArray<FileSystemInfo> libraryPaths,
        ImmutableArray<FileInfo> assemblies,
        bool dryRun)
    {
        var dependencies = ImmutableArray.CreateBuilder<Dependency>();
        var libraryCandidateMap = new CandidateMap(recurse, libraryPaths);

        foreach (var assembly in assemblies)
        {
            console.WriteLine($"searching for dependencies of {assembly}");
            // Memory map as we don't know how large the assemblies we'll be opening are.
            using var fileService = new MemoryMappedFileService();

            var runtimeFolderName = RuntimeIdentifiers.EnumerateSelfAndDescendants(rid)
                .Select(rid => Path.Combine(assembly.Directory!.Name, "runtime", "rid"))
                .FirstOrDefault(Directory.Exists);

            // List all assembly-specific
            var assemblyCandidateMap = new CandidateMap(false, assembly.Directory!);
            var runtimeCandidateMap = runtimeFolderName != null
                ? new CandidateMap(true, new DirectoryInfo(runtimeFolderName))
                : null;

            var assemblyDefinition = AssemblyDefinition.FromFile(
                fileService.OpenFile(assembly.FullName),
                new ModuleReaderParameters(
                    assembly.Directory!.FullName,
                    new PEReaderParameters(ThrowErrorListener.Instance)
                    {
                        FileService = fileService
                    }));

            var imports = AssemblyWalker.ListDllImports(assemblyDefinition);
            var modified = false;
            foreach (var import in imports.DistinctBy(import => import.Library))
            {
                if (import.Library.IndexOfAny(['/', '\\']) >= 0)
                {
                    continue; // Dependency path is already absolute or relative.
                }
                // Libraries in same directory as the assembly or runtime/ are like $ORIGIN in ELF RPATHs.
                else if (assemblyCandidateMap.TryFind(rid, import.Library, out var candidate))
                {
                    console.WriteLine($"    {import.Library} -> found: {Path.GetRelativePath(assembly.Directory.FullName, candidate)}");
                    dependencies.Add(new Dependency(assembly, import.Library, true));
                }
                else if (runtimeCandidateMap?.TryFind(rid, import.Library, out candidate) is true)
                {
                    console.WriteLine($"    {import.Library} -> found: {Path.GetRelativePath(assembly.Directory.FullName, candidate)}");
                    dependencies.Add(new Dependency(assembly, import.Library, true));
                }
                else if (libraryCandidateMap.TryFind(rid, import.Library, out candidate))
                {
                    modified = true;
                    import.Method.SetDllImportLibrary(candidate);
                    console.WriteLine($"    {import.Library} -> found: {candidate}");
                    dependencies.Add(new Dependency(assembly, import.Library, true));
                }
                else
                {
                    console.WriteLine($"    {import.Library} -> not found!");
                    dependencies.Add(new Dependency(assembly, import.Library, false));
                }
            }

            if (modified && !dryRun)
            {
                // Overwrite the contents of the file.
                assemblyDefinition.Write(assembly.FullName);
            }
        }

        return dependencies.DrainToImmutable();
    }
}
