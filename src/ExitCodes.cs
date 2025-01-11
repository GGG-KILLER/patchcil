namespace PatchCil;

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int InvalidRid = 1;
    public const int FileIsNotDotnetAssembly = 2;
    public const int MissingDependencies = 3;
}
