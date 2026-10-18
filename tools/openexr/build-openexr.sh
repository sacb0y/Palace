#!/usr/bin/env bash
# Build palace_exr (tools/openexr/palace_exr.c over a static OpenEXRCore).
#   tools/openexr/build-openexr.sh <out-dir> [win64|linux]
# win64 cross-compiles with mingw-w64 into one self-contained palace_exr.dll
# (static libgcc/libstdc++/winpthread, no VC runtime). linux builds
# libpalace_exr.so for running Palace.Tests on Linux. Needs cmake, ninja,
# curl, git and mingw-w64 (win64) or gcc/g++ (linux).
set -euo pipefail

OUT=${1:?usage: build-openexr.sh <out-dir> [win64|linux]}
TARGET=${2:-win64}
OPENEXR_VERSION=3.5.2
OPENEXR_SHA256=85a291b9b8563fabef1aa3f6e912d744ce1d4b9fd5a8f978eda194cd51b15ae9
HERE=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$OUT"
OUT=$(cd "$OUT" && pwd)
WORK=${WORK:-$(mktemp -d)}

cd "$WORK"
if [ ! -d "openexr-$OPENEXR_VERSION" ]; then
  curl -fsSL -o openexr.tar.gz \
    "https://github.com/AcademySoftwareFoundation/openexr/archive/refs/tags/v$OPENEXR_VERSION.tar.gz"
  echo "$OPENEXR_SHA256  openexr.tar.gz" | sha256sum -c -
  tar xzf openexr.tar.gz
fi
SRC="$WORK/openexr-$OPENEXR_VERSION"

CMAKE_ARGS=(
  -G Ninja -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF
  -DCMAKE_POSITION_INDEPENDENT_CODE=ON
  -DOPENEXR_BUILD_TOOLS=OFF -DOPENEXR_BUILD_EXAMPLES=OFF -DBUILD_TESTING=OFF
  -DOPENEXR_INSTALL=OFF -DOPENEXR_ENABLE_THREADING=OFF
  -DOPENEXR_FORCE_INTERNAL_DEFLATE=ON -DOPENEXR_FORCE_INTERNAL_OPENJPH=ON
  -DOPENEXR_FORCE_INTERNAL_ZSTD=ON -DOPENEXR_FORCE_INTERNAL_IMATH=ON
  "-DCMAKE_C_FLAGS=-ffile-prefix-map=$WORK=."
  "-DCMAKE_CXX_FLAGS=-ffile-prefix-map=$WORK=."
)
if [ "$TARGET" = win64 ]; then
  CC=x86_64-w64-mingw32-gcc
  cat > "$WORK/mingw.cmake" <<TC
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR AMD64)
set(CMAKE_C_COMPILER x86_64-w64-mingw32-gcc)
set(CMAKE_CXX_COMPILER x86_64-w64-mingw32-g++)
set(CMAKE_RC_COMPILER x86_64-w64-mingw32-windres)
set(CMAKE_FIND_ROOT_PATH /usr/x86_64-w64-mingw32)
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)
TC
  CMAKE_ARGS+=("-DCMAKE_TOOLCHAIN_FILE=$WORK/mingw.cmake")
else
  CC=gcc
fi

BUILD="$WORK/build-$TARGET"
cmake -S "$SRC" -B "$BUILD" "${CMAKE_ARGS[@]}"
cmake --build "$BUILD" --target OpenEXRCore -j"$(nproc)"
CORE=$(find "$BUILD" -name 'libOpenEXRCore*.a' | head -1)
INC=(-I"$SRC/src/lib/OpenEXRCore" -I"$BUILD/cmake" -I"$BUILD/OpenEXR_ImathIncludeCompat")

if [ "$TARGET" = win64 ]; then
  $CC -O2 -shared "${INC[@]}" -ffile-prefix-map="$HERE"=. \
    "$HERE/palace_exr.c" "$CORE" \
    -static -static-libgcc -static-libstdc++ -lstdc++ -lm \
    -Wl,--no-insert-timestamp -Wl,--exclude-all-symbols -s \
    -o "$OUT/palace_exr.dll"
  # Only the three pexr_* entry points are exported.
  x86_64-w64-mingw32-objdump -p "$OUT/palace_exr.dll" | sed -n '/\[Ordinal\/Name Pointer\] Table/,/^$/p'
  x86_64-w64-mingw32-objdump -p "$OUT/palace_exr.dll" | grep 'DLL Name' || true
else
  $CC -O2 -fPIC -shared -fvisibility=hidden "${INC[@]}" "$HERE/palace_exr.c" "$CORE" \
    -Wl,--no-undefined -Wl,--exclude-libs,ALL -s -lstdc++ -lm -lpthread \
    -o "$OUT/libpalace_exr.so"
fi

{
  cat "$HERE/LICENSE-header.txt"
  for f in "$SRC/LICENSE.md" "$SRC/external/deflate/COPYING" "$SRC/external/zstd/LICENSE" \
           "$SRC/external/OpenJPH/LICENSE"; do
    [ -f "$f" ] || continue
    printf '\n\n===== %s =====\n\n' "${f#$SRC/}"
    cat "$f"
  done
} > "$OUT/LICENSE.txt"
cat > "$OUT/SOURCE.txt" <<SRCTXT
palace_exr: OpenEXR $OPENEXR_VERSION (OpenEXRCore, vendored libdeflate, zstd and
OpenJPH) statically linked with Palace's shim tools/openexr/palace_exr.c.
Source: https://github.com/AcademySoftwareFoundation/openexr/archive/refs/tags/v$OPENEXR_VERSION.tar.gz
SHA-256: $OPENEXR_SHA256
Built by tools/openexr/build-openexr.sh ($TARGET).
SRCTXT
ls -la "$OUT"
