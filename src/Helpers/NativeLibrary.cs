namespace PatchCil.Helpers;

internal static class NativeLibrary
{
    private static ReadOnlySpan<byte> ElfHeader => [0x7F, (byte)'E', (byte)'L', (byte)'F'];
    private static ReadOnlySpan<byte> MachoMhMagic => [0xFE, 0xED, 0xFA, 0xCE];
    private static ReadOnlySpan<byte> MachoMhMagic64 => [0xFE, 0xED, 0xFA, 0xCF];
    private static ReadOnlySpan<byte> MachoFatMagic => [0xCA, 0xFE, 0xBA, 0xBE];

    public static IEnumerable<string> ListVariations(string rid, string libraryName)
    {
        // Implemented based on https://github.com/dotnet/runtime/blob/v9.0.0/src/tests/Loader/AssemblyDependencyResolver/AssemblyDependencyResolverTests/NativeDependencyTests.cs
        var isUnix = RuntimeIdentifiers.IsUnix(rid);
        var isApple = RuntimeIdentifiers.IsApple(rid);
        var isWindows = RuntimeIdentifiers.IsWindows(rid);

        // Unix-like systems prefer the version with the extension, even if one without exists.
        if (isUnix)
        {
            if (isApple)
            {
                if (!libraryName.EndsWith(".dylib"))
                    yield return libraryName + ".dylib";
            }
            else
            {
                if (!libraryName.EndsWith(".so"))
                    yield return libraryName + ".so";
            }
        }

        yield return libraryName;

        if (isWindows)
        {
            // Add .dll suffix if there is no executable-like suffix already.
            if (!(libraryName.EndsWith(".dll") || libraryName.EndsWith(".exe")))
                yield return libraryName + ".dll";
        }
        else if (isApple)
        {
            // Try adding lib prefix
            if (!libraryName.StartsWith("lib"))
                yield return "lib" + libraryName;

            // Unix-based systems always add the extension.
            yield return libraryName + ".dylib";

            // Unix-based systems always add the extension.
            if (!libraryName.StartsWith("lib"))
                yield return "lib" + libraryName + ".dylib";
        }
        else if (isUnix)
        {
            // Try adding lib prefix
            yield return "lib" + libraryName;

            // Unix-based systems always add the extension.
            yield return libraryName + ".so";

            // Unix-based systems always add the extension.
            if (!libraryName.StartsWith("lib"))
                yield return "lib" + libraryName + ".so";
        }
    }

    public static bool IsForAnotherRuntime(string rid, string libraryName)
    {
        if (RuntimeIdentifiers.IsApple(rid))
        {
            return Path.GetExtension(libraryName) is ".exe" or ".dll" or ".so" || libraryName.Contains(".so.");
        }
        else if (RuntimeIdentifiers.IsUnix(rid))
        {
            return Path.GetExtension(libraryName) is ".exe" or ".dll" or ".dylib";
        }
        else
        {
            return Path.GetExtension(libraryName) is ".so" or ".dylib" || libraryName.Contains(".so.");
        }
    }

    public static bool IsElf(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            return false;
        return header.SequenceEqual(ElfHeader);
    }

    public static bool IsMacho(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            return false;

        // MH_MAGIC || MH_MAGIC_64 || FAT_MAGIC
        if (header.SequenceEqual(MachoMhMagic) || header.SequenceEqual(MachoMhMagic64) || header.SequenceEqual(MachoFatMagic))
            return true;

        // MH_CIGAM || MH_CIGAM_64 || FAT_CIGAM
        header.Reverse();
        return header.SequenceEqual(MachoMhMagic) || header.SequenceEqual(MachoMhMagic64) || header.SequenceEqual(MachoFatMagic);
    }

    public static bool IsLibrary(string rid, string path)
    {
        if (RuntimeIdentifiers.IsApple(rid))
        {
            return IsMacho(path);
        }
        else if (RuntimeIdentifiers.IsUnix(rid))
        {
            return IsElf(path);
        }
        else
        {
            // TODO: Implement proper PE verification.
            return Path.GetExtension(path) is ".dll" or ".exe";
        }
    }
}
