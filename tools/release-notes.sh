#!/usr/bin/env bash
# Prints the GitHub release notes for a version: its section of CHANGELOG.md, then how to download
# and open GZJam. Fails if CHANGELOG.md has no section for the version.
#   tools/release-notes.sh 0.2.0
set -euo pipefail

version="${1:?usage: tools/release-notes.sh <version>}"
root="$(cd "$(dirname "$0")/.." && pwd)"

# The lines after "## <version> (date)", up to the next "## " heading
section="$(awk -v v="$version" '
  /^## / { if (found) exit; found = ($2 == v) ; next }
  found' "$root/CHANGELOG.md" | tr -d '\r')"

if [[ -z "${section//[[:space:]]/}" ]]; then
  echo "CHANGELOG.md has no section for $version: add \"## $version (date)\" with what's new" >&2
  exit 1
fi

# Without the blank lines after the heading, and with wrapped lines joined: GitHub shows every line
# break in release notes
printf '%s\n' "$section" | sed -e '/./,$!d' | awk '
  function flush() { if (have) print line; have = 0 }
  /^[[:space:]]*$/ { flush(); print ""; next }
  /^- / || /^#/ || /^\|/ || !have { flush(); line = $0; have = 1; next }
  { sub(/^[[:space:]]+/, ""); line = line " " $0 }
  END { flush() }'
cat <<EOF

## Download

| Computer | File |
|---|---|
| Windows | \`GZJam-$version-windows-x64-setup.exe\` (installs for you alone, no administrator needed), or the \`.zip\` to run without installing |
| Mac with Apple Silicon (M1 or later) | \`GZJam-$version-macos-arm64.dmg\` |
| Mac with Intel | \`GZJam-$version-macos-x64.dmg\` |
| Linux (x64) | \`GZJam-$version-linux-x64.tar.gz\`: unpack it and run \`./install.sh\` |

GZJam isn't signed with a paid certificate, so the first time you open it:

- **Windows** may say "Windows protected your PC": click **More info**, then **Run anyway**.
- **macOS** may say it can't check GZJam for malicious software: open **System Settings › Privacy & Security**, scroll down and click **Open Anyway**.

Your music is only ever read, never changed. Every release's changes: [CHANGELOG.md](https://github.com/GemZombie/GZJam/blob/main/CHANGELOG.md).
Every key and gesture, not only the new ones: [the user guide](https://github.com/GemZombie/GZJam/blob/main/docs/GUIDE.md#keys-and-gestures).
EOF
