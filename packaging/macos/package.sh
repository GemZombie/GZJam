#!/usr/bin/env bash
# Wraps a macOS publish folder in GZJam.app and a disk image. Runs on macOS (codesign, hdiutil).
#   packaging/macos/package.sh <publish folder> <version> <arch: arm64|x64> <output folder>
#
# The app is signed ad hoc only (Apple Silicon won't run unsigned code at all). Without an Apple
# Developer ID and notarization, macOS asks for confirmation the first time it's opened.
set -euo pipefail

publish="$1"
version="$2"
arch="$3"
out="$4"

here="$(cd "$(dirname "$0")" && pwd)"
short_version="${version%%-*}"   # 1.2.0-beta.3 -> 1.2.0: CFBundleShortVersionString is numbers only
stage="$(mktemp -d)"
app="$stage/GZJam.app"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$out"
cp -R "$publish"/. "$app/Contents/MacOS/"
sed -e "s/@SHORT_VERSION@/$short_version/" -e "s/@VERSION@/$version/" "$here/Info.plist" > "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/GZJam"

# Sign every file inside, then the bundle (the way Avalonia's macOS guide does it).
find "$app/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' f; do
  codesign --force --sign - "$f"
done
codesign --force --sign - "$app"
codesign --verify --verbose "$app"

ln -s /Applications "$stage/Applications"
hdiutil create -volname "GZJam $version" -srcfolder "$stage" -ov -format UDZO \
  "$out/GZJam-$version-macos-$arch.dmg"
rm -rf "$stage"
