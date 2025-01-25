using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Runtime.CompilerServices;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using AsmResolver.PE;
using DotNet.Globbing;
using PatchCil.Helpers;
using PatchCil.Model;

namespace PatchCil.Commands;

internal sealed class AutoCommand
{
    private readonly Option<string> _ridOption;
    private readonly Option<bool> _noRecurseOption;
    private readonly Option<IEnumerable<FileSystemInfo>> _libraryPathsOption;
    private readonly Option<IEnumerable<string>> _skipRewrite;
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
        };

        _skipRewrite = new Option<IEnumerable<string>>(
            name: "--skip-rewrite",
            description: "Skip rewriting libraries that match the provided globs.")
        {
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };

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
        var libraryPaths = context.ParseResult.GetValueForOption(_libraryPathsOption)!
                                              .Where(item => item.Exists)
                                              .ToImmutableArray();
        var skipRewrite = context.ParseResult.GetValueForOption(_skipRewrite)
                                                ?.Where(val => !string.IsNullOrWhiteSpace(val))
                                                .Distinct()
                                                .Select(Glob.Parse)
                                                .ToImmutableArray()
            ?? [];
        var allowedMissing = context.ParseResult.GetValueForOption(_ignoreMissingOption)
                                                ?.Where(val => !string.IsNullOrWhiteSpace(val))
                                                .Distinct()
                                                .Select(Glob.Parse)
                                                .ToImmutableArray()
            ?? [];
        var dryRun = context.ParseResult.GetValueForOption(_dryRunOption);

        if (!RuntimeIdentifiers.IsValid(rid))
        {
            context.Console.WriteError("invalid RID (Runtime Identifier) provided.");
            context.ExitCode = ExitCodes.InvalidRid;
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
            .ToImmutableArray();

        if (assemblies.Length == 0)
        {
            context.Console.WriteLine("notice: no assemblies to patch found.");
            return;
        }

        var dependencies = AutoPatchAssemblies(context.Console, recurse, rid, libraryPaths, assemblies, skipRewrite, dryRun);
        var missingDependencies = dependencies.Where(x => !x.Satisfied).ToImmutableArray();

        var failed = false;
        var actuallyMissing = new HashSet<string>(StringComparer.Ordinal);
        context.Console.WriteLine($"patchcil-auto: {missingDependencies.Length} dependencies could not be satisfied");
        foreach (var missing in missingDependencies)
        {
            if (allowedMissing.Any(pattern => pattern.IsMatch(missing.LibraryName)))
            {
                context.Console.WriteLine($"warn: patchcil-auto ignoring missing {missing.LibraryName} wanted by {missing.Assembly} (allowed by user to be missing)");
            }
            // If the library has an extension and it is not the extension for libraries for a given RID,
            // then we can ignore them since we won't be using them.
            else if (NativeLibrary.IsForAnotherRuntime(rid, missing.LibraryName))
            {
                context.Console.WriteLine($"warn: patchcil-auto ignoring missing {missing.LibraryName} wanted by {missing.Assembly} (extension for different RID?)");
            }
            else
            {
                context.Console.WriteLine($"error: patchcil-auto could not satisfy dependency {missing.LibraryName} wanted by {missing.Assembly}");
                failed = true;
                actuallyMissing.Add(Path.GetFileName(missing.LibraryName));
            }
        }

        if (failed)
        {
            context.Console.Error.WriteLine("patchcil-auto failed to find all the required dependencies.");
            context.Console.Error.WriteLine("Add the missing dependencies to --libs or use `--ignore-missing foo.so.1 bar.so etc.so`.");
            context.Console.Error.WriteLine($"error: missing libraries: {string.Join(", ", actuallyMissing)}");
            context.ExitCode = ExitCodes.MissingDependencies;
            return;
        }

        context.ExitCode = ExitCodes.Ok;
    }

    public static ImmutableArray<Dependency> AutoPatchAssemblies(
        IConsole console,
        bool recurse,
        string rid,
        ImmutableArray<FileSystemInfo> libraryPaths,
        ImmutableArray<FileInfo> assemblies,
        ImmutableArray<Glob> skipRewrite,
        bool dryRun)
    {
        var dependencies = ImmutableArray.CreateBuilder<Dependency>();
        var libraryCandidateMap = new CandidateMap(recurse, libraryPaths);
        var relativeCandidateMapCache = new ConditionalWeakTable<string, CandidateMap>();

        foreach (var assembly in assemblies)
        {
            AssemblyDefinition assemblyDefinition;
            try
            {
                assemblyDefinition = AssemblyDefinition.FromFile(
                assembly.FullName,
                new ModuleReaderParameters(
                    assembly.Directory!.FullName,
                    new PEReaderParameters(ThrowErrorListener.Instance)));
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            console.WriteLine($"searching for dependencies of {assembly}");

            var relativeCandidateMap = relativeCandidateMapCache.GetValue(
                assembly.Directory!.FullName,
                path =>
                {
                    // TODO: Read information from .deps.json instead of using this hacky method.
                    var relativeFolders = RuntimeIdentifiers.EnumerateSelfAndDescendants(rid)
                        .Select(rid => Path.Combine(path, "runtimes", rid, "native"))
                        .Where(Directory.Exists)
                        .Select(path => new DirectoryInfo(path))
                        // But keep this one because it always checks relative paths.
                        .Prepend(new DirectoryInfo(path));

                    return new CandidateMap(false, relativeFolders);
                });

            var imports = AssemblyWalker.ListDllImports(assemblyDefinition);
            var modified = false;
            foreach (var group in imports.GroupBy(import => import.Library))
            {
                if (skipRewrite.Any(skip => skip.IsMatch(group.Key)))
                {
                    continue;
                }
                else if (group.Key.IndexOfAny(['/', '\\']) >= 0)
                {
                    continue; // Dependency path is already absolute or relative.
                }
                // Libraries in same directory as the assembly or runtime/ are like $ORIGIN in ELF RPATHs.
                else if (relativeCandidateMap.TryFind(rid, group.Key, out var candidate))
                {
                    console.WriteLine($"    {group.Key} [x{group.Count()}] -> found: {Path.GetRelativePath(assembly.Directory.FullName, candidate)}");
                    dependencies.Add(new Dependency(assembly, group.Key, true));
                }
                else if (libraryCandidateMap.TryFind(rid, group.Key, out candidate))
                {
                    if (!NativeLibrary.IsLibrary(rid, candidate))
                    {
                        console.WriteLine($"    {group.Key} [x{group.Count()}] -> not a native library: {candidate}");
                        dependencies.Add(new Dependency(assembly, group.Key, false));
                    }
                    else
                    {
                        modified = true;
                        foreach (var import in group)
                        {
                            import.Method.SetDllImportLibrary(candidate);
                        }
                        console.WriteLine($"    {group.Key} [x{group.Count()}] -> found: {candidate}");
                        dependencies.Add(new Dependency(assembly, group.Key, true));
                    }
                }
                else
                {
                    console.WriteLine($"    {group.Key} [x{group.Count()}] -> not found!");
                    dependencies.Add(new Dependency(assembly, group.Key, false));
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
