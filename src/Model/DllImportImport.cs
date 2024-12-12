using AsmResolver.DotNet;

namespace PatchCil.Model;

public readonly record struct DllImportImport(
    MethodDefinition Method,
    string Library,
    string Function);
