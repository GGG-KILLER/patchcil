#! /usr/bin/env nix-shell
#! nix-shell -i bash -p dotnetCorePackages.sdk_9_0 wget
# shellcheck shell=bash
set -euo pipefail

SCRIPT_DIR="$(dirname "${BASH_SOURCE[0]}")"
RID_PATH=$(mktemp)
trap 'rm "$RID_PATH"' EXIT

wget -O "$RID_PATH" https://github.com/dotnet/runtime/raw/refs/heads/main/src/libraries/Microsoft.NETCore.Platforms/src/PortableRuntimeIdentifierGraph.json
dotnet run --project "$SCRIPT_DIR"/tools/rid-graph-gen/rid-graph-gen.csproj -- "$RID_PATH" "$SCRIPT_DIR"/src/Helpers/RuntimeIdentifiers.cs
