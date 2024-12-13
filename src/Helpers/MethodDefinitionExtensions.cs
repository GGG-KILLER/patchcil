using AsmResolver.DotNet;

namespace PatchCil.Helpers;

internal static class MethodDefinitionExtensions
{
    public static void SetDllImportLibrary(this MethodDefinition method, string library)
    {
        method.ImplementationMap!.Scope!.Name = library;
    }
}
