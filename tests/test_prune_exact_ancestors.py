import copy
import importlib.util
import pathlib
import unittest
from unittest.mock import patch

path = pathlib.Path(__file__).resolve().parents[1] / "tools" / "prune-exact-ancestor-branches.py"
spec = importlib.util.spec_from_file_location("exact_ancestor_cleanup", path)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

class ExactAncestorTests(unittest.TestCase):
    def setUp(self):
        self.doc = {
            "schema": 1, "repository": mod.REPO,
            "candidates": [
                {"branch": key, "sha": val} for key, val in mod.FROZEN.items()
            ],
        }
        self.first = self.doc["candidates"][0]

    def test_exact_manifest_accepted(self):
        self.assertEqual(len(mod.FROZEN), len(mod.validate_manifest(self.doc)))

    def test_additional_entry_rejected(self):
        self.doc["candidates"].append({"branch": "main", "sha": "a" * 40})
        with self.assertRaises(ValueError):
            mod.validate_manifest(self.doc)

    def test_sha_mutation_rejected(self):
        self.doc["candidates"][0]["sha"] = "f" * 40
        with self.assertRaises(ValueError):
            mod.validate_manifest(self.doc)

    def test_duplicate_rejected(self):
        self.doc["candidates"][1] = copy.deepcopy(self.first)
        with self.assertRaises(ValueError):
            mod.validate_manifest(self.doc)

    def test_open_pr_blocks(self):
        self.assertIn("open PR", mod.skip_reason(self.first, {self.first["branch"]}, ""))

    def test_workflow_reference_blocks(self):
        self.assertIn("workflow", mod.skip_reason(self.first, set(), self.first["branch"]))

    def test_wrong_head_blocks(self):
        with patch.object(mod, "api", return_value={"protected": False, "commit": {"sha": "0"*40}}):
            self.assertIn("changed HEAD", mod.skip_reason(self.first, set(), ""))

    def test_diverged_branch_blocks(self):
        def fake_api(path, **kw):
            if "/compare/" in path:
                return {"ahead_by": 1, "status": "diverged"}
            return {"protected": False, "commit": {"sha": self.first["sha"]}}
        with patch.object(mod, "api", side_effect=fake_api):
            self.assertIn("not proven", mod.skip_reason(self.first, set(), ""))

    def test_exact_ancestor_passes(self):
        def fake_api(path, **kw):
            if "/compare/" in path:
                return {"ahead_by": 0, "status": "behind"}
            return {"protected": False, "commit": {"sha": self.first["sha"]}}
        with patch.object(mod, "api", side_effect=fake_api):
            self.assertIsNone(mod.skip_reason(self.first, set(), ""))

if __name__ == "__main__":
    unittest.main()
