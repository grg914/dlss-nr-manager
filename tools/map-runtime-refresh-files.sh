#!/usr/bin/env bash
set -euo pipefail

# Maps repository paths that changed on a main push to the smallest runtime
# refresh scope that can be safely rebuilt. Output is either:
#   release-only
#   all
#   comma-separated dependency/runtime ids
#
# DlssNrManager.csproj must only be passed when its non-version content changed.
# A pure version-only csproj change should be filtered by the caller.

declare -A ids=()
force_all=false

add_id() {
  local id="${1:-}"
  [[ -n "$id" ]] || return 0
  ids["$id"]=1
}

for file in "$@"; do
  case "$file" in
    .github/workflows/runtime-refresh.yml|.github/workflows/release.yml)
      # Orchestration-only changes do not invalidate runtime bytes.
      ;;

    DlssNrManager.csproj|tests/DlssNrManager.Tests/DlssNrManager.Tests.csproj)
      add_id "onnxruntime"
      ;;

    tools/build-nuget-offline-seed.ps1|tools/create-deterministic-flat-zip.ps1)
      add_id "onnxruntime"
      ;;

    tools/build-ffmpeg-windows.sh)
      add_id "ffmpeg"
      ;;

    tools/build-realesrgan.ps1)
      add_id "realesrgan"
      ;;

    tools/build-optiscaler.ps1)
      add_id "optiscaler"
      ;;

    tools/build-reshade.ps1)
      add_id "reshade"
      ;;

    tools/publish-streamline-runtime.ps1)
      add_id "streamline"
      ;;

    tools/publish-temurin25-runtime.ps1)
      add_id "java25"
      ;;

    tools/publish-vlc-runtime.ps1)
      add_id "vlc"
      ;;

    tools/bootstrap-minecraft-runtime.ps1)
      add_id "minecraft-fabric-installer"
      ;;

    tools/generate-components-manifest.ps1)
      # Release metadata only.
      ;;

    tools/verify-repository-governance.ps1)
      # Repository policy validation only; never changes runtime bytes.
      ;;

    tools/map-runtime-refresh-files.sh)
      # Scope-routing implementation only.
      ;;

    third_party/NVIDIA-Streamline/*)
      add_id "streamline"
      ;;

    third_party/OptiScaler/*)
      add_id "optiscaler"
      ;;

    third_party/ReShade/*|third_party/ScoopInstaller-Versions/*)
      add_id "reshade"
      ;;

    third_party/Real-ESRGAN-ncnn-vulkan/*)
      add_id "realesrgan"
      ;;

    third_party/FFmpeg/*)
      add_id "ffmpeg"
      ;;

    third_party/FFmpeg-Builds/*)
      # Historical/reference BtbN build recipes only; the manager runtime is
      # built from third_party/FFmpeg + nv-codec-headers and does not consume
      # this snapshot.
      ;;

    third_party/nv-codec-headers/*)
      add_id "nv-codec-headers"
      ;;

    third_party/onnxruntime/*)
      add_id "onnxruntime"
      ;;

    third_party/minecraft/*)
      add_id "minecraft-fabric-installer"
      ;;

    third_party/ai-models/*|third_party/Real-ESRGAN-model-sources/*|third_party/video2dlssnr/*)
      # These are release payload/source-policy inputs, not rebuildable runtime
      # seed binaries in this workflow.
      ;;

    third_party/README.md|third_party/UPSTREAMS.json)
      # Policy/documentation only.
      ;;

    third_party/DEPENDENCIES.lock.json)
      # The lock can change several independent sources in one commit. Until
      # lock-diff routing is implemented, preserve the conservative fallback.
      force_all=true
      ;;

    tools/*|third_party/*)
      # Unknown runtime/build input under a runtime-sensitive tree: preserve
      # correctness over optimization until it receives an explicit mapping.
      force_all=true
      ;;

    *)
      # Files outside runtime-sensitive trees may coexist in the same merge
      # commit but cannot independently invalidate manager-owned runtime bytes.
      ;;
  esac
done

if [[ "$force_all" == "true" ]]; then
  printf '%s\n' "all"
  exit 0
fi

if [[ "${#ids[@]}" -eq 0 ]]; then
  printf '%s\n' "release-only"
  exit 0
fi

printf '%s\n' "${!ids[@]}" | LC_ALL=C sort | paste -sd, -
