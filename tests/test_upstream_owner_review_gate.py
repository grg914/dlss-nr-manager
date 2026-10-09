"""Supply-chain owner review: CI may prepare updates but never merge them."""
from pathlib import Path
import re
import unittest

WORKFLOW = (Path(__file__).resolve().parents[1] /
            ".github/workflows/upstream-monitor.yml").read_text(encoding="utf-8")


def job(name: str) -> str:
    match = re.search(rf"(?m)^  {re.escape(name)}:\s*$", WORKFLOW)
    if not match:
        raise AssertionError(f"Missing job: {name}")
    next_job = re.search(r"(?m)^  [a-z][a-z0-9-]*:\s*$", WORKFLOW[match.end():])
    end = match.end() + next_job.start() if next_job else len(WORKFLOW)
    return WORKFLOW[match.start():end]


class UpstreamOwnerReviewGateTests(unittest.TestCase):
    def test_no_automated_merge_or_bypass(self):
        self.assertNotRegex(WORKFLOW, r"\bgh\s+pr\s+merge\b")
        self.assertNotRegex(WORKFLOW, r"\bgh\s+api\s+.*\/merges?\b")
        self.assertNotIn("promoted=true", WORKFLOW)
        self.assertNotIn("gh workflow run runtime-refresh.yml", WORKFLOW)

    def test_restricted_permissions_in_review_job(self):
        stage = job("promote")
        for expected in ("      contents: read",
                         "      pull-requests: read", "      issues: write"):
            self.assertIn(expected, stage)
        for forbidden in ("      actions: write", "      contents: write",
                          "      pull-requests: write"):
            self.assertNotIn(forbidden, stage)

    def test_explicit_manual_review_after_checks(self):
        stage = job("promote")
        self.assertIn("needs: [prepare, validate-windows, validate-linux]", stage)
        self.assertIn('if [ -n "$pr" ]; then', stage)
        self.assertIn("awaits explicit owner review and manual squash merge", stage)
        self.assertIn("promoted=false", stage)
        self.assertIn("gh issue create", stage)
        self.assertIn("Automatic merging is intentionally disabled", stage)

    def test_prepare_keeps_reviewable_pr_and_notify_only_issues(self):
        prepare = job("prepare")
        self.assertIn("      contents: write", prepare)
        self.assertIn("      pull-requests: write", prepare)
        self.assertIn("gh pr create --base main --head $branch", prepare)
        self.assertIn("gh workflow run build.yml", prepare)
        self.assertIn("notify_ids != ''", prepare)
        self.assertIn("gh issue create", prepare)
        self.assertIn("restricted NVIDIA DLSS SDK remains notify-only", prepare)

    def test_validation_jobs_cannot_write(self):
        for name in ("validate-windows", "validate-linux"):
            body = job(name)
            self.assertNotIn("contents: write", body)
            self.assertNotIn("pull-requests: write", body)
        self.assertIn("permissions:\n  contents: read", WORKFLOW)


if __name__ == "__main__":
    unittest.main()
