﻿// See https://aka.ms/new-console-template for more information
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length < 2)
{
    Console.WriteLine($"""
        Usage:
            {Path.GetFileName(Environment.GetCommandLineArgs()[0])} <PortableRuntimeIdentifierGraph.json> <RuntimeIdentifiers.cs>
        """);
    return 1;
}

if (!File.Exists(args[0]))
{
    Console.WriteLine("error: File not found.");
    return 1;
}

RuntimeGraph graph;
await using (var stream = File.OpenRead(args[0]))
    graph = await JsonSerializer.DeserializeAsync<RuntimeGraph>(stream)
        ?? throw new InvalidOperationException("Invalid portable RID graph file.");

var map = graph.Runtimes.Keys.Where(static x => x is not ("base" or "any"))
    .ToImmutableDictionary(static rid => rid, rid =>
    {
        var added = new HashSet<string>([rid, "base"]);
        var list = new List<string>([rid]);
        for (var idx = 0; idx < list.Count; idx++)
        {
            var currentRid = list[idx];
            list.AddRange(graph.Runtimes[currentRid].Import.Where(import => added.Add(import)));
        }
        return list.ToImmutableArray();
    }, StringComparer.Ordinal);

// No longer need these
graph.Runtimes.Remove("any");
graph.Runtimes.Remove("base");

await using var writer = File.CreateText(args[1]);

await writer.WriteLineAsync($$"""
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace PatchCil.Helpers;

#nullable enable

internal static class RuntimeIdentifiers
{
    public static ImmutableArray<string> All { get; } = ["{{string.Join("\", \"", graph.Runtimes.Keys)}}"];

    public static bool IsValid([NotNullWhen(true)] string? value)
    {
        return value is "{{string.Join("\" or \"", graph.Runtimes.Keys)}}";
    }

    public static bool IsWindows(string value) =>
        value is "{{string.Join("\" or \"", graph.Runtimes.Keys.Where(rid => map[rid].Contains("win")))}}";

    public static bool IsApple(string value) =>
        value is "{{string.Join("\" or \"", graph.Runtimes.Keys.Where(rid => map[rid].Contains("ios") || map[rid].Contains("osx")))}}";

    public static bool IsUnix(string value) =>
        value is "{{string.Join("\" or \"", graph.Runtimes.Keys.Where(rid => map[rid].Contains("unix")))}}";

    public static string GetLibraryExtension(string value)
    {
        if (IsWindows(value))
        {
            return ".dll";
        }
        else if (IsApple(value))
        {
            return ".dylib";
        }
        else if (IsUnix(value))
        {
            return ".so";
        }
        else
        {
            return "";
        }
    }

    public static IEnumerable<string> EnumerateSelfAndDescendants(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsValid(value))
        {
            throw new ArgumentException("Provided RID is not a valid RID.", nameof(value));
        }

        switch (value)
        {
""");

foreach (var rid in graph.Runtimes.Keys)
{
    await writer.WriteLineAsync($"""
            case "{rid}":
""");

    foreach (var currentRid in map[rid])
    {
        await writer.WriteLineAsync($"""
                yield return "{currentRid}";
""");
    }

    await writer.WriteLineAsync("""
                break;
""");
}

await writer.WriteLineAsync("""
        }
    }
}
""");

return 0;

file sealed record RuntimeGraph(
    [property: JsonRequired, JsonPropertyName("runtimes")]
    Dictionary<string, Runtime> Runtimes
);

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Local", Justification = "Implicitly used.")]
file sealed record Runtime(
    [property: JsonRequired, JsonPropertyName("#import")]
    List<string> Import
);
