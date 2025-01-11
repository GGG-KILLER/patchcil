#! /usr/bin/env nix-shell
#! nix-shell -i bash -p nix hyperfine
# shellcheck shell=bash
set -euo pipefail

mapfile -t LIBRARIES < <(nix build --no-link --print-out-paths nixpkgs\#{glibc,xorg.libX11,xorg.libICE,xorg.libSM,xorg.libXrandr,xorg.libXi,xorg.libXcursor,glib,gtk3,libGL}.out)
AVALONIA_ILSPY="$(nix build nixpkgs#avalonia-ilspy --print-out-paths --no-link)"

RUN_FLAGS="auto \
    -r linux-x64 \
    --libs ${LIBRARIES[*]} \
    --ignore-missing kernel32 libAvaloniaNative gdi32 user32 shell32 ntdll clr \
    --paths $AVALONIA_ILSPY \
    --dry-run"

hyperfine \
    --shell=none \
    --warmup 5 \
    --runs 50 \
    'result-aot/bin/patchcil '"$RUN_FLAGS" \
    'result-cil/bin/patchcil '"$RUN_FLAGS"
