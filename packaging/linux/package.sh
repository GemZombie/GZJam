#!/usr/bin/env bash
# Packs a Linux publish folder into a tarball with a desktop entry and a per-user installer.
#   packaging/linux/package.sh <publish folder> <version> <output folder>
set -euo pipefail

publish="$1"
version="$2"
out="$3"

here="$(cd "$(dirname "$0")" && pwd)"
name="GZJam-$version-linux-x64"
stage="$(mktemp -d)"

mkdir -p "$stage/$name" "$out"
cp -R "$publish"/. "$stage/$name/"
cp "$here/install.sh" "$here/gzjam.desktop" "$stage/$name/"
chmod +x "$stage/$name/GZJam" "$stage/$name/install.sh"

tar -C "$stage" -czf "$out/$name.tar.gz" "$name"
rm -rf "$stage"
