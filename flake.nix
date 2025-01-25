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
          patchcil-avalonia-sample = pkgs.writeShellScriptBin "patchcil-avalonia-sample" ''
            set -euo pipefail

            declare -a PATCHCIL_FLAGS
            set_runtime=yes
            set_libs=yes
            set_ignored=yes
            set_dry_run=yes

            params=("$@")
            length=''${#params[*]}
            for ((n = 0; n < length; n += 1)); do
              p="''${params[n]}"
              case $p in
                --no-runtime)
                  set_runtime=no
                ;;
                -r|--runtime|--rid)
                  set_runtime=no
                  PATCHCIL_FLAGS+=("$p" "''${params[n + 1]}")
                  n=$((n + 1))
                ;;
                -r*|--runtime=*|--rid=*)
                  set_runtime=no
                  PATCHCIL_FLAGS+=("$p")
                ;;
                --no-libs)
                  set_libs=no
                ;;
                --no-ignores)
                  set_ignored=no
                ;;
                --no-dry-run)
                  set_dry_run=no
                ;;
                *) # Using an error macro, we will make sure the compiler gives an understandable error message
                  PATCHCIL_FLAGS+=("$p")
                ;;
              esac
            done

            if [[ "$set_runtime" == "yes" ]]; then
              PATCHCIL_FLAGS+=("--runtime" "${pkgs.dotnetCorePackages.systemToDotnetRid pkgs.stdenv.hostPlatform.system}")
            fi

            if [[ "$set_libs" == "yes" ]]; then
              PATCHCIL_FLAGS+=("--libs" ${
                lib.escapeShellArgs (
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
              })
            fi

            if [[ "$set_ignored" == "yes" ]]; then
              PATCHCIL_FLAGS+=( \
                "--ignore-missing" \
                "c" \
                "libc" \
                "gdi32" \
                "kernel32" \
                "ntdll" \
                "shell32" \
                "user32" \
                "Windows.UI.Composition" \
                "winspool.drv" \
                "libAvaloniaNative" \
                "clr" \
              )
            fi

            PATCHCIL_FLAGS+=("--paths" "${
              pkgs.avalonia-ilspy.overrideAttrs (prev: {
                dontAutoPatchcil = true;
              })
            }")

            if [[ "$set_dry_run" == "yes" ]]; then
              PATCHCIL_FLAGS+=("--dry-run")
            fi

            set -x
            exec ${lib.getExe self.packages.${system}.patchcil} auto "''${PATCHCIL_FLAGS[@]}"
          '';
        }
      );
    };
}
