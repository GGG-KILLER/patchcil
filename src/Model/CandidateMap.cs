using System.Diagnostics.CodeAnalysis;
using PatchCil.Helpers;

namespace PatchCil.Model;

internal sealed class CandidateMap(bool recurse)
{
    private readonly List<FileSystemInfo> _candidateLocations = [];

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
        foreach (var candidateLocation in _candidateLocations)
        {
            foreach (var variation in NativeLibrary.ListVariations(rid, name))
            {
                if (tryFindInCandidate(candidateLocation, variation, out candidate, recurse))
                    return true;
            }
        }

        candidate = null;
        return false;

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
                using var enumerator = Directory.EnumerateFiles(directory.FullName, name, new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    MatchCasing = MatchCasing.PlatformDefault,
                    MatchType = MatchType.Simple,
                    RecurseSubdirectories = recurse,
                }).GetEnumerator();

                if (enumerator.MoveNext())
                {
                    match = enumerator.Current;
                    return true;
                }
            }

            match = null;
            return false;
        }
    }
}
