#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="${FFMPEG_SOURCE:-$ROOT/third_party/FFmpeg}"
NV_CODEC="${NV_CODEC_SOURCE:-$ROOT/third_party/nv-codec-headers}"
BUILD="${FFMPEG_BUILD_DIR:-$ROOT/build-ffmpeg-windows}"
OUTPUT="${FFMPEG_OUTPUT_DIR:-$ROOT/build-ffmpeg-package}"

for tool in x86_64-w64-mingw32-gcc x86_64-w64-mingw32-objdump make nasm pkg-config zip; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "Required build tool is missing: $tool" >&2
    exit 1
  }
done

for required in   "$SOURCE/configure"   "$SOURCE/LICENSE.md"   "$SOURCE/COPYING.LGPLv2.1"   "$SOURCE/SOURCE.json"   "$NV_CODEC/Makefile"   "$NV_CODEC/ffnvcodec.pc.in"   "$NV_CODEC/include/ffnvcodec/nvEncodeAPI.h"   "$NV_CODEC/SOURCE.json"; do
  [[ -f "$required" ]] || {
    echo "Required vendored input is missing: $required" >&2
    exit 1
  }
done

rm -rf "$BUILD" "$OUTPUT"
mkdir -p "$BUILD/nvcodec" "$BUILD/ffmpeg" "$OUTPUT"

NV_PREFIX="$BUILD/nvcodec/prefix"
INSTALL_PREFIX="$BUILD/install"

make -C "$NV_CODEC" PREFIX="$NV_PREFIX" LIBDIR=lib install

export PKG_CONFIG_PATH="$NV_PREFIX/lib/pkgconfig"
export PKG_CONFIG_LIBDIR="$PKG_CONFIG_PATH"

pushd "$BUILD/ffmpeg" >/dev/null

"$SOURCE/configure"   --target-os=mingw32   --arch=x86_64   --enable-cross-compile   --cross-prefix=x86_64-w64-mingw32-   --prefix="$INSTALL_PREFIX"   --pkg-config=pkg-config   --pkg-config-flags=--static   --extra-cflags="-I$NV_PREFIX/include"   --extra-ldflags="-static -static-libgcc"   --disable-autodetect   --disable-debug   --disable-doc   --disable-ffplay   --disable-pthreads   --enable-w32threads   --enable-static   --disable-shared   --enable-ffnvcodec   --enable-nvenc   --enable-nvdec   --enable-cuvid

grep -Eq '^#define CONFIG_HEVC_NVENC_ENCODER 1$' ffbuild/config_components.h || {
  echo "FFmpeg configure did not enable hevc_nvenc." >&2
  exit 1
}

grep -Eq '^#define CONFIG_AAC_ENCODER 1$' ffbuild/config_components.h || {
  echo "FFmpeg configure did not enable the native AAC encoder." >&2
  exit 1
}

grep -Eq '^#define CONFIG_H264_DECODER 1$' ffbuild/config_components.h || {
  echo "FFmpeg configure did not enable the H.264 decoder." >&2
  exit 1
}

grep -Eq '^#define CONFIG_HEVC_DECODER 1$' ffbuild/config_components.h || {
  echo "FFmpeg configure did not enable the HEVC decoder." >&2
  exit 1
}

grep -Eq '^#define CONFIG_AV1_DECODER 1$' ffbuild/config_components.h || {
  echo "FFmpeg configure did not enable the AV1 decoder." >&2
  exit 1
}

grep -Eq '^#define CONFIG_VP9_DECODER 1$' ffbuild/config_components.h || {
  echo "FFmpeg configure did not enable the VP9 decoder." >&2
  exit 1
}

make -j"$(nproc)" ffmpeg.exe ffprobe.exe

popd >/dev/null

FFMPEG_EXE="$BUILD/ffmpeg/ffmpeg.exe"
FFPROBE_EXE="$BUILD/ffmpeg/ffprobe.exe"

for exe in "$FFMPEG_EXE" "$FFPROBE_EXE"; do
  [[ -s "$exe" ]] || {
    echo "Expected Windows executable was not generated: $exe" >&2
    exit 1
  }

  x86_64-w64-mingw32-objdump -p "$exe" > "$exe.imports.txt"

  if grep -Eiq 'DLL Name: (libgcc_s|libwinpthread|libstdc\+\+)' "$exe.imports.txt"; then
    echo "Unexpected MinGW runtime DLL dependency detected in $exe:" >&2
    grep -Ei 'DLL Name: (libgcc_s|libwinpthread|libstdc\+\+)' "$exe.imports.txt" >&2
    exit 1
  fi
done

strings "$FFMPEG_EXE" | grep -q 'hevc_nvenc' || {
  echo "Built ffmpeg.exe does not contain the hevc_nvenc encoder name." >&2
  exit 1
}

cp "$FFMPEG_EXE" "$OUTPUT/ffmpeg.exe"
cp "$FFPROBE_EXE" "$OUTPUT/ffprobe.exe"
cp "$SOURCE/LICENSE.md" "$OUTPUT/LICENSE-FFmpeg.md"
cp "$SOURCE/COPYING.LGPLv2.1" "$OUTPUT/COPYING.LGPLv2.1"
cp "$SOURCE/SOURCE.json" "$OUTPUT/SOURCE-FFmpeg.json"
cp "$NV_CODEC/SOURCE.json" "$OUTPUT/SOURCE-nv-codec-headers.json"
cp "$NV_CODEC/README" "$OUTPUT/README-nv-codec-headers.txt"

NOTICE="$OUTPUT/NOTICE-DLSS-NR-MANAGER.txt"
cat > "$NOTICE" <<'EOF'
This FFmpeg Windows x64 package is built by DLSS NR Manager from pinned vendored sources.
It intentionally does not enable FFmpeg GPL or nonfree configure modes.
NVIDIA codec headers are used to expose NVENC/NVDEC interfaces; the NVIDIA driver provides
the runtime implementation on supported systems.
EOF

ZIP="$BUILD/ffmpeg-dlssnr-win-x64.zip"
rm -f "$ZIP"
(
  cd "$OUTPUT"
  zip -9 -q "$ZIP" ./*
)

[[ -s "$ZIP" ]] || {
  echo "FFmpeg package was not generated." >&2
  exit 1
}

echo "FFmpeg Windows x64 package verified: $ZIP"
echo "Package contents:"
find "$OUTPUT" -maxdepth 1 -type f -printf '  %f (%s bytes)\n' | sort
