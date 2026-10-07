#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCK="${ROOT}/third_party/DEPENDENCIES.lock.json"
OUTPUT_DIR="${1:-${ROOT}/build-ffmpeg-windows}"

mkdir -p "$OUTPUT_DIR"

FFMPEG_TAG="$(python3 -c "import json; d=json.load(open(r'$LOCK', encoding='utf-8-sig')); x=next(v for v in d['sources'] if v['id']=='ffmpeg'); print(x.get('source_tag', x['ref'][:12]))")"
NVCODEC_TAG="$(python3 -c "import json; d=json.load(open(r'$LOCK', encoding='utf-8-sig')); x=next(v for v in d['sources'] if v['id']=='nv-codec-headers'); print(x.get('source_tag', x['ref'][:12]))")"

SOURCE_ARCHIVE="$OUTPUT_DIR/ffmpeg-source-${FFMPEG_TAG}-nvcodec-${NVCODEC_TAG}.tar.xz"
rm -f "$SOURCE_ARCHIVE"

(
  cd "$ROOT"
  tar -cJf "$SOURCE_ARCHIVE" third_party/FFmpeg third_party/nv-codec-headers
)

test -s "$SOURCE_ARCHIVE"

echo "FFmpeg corresponding source archive: $SOURCE_ARCHIVE"
sha256sum "$SOURCE_ARCHIVE"
