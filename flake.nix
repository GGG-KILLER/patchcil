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
        in
        {
          patchcil = patchcilFor pkgs;
          default = self.packages.${system}.patchcil;
        }
      );
    };
}
