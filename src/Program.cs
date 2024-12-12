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
using PatchCil;

var rootCommand = new RootCommand("Patches a CIL file to support loading native libraries with the correct paths.");

var assemblyPathArgument = new Argument<FileInfo>(
    name: "assembly",
    description: "The assembly to be used for the requested operations.")
{
    Arity = ArgumentArity.ExactlyOne,
}
    .ExistingOnly();
rootCommand.Add(assemblyPathArgument);

var printNeededOption = new Option<bool>(
    name: "--print-needed",
    description: "Whether libraries loaded through DllImport should be printed at the start of execution.",
    getDefaultValue: static () => false)
{
    Arity = ArgumentArity.ZeroOrOne,
};
rootCommand.Add(printNeededOption);

var libraryOverridesOption = new Option<ImmutableDictionary<string, string>>(
    name: "--set-library-path",
    description: "Sets the path for a given library. Accepts values in the format <name>:<path>.",
    parseArgument: result =>
    {
        var overrides = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal,
            StringComparer.Ordinal);

        foreach (var token in result.Tokens)
        {
            if (!token.Value.Contains(':'))
            {
                result.ErrorMessage = $"library path override '{token.Value}' is not in the correct format. Expected <name>:<path>.";
                return ImmutableDictionary<string, string>.Empty;
            }

            var colonPos = token.Value.IndexOf(':');
            var name = token.Value[..colonPos];
            var path = token.Value[(colonPos + 1)..];

            if (!File.Exists(path))
            {
                result.ErrorMessage = $"path does not exist: {path}";
                return ImmutableDictionary<string, string>.Empty;
            }

            if (!overrides.TryAdd(name, path))
            {
                result.ErrorMessage = $"multiple paths were provided for library: {name}";
                return ImmutableDictionary<string, string>.Empty;
            }
        }

        return overrides.ToImmutable();
    })
{
    Arity = ArgumentArity.ZeroOrMore,
};
rootCommand.Add(libraryOverridesOption);

var outputOption = new Option<FileInfo?>(
    name: "--output",
    description: "Sets the path for a given library. Accepts values in the format <name>:<path>.",
    getDefaultValue: () => null)
{
    Arity = ArgumentArity.ZeroOrOne,
};
outputOption.AddAlias("-o");
rootCommand.Add(outputOption);

rootCommand.SetHandler((InvocationContext context) =>
{
    var console = context.Console;
    var shouldPrintNeeded = context.ParseResult.GetValueForOption(printNeededOption);
    var libraryOverrides = context.ParseResult.GetValueForOption(libraryOverridesOption)
                           ?? ImmutableDictionary<string, string>.Empty;
    var assembly = context.ParseResult.GetValueForArgument(assemblyPathArgument);
    var output = context.ParseResult.GetValueForOption(outputOption) ?? assembly;
    var modified = false;
    var fileService = new ByteArrayFileService();

    if (output.Directory is null || !output.Directory.Exists)
    {
        console.Error.WriteLine("error: output directory does not exist.");
        context.ExitCode = ExitCodes.OutputDirectoryDoesNotExist;
        return;
    }

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
        console.Error.WriteLine($"error: provided file is not a .NET file: {ex.Message.TrimEnd('.')}.");
        context.ExitCode = ExitCodes.FileIsNotDotnetAssembly;
        return;
    }

    var imports = AssemblyWalker.ListDllImports(assemblyDefinition)
        .ToImmutableArray();

    if (shouldPrintNeeded)
    {
        console.WriteLine(string.Join(
            Environment.NewLine,
            imports.Select(library => library.Library)
                .Distinct()
                .Order()));
    }

    if (libraryOverrides.Count > 0)
    {
        for (int i = 0; i < imports.Length; i++)
        {
            PatchCil.Model.DllImportImport import = imports[i];
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

    context.ExitCode = 0;
});

return await rootCommand.InvokeAsync(args, new SystemConsole());
