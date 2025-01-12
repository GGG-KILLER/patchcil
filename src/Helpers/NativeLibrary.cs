namespace PatchCil.Helpers;

internal static class NativeLibrary
{
    public static IEnumerable<string> ListVariations(string rid, string libraryName)
    {
        // Implemented based on https://github.com/dotnet/runtime/blob/v9.0.0/src/tests/Loader/AssemblyDependencyResolver/AssemblyDependencyResolverTests/NativeDependencyTests.cs
        var isUnix = RuntimeIdentifiers.IsUnix(rid);
        var isApple = RuntimeIdentifiers.IsApple(rid);
        var isWindows = RuntimeIdentifiers.IsWindows(rid);

        // We only test for alternatives if the library name is not relative or absolute.
        // And unix-like systems prefer the version with the extension, even if one without exists.
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
}
