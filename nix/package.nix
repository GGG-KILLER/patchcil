{
  src,
  version,
  lib,
  buildDotnetModule,
  dotnetCorePackages,
}:

buildDotnetModule {
  pname = "patchcil";
  inherit version src;

  projectFile = "src/PatchCil.csproj";
  nugetDeps = ./deps.nix;

  dotnet-sdk = dotnetCorePackages.sdk_9_0;
  dotnet-runtime = dotnetCorePackages.runtime_9_0;

  executables = [ "patchcil" ];

  meta = {
    description = "A tool for modifying CIL assemblies.";
    homepage = "https://github.com/GGG-KILLER/patchcil";
    license = lib.licenses.mit;
    maintainers = with lib.maintainers; [ ggg ];
    mainProgram = "patchcil";
    # We run pretty much wherever .NET will let us.
    inherit (dotnetCorePackages.runtime_9_0.meta) platforms;
  };
}
