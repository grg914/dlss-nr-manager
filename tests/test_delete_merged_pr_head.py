import importlib.util
import os
from pathlib import Path
import unittest
from unittest.mock import patch

MODULE = Path(__file__).resolve().parents[1] / "tools" / "delete-merged-pr-head.py"
spec = importlib.util.spec_from_file_location("merged_branch_head_cleanup", MODULE)
cleanup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cleanup)

SHA = "a" * 40
REPO = cleanup.REPO


def event(ref="fix/my-change", sha=SHA, merged=True, head_repo=REPO,
          base="main", number=7):
    return {
        "action": "closed",
        "pull_request": {
            "number": number, "state": "closed",
            "merged": merged,
            "merged_at": "2026-10-09T12:00:00Z" if merged else None,
            "head": {"ref": ref, "sha": sha, "repo": {"full_name": head_repo}},
            "base": {"ref": base, "repo": {"full_name": REPO}},
        },
    }


class BranchCleanupPolicyTests(unittest.TestCase):
    def test_only_same_repo_merged_main_head_is_eligible(self):
        value, reason = cleanup.eligible(event())
        self.assertEqual(("fix/my-change", SHA, 7), value)
        self.assertIsNone(reason)
        for changed in [
            event(merged=False),
            event(head_repo="stranger/fork"),
            event(base="develop"),
            event(ref="main"),
            event(ref="release/v4"),
            event(ref="fix/../unsafe"),
            event(ref="fix//unsafe"),
            event(sha="0" * 7),
        ]:
            self.assertIsNone(cleanup.eligible(changed)[0])

    def test_normal_merge_deletes_only_exact_sha_and_rechecks(self):
        ev = event()
        deleted = []
        requests = []
        def fake_api(path, method="GET", missing_ok=False):
            requests.append((method, path))
            if "/pulls/7" in path:
                return ev["pull_request"]
            if "/pulls?state=open" in path:
                return []
            if "/branches/" in path:
                return None if deleted else {"protected": False, "commit": {"sha": SHA}}
            if method == "DELETE" and path.endswith("/fix/my-change"):
                deleted.append(path)
                return {}
            raise AssertionError("unexpected API call " + path)
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO,
                                     "GITHUB_EVENT_NAME": "pull_request_target"}):
            with patch.object(cleanup, "api", side_effect=fake_api):
                self.assertEqual(0, cleanup.run(ev))
        self.assertEqual(1, len(deleted))

    def test_changed_sha_never_deletes(self):
        ev = event()
        actions = []
        def fake_api(path, method="GET", missing_ok=False):
            actions.append(method)
            if "/pulls/7" in path:
                return ev["pull_request"]
            if "/pulls?state=open" in path:
                return []
            return {"protected": False, "commit": {"sha": "f" * 40}}
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO,
                                     "GITHUB_EVENT_NAME": "pull_request_target"}):
            with patch.object(cleanup, "api", side_effect=fake_api):
                self.assertEqual(0, cleanup.run(ev))
        self.assertNotIn("DELETE", actions)

    def test_open_consumer_pr_blocks_deletion(self):
        ev = event()
        records = []
        def fake_api(path, method="GET", missing_ok=False):
            records.append(method)
            if "/pulls/7" in path:
                return ev["pull_request"]
            if "/pulls?state=open" in path:
                return [{"head": {"ref": "other", "repo": {"full_name": REPO}},
                         "base": {"ref": "fix/my-change"}}]
            raise AssertionError("branch metadata should never be queried")
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO,
                                     "GITHUB_EVENT_NAME": "pull_request_target"}):
            with patch.object(cleanup, "api", side_effect=fake_api):
                self.assertEqual(0, cleanup.run(ev))
        self.assertNotIn("DELETE", records)

    def test_refuses_other_runtime_or_changed_pr_evidence(self):
        ev = event()
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": "other/repo",
                                     "GITHUB_EVENT_NAME": "pull_request_target"}):
            with self.assertRaises(RuntimeError):
                cleanup.run(ev)
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO,
                                     "GITHUB_EVENT_NAME": "push"}):
            with self.assertRaises(RuntimeError):
                cleanup.run(ev)
        with patch.dict(os.environ, {"GITHUB_REPOSITORY": REPO,
                                     "GITHUB_EVENT_NAME": "pull_request_target"}):
            with patch.object(cleanup, "api", return_value=event(ref="fix/changed")["pull_request"]):
                with self.assertRaises(RuntimeError):
                    cleanup.run(ev)


if __name__ == "__main__":
    unittest.main()
