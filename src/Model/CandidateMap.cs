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
        // Implemented based on https://github.com/dotnet/runtime/blob/v9.0.0/src/tests/Loader/AssemblyDependencyResolver/AssemblyDependencyResolverTests/NativeDependencyTests.cs
        var isUnix = RuntimeIdentifiers.IsUnix(rid);
        var isApple = RuntimeIdentifiers.IsApple(rid);
        var isWindows = RuntimeIdentifiers.IsWindows(rid);

        foreach (var candidateLocation in _candidateLocations)
        {
            // We only test for alternatives if the library name is not relative or absolute.
            // And unix-like systems prefer the version with the extension, even if one without exists.
            if (isUnix)
            {
                if (isApple)
                {
                    if (!name.EndsWith(".dylib") && tryFindInCandidate(candidateLocation, name + ".dylib", out candidate, recurse))
                        return true;
                }
                else
                {
                    if (!name.EndsWith(".so") && tryFindInCandidate(candidateLocation, name + ".so", out candidate, recurse))
                        return true;
                }
            }

            if (tryFindInCandidate(candidateLocation, name, out candidate, recurse))
                return true;

            if (isWindows)
            {
                // Add .dll suffix if there is no executable-like suffix already.
                if (!(name.EndsWith(".dll") || name.EndsWith(".exe")) && tryFindInCandidate(candidateLocation, name + ".dll", out candidate, recurse))
                    return true;
            }
            else if (isApple)
            {
                // Try adding lib prefix
                if (!name.StartsWith("lib") && tryFindInCandidate(candidateLocation, "lib" + name, out candidate, recurse))
                    return true;

                // Unix-based systems always add the extension.
                if (tryFindInCandidate(candidateLocation, name + ".dylib", out candidate, recurse))
                    return true;

                // Unix-based systems always add the extension.
                if (!name.StartsWith("lib") && tryFindInCandidate(candidateLocation, "lib" + name + ".dylib", out candidate, recurse))
                    return true;
            }
            else if (isUnix)
            {
                // Try adding lib prefix
                if (tryFindInCandidate(candidateLocation, "lib" + name, out candidate, recurse))
                    return true;

                // Unix-based systems always add the extension.
                if (tryFindInCandidate(candidateLocation, name + ".so", out candidate, recurse))
                    return true;

                // Unix-based systems always add the extension.
                if (!name.StartsWith("lib") && tryFindInCandidate(candidateLocation, "lib" + name + ".so", out candidate, recurse))
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
