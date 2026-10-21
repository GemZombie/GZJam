#!/usr/bin/env bash
# Installs GZJam for the current user: the program in ~/.local/opt/gzjam, a `gzjam` command in
# ~/.local/bin and a launcher entry. Run from the unpacked folder:   ./install.sh
# Remove it again with:                                              ./install.sh --uninstall
# Neither touches settings (~/.config/GZJam), the library database (~/.local/share/GZJam) or music.
set -euo pipefail

prefix="${XDG_DATA_HOME:-$HOME/.local/share}"
dest="$HOME/.local/opt/gzjam"
bin="$HOME/.local/bin/gzjam"
desktop="$prefix/applications/gzjam.desktop"

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$dest"
  rm -f "$bin" "$desktop"
  echo "GZJam removed. Settings and the library database were kept."
  exit 0
fi

here="$(cd "$(dirname "$0")" && pwd)"
if [[ ! -x "$here/GZJam" ]]; then
  echo "Run install.sh from the unpacked GZJam folder." >&2
  exit 1
fi
if [[ "$here" == "$dest" ]]; then
  echo "This is the installed copy; run install.sh from a newly unpacked folder." >&2
  exit 1
fi

rm -rf "$dest"
mkdir -p "$dest" "$(dirname "$bin")" "$(dirname "$desktop")"
cp -R "$here"/. "$dest/"
ln -sf "$dest/GZJam" "$bin"
sed "s|^Exec=.*|Exec=$dest/GZJam|" "$here/gzjam.desktop" > "$desktop"
command -v update-desktop-database > /dev/null && update-desktop-database "$(dirname "$desktop")" || true

echo "GZJam installed in $dest."
echo "Start it from your launcher, or run: gzjam   (if ~/.local/bin is on your PATH)"
