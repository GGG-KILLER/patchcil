using System.CommandLine;
using System.CommandLine.IO;

namespace PatchCil.Helpers;

internal static class ConsoleHelper
{
    public static void WriteError(this IConsole console, string error)
    {
        if (!Console.IsOutputRedirected)
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
        }

        console.Error.WriteLine($"error: {error}");

        if (!Console.IsOutputRedirected)
        {
            Console.ResetColor();
        }
    }
}
