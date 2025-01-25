#! /usr/bin/env nix-shell
#! nix-shell -i bash -p nix hyperfine
# shellcheck shell=bash
set -euo pipefail

PATCHCIL="$(nix build --no-link --print-out-paths .#patchcil)"
mapfile -t LIBRARIES < <(nix build --no-link --print-out-paths nixpkgs\#{glibc,xorg.{libX11,libICE,libSM,libXrandr,libXi,libXcursor},glib,gtk3,libGL}.out)
AVALONIA_ILSPY="$(nix build --no-link --print-out-paths nixpkgs#avalonia-ilspy)"

RUN_FLAGS="auto \
    -r linux-x64 \
    --libs ${LIBRARIES[*]} \
    --ignore-missing c libc gdi32 kernel32 ntdll shell32 user32 Windows.UI.Composition winspool.drv libAvaloniaNative clr \
    --paths $AVALONIA_ILSPY \
    --dry-run"

hyperfine \
    --warmup 5 \
    --runs 50 \
    "$PATCHCIL/bin/patchcil $RUN_FLAGS" \
    "$@"
