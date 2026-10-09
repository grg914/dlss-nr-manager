#!/usr/bin/env python3
"""Delete only a same-repository branch whose exact PR head was merged to main.

Run only from a trusted pull_request_target:closed workflow with the script
checked out from protected main. The payload is untrusted and never executed.
"""
import json
import os
import re
import sys
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

REPO = "grg914/dlss-nr-manager"
API = "https://api.github.com/repos/" + REPO
SAFE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$")
SHA = re.compile(r"^[0-9a-f]{40}$")
PROTECTED = frozenset(("main", "master", "develop", "gh-pages"))


def eligible(event):
    """Return (branch, expected_sha, PR number) or explain a safe skip."""
    pr = event.get("pull_request") or {}
    head = pr.get("head") or {}
    base = pr.get("base") or {}
    branch = head.get("ref")
    sha = head.get("sha")
    number = pr.get("number")
    if event.get("action") != "closed" or not pr.get("merged"):
        return None, "PR is not a completed merge"
    if (base.get("ref") != "main" or
            (base.get("repo") or {}).get("full_name") != REPO or
            (head.get("repo") or {}).get("full_name") != REPO):
        return None, "not a same-repository PR merged to main"
    if (not isinstance(branch, str) or not SAFE.fullmatch(branch) or
            branch in PROTECTED or branch.startswith("release/") or
            ".." in branch or "//" in branch or branch.endswith("/")):
        return None, "unsafe or preserved branch name"
    if not isinstance(sha, str) or not SHA.fullmatch(sha):
        return None, "invalid immutable PR head SHA"
    if not isinstance(number, int) or number <= 0:
        return None, "invalid PR number"
    return (branch, sha, number), None


def api(path, method="GET", missing_ok=False):
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        raise RuntimeError("GITHUB_TOKEN is missing")
    request = Request(
        "https://api.github.com" + path,
        method=method,
        headers={
            "Authorization": "Bearer " + token,
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "dlssnr-pr-head-cleanup",
        },
    )
    try:
        with urlopen(request, timeout=30) as response:
            body = response.read()
            return json.loads(body) if body else {}
    except HTTPError as error:
        if missing_ok and error.code == 404:
            return None
        raise RuntimeError(f"GitHub {method} failed with HTTP {error.code}") from error


def open_pr_uses(branch):
    page = 1
    while True:
        prs = api(f"/repos/{REPO}/pulls?state=open&per_page=100&page={page}")
        for pr in prs:
            head = pr.get("head") or {}
            base = pr.get("base") or {}
            if (head.get("ref") == branch and
                    (head.get("repo") or {}).get("full_name") == REPO):
                return True
            if base.get("ref") == branch:
                return True
        if len(prs) < 100:
            return False
        page += 1


def verified_head(branch, sha):
    ref = quote(branch, safe="")
    response = api(f"/repos/{REPO}/branches/{ref}", missing_ok=True)
    if response is None:
        return False, "already deleted"
    if response.get("protected") or (response.get("commit") or {}).get("sha") != sha:
        return False, "protected or modified since PR merged"
    return True, ""


def run(event, dry_run=False):
    if os.environ.get("GITHUB_REPOSITORY") != REPO:
        raise RuntimeError("refusing to run outside owner repository")
    value, reason = eligible(event)
    if not value:
        print("SKIP:", reason)
        return 0
    branch, sha, number = value
    if os.environ.get("GITHUB_EVENT_NAME") != "pull_request_target":
        raise RuntimeError("requires a protected-base pull_request_target close event")

    # Confirm immutable merge evidence directly from GitHub, never just payload.
    live = api(f"/repos/{REPO}/pulls/{number}")
    current, message = eligible({"action": "closed", "pull_request": live})
    if not current or current != value or not live.get("merged_at") or live.get("state") != "closed":
        raise RuntimeError("live merged PR proof differs from event snapshot")

    if open_pr_uses(branch):
        print("SKIP: branch is a base/head of another open PR:", branch)
        return 0
    ok, reason = verified_head(branch, sha)
    if not ok:
        print("SKIP:", reason, branch)
        return 0
    if dry_run:
        print("DRY RUN: would delete", branch)
        return 0

    # GitHub DELETE lacks an if-match SHA condition. Recheck immediately;
    # refuse any changed ref. No unrelated branch or executable-name sweep.
    if open_pr_uses(branch):
        print("SKIP: branch has a new active PR:", branch)
        return 0
    ok, reason = verified_head(branch, sha)
    if not ok:
        print("SKIP:", reason, branch)
        return 0
    api(f"/repos/{REPO}/git/refs/heads/{quote(branch, safe='/')}", method="DELETE")
    after = api(f"/repos/{REPO}/branches/{quote(branch, safe='')}", missing_ok=True)
    if after is not None:
        raise RuntimeError("GitHub still reports branch after delete")
    print("DELETED:", branch, "after merged PR", number, "at", sha)
    return 0


if __name__ == "__main__":
    try:
        with open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8") as handle:
            event = json.load(handle)
        sys.exit(run(event, dry_run=os.environ.get("DRY_RUN") == "1"))
    except Exception as exc:
        print("FAIL CLOSED:", exc, file=sys.stderr)
        sys.exit(1)
