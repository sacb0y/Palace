#!/usr/bin/env bash
# Reproducible slim LGPL ffmpeg.exe for Palace video thumbnails.
# Cross-compiles on Linux (Debian/Ubuntu) with mingw-w64 into ONE static
# ffmpeg.exe (no DLLs). LGPL-2.1+ only: no --enable-gpl, no nonfree.
#
#   sudo apt-get install -y mingw-w64 nasm meson ninja-build pkg-config make xz-utils curl
#   tools/build-ffmpeg.sh [outdir]      # -> outdir/palace-ffmpeg-<ver>-win64-lgpl.zip
#
# Pinned sources (tarball SHA-256 verified). Bump versions + hashes together.
set -euo pipefail

FFMPEG_VER=8.1.3
FFMPEG_SHA256=7138d28c96d9d3e3af4ee3d8cad72741f8ffb40da90c1112235dea3ecd3178a3
DAV1D_VER=1.5.4
DAV1D_SHA256=686616b7c69eb88d44459391ab25cac13b6647a3b288835c5784e71c1514a5c5
FFMPEG_URL="https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VER}.tar.xz"
DAV1D_URL="https://downloads.videolan.org/pub/videolan/dav1d/${DAV1D_VER}/dav1d-${DAV1D_VER}.tar.xz"

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mkdir -p "${1:-$PWD/ffmpeg-out}" && cd "${1:-$PWD/ffmpeg-out}" && pwd)"
WORK="$(mktemp -d)"
trap '[ -n "${KEEP_WORK:-}" ] || rm -rf "$WORK"' EXIT
HOST=x86_64-w64-mingw32
PREFIX="$WORK/prefix"
JOBS="$(nproc)"
export SOURCE_DATE_EPOCH=1767225600 TZ=UTC  # fixed timestamps in the zip

fetch() { # url sha256 file
  curl -sSfL --retry 3 -o "$WORK/$3" "$1"
  echo "$2  $WORK/$3" | sha256sum -c -
}
fetch "$FFMPEG_URL" "$FFMPEG_SHA256" ffmpeg.tar.xz
fetch "$DAV1D_URL" "$DAV1D_SHA256" dav1d.tar.xz
tar -C "$WORK" -xf "$WORK/ffmpeg.tar.xz"
tar -C "$WORK" -xf "$WORK/dav1d.tar.xz"

# --- libdav1d (BSD-2-Clause), static, for AV1 ---
cat > "$WORK/cross.ini" <<INI
[binaries]
c = '${HOST}-gcc'
cpp = '${HOST}-g++'
ar = '${HOST}-ar'
strip = '${HOST}-strip'
windres = '${HOST}-windres'
nasm = 'nasm'
[host_machine]
system = 'windows'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
INI
meson setup "$WORK/dav1d-build" "$WORK/dav1d-${DAV1D_VER}" --cross-file "$WORK/cross.ini" \
  --prefix "$PREFIX" --libdir lib --buildtype release --default-library static \
  -Denable_tools=false -Denable_tests=false -Denable_examples=false -Dbitdepths=8,16
ninja -C "$WORK/dav1d-build" install

# --- ffmpeg: only what thumbnails need ---
cd "$WORK/ffmpeg-${FFMPEG_VER}"
PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig" ./configure \
  --target-os=mingw32 --arch=x86_64 --cross-prefix=${HOST}- --pkg-config=pkg-config \
--pkg-config-flags=--static \
  --extra-ldflags="-static -static-libgcc" --extra-cflags="-O2" \
  --disable-gpl --disable-nonfree --disable-version3 \
  --enable-static --disable-shared --disable-debug --disable-doc \
  --disable-autodetect --disable-network --disable-everything \
  --disable-ffplay --disable-ffprobe --enable-ffmpeg \
  --disable-avdevice --disable-swresample \
  --enable-libdav1d \
  --enable-protocol=file,pipe \
  --enable-demuxer=mov,matroska,avi,m4v,h264,hevc,ivf \
  --enable-parser=h264,hevc,vp8,vp9,av1,mpeg4video \
  --enable-decoder=h264,hevc,vp8,vp9,mpeg4,libdav1d,av1 \
  --enable-bsf=h264_mp4toannexb,hevc_mp4toannexb,vp9_superframe_split,av1_frame_split \
  --enable-filter=scale,format,null,buffer,buffersink \
  --enable-encoder=mjpeg,png \
  --enable-muxer=image2,image2pipe,mjpeg \
  --enable-swscale
make -j"$JOBS"
${HOST}-strip ffmpeg.exe

# --- package: exe + license + source offer + exact config ---
PKG="$WORK/pkg"; mkdir -p "$PKG/bin"
cp ffmpeg.exe "$PKG/bin/"
cp COPYING.LGPLv2.1 "$PKG/LICENSE.txt"
cp "$WORK/dav1d-${DAV1D_VER}/COPYING" "$PKG/LICENSE-dav1d.txt"
{
  echo "Palace bundles FFmpeg (https://ffmpeg.org), licensed under the GNU LGPL v2.1 or later"
  echo "(see LICENSE.txt), statically linked with dav1d (BSD-2-Clause, LICENSE-dav1d.txt)"
  echo "and the mingw-w64 runtime (ZPL / MIT-style)."
  echo "No GPL or nonfree components are enabled."
  echo
  echo "SOURCE OFFER: the complete corresponding source is available at"
  echo "  $FFMPEG_URL  (sha256 $FFMPEG_SHA256)"
  echo "  $DAV1D_URL  (sha256 $DAV1D_SHA256)"
  echo "and the build script tools/build-ffmpeg.sh in https://github.com/sacb0y/Palace,"
  echo "which rebuilds this exact binary. On request we will also provide the source"
  echo "on a physical medium for three years; open an issue at"
  echo "https://github.com/sacb0y/Palace/issues."
  echo
  echo "ffmpeg.exe is a separate program invoked as a process, and may be replaced"
  echo "with your own build of FFmpeg at the same path."
  echo
  echo "configure:"
  grep -m1 '^FFMPEG_CONFIGURATION\|define FFMPEG_CONFIGURATION' config.h | sed 's/^#define FFMPEG_CONFIGURATION //'
} > "$PKG/SOURCE.txt"
cp "$HERE/build-ffmpeg.sh" "$PKG/build-ffmpeg.sh"
ZIP="$OUT/palace-ffmpeg-${FFMPEG_VER}-win64-lgpl.zip"
rm -f "$ZIP"
find "$PKG" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
(cd "$PKG" && find . -type f | LC_ALL=C sort | zip -X -q -@ "$ZIP")
(cd "$OUT" && sha256sum "$(basename "$ZIP")" | tee "$(basename "$ZIP").sha256")
ls -l "$PKG/bin/ffmpeg.exe" "$ZIP"
