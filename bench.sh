#! /usr/bin/env nix-shell
#! nix-shell -i bash -p nix hyperfine
# shellcheck shell=bash
set -euo pipefail

hyperfine \
    --warmup 5 \
    --runs 50 \
    "$(nix build --no-link --print-out-paths .#patchcil-avalonia-sample)/bin/patchcil-avalonia-sample" \
    "$@"
