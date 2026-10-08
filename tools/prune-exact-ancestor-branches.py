#!/usr/bin/env python3
"""One-shot fail-closed cleanup of five immutable, already-integrated branch refs."""
import argparse
import base64
import json
import os
import re
import sys
import time
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

REPO = "grg914/dlss-nr-manager"
FROZEN = {
    "ci/protected-release-request": "a8fcf36c1cfe41f6b11df7aaeb41421b916e63ef",
    "fix/reproducible-model-and-release": "905c10af23fad21fc8ca0d0b67f80b7e2aee8a0e",
    "hardening/minecraft-native-ngx-policy": "8b4ae3f2bca19e9fa3e0681afd89602038c2dcf4",
    "hardening-minecraft-native-ngx-policy": "8b4ae3f2bca19e9fa3e0681afd89602038c2dcf4",
    "refactor/monorepo-caustica": "700696083425853e3cba3dec8264e4a8222a8c79",
}

def api(path, method="GET", allow_missing=False):
    token = os.environ.get("GITHUB_TOKEN", "")
    if not token:
        raise RuntimeError("GitHub token missing")
    request = Request("https://api.github.com" + path, method=method, headers={
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "Authorization": "Bearer " + token,
        "User-Agent": "dlssnr-safe-exact-ancestor-cleanup",
    })
    try:
        with urlopen(request, timeout=30) as response:
            data = response.read()
            return json.loads(data) if data else {}
    except HTTPError as exc:
        if allow_missing and exc.code == 404:
            return None
        raise RuntimeError(f"GitHub {method} {path} HTTP {exc.code}") from exc

def validate_manifest(doc):
    rows = doc.get("candidates")
    if doc.get("schema") != 1 or doc.get("repository") != REPO:
        raise ValueError("Manifest schema/repository mismatch")
    if not isinstance(rows, list) or len(rows) != len(FROZEN):
        raise ValueError("Manifest must contain exactly five pinned refs")
    seen = set()
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"branch", "sha"}:
            raise ValueError("Unexpected manifest data")
        name, sha = row["branch"], row["sha"]
        if (not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9_.\/-]+", name)
            or ".." in name or name.startswith("/") or name in seen
            or not isinstance(sha, str) or FROZEN.get(name) != sha):
            raise ValueError("Unauthorized branch or SHA")
        seen.add(name)
    if seen != set(FROZEN):
        raise ValueError("Missing frozen manifest entry")
    return rows

def active_heads():
    names = set()
    page = 1
    while True:
        prs = api(f"/repos/{REPO}/pulls?state=open&per_page=100&page={page}")
        for pr in prs:
            head = pr.get("head") or {}
            if (head.get("repo") or {}).get("full_name") == REPO:
                names.add(head.get("ref"))
        if len(prs) < 100:
            break
        page += 1
    return names

def referenced_in_workflows():
    files = api(f"/repos/{REPO}/contents/.github/workflows")
    result = []
    for f in files:
        if f.get("name", "").endswith((".yml", ".yaml")):
            content = api(f"/repos/{REPO}/contents/{f['path']}")
            if not content or content.get("encoding") != "base64":
                raise ValueError("Workflow inspection failed")
            result.append(base64.b64decode(content["content"]).decode("utf-8"))
    return "\n".join(result)

def skip_reason(row, heads, workflows):
    name, sha = row["branch"], row["sha"]
    if name in heads:
        return "open PR uses this branch"
    if name in workflows:
        return "a workflow references this branch"
    ref = api(f"/repos/{REPO}/branches/{quote(name, safe='')}", allow_missing=True)
    if ref is None:
        return "already absent"
    if ref.get("protected") or ref.get("commit", {}).get("sha") != sha:
        return "protected or changed HEAD"
    compare = api(f"/repos/{REPO}/compare/main...{sha}")
    if compare.get("ahead_by") != 0 or compare.get("status") not in ("behind", "identical"):
        return "not proven fully integrated in main"
    return None

def run(path, execute):
    if execute and (
        os.environ.get("GITHUB_REPOSITORY") != REPO or
        os.environ.get("GITHUB_EVENT_NAME") != "push" or
        os.environ.get("GITHUB_REF") != "refs/heads/main"
    ):
        raise RuntimeError("DELETE permitted only on protected main push")
    rows = validate_manifest(json.loads(Path(path).read_text(encoding="utf-8")))
    heads, workflows = active_heads(), referenced_in_workflows()
    result = {"mode": "execute" if execute else "dry-run", "deleted": [],
              "eligible": [], "skipped": [], "errors": []}
    for row in rows:
        name = row["branch"]
        try:
            reason = skip_reason(row, heads, workflows)
            if reason is None and execute:
                reason = skip_reason(row, active_heads(), referenced_in_workflows())
                if reason is None:
                    api(f"/repos/{REPO}/git/refs/heads/{quote(name, safe='/')}", method="DELETE")
                    for retry in range(4):
                        if api(f"/repos/{REPO}/branches/{quote(name, safe='')}",
                               allow_missing=True) is None:
                            result["deleted"].append(name)
                            print(f"DELETED {name} at {row['sha']}", flush=True)
                            break
                        if retry < 3:
                            time.sleep(2)
                    else:
                        result["errors"].append([name, "Post-DELETE 404 was not confirmed"])
            if reason is not None:
                result["skipped"].append([name, reason])
            elif not execute:
                result["eligible"].append(name)
        except Exception as exc:
            result["errors"].append([name, str(exc)])
    Path("exact-ancestor-cleanup-result.json").write_text(
        json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2), flush=True)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(
                f"### Exact ancestor cleanup\n- Mode: {result['mode']}\n"
                f"- Deleted: {len(result['deleted'])}\n"
                f"- Eligible: {len(result['eligible'])}\n"
                f"- Skipped: {len(result['skipped'])}\n"
                f"- Errors: {len(result['errors'])}\n")
    return 1 if result["errors"] else 0

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args()
    try:
        sys.exit(run(args.manifest, args.execute))
    except Exception as exc:
        print(f"FAIL CLOSED: {exc}", file=sys.stderr)
        sys.exit(2)
