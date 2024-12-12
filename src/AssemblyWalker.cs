using AsmResolver.DotNet;
using PatchCil.Model;

namespace PatchCil;

internal static class AssemblyWalker
{
    public static IEnumerable<DllImportImport> ListDllImports(AssemblyDefinition assembly)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var type in module.GetAllTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (method.IsPInvokeImpl)
                    {
                        yield return new DllImportImport(
                            method,
                            method.ImplementationMap!.Scope!.Name!.ToString(),
                            method.ImplementationMap!.Name!.ToString());
                    }
                }
            }
        }
    }
}
