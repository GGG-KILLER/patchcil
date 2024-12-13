namespace PatchCil.Model;

internal readonly record struct Dependency(FileInfo Assembly, string LibraryName, bool Satisfied);
