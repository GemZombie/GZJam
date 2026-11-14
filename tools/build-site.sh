#!/usr/bin/env bash
# Puts the website together in one folder: site/ plus the logo and screenshot it shows,
# which stay where they live in the repo so there's one copy of each.
#   tools/build-site.sh [folder]     (default: _site)
# To look at it locally: tools/build-site.sh && python -m http.server -d _site 8000
set -euo pipefail
cd "$(dirname "$0")/.."
out="${1:-_site}"
rm -rf "$out"
mkdir -p "$out/img"
cp -r site/. "$out/"
cp branding/gzjam-logo.svg branding/gzjam-logo-light.svg branding/gzjam-icon.svg \
   docs/images/main-screen.png "$out/img/"
echo "Website put together in $out"
