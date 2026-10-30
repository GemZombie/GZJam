#!/usr/bin/env bash
# Builds GZJam for Linux here and runs it on a test machine over SSH, without a GitHub release:
#   tools/deploy-linux.sh user@host            build, copy, (re)start on the machine's screen
#   tools/deploy-linux.sh user@host --no-start build and copy only
# The test copy lives in ~/gzjam-dev/app with its own launcher entry, "GZJam (test build)"; the
# installed GZJam (~/.local/opt/gzjam) is left alone. Both use the same settings and library.
# It's started as a systemd user service, which carries the desktop session's display settings
# (as on Omarchy, through uwsm). Its console output goes to: journalctl --user -u gzjam-dev
set -euo pipefail

host="${1:?usage: tools/deploy-linux.sh user@host [--no-start]}"
start=true
[[ "${2:-}" == "--no-start" ]] && start=false

root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

built="$(git -C "$root" rev-parse --short HEAD)"
[[ -n "$(git -C "$root" status --porcelain)" ]] && built+=" plus uncommitted changes"

echo "Building from $built..."
dotnet publish "$root/src/GZJam.App" -c Release -r linux-x64 --self-contained -o "$work/app" -v quiet -nologo
cp "$root/packaging/linux/gzjam.desktop" "$work/app/"
tar czf "$work/gzjam.tar.gz" -C "$work/app" .

echo "Copying to $host..."
scp -q "$work/gzjam.tar.gz" "$host:gzjam-dev.tar.gz"

ssh "$host" START="$start" bash -s <<'EOF'
set -euo pipefail
app="$HOME/gzjam-dev/app"
export XDG_RUNTIME_DIR="/run/user/$(id -u)"
systemctl --user stop gzjam-dev 2> /dev/null || true
pkill -f "^$app/GZJam" && sleep 1 || true
rm -rf "$app" && mkdir -p "$app"
tar xzf ~/gzjam-dev.tar.gz -C "$app" && rm ~/gzjam-dev.tar.gz
chmod +x "$app/GZJam" "$app/createdump" 2> /dev/null || true

entry="${XDG_DATA_HOME:-$HOME/.local/share}/applications/gzjam-dev.desktop"
mkdir -p "$(dirname "$entry")"
sed -e "s|^Exec=.*|Exec=$app/GZJam|" -e "s|^Name=.*|Name=GZJam (test build)|" "$app/gzjam.desktop" > "$entry"

echo "Test build in $app."
if [[ "$START" == true ]]; then
  systemd-run --user --collect --quiet --unit=gzjam-dev "$app/GZJam" && echo "Started on screen."
fi
EOF
