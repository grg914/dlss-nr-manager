#!/usr/bin/env python3
"""One-time fail-closed cleanup of reviewed, exactly pinned merged PR branches.

No deletion by default. The --execute switch requires a GitHub Actions push to
main, the repository's installation token and a frozen per-branch SHA + merged PR.
Only PRs merged to main, with no head changes and no open consumer, are eligible.
"""
import argparse
import base64
import json
import os
import re
import sys
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

REPO = "grg914/dlss-nr-manager"
API = "https://api.github.com/repos/" + REPO
NAME_RE = re.compile(r"^[A-Za-z0-9_.\/-]+$")
FORBIDDEN = ("main", "master", "develop", "gh-pages")
PRESERVE_PREFIXES = ("release/",)


def api(path, method="GET", allow_404=False):
    token = os.environ.get("GITHUB_TOKEN", "")
    if not token:
        raise RuntimeError("GITHUB_TOKEN is required even for dry-run verification")
    req = Request(
        "https://api.github.com" + path,
        method=method,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": "Bearer " + token,
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "dlss-nr-manager-safe-merged-branch-cleanup",
        },
    )
    try:
        with urlopen(req, timeout=30) as response:
            data = response.read()
            return json.loads(data) if data else {}
    except HTTPError as exc:
        if allow_404 and exc.code == 404:
            return None
        detail = exc.read(500).decode("utf-8", "replace")
        raise RuntimeError(f"GitHub API {method} {path}: HTTP {exc.code}: {detail}") from exc


def open_branch_refs():
    result = set()
    page = 1
    while True:
        pulls = api(f"/repos/{REPO}/pulls?state=open&per_page=100&page={page}")
        for pr in pulls:
            if pr.get("head", {}).get("repo", {}).get("full_name") == REPO:
                result.add(pr["head"]["ref"])
        if len(pulls) < 100:
            return result
        page += 1


def workflow_branch_references():
    """Skip any branch explicitly named by another repository workflow."""
    items = api(f"/repos/{REPO}/contents/.github/workflows")
    contents = []
    for item in items:
        if not item["name"].endswith((".yml", ".yaml")):
            continue
        # This cleanup workflow contains no candidate names, and a pinned JSON
        # manifest is not a consumer of a branch.
        file = api(f"/repos/{REPO}/contents/{item['path']}")
        if not file or file.get("encoding") != "base64":
            raise RuntimeError(f"Unable to inspect workflow {item['path']}")
        contents.append(base64.b64decode(file["content"]).decode("utf-8"))
    return "\n".join(contents)


def validate_manifest(document):
    if document.get("schema") != 1 or document.get("repository") != REPO:
        raise RuntimeError("Manifest schema or repository mismatch")
    candidates = document.get("candidates")
    if not isinstance(candidates, list) or len(candidates) != 85:
        raise RuntimeError("Frozen manifest must contain exactly 85 reviewed candidates")
    seen = set()
    for candidate in candidates:
        name = candidate.get("branch", "")
        sha = candidate.get("sha", "")
        number = candidate.get("merged_pr")
        if (not isinstance(name, str) or not NAME_RE.fullmatch(name) or
                name.startswith("/") or ".." in name or
                name in FORBIDDEN or name.startswith(PRESERVE_PREFIXES) or
                name in seen or
                not isinstance(sha, str) or not re.fullmatch("[a-f0-9]{40}", sha) or
                not isinstance(number, int) or number < 1):
            raise RuntimeError(f"Unsafe candidate manifest entry: {name!r}")
        seen.add(name)
    return candidates


def run(manifest_path, execute):
    if execute and (
        os.environ.get("GITHUB_REPOSITORY") != REPO
        or os.environ.get("GITHUB_REF") != "refs/heads/main"
        or os.environ.get("GITHUB_EVENT_NAME") != "push"
    ):
        raise RuntimeError("Execution only permitted on main push in original repository")
    document = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    candidates = validate_manifest(document)
    open_names = open_branch_refs()
    workflow_text = workflow_branch_references()
    deleted, would_delete, skipped, failures = [], [], [], []
    for candidate in candidates:
        name, sha, number = (candidate[k] for k in ("branch", "sha", "merged_pr"))
        reason = None
        if name in open_names:
            reason = "an open PR still uses this branch"
        elif name in workflow_text:
            reason = "explicit dependency in an active workflow"

        if reason:
            skipped.append((name, reason))
            continue
        try:
            pr = api(f"/repos/{REPO}/pulls/{number}")
            head = pr.get("head") or {}
            base = pr.get("base") or {}
            head_repo = head.get("repo") or {}
            if not (
                pr.get("state") == "closed" and pr.get("merged_at")
                and pr.get("merge_commit_sha")
                and base.get("ref") == "main"
                and head.get("ref") == name
                and head.get("sha") == sha
                and head_repo.get("full_name") == REPO
            ):
                skipped.append((name, "merged PR proof no longer matches manifest"))
                continue
            branch = api(f"/repos/{REPO}/branches/{quote(name, safe='')}", allow_404=True)
            if not branch:
                skipped.append((name, "already deleted"))
                continue
            if branch.get("protected") or branch.get("commit", {}).get("sha") != sha:
                skipped.append((name, "protected or changed branch HEAD"))
                continue
            if not execute:
                would_delete.append(name)
                continue

            # Recheck the mutable conditions before each destructive operation.
            if name in open_branch_refs():
                skipped.append((name, "new open PR detected"))
                continue
            current = api(f"/repos/{REPO}/branches/{quote(name, safe='')}", allow_404=True)
            if not current or current.get("protected") or current["commit"]["sha"] != sha:
                skipped.append((name, "branch changed just before delete"))
                continue
            api(f"/repos/{REPO}/git/refs/heads/{quote(name, safe='/')}", method="DELETE")
            after = api(f"/repos/{REPO}/branches/{quote(name, safe='')}", allow_404=True)
            if after is not None:
                failures.append((name, "branch still exists after DELETE"))
            else:
                deleted.append(name)
                print(f"DELETED: {name} (merged PR #{number}, SHA {sha})", flush=True)
        except Exception as exc:
            # A failed safety proof never becomes a deletion.
            failures.append((name, str(exc)))
            print(f"ERROR: {name}: {exc}", file=sys.stderr, flush=True)

    summary = {
        "mode": "execute" if execute else "dry-run",
        "manifest_count": len(candidates),
        "deleted": deleted,
        "would_delete": would_delete,
        "skipped": [{"branch": n, "reason": r} for n, r in skipped],
        "errors": [{"branch": n, "reason": r} for n, r in failures],
    }
    Path("merged-branch-cleanup-result.json").write_text(
        json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    lines = [
        "## Verified merged-branch cleanup",
        f"- Mode: **{summary['mode']}**",
        f"- Manifest: {len(candidates)} frozen candidates",
        f"- Deleted: **{len(deleted)}**",
        f"- Eligible (dry run): {len(would_delete)}",
        f"- Skipped safely: {len(skipped)}",
        f"- Errors: **{len(failures)}**",
        "- Non-merged, diverging, release, protected and open-PR branches are excluded.",
    ]
    summary_file = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_file:
        with open(summary_file, "a", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
    print("\n".join(lines), flush=True)
    if failures:
        return 1
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--execute", action="store_true", help="Actually delete verified refs")
    args = parser.parse_args()
    try:
        sys.exit(run(args.manifest, args.execute))
    except Exception as exc:
        print(f"FAIL CLOSED: {exc}", file=sys.stderr)
        sys.exit(2)
