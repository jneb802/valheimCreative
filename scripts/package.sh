#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE_DIR="$ROOT/package"
PLUGIN_DIR="$PACKAGE_DIR/BepInEx/plugins/valheimCreative"
DIST_DIR="$ROOT/dist"
VERSION="0.2.0"

dotnet build "$ROOT/valheimCreative.csproj" -c Release
mkdir -p "$PLUGIN_DIR" "$DIST_DIR"
cp "$ROOT/bin/Release/valheimCreative.dll" "$PLUGIN_DIR/"
cp "$ROOT/bin/Release/valheimCreative.pdb" "$PLUGIN_DIR/"

(
  cd "$PACKAGE_DIR"
  zip -qr "$DIST_DIR/valheimCreative-$VERSION.zip" .
)

echo "$DIST_DIR/valheimCreative-$VERSION.zip"
