using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using AsmResolver.IO;
using AsmResolver.PE;
using AsmResolver.PE.File;
using PatchCil.Helpers;

namespace PatchCil.Commands;

internal sealed class MainCommand
{
    private readonly Argument<FileInfo> _assemblyPathArgument;
    private readonly Option<bool> _printNeededOption;
    private readonly Option<ImmutableDictionary<string, string>> _libraryOverridesOption;
    private readonly Option<FileInfo?> _outputOption;

    public MainCommand()
    {
        Command = new Command("file", "Patches a CIL file to support loading native libraries with the correct paths.");

        _assemblyPathArgument = new Argument<FileInfo>(
            name: "assembly",
            description: "The assembly to be used for the requested operations.")
        {
            Arity = ArgumentArity.ExactlyOne,
        }
            .ExistingOnly();

        _printNeededOption = new Option<bool>(
            name: "--print-needed",
            description: "Whether libraries loaded through DllImport should be printed at the start of execution.",
            getDefaultValue: static () => false)
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        _libraryOverridesOption = new Option<ImmutableDictionary<string, string>>(
            name: "--set-library-path",
            description: "Sets the path for a given library. Accepts values in the format <name>:<path>.",
            parseArgument: static result =>
            {
                var overrides = ImmutableDictionary.CreateBuilder<string, string>(
                    StringComparer.Ordinal,
                    StringComparer.Ordinal);

                foreach (var token in result.Tokens)
                {
                    if (!token.Value.Contains(':'))
                    {
                        result.ErrorMessage = $"Library path override '{token.Value}' is not in the correct format. Expected <name>:<path>.";
                        return ImmutableDictionary<string, string>.Empty;
                    }

                    var colonPos = token.Value.IndexOf(':');
                    var name = token.Value[..colonPos];
                    var path = token.Value[(colonPos + 1)..];

                    if (!File.Exists(path))
                    {
                        result.ErrorMessage = $"Path does not exist: {path}";
                        return ImmutableDictionary<string, string>.Empty;
                    }

                    if (!overrides.TryAdd(name, path))
                    {
                        result.ErrorMessage = $"Multiple paths were provided for library: {name}";
                        return ImmutableDictionary<string, string>.Empty;
                    }
                }

                return overrides.ToImmutable();
            })
        {
            Arity = ArgumentArity.ZeroOrMore,
        };

        _outputOption = new Option<FileInfo?>(
            aliases: ["-o", "--output"],
            description: "Sets the path for a given library. Accepts values in the format <name>:<path>.",
            getDefaultValue: () => null)
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        _outputOption.AddValidator(result =>
        {
            if (result.GetValueForOption(_outputOption)?.Directory?.Exists is false)
            {
                result.ErrorMessage = "Output directory does not exist.";
            }
        });

        Command.Add(_assemblyPathArgument);
        Command.Add(_printNeededOption);
        Command.Add(_libraryOverridesOption);
        Command.Add(_outputOption);

        Command.SetHandler(CommandHandler);
    }

    public Command Command { get; }

    private void CommandHandler(InvocationContext context)
    {
        var console = context.Console;
        var shouldPrintNeeded = context.ParseResult.GetValueForOption(_printNeededOption);
        var libraryOverrides = context.ParseResult.GetValueForOption(_libraryOverridesOption)
                               ?? ImmutableDictionary<string, string>.Empty;
        var assembly = context.ParseResult.GetValueForArgument(_assemblyPathArgument);
        var output = context.ParseResult.GetValueForOption(_outputOption) ?? assembly;
        var modified = false;
        var fileService = new ByteArrayFileService();

        PEFile peFile;
        PEImage peImage;
        AssemblyDefinition assemblyDefinition;
        try
        {
            var peReaderParameters = new PEReaderParameters
            {
                FileService = fileService,
                ErrorListener = ThrowErrorListener.Instance,
            };
            peFile = PEFile.FromFile(fileService.OpenFile(assembly.FullName));
            peImage = PEImage.FromFile(peFile, peReaderParameters);
            assemblyDefinition = AssemblyDefinition.FromImage(
                peImage,
                new ModuleReaderParameters(
                    assembly.Directory!.FullName,
                    peReaderParameters));
        }
        catch (BadImageFormatException ex)
        {
            if (!Console.IsOutputRedirected)
            {
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Red;
            }
            console.Error.WriteLine($"Provided file is not a .NET Assembly: {ex.Message.TrimEnd('.')}.");
            if (!Console.IsOutputRedirected)
            {
                Console.ResetColor();
            }
            context.ExitCode = ExitCodes.FileIsNotDotnetAssembly;
            return;
        }

        var imports = AssemblyWalker.ListDllImports(assemblyDefinition)
            .ToImmutableArray();

        if (shouldPrintNeeded)
        {
            console.WriteLine(string.Join(
                Environment.NewLine,
                imports.Select(static library => library.Library)
                    .Distinct()
                    .Order()));
        }

        if (libraryOverrides.Count > 0)
        {
            for (int i = 0; i < imports.Length; i++)
            {
                var import = imports[i];
                if (libraryOverrides.TryGetValue(import.Library, out var path))
                {
                    import.Method.ImplementationMap!.Scope!.Name = path;
                    imports = imports.SetItem(i, import with { Library = path });
                    modified = true;
                }
            }
        }

        if (modified)
        {
            assemblyDefinition.Write(output.FullName);
        }

        context.ExitCode = ExitCodes.Ok;
    }
}
