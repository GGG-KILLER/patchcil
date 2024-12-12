{
  description = "A tool for modifying CIL assemblies.";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";

  outputs =
    { self, nixpkgs }:

    let
      supportedSystems = [
        "x86_64-linux"
        "i686-linux"
        "aarch64-linux"
        "riscv64-linux"
      ];
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

      checks = forAllSystems (system: {
        build = self.hydraJobs.build.${system};
      });

      devShells = forAllSystems (system: {
        default = self.packages.${system}.patchcil;
      });

      packages = forAllSystems (
        system:
        let
          pkgs = nixpkgs.legacyPackages.${system};
        in
        {
          patchcil = patchcilFor pkgs;
          default = self.packages.${system}.patchcil;
        }
      );

    };

}
