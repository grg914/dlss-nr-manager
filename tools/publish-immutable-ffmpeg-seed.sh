#!/usr/bin/env bash
# Append-only publisher. A reviewed consumer pin selects new revisions.
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <owner/repo> <release-tag> <ffmpeg-archive.zip>" >&2
  exit 2
fi
repository="$1"
tag="$2"
archive="$3"
canonical="ffmpeg-dlssnr-win-x64.zip"

for tool in gh jq sha256sum unzip stat mktemp; do
  command -v "$tool" >/dev/null 2>&1 || { echo "Required tool missing: $tool" >&2; exit 1; }
done
[[ "$repository" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || { echo "Invalid repository" >&2; exit 1; }
[[ "$tag" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "Invalid release tag" >&2; exit 1; }
[[ -f "$archive" && "$(basename "$archive")" == "$canonical" ]] || {
  echo "Expected $canonical archive" >&2
  exit 1
}
unzip -tqq "$archive" >/dev/null || { echo "FFmpeg ZIP integrity check failed" >&2; exit 1; }
entries="$(unzip -Z1 "$archive")"
grep -Fxq 'ffmpeg.exe' <<<"$entries" && grep -Fxq 'ffprobe.exe' <<<"$entries" || {
  echo "FFmpeg ZIP must include ffmpeg.exe and ffprobe.exe" >&2
  exit 1
}
sha="$(sha256sum "$archive" | cut -d ' ' -f 1)"
size="$(stat -c '%s' "$archive")"
[[ "$sha" =~ ^[a-f0-9]{64}$ && "$size" =~ ^[0-9]+$ && "$size" -gt 0 ]] || {
  echo "Invalid local FFmpeg archive hash or size" >&2
  exit 1
}

fetch_release() {
  gh api "repos/$repository/releases/tags/$tag"
}
find_asset() {
  local release="$1" name="$2"
  jq -c --arg name "$name" '[.assets[] | select(.name == $name)]' <<<"$release"
}
verify_existing() {
  local matches="$1" name="$2"
  local count digest asset_size
  count="$(jq 'length' <<<"$matches")"
  [[ "$count" -le 1 ]] || { echo "Duplicate release asset: $name" >&2; return 1; }
  [[ "$count" -eq 1 ]] || return 2
  digest="$(jq -r '.[0].digest // ""' <<<"$matches")"
  asset_size="$(jq -r '.[0].size // ""' <<<"$matches")"
  [[ "$digest" =~ ^sha256:[a-f0-9]{64}$ && "$asset_size" =~ ^[0-9]+$ ]] || {
    echo "Unverifiable release asset: $name" >&2
    return 1
  }
  [[ "$digest" == "sha256:$sha" && "$asset_size" == "$size" ]] && return 0
  return 3
}

release="$(fetch_release)"
[[ "$(jq -r '.draft' <<<"$release")" == "false" ]] || { echo "Draft release forbidden" >&2; exit 1; }
target="$canonical"
existing="$(find_asset "$release" "$canonical")"
if verify_existing "$existing" "$canonical"; then
  echo "SKIP unchanged FFmpeg seed ($sha)"
  exit 0
else
  result=$?
  if [[ "$result" -eq 3 ]]; then
    target="ffmpeg-dlssnr-win-x64.sha256-$sha.zip"
  elif [[ "$result" -ne 2 ]]; then
    echo "Existing FFmpeg seed cannot be verified" >&2
    exit 1
  fi
fi

selected="$(find_asset "$release" "$target")"
if verify_existing "$selected" "$target"; then
  echo "SKIP existing verified immutable FFmpeg seed: $target"
  exit 0
else
  result=$?
  [[ "$result" -eq 2 ]] || { echo "Refusing to replace conflicting release asset: $target" >&2; exit 1; }
fi

tmp=""
upload="$archive"
cleanup() { if [[ -n "$tmp" ]]; then rm -rf -- "$tmp"; fi; }
trap cleanup EXIT
if [[ "$target" != "$canonical" ]]; then
  tmp="$(mktemp -d)"
  upload="$tmp/$target"
  cp -- "$archive" "$upload"
fi

# Deliberately no --clobber: a racing writer cannot delete a healthy asset.
gh release upload "$tag" "$upload" --repo "$repository"
confirmed="$(fetch_release)"
verified="$(find_asset "$confirmed" "$target")"
if ! verify_existing "$verified" "$target"; then
  echo "Uploaded FFmpeg seed failed release SHA-256/size verification: $target" >&2
  exit 1
fi
echo "Verified append-only FFmpeg seed: $target ($sha)"
if [[ "$target" != "$canonical" ]]; then
  echo "Canonical FFmpeg seed remains pinned; promote immutable revision through a reviewed consumer manifest."
fi
