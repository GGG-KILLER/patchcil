{
  description = "A tool for modifying CIL assemblies.";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";

  outputs =
    { self, nixpkgs }:
    let
      supportedSystems =
        nixpkgs.legacyPackages.x86_64-linux.dotnetCorePackages.runtime_9_0.meta.platforms;
      forAllSystems = nixpkgs.lib.genAttrs supportedSystems;

      version = nixpkgs.lib.removeSuffix "\n" (builtins.readFile ./version);

      patchcilFor =
        pkgs:
        pkgs.callPackage ./nix/package.nix {
          inherit version;
          src = self;
        };
    in
    {
      overlays.default = final: prev: {
        patchcil-new = patchcilFor final;
      };

      devShells = forAllSystems (system: {
        default = self.packages.${system}.patchcil;
      });

      packages = forAllSystems (
        system:
        let
          pkgs = nixpkgs.legacyPackages.${system};
          inherit (pkgs) lib;
        in
        {
          patchcil = patchcilFor pkgs;
          default = self.packages.${system}.patchcil;
        }
        // lib.optionalAttrs pkgs.stdenvNoCC.hostPlatform.isLinux {
          patchcil-avalonia-sample =
            pkgs.runCommandNoCC "patchcil-avalonia-sample"
              {
                nativeBuildInputs = [ pkgs.makeBinaryWrapper ];
                meta.mainProgram = "patchcil-avalonia-sample";
              }
              ''
                mkdir -p $out/bin
                makeWrapper ${lib.getExe self.packages.${system}.patchcil} $out/bin/patchcil-avalonia-sample \
                  --add-flags auto \
                  --add-flags "--runtime ${pkgs.dotnetCorePackages.systemToDotnetRid pkgs.stdenv.hostPlatform.system}" \
                  --add-flags "--libs ${
                    lib.concatStringsSep " " (
                      map (pkg: "${(lib.getLib pkg)}/lib") (
                        with pkgs;
                        [
                          glibc
                          xorg.libX11
                          xorg.libICE
                          xorg.libSM
                          xorg.libXrandr
                          xorg.libXi
                          xorg.libXcursor
                          glib
                          gtk3
                          libGL
                        ]
                      )
                    )
                  }" \
                  --add-flags "--ignore-missing c libc gdi32 kernel32 ntdll shell32 user32 Windows.UI.Composition winspool.drv libAvaloniaNative clr" \
                  --add-flags "--paths ${
                    pkgs.avalonia-ilspy.overrideAttrs (prev: {
                      dontAutoPatchcil = true;
                    })
                  }" \
                  --add-flags "--dry-run"
              '';
        }
      );
    };
}
