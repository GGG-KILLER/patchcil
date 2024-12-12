// See https://aka.ms/new-console-template for more information
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

var printImportsOption = new Option<bool>(
    name: "--print-imports",
    description: "Whether libraries loaded through DllImport should be printed at the start of execution.",
    getDefaultValue: static () => false)
{
    Arity = ArgumentArity.ZeroOrOne,
};
rootCommand.Add(printImportsOption);

var assemblyPathArgument = new Argument<FileInfo>(
    name: "assembly",
    description: "The assembly to be used for the requested operations.")
{
    Arity = ArgumentArity.ExactlyOne,
}
    .ExistingOnly();
rootCommand.Add(assemblyPathArgument);

rootCommand.SetHandler((InvocationContext context) =>
{
    var console = context.Console;
    var shouldPrintImports = context.ParseResult.GetValueForOption(printImportsOption);
    var assembly = context.ParseResult.GetValueForArgument(assemblyPathArgument);

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
        console.Error.WriteLine($"error: provided file is not a .NET file: {ex.Message.TrimEnd('.')}.");
        context.ExitCode = -1;
        return;
    }

    if (shouldPrintImports)
    {
        var imports = AssemblyWalker.ListDllImports(assemblyDefinition)
            .Select(library => library.Library)
            .Distinct()
            .Order();
        console.WriteLine(string.Join(Environment.NewLine, imports));
    }

    context.ExitCode = 0;
});

return await rootCommand.InvokeAsync(args, new SystemConsole());
