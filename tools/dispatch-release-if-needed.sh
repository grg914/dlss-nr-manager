#!/usr/bin/env bash
set -euo pipefail

repo="${1:-}"
source_sha="${2:-}"
dry_run="${3:-false}"

if [[ -z "$repo" || -z "$source_sha" ]]; then
  echo "Usage: dispatch-release-if-needed.sh <owner/repo> <source-sha> [dry-run]" >&2
  exit 2
fi

if [[ ! "$source_sha" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "Source SHA must be a full 40-character Git commit SHA." >&2
  exit 2
fi

if [[ -z "${GH_TOKEN:-}" ]]; then
  echo "GH_TOKEN is required." >&2
  exit 2
fi

project="$(gh api -H "Accept: application/vnd.github.raw+json" "repos/$repo/contents/DlssNrManager.csproj?ref=$source_sha")"
version="$(printf '%s\n' "$project" | sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' | head -n 1)"
if [[ -z "$version" ]]; then
  echo "Unable to resolve application version from DlssNrManager.csproj." >&2
  exit 1
fi

tag="v$version"
exact_ref="$(gh api "repos/$repo/git/matching-refs/tags/$tag" --jq '.[] | select(.ref == "refs/tags/'"$tag"'") | [.object.type, .object.sha] | @tsv')"

if [[ -n "$exact_ref" ]]; then
  IFS=$'\t' read -r tag_type tag_sha <<< "$exact_ref"

  if [[ "$tag_type" == "tag" ]]; then
    annotated_sha="$tag_sha"
    tag_type="$(gh api "repos/$repo/git/tags/$annotated_sha" --jq '.object.type')"
    tag_sha="$(gh api "repos/$repo/git/tags/$annotated_sha" --jq '.object.sha')"
  fi

  if [[ "$tag_type" != "commit" ]]; then
    echo "Stable tag $tag does not resolve to a commit." >&2
    exit 1
  fi

  if [[ "$tag_sha" != "$source_sha" ]]; then
    echo "Stable $tag already belongs to $tag_sha; requested source is $source_sha."
    echo "Runtime seed may be refreshed, but production release dispatch is skipped until the application version is bumped."
    exit 0
  fi
fi

if [[ "$dry_run" == "true" ]]; then
  echo "DRY RUN: release.yml would be dispatched for $tag at $source_sha."
  exit 0
fi

gh workflow run release.yml --repo "$repo" --ref main
echo "Dispatched release.yml for $tag at $source_sha."
