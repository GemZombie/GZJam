#!/bin/sh
# Builds GZJam's sound processing library (gzjam_dsp.c) into lib/bass/<platform>:
#   macOS: libgzjam_dsp.dylib, Intel and Apple Silicon in one (as BASS's own are)
#   Linux: libgzjam_dsp.so, x64, needing no C library (so any glibc will do)
# Windows: native\build.cmd.
set -e
here=$(cd "$(dirname "$0")" && pwd)
case "$(uname -s)" in
  Darwin)
    out="$here/../lib/bass/osx/libgzjam_dsp.dylib"
    clang -O2 -Wall -Wextra -Werror -dynamiclib -arch x86_64 -arch arm64 -mmacosx-version-min=10.13 \
      -install_name @rpath/libgzjam_dsp.dylib -o "$out" "$here/gzjam_dsp.c"
    ;;
  Linux)
    out="$here/../lib/bass/linux-x64/libgzjam_dsp.so"
    cc -O2 -Wall -Wextra -Werror -shared -fPIC -fvisibility=hidden -nostdlib -o "$out" "$here/gzjam_dsp.c"
    ;;
  *)
    echo "On Windows, run native\\build.cmd" >&2
    exit 1
    ;;
esac
echo "Built $out"
