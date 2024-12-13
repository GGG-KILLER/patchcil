using System.CommandLine;
using System.CommandLine.IO;
using PatchCil.Commands;

var rootCommand = new RootCommand("A tool for modifying CIL assemblies.")
{
    new MainCommand().Command,
};

return await rootCommand.InvokeAsync(args, new SystemConsole());
