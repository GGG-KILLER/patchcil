using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using PatchCil.Helpers;

namespace PatchCil.Model;

internal sealed class CandidateMap(bool recurse)
{
    private readonly List<FileSystemInfo> _candidateLocations = [];
    private readonly ConditionalWeakTable<string, string?> _candidateCache = [];

    public CandidateMap(bool recurse, params IEnumerable<FileSystemInfo> candidateLocations)
        : this(recurse)
    {
        _candidateLocations = [.. candidateLocations];
    }

    public void Add(FileSystemInfo candidateLocation)
    {
        _candidateLocations.Add(candidateLocation);
    }

    public bool TryFind(string rid, string name, [NotNullWhen(true)] out string? candidate)
    {
        candidate = _candidateCache.GetValue($"{rid}:{name}", _ =>
        {
            foreach (var candidateLocation in _candidateLocations)
            {
                foreach (var variation in NativeLibrary.ListVariations(rid, name))
                {
                    if (tryFindInCandidate(candidateLocation, variation, out var candidate, recurse))
                        return candidate;
                }
            }

            return null;
        });

        return candidate != null;

        static bool tryFindInCandidate(FileSystemInfo candidateLocation, string name, [NotNullWhen(true)] out string? match, bool recurse)
        {
            if (candidateLocation is FileInfo file)
            {
                if (file.Name == name)
                {
                    match = file.FullName;
                    return true;
                }
            }
            else if (candidateLocation is DirectoryInfo directory)
            {
                using var enumerator = directory.EnumerateFiles(name, new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    MatchCasing = MatchCasing.PlatformDefault,
                    MatchType = MatchType.Simple,
                    RecurseSubdirectories = recurse,
                }).GetEnumerator();

                if (enumerator.MoveNext())
                {
                    match = enumerator.Current.FullName;
                    return true;
                }
            }

            match = null;
            return false;
        }
    }
}
