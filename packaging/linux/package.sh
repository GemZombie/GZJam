#!/usr/bin/env bash
# Packs a Linux publish folder into a tarball with a desktop entry and a per-user installer, and
# into a .deb, an .rpm and an Arch package (#249), made by nFPM (nfpm.yaml) if it's installed.
#   packaging/linux/package.sh <publish folder> <version> <output folder>
# GZJAM_PACKAGES=required (as the release workflow sets it) fails without nFPM, rather than
# making the tarball alone.
set -euo pipefail

publish="$1"
version="$2"
out="$(mkdir -p "$3" && cd "$3" && pwd)"

here="$(cd "$(dirname "$0")" && pwd)"
root="$here/../.."
name="GZJam-$version-linux-x64"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/$name"
cp -R "$publish"/. "$stage/$name/"
cp "$here/install.sh" "$here/gzjam.desktop" "$stage/$name/"
cp "$root/src/GZJam.App/Assets/gzjam.png" "$stage/$name/"
chmod +x "$stage/$name/GZJam" "$stage/$name/install.sh"

tar -C "$stage" -czf "$out/$name.tar.gz" "$name"

if ! command -v nfpm > /dev/null; then
  if [[ "${GZJAM_PACKAGES:-}" == "required" ]]; then
    echo "nfpm isn't installed: it makes the .deb, .rpm and Arch package" >&2
    exit 1
  fi
  echo "nfpm isn't installed: made the tarball only"
  exit 0
fi

# The packages' files: the program, a launcher entry that runs the `gzjam` command, the icon and
# the license
pkg="$stage/pkg"
mkdir -p "$pkg"
cp -R "$publish"/. "$pkg/app/"
chmod +x "$pkg/app/GZJam"
sed "s|^Exec=.*|Exec=/usr/bin/gzjam %F|" "$here/gzjam.desktop" > "$pkg/gzjam.desktop"
cp "$root/src/GZJam.App/Assets/gzjam.png" "$pkg/"
cp "$root/LICENSE" "$pkg/"

# A pre-release (1.0.0-beta.2) becomes 1.0.0~beta.2 in the .deb and .rpm, which sorts before
# 1.0.0. nFPM leaves it out of the Arch package (1.0.0-1, the same as the release), and Arch
# versions can't have a "-", so the Arch package gets 1.0.0beta.2: pacman sorts that before
# 1.0.0 too.
for packager in deb rpm archlinux; do
  pkgversion="$version"
  case "$packager" in
    deb) file="$name.deb" ;;
    rpm) file="$name.rpm" ;;
    archlinux) file="$name.pkg.tar.zst"; pkgversion="${version/-/}" ;;
  esac
  (cd "$pkg" && VERSION="$pkgversion" nfpm package --config "$here/nfpm.yaml" --packager "$packager" \
    --target "$out/$file")
done
