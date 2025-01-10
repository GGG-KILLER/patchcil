{
  src,
  version,
  lib,
  buildDotnetModule,
  dotnetCorePackages,
  stdenv,
}:

buildDotnetModule {
  pname = "patchcil";
  inherit version src;

  nativeBuildInputs = [
    stdenv.cc
  ];

  projectFile = "src/PatchCil.csproj";
  nugetDeps = ./deps.nix;

  dotnet-sdk = dotnetCorePackages.sdk_9_0;
  dotnet-runtime = null;

  selfContainedBuild = true;
  dotnetFlags = [
    "-p:DebuggerSupport=false"
    "-p:StripSymbols=true"
    "-p:TrimmerRemoveSymbols=true"
  ];
  executables = [ "patchcil" ];

  postFixup = ''
    # Remove debug symbols as they shouldn't have anything in them.
    rm $out/lib/patchcil/patchcil.dbg
  '';

  meta = {
    description = "A tool for modifying CIL assemblies.";
    homepage = "https://github.com/GGG-KILLER/patchcil";
    license = lib.licenses.mit;
    maintainers = with lib.maintainers; [ ggg ];
    mainProgram = "patchcil";
    # There's no easy way to filter which platforms we have an ILCompiler for, so...
    platforms = [
      "x86_64-linux"
      "aarch64-linux"
      "x86_64-darwin"
      "aarch64-darwin"
      "x86_64-windows"
      "i686-windows"
    ];
  };
}
